// HarmonyCompatUtil.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace EMpressCompatChecker
{
    internal static class HarmonyCompatUtil
    {
        internal class PatchRecord
        {
            public string Owner = string.Empty;
            public int Priority;
            public string[] Before = Array.Empty<string>();
            public string[] After = Array.Empty<string>();
            public MethodInfo? Method;
        }

        internal class PatchBundle
        {
            public List<PatchRecord> Prefixes = new List<PatchRecord>();
            public List<PatchRecord> Postfixes = new List<PatchRecord>();
            public List<PatchRecord> Transpilers = new List<PatchRecord>();
            public List<PatchRecord> Finalizers = new List<PatchRecord>();
            public HashSet<string> AllOwners
            {
                get
                {
                    var s = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
                    foreach (var p in Prefixes) s.Add(p.Owner);
                    foreach (var p in Postfixes) s.Add(p.Owner);
                    foreach (var p in Transpilers) s.Add(p.Owner);
                    foreach (var p in Finalizers) s.Add(p.Owner);
                    return s;
                }
            }
        }

        internal static PatchBundle GetPatches(MethodBase m)
        {
            var bundle = new PatchBundle();
            var harmonyType = Type.GetType("HarmonyLib.Harmony, HarmonyLib") ?? Type.GetType("HarmonyLib.Harmony, 0Harmony") ?? typeof(HarmonyLib.Harmony);
            var getPatchInfo = harmonyType.GetMethod("GetPatchInfo", BindingFlags.Public | BindingFlags.Static);
            if (getPatchInfo == null) return bundle;
            var info = getPatchInfo.Invoke(null, new object[] { m });
            if (info == null) return bundle;

            Fill(bundle.Prefixes, info, "Prefixes");
            Fill(bundle.Postfixes, info, "Postfixes");
            Fill(bundle.Transpilers, info, "Transpilers");
            Fill(bundle.Finalizers, info, "Finalizers");

            return bundle;
        }

        private static void Fill(List<PatchRecord> dst, object info, string propName)
        {
            var t = info.GetType();
            MemberInfo? prop =
                (t.GetProperty(propName) as MemberInfo) ??
                (t.GetProperty(propName.ToLowerInvariant()) as MemberInfo) ??
                (t.GetField(propName) as MemberInfo) ??
                (t.GetField(propName.ToLowerInvariant()) as MemberInfo);
            if (prop == null) return;

            object? val = prop is PropertyInfo pi ? pi.GetValue(info)
                           : prop is FieldInfo fi ? fi.GetValue(info)
                           : null;
            if (val == null) return;

            var seq = val as IEnumerable;
            if (seq == null) return;

            foreach (var p in seq)
            {
                if (p == null) continue;
                var rec = new PatchRecord();
                rec.Owner = (GetString(p, "owner") ?? GetString(p, "Owner")) ?? "";
                rec.Priority = (int)(GetInt(p, "priority") ?? GetInt(p, "Priority") ?? 0);
                rec.Before = GetStringArray(p, "before") ?? GetStringArray(p, "Before") ?? Array.Empty<string>();
                rec.After = GetStringArray(p, "after") ?? GetStringArray(p, "After") ?? Array.Empty<string>();
                rec.Method = (GetObj(p, "patchMethod") as MethodInfo) ?? (GetObj(p, "PatchMethod") as MethodInfo);
                dst.Add(rec);
            }
        }

        private static object? GetObj(object target, string name)
        {
            var t = target.GetType();
            var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) return p.GetValue(target);
            var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) return f.GetValue(target);
            return null;
        }

        private static string? GetString(object target, string name)
        {
            return GetObj(target, name) as string;
        }

        private static int? GetInt(object target, string name)
        {
            var o = GetObj(target, name);
            if (o == null) return null;
            if (o is int i) return i;
            try { return Convert.ToInt32(o); } catch { return null; }
        }

        private static string[]? GetStringArray(object target, string name)
        {
            var o = GetObj(target, name);
            if (o == null) return null;
            if (o is string[] sa) return sa;
            if (o is IEnumerable e)
            {
                var list = new List<string>();
                foreach (var x in e) if (x != null) list.Add(x.ToString() ?? "");
                return list.ToArray();
            }
            return null;
        }
    }
}
