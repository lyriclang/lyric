using Lyric.Resolver;
using System.Text;

namespace Lyric.Sema;

/// <summary>Classification, display and convertibility of types.</summary>
public static class TypeFacts
{
    /// <summary>
    /// An integer in the sense of the arithmetic rules. <c>char</c> is NOT one (design/v5/spec/03
    /// T1e): it is a Unicode scalar value, with no arithmetic and no comparison against a number;
    /// the number behind it is reached through <c>as uint32</c> (T1d). Lyric 4 counted it as an
    /// integer so that <c>std.string</c> could ask "is this a digit?" without the host, and paid
    /// for it with a range check on every operation that produced a <c>char</c>.
    ///
    /// <para>The same question is answered a second time in <c>IrVerifier.IsInteger</c>, on
    /// <c>IrType</c> instead of <c>LyrType</c>, because the verifier checks IR without the sema. A
    /// type added here has to be added there too, or the verifier rejects what the sema
    /// allows.</para>
    /// </summary>
    public static bool IsInteger(LyrType t) => t is PrimitiveType p && p.Kind is
        PrimitiveKind.Int or PrimitiveKind.Uint
        or PrimitiveKind.Int8 or PrimitiveKind.Int16 or PrimitiveKind.Int32
        or PrimitiveKind.Uint8 or PrimitiveKind.Uint16 or PrimitiveKind.Uint32;

    public static bool IsChar(LyrType t) => t is PrimitiveType { Kind: PrimitiveKind.Char };

    /// <summary>The width in bits of an integer kind, and whether it is signed. <c>char</c> is
    /// not an integer and has no entry.</summary>
    public static (int Bits, bool Signed)? IntegerShape(PrimitiveKind kind) => kind switch
    {
        PrimitiveKind.Int8 => (8, true),
        PrimitiveKind.Int16 => (16, true),
        PrimitiveKind.Int32 => (32, true),
        PrimitiveKind.Int => (64, true),
        PrimitiveKind.Uint8 => (8, false),
        PrimitiveKind.Uint16 => (16, false),
        PrimitiveKind.Uint32 => (32, false),
        PrimitiveKind.Uint => (64, false),
        _ => null,
    };

    /// <summary>
    /// Whether a value of one scalar type stands implicitly where the other is expected
    /// (design/v5/spec/03 T1c; Zig and Java do the same): an integer widens into every integer
    /// type whose RANGE contains its own — <c>int8→int16→int32→int</c>, <c>uint8→…→uint</c>,
    /// and the unsigned into the next wider signed, <c>uint8→int16</c>, <c>uint32→int</c> — and
    /// <c>float32</c> widens into <c>float</c>. Never a narrowing, never an integer into a float
    /// (not even the lossless cases: an integer stays an integer; C#'s <c>long→double</c> loses
    /// bits above 2⁵³ without a word), never <c>char</c> or <c>bool</c>. The rule holds at
    /// coercion sites only — an assignment, an argument, a return — and never inside the
    /// inference: <c>a + b</c> with an <c>int8</c> and an <c>int</c> is still an error.
    /// </summary>
    public static bool Widens(LyrType from, LyrType to)
    {
        if (from is not PrimitiveType source || to is not PrimitiveType target) return false;
        if (source.Kind == target.Kind) return false;
        if (source.Kind == PrimitiveKind.Float32 && target.Kind == PrimitiveKind.Float) return true;
        if (IntegerShape(source.Kind) is not { } s || IntegerShape(target.Kind) is not { } t) return false;
        if (s.Signed && !t.Signed) return false;              // a sign has nowhere to go
        if (s.Signed == t.Signed) return t.Bits > s.Bits;     // same signedness: strictly wider
        return t.Bits > s.Bits;                                // unsigned into signed: needs a bit for the sign
    }

    public static bool IsFloat(LyrType t) => t is PrimitiveType p && p.Kind is
        PrimitiveKind.Float or PrimitiveKind.Float32;

