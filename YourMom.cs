// EMpressCompatChecker.cs
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace EMpressCompatChecker
{
    [BepInPlugin("Empress.EmpressCompatChecker", "EmpressCompatChecker", "1.2.0")]
    public class EMpressCompatChecker : BaseUnityPlugin
    {
        internal static EMpressCompatChecker Instance { get; private set; } = null!;
        internal new static ManualLogSource Logger => Instance._logger;
        private ManualLogSource _logger => base.Logger;
        internal Harmony? Harmony { get; set; }
        private bool showWindow;
        private Rect windowRect = new Rect(80, 80, 1200, 800);
        private Vector2 scrollLeft;
        private Vector2 scrollRight;
        private Vector2 scrollAnalyzer;
        private Vector2 scrollTrace;
        private string search = string.Empty;
        private List<PluginRow> plugins = new List<PluginRow>();
        private List<ConflictRow> duplicateDlls = new List<ConflictRow>();
        private List<MethodOverlap> overlaps = new List<MethodOverlap>();
        private List<AnalyzerRow> analysis = new List<AnalyzerRow>();
        private GUIStyle windowStyle;
        private GUIStyle headerStyle;
        private GUIStyle titleStyle;
        private GUIStyle labelStyle;
        private GUIStyle buttonStyle;
        private GUIStyle toggleStyle;
        private GUIStyle textFieldStyle;
        private GUIStyle boxStyle;
        private GUIStyle scrollStyle;
        private GUIStyle terminalStyle;
        private GUIStyle terminalBoxStyle;
        private Texture2D bgTex;
        private Texture2D dotRed;
        private Texture2D dotYellow;
        private Texture2D evilPink;
        private string disabledFolder = string.Empty;
        private float openTimer;
        private CursorLockMode _prevLock;
        private bool _prevVisible;
        private bool _cursorTaken;
        private bool _rebuildRequested;
        private int activeTab;
        private ConfigEntry<bool> cfgDeep;
        private ConfigEntry<bool> cfgExt;
        private ConfigEntry<bool> cfgTrace;
        private ConfigEntry<bool> cfgIL;
        internal static bool UIActive => Instance != null && Instance.showWindow;

        private void Awake()
        {
            Instance = this;
            gameObject.transform.parent = null;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            Harmony ??= new Harmony(Info.Metadata.GUID);
            Harmony.PatchAll();
            cfgDeep = Config.Bind("Analyzer", "EnableDeepAnalyzer", false, "");
            cfgExt = Config.Bind("Analyzer", "EnableExtendedHeuristics", false, "");
            cfgTrace = Config.Bind("Analyzer", "EnableRuntimeTrace", false, "");
            cfgIL = Config.Bind("Analyzer", "EnableILSignatures", false, "");
            disabledFolder = Path.GetFullPath(Path.Combine(Paths.PluginPath, "..", "disabled"));
            BuildPlugins();
            BuildDuplicates();
            BuildPatchGraph();
            if (cfgDeep.Value) BuildAnalysis();
            if (cfgTrace.Value) ApplyTracePatches();
            showWindow = false;
            openTimer = 5f;
            Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} ready");
        }

        private void Update()
        {
            if (openTimer > 0f)
            {
                openTimer -= Time.unscaledDeltaTime;
                if (openTimer <= 0f)
                {
                    BuildPlugins();
                    BuildDuplicates();
                    BuildPatchGraph();
                    if (cfgDeep.Value) BuildAnalysis();
                    if (cfgTrace.Value) ApplyTracePatches();
                    showWindow = true;
                    TakeCursor();
                }
            }
            if (Input.GetKey(KeyCode.LeftControl) && Input.GetKeyDown(KeyCode.F10))
            {
                showWindow = !showWindow;
                if (showWindow) TakeCursor(); else RestoreCursor();
            }
            if (showWindow) KeepCursorFree();
            if (_rebuildRequested)
            {
                _rebuildRequested = false;
                BuildPlugins();
                BuildDuplicates();
                BuildPatchGraph();
                if (cfgDeep.Value) BuildAnalysis();
                if (cfgTrace.Value) ApplyTracePatches();
            }
        }

        private void LateUpdate()
        {
            if (showWindow) KeepCursorFree();
        }

        private void OnGUI()
        {
            if (windowStyle == null) InitStyles();
            if (!showWindow) return;
            var targetW = Mathf.Min(Screen.width - 40f, 1600f);
            var targetH = Mathf.Min(Screen.height - 40f, 1000f);
            if (Mathf.Abs(windowRect.width - targetW) > 1f || Mathf.Abs(windowRect.height - targetH) > 1f)
            {
                windowRect.width = targetW;
                windowRect.height = targetH;
            }
            windowRect.x = Mathf.Clamp(windowRect.x, 10f, Mathf.Max(10f, Screen.width - windowRect.width - 10f));
            windowRect.y = Mathf.Clamp(windowRect.y, 10f, Mathf.Max(10f, Screen.height - windowRect.height - 10f));
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), bgTex);
            windowRect = GUILayout.Window(8842201, windowRect, DrawWindow, string.Empty, windowStyle);
        }

        private void InitStyles()
        {
            evilPink = MakeTex(2, 2, new Color(1f, 0.1f, 0.6f));
            bgTex = MakeTex(2, 2, new Color(0f, 0f, 0f, 1f));
            dotRed = MakeTex(1, 1, new Color(1f, 0.2f, 0.4f));
            dotYellow = MakeTex(1, 1, new Color(1f, 0.9f, 0.3f));
            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.normal.background = bgTex;
            windowStyle.hover.background = bgTex;
            windowStyle.active.background = bgTex;
            windowStyle.focused.background = bgTex;
            windowStyle.onNormal.background = bgTex;
            windowStyle.onHover.background = bgTex;
            windowStyle.onActive.background = bgTex;
            windowStyle.onFocused.background = bgTex;
            windowStyle.border = new RectOffset(20, 20, 40, 20);
            windowStyle.overflow = new RectOffset(10, 10, 10, 10);
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.3f, 0.8f) }
            };
            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.4f, 0.9f) }
            };
            var hotPink = new Color(1f, 0.41f, 0.71f);
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = hotPink }
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(8, 8, 6, 6)
            };
            buttonStyle.normal.background = MakeTex(2, 2, new Color(0.9f, 0.1f, 0.5f));
            buttonStyle.hover.background = MakeTex(2, 2, new Color(1f, 0.3f, 0.7f));
            buttonStyle.active.background = MakeTex(2, 2, new Color(0.7f, 0f, 0.4f));
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.textColor = Color.white;
            toggleStyle = new GUIStyle(GUI.skin.toggle)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.magenta },
                onNormal = { textColor = Color.cyan }
            };
            textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 14,
                normal = { background = MakeTex(2, 2, new Color(0f, 0f, 0f, 1f)), textColor = hotPink },
                focused = { background = MakeTex(2, 2, new Color(0.08f, 0.08f, 0.08f, 1f)), textColor = Color.white },
                hover = { background = MakeTex(2, 2, new Color(0.05f, 0.05f, 0.05f, 1f)) }
            };
            boxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, new Color(0f, 0f, 0f, 1f)) },
                border = new RectOffset(4, 4, 4, 4)
            };
            scrollStyle = new GUIStyle(GUI.skin.scrollView)
            {
                normal = { background = MakeTex(2, 2, new Color(0f, 0f, 0f, 0f)) }
            };
            terminalStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = new Color(0.6f, 1f, 0.7f) },
                wordWrap = false,
                alignment = TextAnchor.UpperLeft
            };
            terminalBoxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = MakeTex(2, 2, new Color(0f, 0.05f, 0f, 1f)) },
                border = new RectOffset(4, 4, 4, 4),
                padding = new RectOffset(8, 8, 6, 6)
            };
        }

        private Texture2D MakeTex(int width, int height, Color col)
        {
            var pix = new Color[width * height];
            for (int i = 0; i < pix.Length; ++i) pix[i] = col;
            var result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("EMPRESS COMPATIBILITY DOMINATRIX", titleStyle);
            GUILayout.Space(10);
            GUILayout.Label($"Plugins: {plugins.Count} • Duplicates: {duplicateDlls.Count} • Conflicts: {overlaps.Count(o => o.GroupCount > 1)}", headerStyle);
            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            GUILayout.Label("SEARCH:", labelStyle, GUILayout.Width(80));
            search = GUILayout.TextField(search, textFieldStyle, GUILayout.MinWidth(300));
            if (GUILayout.Button("REBUILD", buttonStyle, GUILayout.Width(120))) { _rebuildRequested = true; }
            if (GUILayout.Button("CLOSE", buttonStyle, GUILayout.Width(100))) { showWindow = false; RestoreCursor(); }
            GUILayout.FlexibleSpace();
            var deepNow = GUILayout.Toggle(cfgDeep.Value, "DEEP", toggleStyle, GUILayout.Width(70));
            if (deepNow != cfgDeep.Value)
            {
                cfgDeep.Value = deepNow;
                if (cfgDeep.Value) { BuildAnalysis(); activeTab = 1; } else { activeTab = 0; }
            }
            var extNow = GUILayout.Toggle(cfgExt.Value, "EXT", toggleStyle, GUILayout.Width(60));
            if (extNow != cfgExt.Value)
            {
                cfgExt.Value = extNow;
                if (cfgDeep.Value) { BuildAnalysis(); }
            }
            var traceNow = GUILayout.Toggle(cfgTrace.Value, "TRACE", toggleStyle, GUILayout.Width(70));
            if (traceNow != cfgTrace.Value)
            {
                cfgTrace.Value = traceNow;
                if (cfgTrace.Value) { ApplyTracePatches(); activeTab = 2; } else { TraceRuntime.UnpatchAll(); }
            }
            var ilNow = GUILayout.Toggle(cfgIL.Value, "IL", toggleStyle, GUILayout.Width(50));
            if (ilNow != cfgIL.Value)
            {
                cfgIL.Value = ilNow;
                if (cfgDeep.Value) { BuildAnalysis(); }
            }
            GUILayout.Space(12);
            if (cfgDeep.Value)
            {
                if (GUILayout.Toggle(activeTab == 0, "OVERVIEW", buttonStyle, GUILayout.Width(110))) activeTab = 0;
                if (GUILayout.Toggle(activeTab == 1, "ANALYZER", buttonStyle, GUILayout.Width(110))) activeTab = 1;
            }
            if (cfgTrace.Value)
            {
                if (GUILayout.Toggle(activeTab == 2, "TRACE", buttonStyle, GUILayout.Width(110))) activeTab = 2;
            }
            GUILayout.Space(12);
            GUILayout.Label("CTRL+F10", labelStyle);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            if (activeTab == 2 && cfgTrace.Value)
            {
                DrawTraceTab();
            }
            else if (activeTab == 1 && cfgDeep.Value)
            {
                DrawAnalyzerTab();
            }
            else
            {
                DrawOverviewTab();
            }

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 40));
        }

        private void DrawOverviewTab()
        {
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));

            float totalW = windowRect.width - 40f;
            float gap = 30f;
            float leftW = Mathf.Clamp(totalW * 0.65f, 680f, totalW - 420f - gap);
            float rightW = Mathf.Clamp(totalW - leftW - gap, 420f, totalW - leftW - gap);
            float vBar = 18f;

            GUILayout.BeginVertical(GUILayout.Width(leftW));
            GUILayout.Label("LOADED PLUGINS", headerStyle);
            scrollLeft = GUILayout.BeginScrollView(scrollLeft, false, true, GUILayout.Width(leftW));

            float pad = 20f;
            float iconW = 16f;
            float activeW = 90f;
            float banishW = 100f;
            float enableW = 100f;
            float fixedTail = iconW + activeW + banishW + enableW + 40f;
            float colsAvail = Mathf.Max(320f, leftW - fixedTail - pad);

            float versionW = 60f;
            float nameW = Mathf.Floor(colsAvail * 0.34f);
            float guidW = Mathf.Floor(colsAvail * 0.33f);
            float fileW = Mathf.Max(80f, colsAvail - nameW - guidW - versionW);

            foreach (var row in FilteredPlugins().ToList())
            {
                GUILayout.BeginHorizontal(boxStyle, GUILayout.Width(leftW - 8f));
                if (HasOverlapFor(row)) GUILayout.Label(dotRed, GUILayout.Width(iconW), GUILayout.Height(16));
                else GUILayout.Space(iconW + 2f);
                GUILayout.Label(row.Name, labelStyle, GUILayout.Width(nameW));
                GUILayout.Label(row.GUID, labelStyle, GUILayout.Width(guidW));
                GUILayout.Label(row.Version, labelStyle, GUILayout.Width(versionW));
                GUILayout.Label(row.FileName, labelStyle, GUILayout.Width(fileW));
                GUILayout.FlexibleSpace();
                var active = GUILayout.Toggle(row.Active, "ACTIVE", toggleStyle, GUILayout.Width(activeW));
                if (active != row.Active) { row.Active = active; ToggleActive(row); }
                var disable = GUILayout.Toggle(row.DisableNextBoot, "BANISH", toggleStyle, GUILayout.Width(banishW));
                if (disable != row.DisableNextBoot)
                {
                    row.DisableNextBoot = disable;
                    if (disable) { DisableNowOrQueue(row); _rebuildRequested = true; }
                    else { TryRestore(row); _rebuildRequested = true; }
                }
                bool canEnable = (!row.Active && row.DisableNextBoot && row.Location.EndsWith(".dll.old", StringComparison.InvariantCultureIgnoreCase));
                if (canEnable)
                {
                    if (GUILayout.Button("ENABLE", buttonStyle, GUILayout.Width(enableW))) { TryRestore(row); _rebuildRequested = true; }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.Space(gap);

            GUILayout.BeginVertical(GUILayout.Width(rightW));
            GUILayout.Label("PATCH OVERLAPS (EVIL)", headerStyle);

            scrollRight = GUILayout.BeginScrollView(
                scrollRight,
                false,
                true,
                GUILayout.Width(rightW)
            );

            var snapshotOverlaps = overlaps.Where(o => o.GroupCount > 1).ToList();
            foreach (var m in snapshotOverlaps)
            {
                GUILayout.BeginVertical(boxStyle, GUILayout.Width(rightW - vBar - 8f));
                GUILayout.BeginHorizontal();
                GUILayout.Label(dotYellow, GUILayout.Width(16), GUILayout.Height(16));
                float targetW = Mathf.Max(120f, rightW - vBar - 70f - 40f);
                GUILayout.Label(m.Target, labelStyle, GUILayout.Width(targetW));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("COPY", buttonStyle, GUILayout.Width(70))) GUIUtility.systemCopyBuffer = m.Target;
                GUILayout.EndHorizontal();

                foreach (var oc in m.Owners.ToList())
                {
                    var match = FindPluginByOwnerKeyOrAlias(oc.MatchKey);
                    var gk = match != null ? match.Location : ("owner:" + oc.MatchKey);
                    if (!m.GroupKeys.Contains(gk)) continue;
                    GUILayout.BeginHorizontal();
                    float countsW = 200f;
                    float ownerW = Mathf.Max(140f, rightW - vBar - countsW - 120f - 40f);
                    GUILayout.Label(oc.Display, labelStyle, GUILayout.Width(ownerW));
                    GUILayout.Label($"pre:{oc.Counts.Pre} post:{oc.Counts.Post} trans:{oc.Counts.Trans} final:{oc.Counts.Final}", labelStyle, GUILayout.Width(countsW));
                    GUILayout.FlexibleSpace();
                    GUI.enabled = match != null && match.GUID != Info.Metadata.GUID;
                    if (GUILayout.Button("BANISH", buttonStyle, GUILayout.Width(120)) && match != null) { match.DisableNextBoot = true; DisableNowOrQueue(match); _rebuildRequested = true; }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndVertical();
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawAnalyzerTab()
        {
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(GUILayout.Width(windowRect.width - 40f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("DEEP ANALYZER", headerStyle);
            GUILayout.FlexibleSpace();
            if (!cfgIL.Value) GUILayout.Label("Limits: no IL diffing", labelStyle);
            if (cfgIL.Value && !cfgExt.Value) GUILayout.Label("IL sigs on - heuristics off", labelStyle);
            if (!cfgTrace.Value) GUILayout.Label("Trace off", labelStyle);
            GUILayout.Space(10);
            if (GUILayout.Button("SCAN", buttonStyle, GUILayout.Width(100))) { BuildAnalysis(); }
            GUILayout.EndHorizontal();
            scrollAnalyzer = GUILayout.BeginScrollView(scrollAnalyzer, false, true, GUILayout.Width(windowRect.width - 40f));
            foreach (var row in analysis)
            {
                GUILayout.BeginVertical(terminalBoxStyle);
                GUILayout.Label($"[{row.Risk}] {row.Target} - {row.Reason}", terminalStyle);
                foreach (var p in row.Patches)
                {
                    var flags = "";
                    if (p.Kind == "Prefix" && p.ReturnsBool) flags += " retBool";
                    if (p.Kind == "Prefix" && p.HasRunOriginalParam) flags += " runOriginal";
                    if (p.Kind == "Postfix" && p.ModifiesResult) flags += " modResult";
                    var ba = "";
                    if (p.Before != null && p.Before.Length > 0) ba += " before:" + string.Join(",", p.Before);
                    if (p.After != null && p.After.Length > 0) ba += " after:" + string.Join(",", p.After);
                    GUILayout.Label($"  {p.Display} :: {p.Kind} pri:{p.Priority}{flags} {ba}", terminalStyle);
                }
                if (row.Notes != null && row.Notes.Count > 0)
                {
                    foreach (var n in row.Notes) GUILayout.Label($"  - {n}", terminalStyle);
                }
                GUILayout.EndVertical();
                GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawTraceTab()
        {
            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            GUILayout.BeginVertical(GUILayout.Width(windowRect.width - 40f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("RUNTIME TRACE", headerStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("CLEAR", buttonStyle, GUILayout.Width(100))) { TraceRuntime.Clear(); }
            GUILayout.EndHorizontal();
            scrollTrace = GUILayout.BeginScrollView(scrollTrace, false, true, GUILayout.Width(windowRect.width - 40f));
            var events = TraceRuntime.Snapshot();
            foreach (var e in events)
            {
                GUILayout.BeginVertical(terminalBoxStyle);
                GUILayout.Label($"{e.T} {e.Kind} {e.Target} dur:{e.DurMs}ms thr:{e.Thread} err:{e.Error}", terminalStyle);
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private IEnumerable<PluginRow> FilteredPlugins()
        {
            if (string.IsNullOrWhiteSpace(search)) return plugins;
            var s = search.Trim().ToLowerInvariant();
            return plugins.Where(p => p.Name.ToLowerInvariant().Contains(s) || p.GUID.ToLowerInvariant().Contains(s) || p.FileName.ToLowerInvariant().Contains(s));
        }

        private void BuildPlugins()
        {
            var list = new List<PluginRow>();
            foreach (var kv in Chainloader.PluginInfos)
            {
                var info = kv.Value;
                var loc = info.Location;
                if (!File.Exists(loc) || loc.EndsWith(".dll.old", StringComparison.InvariantCultureIgnoreCase)) continue;
                var row = new PluginRow
                {
                    GUID = info.Metadata.GUID,
                    Name = info.Metadata.Name,
                    Version = info.Metadata.Version.ToString(),
                    Location = loc,
                    FileName = Path.GetFileName(loc),
                    Active = info.Instance != null && info.Instance.isActiveAndEnabled,
                    Info = info,
                    HarmonyIdCandidate = info.Metadata.GUID,
                    Deps = ReadDeps(info)
                };
                list.Add(row);
            }
            try
            {
                var disabled = Directory.Exists(Paths.PluginPath) ? Directory.GetFiles(Paths.PluginPath, "*.dll.old", SearchOption.AllDirectories) : Array.Empty<string>();
                foreach (var path in disabled)
                {
                    var name = Path.GetFileName(path);
                    if (name.EndsWith(".dll.old", StringComparison.InvariantCultureIgnoreCase)) name = name.Substring(0, name.Length - ".dll.old".Length);
                    var row = new PluginRow
                    {
                        GUID = "(disabled)",
                        Name = name,
                        Version = "-",
                        Location = path,
                        FileName = Path.GetFileName(path),
                        Active = false,
                        DisableNextBoot = true,
                        Info = null,
                        HarmonyIdCandidate = name,
                        Deps = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase)
                    };
                    list.Add(row);
                }
            }
            catch (Exception e) { Logger.LogWarning(e.Message); }
            plugins = list.OrderBy(p => p.Active ? 0 : 1).ThenBy(p => p.Name, StringComparer.InvariantCultureIgnoreCase).ToList();
        }

        private HashSet<string> ReadDeps(BepInEx.PluginInfo info)
        {
            var set = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            try
            {
                var t = info.Instance != null ? info.Instance.GetType() : null;
                if (t != null)
                {
                    var attrs = t.GetCustomAttributes(true);
                    foreach (var a in attrs)
                    {
                        var at = a.GetType();
                        var n = at.Name;
                        if (n.Contains("BepInDependency", StringComparison.InvariantCultureIgnoreCase))
                        {
                            var p = at.GetProperty("DependencyGUID") ?? at.GetProperty("GUID");
                            var v = p != null ? p.GetValue(a) as string : null;
                            if (!string.IsNullOrEmpty(v)) set.Add(v);
                        }
                    }
                }
            }
            catch { }
            return set;
        }

        private void BuildDuplicates()
        {
            duplicateDlls.Clear();
            try
            {
                var allDlls = Directory.Exists(Paths.PluginPath) ? Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories) : Array.Empty<string>();
                var byName = allDlls.GroupBy(p => Path.GetFileName(p), StringComparer.InvariantCultureIgnoreCase).Where(g => g.Count() > 1);
                foreach (var g in byName)
                {
                    var cr = new ConflictRow { KeyFile = g.Key, Paths = g.ToList(), FileNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase) };
                    foreach (var p in g) cr.FileNames.Add(Path.GetFileName(p));
                    duplicateDlls.Add(cr);
                }
            }
            catch (Exception e) { Logger.LogWarning(e.Message); }
        }

        private void BuildPatchGraph()
        {
            overlaps.Clear();
            try
            {
                var methods = HarmonyLib.Harmony.GetAllPatchedMethods().ToList();
                foreach (var m in methods)
                {
                    var bundle = HarmonyCompatUtil.GetPatches(m);
                    if (bundle == null) continue;
                    var owners = bundle.AllOwners.ToList();
                    if (owners.Count == 0) continue;

                    var mo = new MethodOverlap
                    {
                        Target = ((m.DeclaringType != null ? m.DeclaringType.FullName : "?") + "::" + m.Name),
                        Owners = new List<OwnerCount>()
                    };

                    var tally = new Dictionary<string, PatchCounts>(StringComparer.InvariantCultureIgnoreCase);
                    foreach (var p in bundle.Prefixes) Inc(tally, p.Owner, 0);
                    foreach (var p in bundle.Postfixes) Inc(tally, p.Owner, 1);
                    foreach (var p in bundle.Transpilers) Inc(tally, p.Owner, 2);
                    foreach (var p in bundle.Finalizers) Inc(tally, p.Owner, 3);

                    var groupKeys = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                    foreach (var id in owners.Distinct(StringComparer.InvariantCultureIgnoreCase))
                    {
                        var counts = tally.ContainsKey(id) ? tally[id] : new PatchCounts();
                        var display = ResolveOwnerDisplay(id);
                        mo.Owners.Add(new OwnerCount { MatchKey = id, Display = display, Counts = counts });
                        var pr = FindPluginByOwnerKeyOrAlias(id);
                        var gk = pr != null ? pr.Location : ("owner:" + id);
                        groupKeys.Add(gk);
                    }
                    mo.OwnerIds = mo.Owners.Select(o => o.MatchKey).ToHashSet(StringComparer.InvariantCultureIgnoreCase);
                    mo.GroupKeys = groupKeys;
                    overlaps.Add(mo);
                }
            }
            catch (Exception e) { Logger.LogWarning(e.Message); }
        }

        private void BuildAnalysis()
        {
            analysis.Clear();
            try
            {
                var methods = HarmonyLib.Harmony.GetAllPatchedMethods().ToList();
                foreach (var m in methods)
                {
                    var bundle = HarmonyCompatUtil.GetPatches(m);
                    if (bundle == null) continue;
                    var any = bundle.Prefixes.Count + bundle.Postfixes.Count + bundle.Transpilers.Count + bundle.Finalizers.Count;
                    if (any == 0) continue;

                    var row = new AnalyzerRow();
                    row.Target = ((m.DeclaringType != null ? m.DeclaringType.FullName : "?") + "::" + m.Name);
                    var patches = new List<AnalyzerPatch>();

                    int prefixCount = bundle.Prefixes.Count;
                    int postfixCount = bundle.Postfixes.Count;
                    int transpilerCount = bundle.Transpilers.Count;
                    int finalizerCount = bundle.Finalizers.Count;

                    bool anyPrefixCanCancel = false;
                    bool anyPostModifyResult = false;

                    foreach (var p in bundle.Prefixes)
                    {
                        var ap = new AnalyzerPatch
                        {
                            Owner = p.Owner,
                            Display = ResolveOwnerDisplay(p.Owner),
                            Kind = "Prefix",
                            Priority = p.Priority,
                            Before = p.Before ?? Array.Empty<string>(),
                            After = p.After ?? Array.Empty<string>(),
                            ReturnsBool = p.Method != null && p.Method.ReturnType == typeof(bool),
                            HasRunOriginalParam = p.Method != null && HasRunOriginal(p.Method)
                        };
                        if (ap.ReturnsBool || ap.HasRunOriginalParam) anyPrefixCanCancel = true;
                        patches.Add(ap);
                    }
                    foreach (var p in bundle.Postfixes)
                    {
                        var ap = new AnalyzerPatch
                        {
                            Owner = p.Owner,
                            Display = ResolveOwnerDisplay(p.Owner),
                            Kind = "Postfix",
                            Priority = p.Priority,
                            Before = p.Before ?? Array.Empty<string>(),
                            After = p.After ?? Array.Empty<string>(),
                            ModifiesResult = p.Method != null && CanModifyResult(p.Method)
                        };
                        if (ap.ModifiesResult) anyPostModifyResult = true;
                        patches.Add(ap);
                    }
                    foreach (var p in bundle.Transpilers)
                    {
                        var ap = new AnalyzerPatch
                        {
                            Owner = p.Owner,
                            Display = ResolveOwnerDisplay(p.Owner),
                            Kind = "Transpiler",
                            Priority = p.Priority,
                            Before = p.Before ?? Array.Empty<string>(),
                            After = p.After ?? Array.Empty<string>()
                        };
                        patches.Add(ap);
                    }
                    foreach (var p in bundle.Finalizers)
                    {
                        var ap = new AnalyzerPatch
                        {
                            Owner = p.Owner,
                            Display = ResolveOwnerDisplay(p.Owner),
                            Kind = "Finalizer",
                            Priority = p.Priority,
                            Before = p.Before ?? Array.Empty<string>(),
                            After = p.After ?? Array.Empty<string>()
                        };
                        patches.Add(ap);
                    }

                    string risk = "Low";
                    string reason = "No control flow risks detected";
                    if (transpilerCount > 1)
                    {
                        risk = "High";
                        reason = "Multiple transpilers rewriting IL";
                    }
                    else if (anyPrefixCanCancel && prefixCount > 1)
                    {
                        risk = "High";
                        reason = "Multiple prefixes may cancel or re-route original";
                    }
                    else if (transpilerCount == 1 && (prefixCount > 0 || anyPostModifyResult))
                    {
                        risk = "Medium";
                        reason = "Transpiler combined with other patches";
                    }
                    else if (finalizerCount > 0)
                    {
                        risk = "Medium";
                        reason = "Finalizer may alter exception handling";
                    }
                    else if (anyPrefixCanCancel)
                    {
                        risk = "Medium";
                        reason = "Prefix can skip original";
                    }
                    else if (anyPostModifyResult)
                    {
                        risk = "Low";
                        reason = "Postfix may modify return value";
                    }

                    var notes = new List<string>();
                    if (cfgExt.Value)
                    {
                        var owners = patches.Select(p => p.Owner).Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();
                        var adj = new Dictionary<string, HashSet<string>>(StringComparer.InvariantCultureIgnoreCase);
                        foreach (var o in owners) adj[o] = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                        var afterEdges = new List<(string A, string B)>();
                        foreach (var p in patches)
                        {
                            if (p.Before != null)
                            {
                                foreach (var b in p.Before)
                                {
                                    if (string.IsNullOrWhiteSpace(b)) continue;
                                    if (adj.ContainsKey(p.Owner) && adj.ContainsKey(b)) adj[p.Owner].Add(b);
                                }
                            }
                            if (p.After != null)
                            {
                                foreach (var a in p.After)
                                {
                                    if (string.IsNullOrWhiteSpace(a)) continue;
                                    if (adj.ContainsKey(a) && adj.ContainsKey(p.Owner)) adj[a].Add(p.Owner);
                                    afterEdges.Add((p.Owner, a));
                                }
                            }
                        }
                        bool hasCycle = HasCycle(adj);
                        if (hasCycle)
                        {
                            risk = "High";
                            notes.Add("Ordering cycle detected between patches");
                        }
                        foreach (var e in afterEdges)
                        {
                            var pa = FindPluginByOwnerKeyOrAlias(e.A);
                            var pb = FindPluginByOwnerKeyOrAlias(e.B);
                            if (pa != null && pb != null && !string.Equals(pa.GUID, pb.GUID, StringComparison.InvariantCultureIgnoreCase))
                            {
                                if (!pa.Deps.Contains(pb.GUID))
                                {
                                    notes.Add($"{pa.Name} orders after {pb.Name} but has no BepInDependency");
                                }
                            }
                        }
                    }

                    if (cfgIL.Value && bundle.Transpilers.Count > 1)
                    {
                        var sigs = new Dictionary<string, ILSig>();
                        foreach (var t in bundle.Transpilers)
                        {
                            if (t.Method != null)
                            {
                                var s = ILDiffUtil.BuildSig(t.Method);
                                sigs[t.Owner] = s;
                            }
                        }
                        var owners = sigs.Keys.ToList();
                        for (int i = 0; i < owners.Count; i++)
                        {
                            for (int j = i + 1; j < owners.Count; j++)
                            {
                                var a = owners[i];
                                var b = owners[j];
                                var sim = ILDiffUtil.Similarity(sigs[a], sigs[b]);
                                if (sim >= 50) notes.Add($"Transpilers {a} and {b} share IL signature overlap {sim}%");
                            }
                        }
                    }

                    row.Patches = patches.OrderBy(p => p.Kind).ThenByDescending(p => p.Priority).ToList();
                    row.Risk = risk;
                    row.Reason = reason;
                    row.Notes = notes;
                    analysis.Add(row);
                }

                analysis = analysis.OrderByDescending(a => a.Risk == "High" ? 2 : a.Risk == "Medium" ? 1 : 0).ThenBy(a => a.Target).ToList();
            }
            catch (Exception e) { Logger.LogWarning(e.Message); }
        }

        private void ApplyTracePatches()
        {
            try
            {
                var methods = HarmonyLib.Harmony.GetAllPatchedMethods().ToList();
                TraceRuntime.Init(Info.Metadata.GUID + ".Trace");
                TraceRuntime.PatchAll(methods);
            }
            catch (Exception e) { Logger.LogWarning(e.Message); }
        }

        private static bool HasCycle(Dictionary<string, HashSet<string>> adj)
        {
            var visited = new HashSet<string>();
            var stack = new HashSet<string>();
            bool Dfs(string u)
            {
                if (stack.Contains(u)) return true;
                if (visited.Contains(u)) return false;
                visited.Add(u);
                stack.Add(u);
                if (adj.TryGetValue(u, out var next))
                {
                    foreach (var v in next)
                    {
                        if (Dfs(v)) return true;
                    }
                }
                stack.Remove(u);
                return false;
            }
            foreach (var k in adj.Keys)
            {
                if (Dfs(k)) return true;
            }
            return false;
        }

        private static bool HasRunOriginal(MethodInfo mi)
        {
            try
            {
                var pars = mi.GetParameters();
                for (int i = 0; i < pars.Length; i++)
                {
                    var p = pars[i];
                    if (p.ParameterType.IsByRef && string.Equals(p.ParameterType.GetElementType()?.FullName, "System.Boolean", StringComparison.InvariantCultureIgnoreCase) && string.Equals(p.Name, "__runOriginal", StringComparison.InvariantCultureIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool CanModifyResult(MethodInfo mi)
        {
            try
            {
                var pars = mi.GetParameters();
                for (int i = 0; i < pars.Length; i++)
                {
                    var p = pars[i];
                    if (p.ParameterType.IsByRef && string.Equals(p.Name, "__result", StringComparison.InvariantCultureIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void Inc(Dictionary<string, PatchCounts> map, string owner, int type)
        {
            if (string.IsNullOrEmpty(owner)) owner = "?";
            if (!map.TryGetValue(owner, out var c)) c = new PatchCounts();
            if (type == 0) c.Pre++;
            else if (type == 1) c.Post++;
            else if (type == 2) c.Trans++;
            else c.Final++;
            map[owner] = c;
        }

        private string ResolveOwnerDisplay(string owner)
        {
            var p = FindPluginByOwnerKeyOrAlias(owner);
            if (p != null) return p.Name + " [" + p.GUID + "]";
            var byName = plugins.FirstOrDefault(x => x.Name.Equals(owner, StringComparison.InvariantCultureIgnoreCase));
            if (byName != null) return byName.Name + " [" + byName.GUID + "]";
            return owner;
        }

        private PluginRow? FindPluginByOwnerKeyOrAlias(string owner)
        {
            var p = plugins.FirstOrDefault(x => x.GUID.Equals(owner, StringComparison.InvariantCultureIgnoreCase));
            if (p != null) return p;
            p = plugins.FirstOrDefault(x => x.HarmonyIdCandidate.Equals(owner, StringComparison.InvariantCultureIgnoreCase));
            if (p != null) return p;
            p = plugins.FirstOrDefault(x => x.Name.Equals(owner, StringComparison.InvariantCultureIgnoreCase));
            if (p != null) return p;
            PluginRow? best = null;
            var bestLen = -1;
            foreach (var row in plugins)
            {
                if (owner.IndexOf(row.GUID, StringComparison.InvariantCultureIgnoreCase) >= 0)
                {
                    var len = row.GUID.Length;
                    if (len > bestLen) { best = row; bestLen = len; }
                }
                var normOwner = San(owner);
                var normName = San(row.Name);
                if (!string.IsNullOrEmpty(normName) && normOwner.Contains(normName))
                {
                    var len2 = normName.Length;
                    if (len2 > bestLen) { best = row; bestLen = len2; }
                }
            }
            return best;
        }

        private static string San(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var a = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                var ch = s[i];
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9')) a.Append(char.ToLowerInvariant(ch));
            }
            return a.ToString();
        }

        private void ToggleActive(PluginRow row)
        {
            try
            {
                if (row.Info != null && row.Info.Instance != null) row.Info.Instance.enabled = row.Active;
            }
            catch (Exception e) { Logger.LogWarning(e.Message); }
        }

        private void DisableNowOrQueue(PluginRow row)
        {
            try
            {
                if (row.GUID == Info.Metadata.GUID) return;
                var src = row.Location;
                if (src.EndsWith(".dll.old", StringComparison.InvariantCultureIgnoreCase)) return;
                var dst = src + ".dll.old";
                try
                {
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                    row.Location = dst;
                    row.FileName = Path.GetFileName(dst);
                    row.Active = false;
                }
                catch (Exception ex) { Logger.LogWarning(ex.Message); }
            }
            catch (Exception e) { Logger.LogWarning(e.Message); }
        }

        private void TryRestore(PluginRow row)
        {
            try
            {
                var src = row.Location;
                if (!src.EndsWith(".dll.old", StringComparison.InvariantCultureIgnoreCase)) return;
                var dst = src.Substring(0, src.Length - 8);
                try
                {
                    if (File.Exists(dst)) File.Delete(dst);
                    File.Move(src, dst);
                    row.Location = dst;
                    row.FileName = Path.GetFileName(dst);
                    row.DisableNextBoot = false;
                }
                catch (Exception ex) { Logger.LogWarning(ex.Message); }
            }
            catch (Exception e) { Logger.LogWarning(e.Message); }
        }

        private bool HasOverlapFor(PluginRow row)
        {
            var key = row.Location;
            return overlaps.Any(x => x.GroupCount > 1 && x.GroupKeys.Contains(key));
        }

        private void TakeCursor()
        {
            if (_cursorTaken) return;
            _prevLock = Cursor.lockState;
            _prevVisible = Cursor.visible;
            var cmType = FindTypeBySimpleName("CursorManager");
            var inst = cmType?.GetField("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            if (inst != null)
            {
                try { cmType.GetMethod("Unlock", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(inst, new object[] { 9999f }); } catch { }
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorTaken = true;
        }

        private void KeepCursorFree()
        {
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        private void RestoreCursor()
        {
            if (!_cursorTaken) return;
            Cursor.lockState = _prevLock;
            Cursor.visible = _prevVisible;
            _cursorTaken = false;
        }

        private static Type? FindTypeBySimpleName(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? t = null;
                try { t = asm.GetTypes().FirstOrDefault(x => x.Name == simpleName || (x.FullName?.EndsWith("." + simpleName) ?? false)); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        [HarmonyPatch]
        private static class Patch_CameraAim_Update
        {
            private static MethodBase TargetMethod()
            {
                var t = FindTypeBySimpleName("CameraAim");
                return t?.GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? null!;
            }
            private static bool Prefix() => !UIActive;
        }

        private class PluginRow
        {
            public string GUID = string.Empty;
            public string Name = string.Empty;
            public string Version = string.Empty;
            public string Location = string.Empty;
            public string FileName = string.Empty;
            public bool Active;
            public bool DisableNextBoot;
            public string HarmonyIdCandidate = string.Empty;
            public BepInEx.PluginInfo? Info;
            public HashSet<string> Deps = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        }

        private class ConflictRow
        {
            public string KeyFile = string.Empty;
            public List<string> Paths = new List<string>();
            public HashSet<string> FileNames = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        }

        private class MethodOverlap
        {
            public string Target = string.Empty;
            public List<OwnerCount> Owners = new List<OwnerCount>();
            public HashSet<string> OwnerIds = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            public HashSet<string> GroupKeys = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            public int GroupCount => GroupKeys.Count;
        }

        private struct PatchCounts
        {
            public int Pre;
            public int Post;
            public int Trans;
            public int Final;
        }

        private class OwnerCount
        {
            public string MatchKey = string.Empty;
            public string Display = string.Empty;
            public PatchCounts Counts;
        }

        private class AnalyzerRow
        {
            public string Target = string.Empty;
            public List<AnalyzerPatch> Patches = new List<AnalyzerPatch>();
            public string Risk = string.Empty;
            public string Reason = string.Empty;
            public List<string> Notes = new List<string>();
        }

        private class AnalyzerPatch
        {
            public string Owner = string.Empty;
            public string Display = string.Empty;
            public string Kind = string.Empty;
            public int Priority;
            public string[] Before = Array.Empty<string>();
            public string[] After = Array.Empty<string>();
            public bool ReturnsBool;
            public bool HasRunOriginalParam;
            public bool ModifiesResult;
        }
    }
}
