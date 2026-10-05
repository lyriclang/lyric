using Lyric.Core;

namespace Lyric.AST;

// Expressions. Every node carries its own span, the union of its children's spans, so
// diagnostics and later stages point precisely at the source.

public enum IntSuffix
{
    I8, I16, I32, I64, U8, U16, U32, U64
}

public enum FloatSuffix
{
    F32, F64
}

public abstract record Expr(Span Span) : Node(Span);

// --- literals ---
public sealed record IntLiteralExpr(ulong Value, IntSuffix? Suffix, Span Span) : Expr(Span);
public sealed record FloatLiteralExpr(double Value, FloatSuffix? Suffix, Span Span) : Expr(Span);
public sealed record StringLiteralExpr(string Value, Span Span) : Expr(Span);
public sealed record CharLiteralExpr(int CodePoint, Span Span) : Expr(Span);
public sealed record BoolLiteralExpr(bool Value, Span Span) : Expr(Span);
public sealed record NullLiteralExpr(Span Span) : Expr(Span);

// --- names ---
public sealed record IdentifierExpr(string Name, Span Span) : Expr(Span);

/// <summary><c>.Red</c>: a member of the type the position expects, the type unnamed
/// (design/v5/spec/03 T9, 08 Y9). A unit variant as it stands; the callee of <c>.Num(3)</c>;
/// what the sema binds it to says which. Without an expected enum type it is an error.</summary>
public sealed record ImplicitMemberExpr(string Name, Span Span) : Expr(Span);
public sealed record AtIdentifierExpr(string Name, Expr[]? Arguments, Span Span) : Expr(Span); // Name INCLUDING the leading '@' (for example "@test"); Arguments == null when none were written
public sealed record ThisExpr(Span Span) : Expr(Span);

// --- operators ---
public sealed record UnaryExpr(UnaryOp Operator, Expr Operand, Span Span) : Expr(Span);
/// <summary>
/// <c>comptime e</c>: the value of <c>e</c>, computed by the compiler. Semantically the same
/// value <c>e</c> has at run time — the prefix says WHEN it is computed and what <c>e</c> may
/// therefore reach: no local, no parameter, no <c>this</c>, no capability. The lowering replaces
/// the expression by the literal the evaluator produced.
/// </summary>
public sealed record ComptimeExpr(Expr Inner, Span Span) : Expr(Span);
// 'throw e' in expression position: the type never (§6.9, §9.4). 'throw e;' at statement start stays a
// ThrowStmt — one form per position, and the statement form has always been the one the flow rules name.
public sealed record ThrowExpr(Expr Value, Span Span) : Expr(Span);
/// <summary>Which member of the <c>try</c> family a <see cref="TryExpr"/> is (design/v5/spec/05
/// E4; 08 Y4).</summary>
public enum TryKind
{
    /// <summary><c>try e</c>: an error goes on — to a clause of this expression, to a <c>catch</c>
    /// around it, or out of the function.</summary>
    Propagate,

    /// <summary><c>try? e</c>: an error becomes <c>null</c>. The value is <c>?T</c>, never
    /// flattened — a <c>?int</c> operand makes a <c>??int</c>, so "failed" stays apart from "gave
    /// null".</summary>
    Optional,

    /// <summary><c>try! e</c>: an error is a panic (05 E8, <c>LYR-RT0010</c>).</summary>
    Force,
}

/// <summary><c>try e</c> (design/v5/spec/05 E1, E4; 08 Y4): marks every throw site in <c>e</c> —
/// the whole expression to its right (<c>try a + b</c> is <c>try (a + b)</c>) — as propagated to the
/// enclosing function, or to a <c>catch</c> around it. The value is <c>e</c>'s. <c>try?</c> and
/// <c>try!</c> take every error themselves (<see cref="Kind"/>); <c>try e catch (x: A) v</c> takes
/// what its clauses cover and is worth the taking clause's value then.</summary>
public sealed record TryExpr(Expr Value, Span Span) : Expr(Span)
{
    /// <summary>
    /// Set on a mark that stands to the RIGHT of an operator, <c>a + try b() + c()</c> (the
    /// review's M5-9). The mark changes nothing about how the expression is grouped — it is a
    /// prefix of its operand, <see cref="Value"/> — and it covers every throw site from the
    /// keyword to the end of the expression it stands in: <c>b()</c> and <c>c()</c>, not what
    /// stands left of it. <c>null</c> for a mark at the start of its expression, whose Value IS
    /// what it covers.
    /// </summary>
    public TryReach? Reach { get; init; }

