// ILDiffUtil.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace EMpressCompatChecker
{
    internal class ILSig
    {
        public HashSet<string> Tokens = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
    }

    internal static class ILDiffUtil
    {
        private static readonly Dictionary<short, OpCode> _opCodes = BuildOpMap();

        private static Dictionary<short, OpCode> BuildOpMap()
        {
            var d = new Dictionary<short, OpCode>();
            var fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var f in fields)
            {
                if (f.GetValue(null) is OpCode oc) d[oc.Value] = oc;
            }
            return d;
        }

        internal static ILSig BuildSig(MethodInfo mi)
        {
            var sig = new ILSig();
            try
            {
                var mb = mi.GetMethodBody();
                if (mb == null) return sig;
                var il = mb.GetILAsByteArray();
                if (il == null) return sig;
                int i = 0;
                while (i < il.Length)
                {
                    OpCode oc;
                    var b = il[i++];
                    if (b == 0xFE)
                    {
                        var b2 = il[i++];
                        short v = (short)(0xFE00 | b2);
                        if (!_opCodes.TryGetValue(v, out oc)) continue;
                    }
                    else
                    {
                        if (!_opCodes.TryGetValue(b, out oc)) continue;
                    }
                    switch (oc.OperandType)
                    {
                        case OperandType.InlineBrTarget:
                        case OperandType.InlineField:
                        case OperandType.InlineMethod:
                        case OperandType.InlineSig:
                        case OperandType.InlineString:
                        case OperandType.InlineTok:
                        case OperandType.InlineType:
                        case OperandType.ShortInlineR:
                        case OperandType.InlineI:
                        case OperandType.InlineR:
                        case OperandType.InlineI8:
                            i += OperandSize(oc.OperandType);
                            break;
                        case OperandType.ShortInlineBrTarget:
                        case OperandType.ShortInlineI:
                        case OperandType.ShortInlineVar:
                            i += 1;
                            break;
                        case OperandType.InlineVar:
                            i += 2;
                            break;
                        case OperandType.InlineSwitch:
                            int n = BitConverter.ToInt32(il, i);
                            i += 4 + (n * 4);
                            break;
                        case OperandType.InlineNone:
                            break;
                    }
                    sig.Tokens.Add(oc.Name);
                }
            }
            catch { }
            return sig;
        }

        private static int OperandSize(OperandType t)
        {
            switch (t)
            {
                case OperandType.InlineBrTarget: return 4;
                case OperandType.InlineField: return 4;
                case OperandType.InlineMethod: return 4;
                case OperandType.InlineSig: return 4;
                case OperandType.InlineString: return 4;
                case OperandType.InlineTok: return 4;
                case OperandType.InlineType: return 4;
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI: return 4;
                case OperandType.InlineR: return 8;
                case OperandType.InlineI8: return 8;
                default: return 0;
            }
        }

        internal static int Similarity(ILSig a, ILSig b)
        {
            if (a == null || b == null) return 0;
            var inter = 0;
            foreach (var t in a.Tokens) if (b.Tokens.Contains(t)) inter++;
            var uni = a.Tokens.Count + b.Tokens.Count - inter;
            if (uni <= 0) return 0;
            return (int)Math.Round(100.0 * inter / uni);
        }
    }
}