    public static bool IsNumeric(LyrType t) => IsInteger(t) || IsFloat(t);
    public static bool IsBool(LyrType t) => t is PrimitiveType { Kind: PrimitiveKind.Bool };
    public static bool IsString(LyrType t) => t is PrimitiveType { Kind: PrimitiveKind.String };
    public static bool IsVoid(LyrType t) => t is PrimitiveType { Kind: PrimitiveKind.Void };

    private static readonly Dictionary<string, PrimitiveKind> Builtins = new()
    {
        ["int"] = PrimitiveKind.Int, ["uint"] = PrimitiveKind.Uint, ["float"] = PrimitiveKind.Float,
        // The 64-bit names are aliases (03 T1a): 'int64' IS 'int', and displays as 'int'.
        ["int64"] = PrimitiveKind.Int, ["uint64"] = PrimitiveKind.Uint, ["float64"] = PrimitiveKind.Float,
        ["int8"] = PrimitiveKind.Int8, ["int16"] = PrimitiveKind.Int16, ["int32"] = PrimitiveKind.Int32,
        ["uint8"] = PrimitiveKind.Uint8, ["uint16"] = PrimitiveKind.Uint16, ["uint32"] = PrimitiveKind.Uint32,
        ["float32"] = PrimitiveKind.Float32,
        ["bool"] = PrimitiveKind.Bool, ["char"] = PrimitiveKind.Char, ["string"] = PrimitiveKind.String, ["void"] = PrimitiveKind.Void
    };

    public static LyrType? FromBuiltinName(string name) =>
        name == "never" ? LyrType.Never
        : Builtins.TryGetValue(name, out var kind) ? new PrimitiveType(kind) : null;

    /// <summary>Does an integer literal, possibly negated, fit into the target type?</summary>
    public static bool IntLiteralFits(bool negative, ulong magnitude, PrimitiveKind target) => target switch
    {
        PrimitiveKind.Int8 => negative ? magnitude <= 128 : magnitude <= 127,
        PrimitiveKind.Int16 => negative ? magnitude <= 32768 : magnitude <= 32767,
        PrimitiveKind.Int32 => negative ? magnitude <= 2147483648 : magnitude <= 2147483647,
        PrimitiveKind.Int => negative ? magnitude <= 9223372036854775808 : magnitude <= 9223372036854775807,
        PrimitiveKind.Uint8 => !negative && magnitude <= 255,
        PrimitiveKind.Uint16 => !negative && magnitude <= 65535,
        PrimitiveKind.Uint32 => !negative && magnitude <= 4294967295,
        PrimitiveKind.Uint => !negative,

        // No integer literal is a 'char' (03 T1e): 'let c: char = 65' names a number where a
        // scalar value belongs; the way is '\u{41}', or '65 as char' through uint32.
        _ => false
    };

    /// <summary>
    /// Does an integer literal fit a FLOAT target exactly? "Fits" is exact by the
    /// specification (§3.1): 2⁵³+1 meeting a <c>float</c> is an error, never a silent
    /// rounding. A magnitude is exactly representable when its significant bit-span — top set
    /// bit down to bottom set bit — fits the target's significand (24 bits for float32, 53 for
    /// float64); the exponent range is no concern, 2⁶⁴ sits far inside both.
    /// </summary>
    public static bool IntLiteralExactInFloat(ulong magnitude, PrimitiveKind target)
    {
        if (magnitude == 0) return true;
        var significand = target is PrimitiveKind.Float32 ? 24 : 53;
        var hi = 63 - System.Numerics.BitOperations.LeadingZeroCount(magnitude);
        var lo = System.Numerics.BitOperations.TrailingZeroCount(magnitude);
        return hi - lo + 1 <= significand;
    }