    /// <summary>The keyword alone — with its <c>?</c> or <c>!</c> — where a diagnostic about the
    /// mark itself points.</summary>
    public required Span KeywordSpan { get; init; }

    public TryKind Kind { get; init; }

    /// <summary>The clauses of the expression form, in order; empty for a mark. A clause body is a
    /// value block: <c>catch (e) v</c> is <c>catch (e) { v }</c> (<see cref="CatchClause.ExpressionBody"/>).</summary>
    public CatchClause[] Catches { get; init; } = [];
}
/// <summary>How far a mark to the right of an operator covers: the expression it stands in, from
/// its start to its end. The parser fills it in when it has read that expression to its end —
/// the mark's node is built before that.</summary>
public sealed class TryReach
{
    /// <summary>Where the expression the mark stands in begins: what stands between here and the
    /// keyword is LEFT of the mark, and not covered.</summary>
    public int From { get; set; }

    /// <summary>Where that expression ends: the mark covers up to here.</summary>
    public int End { get; set; }
}

public sealed record PostfixExpr(Expr Operand, PostfixOp Operator, Span Span) : Expr(Span);
public sealed record BinaryExpr(Expr Left, BinaryOp Operator, Expr Right, Span Span) : Expr(Span);
public sealed record AssignExpr(Expr Target, BinaryOp? Operator, Expr Value, Span Span) : Expr(Span); // Operator == null means '='; otherwise a compound assignment
public sealed record RangeExpr(Expr Low, Expr High, bool IsInclusive, Span Span) : Expr(Span);
/// <summary>The range inside <c>[…]</c> that takes a view (design/v5/spec/03 T13 A2): <c>a..b</c>,
/// <c>a..=b</c>, <c>..b</c>, <c>a..</c>, <c>..</c> — either bound may be left open.</summary>
public sealed record SliceRangeExpr(Expr? Low, Expr? High, bool IsInclusive, Span Span) : Expr(Span);
public sealed record CastExpr(Expr Operand, TypeNode Type, Span Span) : Expr(Span);

/// <summary><c>x is Circle</c> (design/v5/spec/03 T11): does the interface value hold a value
/// of that type? In the branch the test guards, the name is that type (smart cast).</summary>
public sealed record TypeTestExpr(Expr Operand, TypeNode Type, Span Span) : Expr(Span);

// --- nodes produced by postfix ---
/// <param name="TypeArguments">Explicitly written type arguments: <c>f&lt;int&gt;()</c>. Empty
/// when none were written; the sema then infers them from the arguments. They are needed where
/// the arguments give nothing: a factory <c>empty&lt;T&gt;(): List&lt;T&gt;</c> has none.</param>
public sealed record CallExpr(Expr Callee, Expr[] Arguments, Span Span,
    TypeNode[]? TypeArguments = null) : Expr(Span)
{
    /// <summary>The name an argument was written with — <c>connect(host: "h")</c>, design/v5/spec/04
    /// D5 — by argument index, <c>null</c> where the argument is positional; <c>null</c> as a whole
    /// when no argument is named, which is nearly every call.</summary>
    public string?[]? ArgumentNames { get; init; }

    /// <summary>The arguments written in parentheses of their own, <c>f((a + b))</c>, each with
    /// the span that holds its parentheses: the argument's node is the tree inside them, and
    /// <c>@callerExpr</c> hands on the text the call wrote (09 A11). <c>null</c> where none is,
    /// which is nearly every call.</summary>
    public (Expr Argument, Span Written)[]? Parenthesized { get; init; }

    /// <summary>The arguments written with <c>...</c> behind them, <c>sum(base, xs...)</c>
    /// (design/v5/spec/08; the review's A2), each with where its dots stand: an array spread
    /// over the variadic parameter — the parameter IS the array — where without the dots the
    /// array would be one element. A call spreads one argument, the rest; the parser notes the
    /// dots wherever they stand, and the checker says where they may not (LYR-SEM0166).
    /// <c>null</c> where the call spreads nothing, which is nearly every call.</summary>
    public (Expr Argument, Span Dots)[]? Spreads { get; init; }
}
public sealed record IndexExpr(Expr Target, Expr Index, Span Span) : Expr(Span);
/// <remarks>IsOptional means '?.' rather than '.'.</remarks>
public sealed record MemberExpr(Expr Target, string Member, bool IsOptional, Span Span) : Expr(Span)
{
    /// <summary>Where the member name alone stands — what a consumer that must edit exactly the
    /// name reads, where <see cref="Node.Span"/> covers the whole access. INVALID (default) on a
    /// node the sema synthesized: an operator use carries no member name in the text, so there is
    /// nothing to edit.</summary>
    public required Span MemberSpan { get; init; }
}

