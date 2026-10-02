using Lyric.Resolver;

namespace Lyric.Sema;

// Semantic types, separate from the syntactic AST TypeNodes. The names differ deliberately
// (NamedRef, Optional, ArrayOf, …), because the TypeChecker uses both namespaces. Equality is
// structural through LyrType.Equal, NOT through record ==, under which arrays would compare by
// reference.

/// <summary>
/// The scalar kinds. <c>int</c>, <c>uint</c> and <c>float</c> are 64 bits wide, and the names
/// <c>int64</c>, <c>uint64</c> and <c>float64</c> are ALIASES of them (design/v5/spec/03 T1a):
/// one type with two names, the C# form, so a generic instantiates once for both, the FFI maps
/// them once and no <c>as</c> is needed between types of the same width. Lyric 4 kept them
/// distinct, buying a 32-bit portability the language does not need.
/// </summary>
public enum PrimitiveKind
{
    Int, Uint, Float,
    Int8, Int16, Int32,
    Uint8, Uint16, Uint32,
    Float32,
    Bool, Char, String, Void
}

public abstract record LyrType
{
    public static readonly LyrType Error = new ErrorType();
    public static readonly LyrType Null = new NullType();
    public static readonly LyrType Never = new NeverType();
    public static readonly LyrType Bool = new PrimitiveType(PrimitiveKind.Bool);
    public static readonly LyrType Int = new PrimitiveType(PrimitiveKind.Int);
    public static readonly LyrType Float = new PrimitiveType(PrimitiveKind.Float);
    public static readonly LyrType Char = new PrimitiveType(PrimitiveKind.Char);
    public static readonly LyrType String = new PrimitiveType(PrimitiveKind.String);
    public static readonly LyrType Void = new PrimitiveType(PrimitiveKind.Void);

    /// <summary>Structural type equality.</summary>
    public static bool Equal(LyrType a, LyrType b) => (a, b) switch
    {
        (PrimitiveType x, PrimitiveType y) => x.Kind == y.Kind,
        (NamedRef x, NamedRef y) => ReferenceEquals(x.Symbol, y.Symbol),
        // By SYMBOL, deliberately not by underlying: two opaque aliases of int are two types,
        // and an opaque alias never equals its underlying — that is the point of it.
        (OpaqueRef x, OpaqueRef y) => ReferenceEquals(x.Symbol, y.Symbol),
        (TypeParamType x, TypeParamType y) => ReferenceEquals(x.Param, y.Param),
        (GenericInstance x, GenericInstance y) => ReferenceEquals(x.Definition, y.Definition) && SameSequence(x.Arguments, y.Arguments)
                                                  && SameFixations(x.Fixations, y.Fixations)
                                                  && (x.Throws is null) == (y.Throws is null)
                                                  && (x.Throws is null || Equal(x.Throws, y.Throws!)),
        (AssocOf x, AssocOf y) => Equal(x.Base, y.Base) && ReferenceEquals(x.Member, y.Member),
        (Optional x, Optional y) => Equal(x.Inner, y.Inner),
        (ArrayOf x, ArrayOf y) => Equal(x.Element, y.Element),
        (SliceOf x, SliceOf y) => Equal(x.Element, y.Element),
        (InlineArrayOf x, InlineArrayOf y) => x.Length == y.Length && Equal(x.Element, y.Element),
        (TupleOf x, TupleOf y) => SameSequence(x.Elements, y.Elements),
        // The thrown set counts (03 T17): 'fn() -> int' and 'fn() -> int throws E' are two types —
        // a set, in any order (05 E2 K1).
        (FnType x, FnType y) => Equal(x.Return, y.Return) && SameSequence(x.Parameters, y.Parameters)
                                && SameSet(x.Throws, y.Throws),
        (RangeOf x, RangeOf y) => Equal(x.Element, y.Element),
        // Throwability counts: 'Coroutine<int>' and 'Coroutine<int> throws E' are two types, or
        // the second would pass for the first and the demand would be lost again at the binding.
        (CoroutineOf x, CoroutineOf y) => Equal(x.Yield, y.Yield) && Equal(x.Result, y.Result)
                                          && (x.Throws is null) == (y.Throws is null)
                                          && (x.Throws is null || Equal(x.Throws, y.Throws!)),
        (ErrorType, ErrorType) => true,
        (NullType, NullType) => true,
        (NeverType, NeverType) => true,
        _ => false
    };