    /// <summary>
    /// The <see cref="TypeSymbol"/> behind a named type; <c>null</c> when there is none — a scalar,
    /// an array, a function type.
    ///
    /// <para>A named type appears in two forms: <see cref="NamedRef"/> for <c>Box</c> and
    /// <see cref="GenericInstance"/> for <c>Box&lt;int&gt;</c>. Nearly every question asked of it —
    /// which kind, which conformance, which field — has the same answer for both, and handling them
    /// separately means forgetting the second one.</para>
    /// </summary>
    public static TypeSymbol? SymbolOf(LyrType type) => type switch
    {
        NamedRef named => named.Symbol,
        GenericInstance instance => instance.Definition,
        _ => null,
    };

    /// <summary>The kind of a named type: class, struct, enum or interface. <c>null</c> when it is
    /// not a named type.</summary>
    public static TypeSymbolKind? KindOf(LyrType type) => SymbolOf(type)?.Kind;

    /// <summary>Is this a named type of that kind? The case most callers need, instances
    /// included.</summary>
    public static bool Is(LyrType type, TypeSymbolKind kind) => KindOf(type) == kind;

    /// <summary>Is this a named type of ONE of these kinds?</summary>
    public static bool IsAny(LyrType type, params TypeSymbolKind[] kinds) =>
        KindOf(type) is { } actual && Array.IndexOf(kinds, actual) >= 0;

    /// <summary>
    /// Does <paramref name="actual"/> fit <paramref name="pattern"/>, a type with parameters in it
    /// (a block's <c>List&lt;T&gt;</c>, 03 T7)? The parameters bind in <paramref name="map"/>, a
    /// parameter met twice has to agree; everything else matches by equality.
    /// </summary>
    public static bool Match(LyrType pattern, LyrType actual, Dictionary<GenericParamSymbol, LyrType> map)
    {
        switch (pattern)
        {
            case TypeParamType tp:
                if (map.TryGetValue(tp.Param, out var bound)) return LyrType.Equal(bound, actual);
                map[tp.Param] = actual;
                return true;
            case GenericInstance pg when actual is GenericInstance ag && ReferenceEquals(pg.Definition, ag.Definition)
                && pg.Arguments.Length == ag.Arguments.Length:
                for (var i = 0; i < pg.Arguments.Length; i++)
                    if (!Match(pg.Arguments[i], ag.Arguments[i], map)) return false;
                return true;
            case ArrayOf pa when actual is ArrayOf aa: return Match(pa.Element, aa.Element, map);
            case SliceOf ps when actual is SliceOf sa: return Match(ps.Element, sa.Element, map);
            case Optional po when actual is Optional ao: return Match(po.Inner, ao.Inner, map);
            case TupleOf pt when actual is TupleOf at && pt.Elements.Length == at.Elements.Length:
                for (var i = 0; i < pt.Elements.Length; i++)
                    if (!Match(pt.Elements[i], at.Elements[i], map)) return false;
                return true;
            default:
                return LyrType.Equal(pattern, actual);
        }
    }

    /// <summary>Could the two meet on one type — a parameter on either side stands for anything
    /// (03 T7 X3, X4)?</summary>
    public static bool Overlaps(LyrType a, LyrType b)
    {
        if (a is TypeParamType || b is TypeParamType) return true;
        if (a is GenericInstance ga && b is GenericInstance gb)
            return ReferenceEquals(ga.Definition, gb.Definition) && ga.Arguments.Length == gb.Arguments.Length
                && ga.Arguments.Zip(gb.Arguments).All(p => Overlaps(p.First, p.Second));
        if (a is ArrayOf xa && b is ArrayOf xb) return Overlaps(xa.Element, xb.Element);
        if (a is Optional oa && b is Optional ob) return Overlaps(oa.Inner, ob.Inner);
        if (a is TupleOf ta && b is TupleOf tb)
            return ta.Elements.Length == tb.Elements.Length && ta.Elements.Zip(tb.Elements).All(p => Overlaps(p.First, p.Second));
        return LyrType.Equal(a, b);
    }

    /// <summary>A coroutine's result type in its display: ', R' where it is not void.</summary>
    private static string ResultText(CoroutineOf co) => IsVoid(co.Result) ? "" : ", " + Display(co.Result);