// --- composite literals ---
public sealed record ArrayLitExpr(Expr[] Elements, Span Span) : Expr(Span);
public sealed record TupleLitExpr(Expr[] Elements, Span Span) : Expr(Span);

// --- f-strings ---
public sealed record InterpolatedStringExpr(InterpSegment[] Segments, Span Span) : Expr(Span);
public abstract record InterpSegment(Span Span) : Node(Span);
public sealed record InterpText(string Text, Span Span) : InterpSegment(Span);                     // raw text, escapes NOT resolved
public sealed record InterpHole(Expr Expr, string? FormatSpec, Span Span) : InterpSegment(Span);   // {expr} and {expr:spec}

// --- lambdas ---
public sealed record LambdaExpr(LambdaParam[] Parameters, TypeNode? ReturnType, Node Body, Span Span) : Expr(Span) // Body is an Expr or a Block
{
    /// <summary>How the lambda was written: with a parenthesized parameter list, as a bare
    /// <c>x =&gt; …</c>, or as a trailing block <c>f { … }</c> whose single parameter is the
    /// implicit <c>it</c>. The formatter prints the form back; the meaning is the same.</summary>
    public LambdaForm Form { get; init; } = LambdaForm.Parenthesized;

    /// <summary>The set the parenthesized form writes after its return type,
    /// <c>(s: string): int throws ParseError =&gt; …</c> (design/v5/spec/08 Y11 F7); null where the
    /// lambda's set comes from its position or from its body (05 E2 K3).</summary>
    public ThrowsClause? Throws { get; init; }

    /// <summary>Whether the return type is written in parentheses — as on a declaration
    /// (<see cref="FunctionDecl.ReturnGrouped"/>).</summary>
    public bool ReturnGrouped { get; init; }
}

public enum LambdaForm { Parenthesized, Bare, Trailing }

/// <summary>A lambda parameter: a name, or an irrefutable pattern (<c>((k, v)) =&gt; …</c>), in
/// which case <see cref="Name"/> is <c>_</c> and the pattern binds the names. A trailing lambda's
/// implicit <c>it</c> is a parameter with <see cref="Implicit"/> set; the checker drops it when
/// the expected function type takes nothing.</summary>

/// <summary><c>let Pattern = Expr</c> as the condition of an <c>if</c> or a <c>while</c>: true
/// when the pattern matches, and the names it binds are in scope in the branch or the body.
/// Only there — anywhere else it is <c>LYR-SEM0098</c>.</summary>
public sealed record LetCondExpr(Pattern Pattern, Expr Initializer, Span Span) : Expr(Span);
public sealed record LambdaParam(string Name, TypeNode? Type, Span Span) : Node(Span), INamedDecl
{
    public required Span NameSpan { get; init; }
    public Pattern? Pattern { get; init; }
    public bool Implicit { get; init; }
}

