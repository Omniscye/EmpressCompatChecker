// TraceRuntime.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace EMpressCompatChecker
{
    internal static class TraceRuntime
    {
        private static string _id = "Empress.Trace";
        private static Harmony _h = null!;
        private static HashSet<MethodBase> _patched = new HashSet<MethodBase>();
        private static int _cap = 2000;
        private static readonly object _lock = new object();
        private static readonly List<TraceLine> _lines = new List<TraceLine>(_cap);
        [ThreadStatic] private static bool _inTrace;

        internal struct TraceLine
        {
            public string T;
            public string Kind;
            public string Target;
            public int Thread;
            public double DurMs;
            public string Error;
        }

        internal static void Init(string id)
        {
            _id = id;
            _h = new Harmony(_id);
        }

        internal static void PatchAll(IEnumerable<MethodBase> methods)
        {
            foreach (var m in methods)
            {
                if (m == null) continue;
                if (_patched.Contains(m)) continue;
                try
                {
                    var pre = new HarmonyMethod(typeof(TraceRuntime).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic));
                    pre.priority = int.MaxValue;
                    var post = new HarmonyMethod(typeof(TraceRuntime).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic));
                    post.priority = int.MinValue;
                    var fin = new HarmonyMethod(typeof(TraceRuntime).GetMethod(nameof(Finalizer), BindingFlags.Static | BindingFlags.NonPublic));
                    fin.priority = int.MinValue;
                    _h.Patch(original: m, prefix: pre, postfix: post, transpiler: null, finalizer: fin, ilmanipulator: null);
                    _patched.Add(m);
                }
                catch { }
            }
        }

        internal static void UnpatchAll()
        {
            try
            {
                _h.UnpatchSelf();
            }
            catch { }
            _patched.Clear();
        }

        private static void Add(string kind, string target, double durMs, string err)
        {
            var tl = new TraceLine
            {
                T = DateTime.Now.ToString("HH:mm:ss.fff"),
                Kind = kind,
                Target = target,
                Thread = Thread.CurrentThread.ManagedThreadId,
                DurMs = durMs,
                Error = err
            };
            lock (_lock)
            {
                if (_lines.Count >= _cap) _lines.RemoveAt(0);
                _lines.Add(tl);
            }
        }

        private static string Name(MethodBase m)
        {
            var t = m.DeclaringType != null ? m.DeclaringType.FullName : "?";
            return t + "::" + m.Name;
        }

        private static void Prefix(MethodBase __originalMethod, ref long __state)
        {
            if (_inTrace) return;
            _inTrace = true;
            __state = Stopwatch.GetTimestamp();
            Add("enter", Name(__originalMethod), 0, "");
            _inTrace = false;
        }

        private static void Postfix(MethodBase __originalMethod, long __state)
        {
            if (_inTrace) return;
            _inTrace = true;
            var dt = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
            Add("exit", Name(__originalMethod), dt, "");
            _inTrace = false;
        }

        private static Exception Finalizer(MethodBase __originalMethod, Exception __exception, long __state)
        {
            if (_inTrace) return __exception;
            _inTrace = true;
            var dt = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
            var err = __exception != null ? __exception.GetType().Name : "";
            if (__exception != null) Add("error", Name(__originalMethod), dt, err);
            _inTrace = false;
            return __exception;
        }

        internal static void Clear()
        {
            lock (_lock) _lines.Clear();
        }

        internal static List<TraceLine> Snapshot()
        {
            lock (_lock) return _lines.ToList();
        }
    }
}