    private static bool SameFixations((AssociatedTypeSymbol Member, LyrType Type)[]? a, (AssociatedTypeSymbol Member, LyrType Type)[]? b)
    {
        if (a is null || a.Length == 0) return b is null || b.Length == 0;
        if (b is null || a.Length != b.Length) return false;
        foreach (var (member, type) in a)
        {
            var other = Array.FindIndex(b, f => ReferenceEquals(f.Member, member));
            if (other < 0 || !Equal(type, b[other].Type)) return false;
        }
        return true;
    }

    /// <summary>Two thrown sets name the same types, in any order. A set names each type once
    /// (05 E2 K1), so the lengths and one containment decide.</summary>
    private static bool SameSet(LyrType[] a, LyrType[] b) =>
        a.Length == b.Length && a.All(x => b.Any(y => Equal(x, y)));

    private static bool SameSequence(LyrType[] a, LyrType[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
            if (!Equal(a[i], b[i])) return false;
        return true;
    }

    public bool IsError => this is ErrorType;
}

public sealed record PrimitiveType(PrimitiveKind Kind) : LyrType;
public sealed record NamedRef(TypeSymbol Symbol) : LyrType;          // a struct, class, enum or interface instance, non-generic

/// <summary>An <c>opaque type</c> alias (v1.15): its IDENTITY is the symbol, its layout the
/// underlying type. The sema compares by symbol — nothing converts implicitly, only an explicit
/// <c>as</c> crosses — while the lowering sees only <see cref="Underlying"/>: at runtime an
/// opaque value IS its underlying value, which is what lets it cross the native boundary.</summary>
public sealed record OpaqueRef(TypeSymbol Symbol, LyrType Underlying) : LyrType;
public sealed record TypeParamType(GenericParamSymbol Param) : LyrType; // T inside a generic definition
public sealed record GenericInstance(TypeSymbol Definition, LyrType[] Arguments) : LyrType // Stack<int>
{
    /// <summary><c>Iterator&lt;Item = int&gt;</c> (03 T6): the associated types this instance of an
    /// interface fixes, <c>null</c> when none. Part of the type's identity.</summary>
    public (AssociatedTypeSymbol Member, LyrType Type)[]? Fixations { get; init; }