// --- control flow as an expression ---
// IfExpr branches are EXPRESSIONS: 'if (c) a else b', or 'if (c) { …; a } else { …; b }' with a
// value block as a branch (a BlockExpr). The else is mandatory; 'else if' is a nested IfExpr. An
// 'if' whose branches are statement blocks, with or without an else, is an IfStmt — unless it
// stands last in a value block, where it is the block's value (08 Y4, the tail rule).
public sealed record IfExpr(Expr Condition, Expr Then, Expr Else, Span Span) : Expr(Span);
public sealed record MatchExpr(Expr Scrutinee, MatchArm[] Arms, Span Span) : Expr(Span);

/// <summary>
/// A value block where an expression is wanted (08 Y4): a branch of an <c>if</c> expression,
/// <c>if (c) { …; a } else { …; b }</c>, and the right of <c>??</c>, <c>x ?? { return 0; }</c>.
/// It is worth its tail; without one it gives no value — it leaves, or it is worth nothing.
///
/// <para>NOT a general block expression: the parser makes one in those two places and nowhere
/// else. A brace anywhere else in an expression is an initializer's or a trailing lambda's, and
/// a third meaning there would have to be told from the two by what the braces hold.</para>
/// </summary>
public sealed record BlockExpr(Block Block, Span Span) : Expr(Span);

/// <summary><c>loop { … }</c> (design/v5/spec/05 E11, 08 S3/S4): the block again and again, left by
/// a <c>break</c> — <c>break value</c> gives the loop its value — or by nothing at all, the loop
/// then of type <c>never</c>. At the start of a statement it is one, with no <c>;</c> after its
/// block; elsewhere an expression, <c>let found = loop { …; break x; };</c>. Labeled like the other
/// loops, in either place: <c>let pair = outer: loop { … };</c>.</summary>
public sealed record LoopExpr(Block Body, Span Span) : Expr(Span)
{
    public string? Label { get; init; }
    public Span LabelSpan { get; init; }
}

// --- struct initializers: TypePath '{' field = expr, … '}' ---
// Recognised in value position only, not at the start of an ExprStmt, where it would be ambiguous
// with a block. The field separator is '='; ':' is reserved for types.
public sealed record StructInitExpr(string[] Path, TypeNode[] TypeArguments, StructInitField[] Fields, Span Span) : Expr(Span)
{
    /// <summary><c>.Rect { w = 1 }</c>: the path is the one variant name, and the enum is the
    /// type the position expects (08 Y9).</summary>
    public bool IsImplicit { get; init; }

    /// <summary>The span of the LAST path segment — the name the initializer's symbol answers for.
    /// The segments before it qualify; only this one is the type's own name.</summary>
    public required Span NameSpan { get; init; }
}

// --- a type path in value position: 'Pair<int>.of(3)' ---
//
// The non-generic case does not need this node: 'P.neu()' is an IdentifierExpr whose symbol is a
// type, and CheckMember works through the symbol anyway. Only type arguments carry something an
// identifier cannot express.
//
// It always stands as the target of a MemberExpr; alone it is a type rather than a value, and
// CheckExpr reports it there as LYR-SEM0052, like any other type name.
public sealed record TypePathExpr(string[] Path, TypeNode[] TypeArguments, Span Span) : Expr(Span)
{
    /// <summary>The span of the LAST path segment, as on <see cref="StructInitExpr.NameSpan"/>.
    /// </summary>
    public required Span NameSpan { get; init; }
}

/// <summary><c>p with { x = 3, pos.y = 4 }</c> (design/v5/spec/02 M6): a copy of the struct
/// <c>p</c> with the named fields replaced, <c>p</c> untouched; a path reaches into a struct
/// held by value. The values see the old <c>p</c>.</summary>
public sealed record WithExpr(Expr Target, WithField[] Fields, Span Span) : Expr(Span);
public sealed record WithField(string[] Path, Expr Value, Span Span) : Node(Span);

public sealed record StructInitField(string Name, Expr Value, Span Span) : Node(Span)
{
    /// <summary>Where the field name alone stands; <see cref="Node.Span"/> covers
    /// <c>name = value</c>.</summary>
    public required Span NameSpan { get; init; }
}

// --- recovery ---
public sealed record ErrorExpr(Span Span) : Expr(Span);
