using Lyric.Core;

namespace Lyric.AST;

// Statements. Every node carries its span. A block is required where the grammar demands one, so a
// branch is never a loose statement.

public abstract record Stmt(Span Span) : Node(Span);

public sealed record Block(Stmt[] Statements, Span Span) : Stmt(Span)
{
    /// <summary>The tail expression of a VALUE block — the last statement, written without a ';',
    /// whose value the block delivers (§6.9). Only a block in value position (a match arm, a block
    /// lambda's body) may carry one; the parser produces it nowhere else.</summary>
    public TailExprStmt? Tail => Statements.Length > 0 ? Statements[^1] as TailExprStmt : null;
}

/// <summary>The tail of a value block: an expression standing last, with no ';'. Never anywhere
/// but as the last statement of a <see cref="Block"/> in value position.</summary>
public sealed record TailExprStmt(Expr Expr, Span Span) : Stmt(Span);

// let (immutable) / var (mutable); type and initializer each optional.
public sealed record BindingStmt(bool IsMutable, string Name, TypeNode? Type, Expr? Initializer, Span Span) : Stmt(Span), INamedDecl
{
    public required Span NameSpan { get; init; }
}

/// <summary>
/// <c>let (a, b) = pair;</c> — binds several names from a tuple.
///
/// <para>Its own statement rather than a variant of <see cref="BindingStmt"/>: there one name
/// stands, here several, and the initializer is required. The difference in type makes the two
/// impossible to confuse.</para>
///
/// <para>There is no element access (<c>t.0</c>); this is the way to the elements.</para>
/// </summary>
public sealed record DestructuringStmt(bool IsMutable, TuplePattern Pattern, TypeNode? Type,
    Expr Initializer, Span Span) : Stmt(Span);

/// <summary>
/// <c>let Pattern = Expr;</c> with any pattern, and <c>let Pattern = Expr else { … };</c> when it
/// can fail. The names the pattern binds live in the block around the statement; the else block
/// runs when the pattern does not match and has to leave (return, throw, break, continue or
/// panic), so afterwards the names are bound on every path (§7.7).
/// </summary>
public sealed record LetPatternStmt(bool IsMutable, Pattern Pattern, TypeNode? Type,
    Expr Initializer, Block? Else, Span Span) : Stmt(Span);

// Else is a block, an IfStmt (else-if) or null.
public sealed record IfStmt(Expr Condition, Block Then, Stmt? Else, Span Span) : Stmt(Span);

// A loop may carry a label ('outer: while (…) { … }'), the target of 'break outer' and
// 'continue outer'. Labels are no symbols: they live beside the value namespace, scoped to the
// body of the loop they name.
public sealed record WhileStmt(Expr Condition, Block Body, Span Span) : Stmt(Span)
{
    public string? Label { get; init; }
    public Span LabelSpan { get; init; }
}
public sealed record DoWhileStmt(Block Body, Expr Condition, Span Span) : Stmt(Span)
{
    public string? Label { get; init; }
    public Span LabelSpan { get; init; }
}
/// <remarks>The loop variable is a declaration of its own; <see cref="INamedDecl.Name"/> is
/// implemented explicitly so the node keeps calling it what it is.</remarks>
public sealed record ForInStmt(string Variable, Expr Iterable, Block Body, Span Span) : Stmt(Span), INamedDecl
{
    public required Span NameSpan { get; init; }
    public string? Label { get; init; }
    public Span LabelSpan { get; init; }

    /// <summary><c>for ((k, v) in …)</c>: an irrefutable pattern over the element instead of a
    /// name. <see cref="Variable"/> is then <c>_</c> — the element still gets a slot, and the
    /// pattern takes it apart at the top of every iteration.</summary>
    public Pattern? Pattern { get; init; }

    string INamedDecl.Name => Variable;
}

// Label == null means the innermost loop.
public sealed record BreakStmt(Span Span) : Stmt(Span)
{
    public string? Label { get; init; }
    public Span LabelSpan { get; init; }
}
public sealed record ContinueStmt(Span Span) : Stmt(Span)
{
    public string? Label { get; init; }
    public Span LabelSpan { get; init; }
}
public sealed record ReturnStmt(Expr? Value, Span Span) : Stmt(Span);
public sealed record YieldStmt(Expr? Value, Span Span) : Stmt(Span);
// resume is an EXPRESSION (ResumeExpr in Expressions.cs); as a statement 'resume co;' runs through
// ExprStmt. Send values ('resume co, v') are post-v1.

// The body is a block or an ExprStmt.
public sealed record DeferStmt(Stmt Body, Span Span) : Stmt(Span);

public sealed record ThrowStmt(Expr Value, Span Span) : Stmt(Span);

public sealed record MatchStmt(Expr Scrutinee, MatchArm[] Arms, Span Span) : Stmt(Span);

public sealed record TryStmt(Block Body, CatchClause[] Catches, Span Span) : Stmt(Span);

// BindingName == null means '_', a catch-all without a binding
// BindingType == null: catch-all with a binding (Throwable); otherwise a typed catch.
/// <remarks>
/// The grammar gives a catch binding exactly one token, <c>_</c> included, so
/// <see cref="INamedDecl.NameSpan"/> covers it in either form and <see cref="INamedDecl.Name"/>
/// reports the text that stands there.
///
/// <para>A <c>_</c> binds nothing and the sema creates no symbol for it, so no symbol's declaration
/// ever points at a clause in that form.</para>
/// </remarks>
public sealed record CatchClause(string? BindingName, TypeNode? BindingType, Block Body, Span Span) : Node(Span), INamedDecl
{
    public required Span NameSpan { get; init; }

    string INamedDecl.Name => BindingName ?? "_";
}

// Only calls and assignments are semantically valid; the parser accepts expressions generically here
// and the sema restricts them.
public sealed record ExprStmt(Expr Expr, Span Span) : Stmt(Span);

public sealed record ErrorStmt(Span Span) : Stmt(Span); // recovery placeholder