    /// <summary>A task's error (design/v5/spec/10 §2): <c>Task&lt;T&gt; throws E</c> — what its body
    /// may end with, a thing of the type as on a coroutine (05 E10); <c>null</c> where it throws
    /// nothing. std.task's <c>Task</c> alone carries one. Part of the type's identity.</summary>
    public LyrType? Throws { get; init; }
}

/// <summary><c>T.Item</c> (03 T6): the associated type <see cref="Member"/> of whatever
/// <see cref="Base"/> turns out to be — a type parameter until its binding is known, then the
/// conformer's answer. <see cref="Base"/> is a type parameter or <c>Self</c>; a concrete base
/// resolves to its binding and never stays here.</summary>
public sealed record AssocOf(LyrType Base, AssociatedTypeSymbol Member) : LyrType;
public sealed record Optional(LyrType Inner) : LyrType;              // ?T
public sealed record ArrayOf(LyrType Element) : LyrType;             // T[]
public sealed record SliceOf(LyrType Element) : LyrType;             // Slice<T>: a view of T[] (03 T13 A2)
public sealed record InlineArrayOf(LyrType Element, int Length) : LyrType; // T[N]: N elements inline, a value (03 T13 A4)
/// <summary>A tuple; the labels, if any, name elements for <c>.x</c> and are no part of the
/// type's identity (03 T16): <c>(x: int, y: int)</c> and <c>(int, int)</c> are one type.</summary>
public sealed record TupleOf(LyrType[] Elements) : LyrType
{
    public string?[]? Labels { get; init; }
}
public sealed record FnType(LyrType[] Parameters, LyrType Return) : LyrType
{
    /// <summary>What a call of this function may throw (design/v5/spec/05 E2): the declared set,
    /// substituted with the function's instance (K5); empty when it throws nothing. Part of the
    /// type's identity as a set (03 T17); a value with a smaller set coerces to a type with a
    /// larger one (K6), never the reverse.</summary>
    public LyrType[] Throws { get; init; } = [];
}
public sealed record RangeOf(LyrType Element) : LyrType;             // the internal type of 0..9, not a spec type
/// <param name="Throws">What a PULL of this coroutine may throw: null when it cannot, the
/// <c>Error</c> of <c>std.core</c> for a typeless <c>throws</c>, otherwise the declared type.
///
/// <para>Part of the TYPE since 3.0, and it has to be: the call of a coroutine function runs no
/// body and cannot throw, so a check at the call is a check at the wrong event. Riding on the
/// local instead — which is what it did until 3.0 — meant the demand vanished at the first field
/// or optional, and a coroutine held in a field is the idiom this exists for.</para></param>
public sealed record CoroutineOf(LyrType Yield, LyrType? Throws = null) : LyrType
{
    /// <summary>What the body returns when it ends (06 A1): <c>Coroutine&lt;Y, R&gt;</c>, read by
    /// <c>result()</c>; <c>void</c> for <c>Coroutine&lt;Y&gt;</c>.</summary>
    public LyrType Result { get; init; } = LyrType.Void;
}
/// <summary>
/// The recovery sentinel. It means "a diagnostic has already been reported here" — not "unknown",
/// not "not computed yet", not "do not care".
///
/// <para>The invariant behind it: whoever sees an <c>ErrorType</c> stays silent, so one error does
/// not turn into an avalanche of follow-ups. Whoever PRODUCES one must therefore have reported
/// first.</para>
///
/// <para><c>Lyric.Tests.Sema.ErrorTypeInvariantTests</c> checks it mechanically: if an
/// <c>ErrorType</c> appears anywhere in a program, a diagnostic has to be present.</para>
/// </summary>
public sealed record ErrorType : LyrType;

/// <summary>
/// The expression NAMES something — a type, a module — that is not a value.
///
/// <para>Distinct from <see cref="ErrorType"/>: <c>Error</c> means "a diagnostic has already been
/// reported here", and every consumer stays silent on it. Mixing the two turns "I do not know what
/// this is" into a silent pass, under which <c>P(1,2,3).nonsense</c> would check out completely
/// without anything being reported.</para>
///
/// <para>Legal at exactly one place: as the TARGET OF A MEMBER ACCESS (<c>Point.new(…)</c>,
/// <c>console.println(…)</c>). Everywhere else <c>TypeChecker.CheckExpr</c> reports
/// <c>LYR-SEM0052</c> and degrades to <see cref="ErrorType"/>, from where the ordinary poison rule
/// applies again.</para>
/// </summary>
/// <param name="Instance">The resolved instance in <c>Pair&lt;int&gt;.of(3)</c>. Without it the
/// member yields its type with <c>T</c> still in it, and the error arrives as "cannot assign 'int'
/// to 'T'" one level too late. <c>null</c> for every non-generic type and every module.</param>
public sealed record NonValueType(Symbol Symbol, string Kind, GenericInstance? Instance = null) : LyrType;
public sealed record NullType : LyrType;                            // the type of the null literal, assignable only to ?T
public sealed record NeverType : LyrType;                           // the return type of panic; a bottom type, not nameable
