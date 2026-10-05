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
        if (a is SliceOf sa && b is SliceOf sb) return Overlaps(sa.Element, sb.Element);
        if (a is InlineArrayOf ia && b is InlineArrayOf ib) return ia.Length == ib.Length && Overlaps(ia.Element, ib.Element);
        if (a is Optional oa && b is Optional ob) return Overlaps(oa.Inner, ob.Inner);
        if (a is TupleOf ta && b is TupleOf tb)
            return ta.Elements.Length == tb.Elements.Length && ta.Elements.Zip(tb.Elements).All(p => Overlaps(p.First, p.Second));
        return LyrType.Equal(a, b);
    }

    /// <summary>Would this type, written bare before a suffix — '[]', '[3]' — take the suffix
    /// for itself? An optional, a function type, and whatever ends in a set.</summary>
    private static bool TakesTheSuffix(LyrType type) => type is Optional or FnType || EndsInASet(type);

    /// <summary>Does the type's text end in a thrown set — its own, or, a '?' in front, that of
    /// what it is the optional of?</summary>
    private static bool EndsInASet(LyrType type) => type switch
    {
        Optional optional => EndsInASet(optional.Inner),
        FnType { Throws.Length: > 0 } or GenericInstance { Throws: not null } or CoroutineOf { Throws: not null } => true,
        _ => false,
    };

    /// <summary>Could a 'throws' written behind this type be the type's own (03 T17, 10 §2): a
    /// function type, a coroutine, std.task's task — under any '?'.</summary>
    private static bool CouldTakeASet(LyrType type) => type switch
    {
        Optional optional => CouldTakeASet(optional.Inner),
        FnType or CoroutineOf => true,
        GenericInstance { Definition: { Name: "Task", Home.FullName: "std.task" } } => true,
        _ => false,
    };

    /// <summary>A coroutine's result type in its display: ', R' where it is not void.</summary>
    private static string ResultText(CoroutineOf co, Func<TypeSymbol, string> name) =>
        IsVoid(co.Result) ? "" : ", " + Render(co.Result, name);

    /// <summary>A function type's set as written after its return type, in one order whatever the
    /// declaration's (05 E2 K1): the display names an instance, and two orders are one type.</summary>
    private static string ThrownText(LyrType[] thrown, Func<TypeSymbol, string> name) => thrown.Length switch
    {
        0 => "",
        1 => " throws " + Render(thrown[0], name),
        _ => " throws [" + string.Join(", ", thrown.Select(t => Render(t, name)).Order(StringComparer.Ordinal)) + "]",
    };

    /// <summary>A type as the diagnostics write it: a named type by its name.</summary>
    public static string Display(LyrType t) => Render(t, PlainName);

    /// <summary>A type with <paramref name="name"/> writing each named type in it — the lowering's
    /// key for an instance, where two modules declaring one name must not make one instance of two
    /// types.</summary>
    public static string Display(LyrType t, Func<TypeSymbol, string> name) => Render(t, name);

    private static readonly Func<TypeSymbol, string> PlainName = symbol => symbol.Name;

    private static string Render(LyrType t, Func<TypeSymbol, string> name)
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
            case NamedRef n: return name(n.Symbol);
            case OpaqueRef o: return name(o.Symbol);
            case TypeParamType tp: return tp.Param.Name;
            case GenericInstance gi:
                var fixations = gi.Fixations is { Length: > 0 } fs ? fs.Select(f => $"{f.Member.Name} = {Render(f.Type, name)}") : [];
                return name(gi.Definition) + "<" + string.Join(", ", gi.Arguments.Select(a => Render(a, name)).Concat(fixations)) + ">"
                       + (gi.Throws is { } thrown
                           ? " throws" + (thrown is NamedRef { Symbol.Name: "Error" } ? "" : " " + Render(thrown, name))
                           : "");
            case AssocOf a: return Render(a.Base, name) + "." + a.Member.Name;
            case Optional o: return "?" + Render(o.Inner, name);
            // An element type that would take the suffix for itself MUST be parenthesized, as the
            // source writes it: 'fn(int) -> void[]' reads as a function returning 'void[]',
            // '?int[]' as the optional of an array, 'Task<int> throws E[]' as a task that throws
            // an array. Without the parentheses two types that ARE different looked the same —
            // "cannot assign '?int[]' to '?int[]'" — and were the same to the lowering, whose key
            // for an instance this text is: 'Box<(?int)[]>' and 'Box<?int[]>' were one instance.
            case ArrayOf a:
                return (TakesTheSuffix(a.Element) ? $"({Render(a.Element, name)})" : Render(a.Element, name)) + "[]";
            case SliceOf s: return "Slice<" + Render(s.Element, name) + ">";
            case InlineArrayOf ia:
                return (TakesTheSuffix(ia.Element) ? $"({Render(ia.Element, name)})" : Render(ia.Element, name)) + $"[{ia.Length}]";
            case TupleOf tu:
                return "(" + string.Join(", ", tu.Elements.Select((e, i) => tu.Labels?[i] is { } l ? l + ": " + Render(e, name) : Render(e, name))) + ")";
            case FnType f:
                // The return type in the parentheses that say whose a set is (03 T17; the review's
                // M5-7, M6-6), as the source writes them: around one that ends in a set of its
                // own, 'fn() -> (Task<int> throws E)', and around one that could have taken the
                // set behind it, 'fn() -> (Task<int>) throws E'. Without them the two were one text.
                return "fn(" + string.Join(", ", f.Parameters.Select((p, i) => (f.PlaceAt(i) ? "&" : "") + Render(p, name))) + ") -> "
                       + (EndsInASet(f.Return) || (f.Throws.Length > 0 && CouldTakeASet(f.Return))
                           ? $"({Render(f.Return, name)})" : Render(f.Return, name))
                       + ThrownText(f.Throws, name);
            case RangeOf r: return "range<" + Render(r.Element, name) + ">";
            case CoroutineOf { Throws: null } co: return "Coroutine<" + Render(co.Yield, name) + ResultText(co, name) + ">";
            case CoroutineOf co:
                return "Coroutine<" + Render(co.Yield, name) + ResultText(co, name) + "> throws"
                       + (co.Throws is NamedRef { Symbol.Name: "Error" } ? "" : " " + Render(co.Throws!, name));
            case NullType: return "null";
            case NeverType: return "never";
            case ErrorType: return "<error>";
            default: return "<?>";
        }
    }
}
