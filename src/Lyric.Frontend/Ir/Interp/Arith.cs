using Lyric.Core;

namespace Lyric.Ir.Interp;

/// <summary>
/// The number tower's operations as the C back end performs them (lyr/panic.h, lyr/numeric.h;
/// <c>CEmitter.Binary</c>, <c>Unary</c>, <c>Conversion</c>): the same result bit for bit, and the
/// same panic where C panics (design/v5/spec/09 Q7, 13 M9a P2).
/// </summary>
public static class Arith
{
    public static bool IsSigned(IrScalar k) => k is IrScalar.I8 or IrScalar.I16 or IrScalar.I32 or IrScalar.I64;
    public static bool IsUnsigned(IrScalar k) => k is IrScalar.U8 or IrScalar.U16 or IrScalar.U32 or IrScalar.U64;
    public static bool IsInteger(IrScalar k) => IsSigned(k) || IsUnsigned(k);
    public static bool IsFloat(IrScalar k) => k is IrScalar.F32 or IrScalar.F64;

    public static int Width(IrScalar k) => k switch
    {
        IrScalar.I8 or IrScalar.U8 => 8,
        IrScalar.I16 or IrScalar.U16 => 16,
        IrScalar.I32 or IrScalar.U32 or IrScalar.F32 => 32,
        _ => 64,
    };

    /// <summary>Raw bits reduced to the type's width and extended to 64 — the canonical form of
    /// <see cref="Value.Bits"/>: C's conversion to the type.</summary>
    public static ulong Norm(ulong raw, IrScalar k) => k switch
    {
        IrScalar.I8 => unchecked((ulong)(long)(sbyte)raw),
        IrScalar.I16 => unchecked((ulong)(long)(short)raw),
        IrScalar.I32 => unchecked((ulong)(long)(int)raw),
        IrScalar.U8 => raw & 0xFF,
        IrScalar.U16 => raw & 0xFFFF,
        IrScalar.U32 or IrScalar.F32 => raw & 0xFFFF_FFFF,
        IrScalar.Bool => raw & 1,
        _ => raw,
    };

    /// <summary>Canonical bits as an <see cref="IntConst"/> holds them: two's complement at the
    /// type's width, zero-extended to 64 (the verifier's encoding).</summary>
    public static ulong Pattern(ulong bits, IrScalar k) => Width(k) == 64 ? bits : bits & ((1UL << Width(k)) - 1);

    private static Int128 Signed(ulong bits) => unchecked((long)bits);
    private static UInt128 Unsigned(ulong bits) => bits;

    private static (Int128 Min, Int128 Max) Range(IrScalar k) => k switch
    {
        IrScalar.I8 => (sbyte.MinValue, sbyte.MaxValue),
        IrScalar.I16 => (short.MinValue, short.MaxValue),
        IrScalar.I32 => (int.MinValue, int.MaxValue),
        IrScalar.I64 => (long.MinValue, long.MaxValue),
        IrScalar.U8 => (0, byte.MaxValue),
        IrScalar.U16 => (0, ushort.MaxValue),
        IrScalar.U32 => (0, uint.MaxValue),
        _ => (0, ulong.MaxValue),
    };

    /// <summary>The exact result of <c>+ - *</c> over the two operands of type <paramref name="k"/>,
    /// and whether it leaves the type — <c>__builtin_*_overflow</c>.</summary>
    public static (ulong Wrapped, bool Overflows) Exact(IrBinKind op, IrScalar k, ulong a, ulong b)
    {
        if (IsSigned(k))
        {
            var x = Signed(a);
            var y = Signed(b);
            var r = op switch { IrBinKind.Add or IrBinKind.AddWrap => x + y, IrBinKind.Sub or IrBinKind.SubWrap => x - y, _ => x * y };
            var (min, max) = Range(k);
            return (Norm(unchecked((ulong)r), k), r < min || r > max);
        }
        var ux = Unsigned(a);
        var uy = Unsigned(b);
        var ur = op switch
        {
            IrBinKind.Add or IrBinKind.AddWrap => ux + uy,
            IrBinKind.Sub or IrBinKind.SubWrap => unchecked(ux - uy),
            _ => ux * uy,
        };
        var wraps = op is IrBinKind.Sub or IrBinKind.SubWrap ? ux < uy : ur > (UInt128)Range(k).Max;
        return (Norm(unchecked((ulong)ur), k), wraps);
    }

    private static string Symbol(IrBinKind op) => op switch
    {
        IrBinKind.Add => "+", IrBinKind.Sub => "-", IrBinKind.Mul => "*", IrBinKind.Div => "/", _ => "%",
    };

