using Lyric.Core;

namespace Lyric.AST;

// Patterns and match arms.
//
// Bind versus unit variant: a bare identifier (`x`, `Empty`) parses as a BindingPattern. Whether
// it is a binding or a unit variant is something the parser cannot know without a namespace; the
// sema decides. A qualified path (`Shape.Circle`) or a path with `(…)`/`{…}` is always a
// VariantPattern.

public abstract record Pattern(Span Span) : Node(Span);

public sealed record WildcardPattern(Span Span) : Pattern(Span);                        // _
public sealed record LiteralPattern(Expr Literal, Span Span) : Pattern(Span);           // 42, "x", true, null, 'c', -1
public sealed record BindingPattern(string Name, Span Span) : Pattern(Span);            // x: a binding OR a unit variant, decided by the sema
/// <summary><c>c: Circle</c>, <c>_: Circle</c> (03 T11): the scrutinee, an interface value, holds a
/// value of that type; <see cref="Name"/> binds it as that type, <c>null</c> for <c>_</c>.</summary>
public sealed record TypePattern(string? Name, TypeNode Type, Span Span) : Pattern(Span), INamedDecl
{
    public required Span NameSpan { get; init; }

    string INamedDecl.Name => Name ?? "_";
}
/// <summary><c>s in [Circle, Rect]</c>, <c>_ in [A, B]</c>, <c>s in Circle</c> (08 Y6): the scrutinee,
/// an interface value, holds a value of one of the types — the form of <c>catch (e in [A, B])</c>.
/// <see cref="Name"/> binds it as the one type of a set of one, as the scrutinee's type otherwise,
/// carrying the set (K7); <c>null</c> for <c>_</c>.</summary>
public sealed record TypeSetPattern(string? Name, TypeNode[] Types, Span Span) : Pattern(Span), INamedDecl
{
    public required Span NameSpan { get; init; }

    string INamedDecl.Name => Name ?? "_";
}
public sealed record VariantPattern(string[] Path, Pattern[]? TupleElements, FieldPattern[]? StructFields, Span Span) : Pattern(Span)
{
    /// <summary><c>.Red</c>, <c>.Num(v)</c>, <c>.Rect { w = 0 }</c>: the variant of the
    /// scrutinee's enum, the enum unnamed (08 Y6). The path is the one variant name.</summary>
    public bool IsImplicit { get; init; }
}
public sealed record TuplePattern(Pattern[] Elements, Span Span) : Pattern(Span);       // (a, b)
public sealed record RangePattern(Expr Low, Expr High, bool IsInclusive, Span Span) : Pattern(Span); // 0..=9
public sealed record OrPattern(Pattern[] Alternatives, Span Span) : Pattern(Span);      // a | b | c

/// <summary><c>[a, b]</c>, <c>[first, ..]</c>, <c>[.., last]</c>, <c>[a, ..rest, z]</c>: the
/// elements of an array, with at most one <see cref="RestPattern"/> among them. Without a rest
/// the pattern tests the length exactly; with one it tests the minimum.</summary>
public sealed record ArrayPattern(Pattern[] Elements, Span Span) : Pattern(Span);

/// <summary><c>..</c> or <c>..name</c> inside an array pattern: the elements the fixed
/// positions do not name. A name binds them as an array of their own; without one the span of
/// the name is empty, as it is for every declaration the source does not write.</summary>
public sealed record RestPattern(string? Name, Span Span) : Pattern(Span), INamedDecl
{
    public required Span NameSpan { get; init; }

    string INamedDecl.Name => Name ?? "_";
}
public sealed record FieldPattern(string Name, Pattern? Pattern, Span Span) : Node(Span); // x, or x = Pattern
public sealed record ErrorPattern(Span Span) : Pattern(Span);

// MatchArm = Pattern [ 'if' Guard ] '=>' ( Expr | Block ).
public sealed record MatchArm(Pattern Pattern, Expr? Guard, Node Body, Span Span) : Node(Span);
