using Lyric.Core;

namespace Lyric.AST;

// Type expressions.
// Built-in type names have no node of their own: the lexer tokenizes them as identifiers and they
// are represented as a NamedType with a single-element path. Whether a name is a built-in or a
// user type is decided by the sema.
public abstract record TypeNode(Span Span) : Node(Span);

public sealed record NullableType(TypeNode Inner, Span Span) : TypeNode(Span);                         // ?T
/// <remarks><c>a.b.C&lt;...&gt;</c>. <see cref="NameSpan"/> is the LAST path segment — the name
/// the type's symbol answers for; the segments before it qualify. INVALID (default) on the nodes
/// <see cref="Lyric.Resolver.BuiltinTypes"/> synthesizes, which stand in no text.</remarks>
public sealed record NamedType(string[] Path, TypeNode[] TypeArguments, Span Span) : TypeNode(Span)
{
    public required Span NameSpan { get; init; }
}
public sealed record ArrayType(TypeNode Element, Span Span) : TypeNode(Span);    // T[]; the length belongs to the value, not the type

/// <remarks><c>Coroutine&lt;int&gt; throws Exception</c> — throwability as part of the TYPE, so it
/// survives a field, an optional and a parameter. <see cref="Thrown"/> is null for the typeless
/// form (<c>throws</c> alone), which stands for any Throwable; the absence of this node means the
/// coroutine cannot throw. Only a coroutine type may carry it (<c>LYR-SEM0084</c>): everything else
/// runs at its call, where the function's own clause already says so.</remarks>
public sealed record ThrowingType(TypeNode Inner, TypeNode? Thrown, Span Span) : TypeNode(Span);
public sealed record TupleType(TypeNode[] Elements, Span Span) : TypeNode(Span);                       // (A, B[, C])
public sealed record FunctionType(TypeNode[] Parameters, TypeNode ReturnType, Span Span) : TypeNode(Span); // fn(A, B) -> R

// Recovery placeholder, set when ParseType cannot continue, so later stages do not meet a null.
// The counterpart of ErrorExpr.
public sealed record ErrorType(Span Span) : TypeNode(Span);