    public static Value Binary(IrBinKind op, IrScalarType type, Value l, Value r, IrScalar rightKind, bool overflowChecks)
    {
        var k = type.Kind;
        if (k == IrScalar.F64 && op is IrBinKind.Add or IrBinKind.Sub or IrBinKind.Mul or IrBinKind.Div or IrBinKind.Rem)
        {
            double a = l.AsDouble, b = r.AsDouble;
            return Value.Double(op switch { IrBinKind.Add => a + b, IrBinKind.Sub => a - b, IrBinKind.Mul => a * b, IrBinKind.Div => a / b, _ => a % b });
        }
        if (k == IrScalar.F32 && op is IrBinKind.Add or IrBinKind.Sub or IrBinKind.Mul or IrBinKind.Div or IrBinKind.Rem)
        {
            float a = l.AsSingle, b = r.AsSingle;
            return Value.Single(op switch { IrBinKind.Add => a + b, IrBinKind.Sub => a - b, IrBinKind.Mul => a * b, IrBinKind.Div => a / b, _ => a % b });
        }
        switch (op)
        {
            case IrBinKind.Add or IrBinKind.Sub or IrBinKind.Mul:
            {
                var (wrapped, overflows) = Exact(op, k, l.Bits, r.Bits);
                if (overflows && overflowChecks)
                    throw ComptimeFailure.Panic("LYR-RT0002", $"arithmetic overflow in '{Symbol(op)}'");
                return Value.Uint(wrapped);
            }
            case IrBinKind.AddWrap or IrBinKind.SubWrap or IrBinKind.MulWrap:
                return Value.Uint(Exact(op, k, l.Bits, r.Bits).Wrapped);
            case IrBinKind.Div or IrBinKind.Rem:
                if (r.Bits == 0) throw ComptimeFailure.Panic("LYR-RT0001", "division by zero");
                if (IsSigned(k))
                {
                    var a = unchecked((long)l.Bits);
                    var b = unchecked((long)r.Bits);
                    if (b == -1 && (Int128)a == Range(k).Min)
                        throw ComptimeFailure.Panic("LYR-RT0002", $"arithmetic overflow in '{Symbol(op)}'");
                    return Value.Uint(Norm(unchecked((ulong)(op == IrBinKind.Div ? a / b : a % b)), k));
                }
                return Value.Uint(Norm(op == IrBinKind.Div ? l.Bits / r.Bits : l.Bits % r.Bits, k));
            case IrBinKind.Shl or IrBinKind.Shr:
            {
                var bits = Width(k);
                // the count in its own type: a negative one is a huge unsigned one (numeric.h)
                var count = IsSigned(rightKind) && unchecked((long)r.Bits) < 0 ? ulong.MaxValue : r.Bits;
                if (count >= (ulong)bits)
                    throw ComptimeFailure.Panic("LYR-RT0002", $"shift by {unchecked((long)r.Bits)} exceeds the width of {bits} bits");
                var n = (int)count;
                if (op == IrBinKind.Shl) return Value.Uint(Norm(Norm(l.Bits, Unsign(k)) << n, k));
                return Value.Uint(IsSigned(k) ? Norm(unchecked((ulong)(unchecked((long)l.Bits) >> n)), k) : Norm(l.Bits >> n, k));
            }
            case IrBinKind.BitAnd: return Value.Uint(Norm(l.Bits & r.Bits, k));
            case IrBinKind.BitOr: return Value.Uint(Norm(l.Bits | r.Bits, k));
            case IrBinKind.BitXor: return Value.Uint(Norm(l.Bits ^ r.Bits, k));
        }
        return Value.Bool(Compare(op, k, l, r));
    }

    private static IrScalar Unsign(IrScalar k) => k switch
    {
        IrScalar.I8 => IrScalar.U8, IrScalar.I16 => IrScalar.U16, IrScalar.I32 => IrScalar.U32, IrScalar.I64 => IrScalar.U64, _ => k,
    };

    public static bool Compare(IrBinKind op, IrScalar k, Value l, Value r)
    {
        int order;
        if (k == IrScalar.F64 || k == IrScalar.F32)
        {
            double a = k == IrScalar.F64 ? l.AsDouble : l.AsSingle;
            double b = k == IrScalar.F64 ? r.AsDouble : r.AsSingle;
            return op switch
            {
                IrBinKind.Lt => a < b, IrBinKind.Le => a <= b, IrBinKind.Gt => a > b, IrBinKind.Ge => a >= b,
                IrBinKind.Eq => a == b, _ => a != b,
            };
        }
        if (k == IrScalar.String || l.Ref is not null || r.Ref is not null)
        {
            if (op is not (IrBinKind.Eq or IrBinKind.Ne))
                throw new InternalCompilationException($"comptime: '{op}' over references");
            var same = SameValue(l, r);
            return op == IrBinKind.Eq ? same : !same;
        }
        order = IsSigned(k) ? unchecked((long)l.Bits).CompareTo(unchecked((long)r.Bits)) : l.Bits.CompareTo(r.Bits);
        return op switch
        {
            IrBinKind.Lt => order < 0, IrBinKind.Le => order <= 0, IrBinKind.Gt => order > 0, IrBinKind.Ge => order >= 0,
            IrBinKind.Eq => order == 0, _ => order != 0,
        };
    }