    /// <summary>A function type's set as written after its return type, in one order whatever the
    /// declaration's (05 E2 K1): the display names an instance, and two orders are one type.</summary>
    private static string ThrownText(LyrType[] thrown) => thrown.Length switch
    {
        0 => "",
        1 => " throws " + Display(thrown[0]),
        _ => " throws [" + string.Join(", ", thrown.Select(Display).Order(StringComparer.Ordinal)) + "]",
    };

    public static string Display(LyrType t)
    {
        switch (t)
        {
            case PrimitiveType p: return p.Kind switch
            {
                PrimitiveKind.Int => "int", PrimitiveKind.Uint => "uint", PrimitiveKind.Float => "float",
                PrimitiveKind.Int8 => "int8", PrimitiveKind.Int16 => "int16", PrimitiveKind.Int32 => "int32",
                PrimitiveKind.Uint8 => "uint8", PrimitiveKind.Uint16 => "uint16", PrimitiveKind.Uint32 => "uint32",
                PrimitiveKind.Float32 => "float32",
                PrimitiveKind.Bool => "bool", PrimitiveKind.Char => "char", PrimitiveKind.String => "string", PrimitiveKind.Void => "void",
                _ => "?"
            };
            case NamedRef n: return n.Symbol.Name;
            case OpaqueRef o: return o.Symbol.Name;
            case TypeParamType tp: return tp.Param.Name;
            case GenericInstance gi:
                var fixations = gi.Fixations is { Length: > 0 } fs ? fs.Select(f => $"{f.Member.Name} = {Display(f.Type)}") : [];
                return gi.Definition.Name + "<" + string.Join(", ", gi.Arguments.Select(Display).Concat(fixations)) + ">"
                       + (gi.Throws is { } thrown
                           ? " throws" + (thrown is NamedRef { Symbol.Name: "Error" } ? "" : " " + Display(thrown))
                           : "");
            case AssocOf a: return Display(a.Base) + "." + a.Member.Name;
            case Optional o: return "?" + Display(o.Inner);
            // A function type as an element type MUST be parenthesized: 'fn(int) -> void[]' would
            // otherwise read as a function returning 'void[]'. Without the parenthesis the sema
            // reported "cannot assign 'fn(int) -> void[]' to '(fn(int) -> void)[]'" — two displays
            // for types that ARE different but looked the same.
            case ArrayOf { Element: FnType } fnArray:
                return $"({Display(fnArray.Element)})[]";
            case ArrayOf a: return Display(a.Element) + "[]";
            case SliceOf s: return "Slice<" + Display(s.Element) + ">";
            case InlineArrayOf ia:
                return (ia.Element is Optional or FnType ? $"({Display(ia.Element)})" : Display(ia.Element)) + $"[{ia.Length}]";
            case TupleOf tu:
                return "(" + string.Join(", ", tu.Elements.Select((e, i) => tu.Labels?[i] is { } l ? l + ": " + Display(e) : Display(e))) + ")";
            case FnType f:
                // A function type returned by one with a set reads parenthesized: the nearest
                // function type takes a 'throws' (03 T17).
                return "fn(" + string.Join(", ", f.Parameters.Select((p, i) => (f.PlaceAt(i) ? "&" : "") + Display(p))) + ") -> "
                       + (f.Return is FnType && f.Throws.Length > 0 ? $"({Display(f.Return)})" : Display(f.Return))
                       + ThrownText(f.Throws);
            case RangeOf r: return "range<" + Display(r.Element) + ">";
            case CoroutineOf { Throws: null } co: return "Coroutine<" + Display(co.Yield) + ResultText(co) + ">";
            case CoroutineOf co:
                return "Coroutine<" + Display(co.Yield) + ResultText(co) + "> throws "
                       + (co.Throws is NamedRef { Symbol.Name: "Error" } ? "" : Display(co.Throws!));
            case NullType: return "null";
            case NeverType: return "never";
            case ErrorType: return "<error>";
            default: return "<?>";
        }
    }
}