    /// <summary><c>==</c> as C writes it: two strings and two views of bytes by their bytes
    /// (<c>lyr_str_eq</c>, <c>LYR_VIEW_EQ</c>), any other reference by identity, a scalar by bits.</summary>
    public static bool SameValue(Value l, Value r)
    {
        if (l.Ref is StrObj a && r.Ref is StrObj b) return a.Bytes.AsSpan().SequenceEqual(b.Bytes);
        if (l.Ref is SliceObj sa && r.Ref is SliceObj sb && Heap_IsBytes(sa) && Heap_IsBytes(sb))
            return Bytes.Of(sa).SequenceEqual(Bytes.Of(sb));
        if (l.Ref is not null || r.Ref is not null) return ReferenceEquals(l.Ref, r.Ref);
        return l.Bits == r.Bits;
    }

    private static bool Heap_IsBytes(SliceObj s) => s.Source is StrObj || s.Source is ArrayObj;

    public static Value Unary(IrUnKind op, IrScalarType type, Value v, bool overflowChecks)
    {
        var k = type.Kind;
        switch (op)
        {
            case IrUnKind.Not: return Value.Bool(!v.AsBool);
            case IrUnKind.BitNot: return Value.Uint(Norm(~v.Bits, k));
        }
        if (k == IrScalar.F64) return Value.Double(-v.AsDouble);
        if (k == IrScalar.F32) return Value.Single(-v.AsSingle);
        var (wrapped, overflows) = Exact(IrBinKind.Sub, k, 0, v.Bits);
        if (overflows && overflowChecks) throw ComptimeFailure.Panic("LYR-RT0002", "arithmetic overflow in '-'");
        return Value.Uint(wrapped);
    }

    /// <summary><c>as</c> and the implicit widening (<c>CEmitter.Conversion</c>).</summary>
    public static Value Convert(IrScalar from, IrScalar to, Value v)
    {
        if (to == IrScalar.Char)
        {
            var c = v.Bits & 0xFFFF_FFFF;
            if (c > 0x10FFFF || (c >= 0xD800 && c <= 0xDFFF))
                throw ComptimeFailure.Panic("LYR-RT0009", $"0x{c:X} is not a Unicode scalar value");
            return Value.Uint(c);
        }
        if (IsFloat(from) && IsInteger(to))
            return Value.Uint(Saturate(from == IrScalar.F64 ? v.AsDouble : v.AsSingle, to));
        if (IsFloat(from) && IsFloat(to))
            return from == to ? v : to == IrScalar.F32 ? Value.Single((float)v.AsDouble) : Value.Double(v.AsSingle);
        if (IsInteger(from) && IsFloat(to))
        {
            if (to == IrScalar.F64)
                return Value.Double(IsSigned(from) ? (double)unchecked((long)v.Bits) : (double)v.Bits);
            return Value.Single(IsSigned(from) ? (float)unchecked((long)v.Bits) : (float)v.Bits);
        }
        // integer to integer, char to uint32, bool to an integer: C's conversion
        return Value.Uint(Norm(v.Bits, to));
    }

    /// <summary>A float to an integer (T1d, Rust's rule; numeric.h's <c>LYR_SATURATE_</c>).</summary>
    private static ulong Saturate(double x, IrScalar to)
    {
        if (double.IsNaN(x)) return 0;
        var (low, high) = to switch
        {
            IrScalar.I8 => (-129.0, 128.0), IrScalar.I16 => (-32769.0, 32768.0),
            IrScalar.I32 => (-2147483649.0, 2147483648.0), IrScalar.I64 => (-9223372036854775808.0, 9223372036854775808.0),
            IrScalar.U8 => (-1.0, 256.0), IrScalar.U16 => (-1.0, 65536.0),
            IrScalar.U32 => (-1.0, 4294967296.0), _ => (-1.0, 18446744073709551616.0),
        };
        var (min, max) = Range(to);
        if (x >= high) return unchecked((ulong)max);
        if (x <= low) return IsUnsigned(to) ? 0UL : Norm(unchecked((ulong)(long)min), to);
        return IsSigned(to) ? Norm(unchecked((ulong)(long)x), to) : Norm((ulong)x, to);
    }
}

/// <summary>The bytes a string, a view or a byte array shows.</summary>
public static class Bytes
{
    public static ReadOnlySpan<byte> Of(StrObj s) => s.Bytes;

    public static byte[] Of(SliceObj s) => s.Source switch
    {
        StrObj str => str.Bytes.AsSpan(s.Offset, s.Length).ToArray(),
        ArrayObj array => [.. array.Items.AsSpan(s.Offset, s.Length).ToArray().Select(v => (byte)v.Bits)],
        AggObj inline => [.. inline.Slots.AsSpan(s.Offset, s.Length).ToArray().Select(v => (byte)v.Bits)],
        _ => throw new InternalCompilationException("comptime: a view of nothing"),
    };

    public static byte[] Of(Value v) => v.Ref switch
    {
        StrObj s => s.Bytes,
        SliceObj s => Of(s),
        ArrayObj a => [.. a.Items.Select(x => (byte)x.Bits)],
        _ => throw new InternalCompilationException($"comptime: no bytes in a {v.Ref?.GetType().Name ?? "scalar"}"),
    };
}
