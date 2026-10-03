using Lyric.Core;

namespace Lyric.AST;

// Module structure and declarations. Enum keeps variants and methods apart, because they are
// structurally different.

public sealed record Module(ModulePath? Header, Decl[] Declarations, Span Span) : Node(Span)
{
    /// <summary>Attributes written before the module header. A file without a header cannot carry
    /// them: at the top of such a file an attribute belongs to the first declaration.</summary>
    public AttributeNode[] Attributes { get; init; } = [];
}
public sealed record ModulePath(string[] Segments, Span Span) : Node(Span);

/// <summary>
/// An attribute before a declaration or the module header: <c>@Name</c> or
/// <c>@Name { field = literal, … }</c>.
///
/// <para>The path names a struct type and the fields reuse the initializer shape, because the
/// checking is the same — an attribute IS a struct, and where it may sit is the marker interface
/// it declares (<c>OnModule</c>, <c>OnType</c>, <c>OnFunction</c>).</para>
/// </summary>
public sealed record AttributeNode(string[] Path, StructInitField[] Fields, Span Span) : Node(Span)
{
    /// <summary>The span of the <c>@Name</c> path alone, without the argument block.</summary>
    public required Span PathSpan { get; init; }

    /// <summary>The LAST path segment without the <c>@</c> — the name of the struct the attribute
    /// refers to, which is what an editor renaming that struct has to edit.</summary>
    public required Span NameSpan { get; init; }

    /// <summary>The parenthesized argument — <c>@On(Event.Damage)</c> — or <c>null</c>. It fills
    /// the attribute's first field; which attributes admit the form is the checker's rule
    /// (<c>WithArg&lt;T&gt;</c>). Grammatically an attribute carries either this or
    /// <see cref="Fields"/>, never both.</summary>
    public Expr? Positional { get; init; }
}

public abstract record Decl(Span Span) : Node(Span);

/// <summary>
/// The visibility word written before a declaration (design/v5/spec/07 V2, 08 D3): <c>pub</c> —
/// exported —, <c>internal</c> — the package's —, <c>private</c> — the module's —, or none. What
/// none means is the reader's: Lyric 5 reads it as <c>internal</c>, the 4.x tools as the module's.
/// </summary>
public enum VisibilityWord
{
    None,
    Private,
    Internal,
    Pub,
}

// --- imports ---
public sealed record ImportDecl(string[] Path, ImportClause? Clause, Span Span) : Decl(Span)
{
    /// <summary><c>pub import</c> (design/v5/spec/07 V3 I4): what it binds the module passes on —
    /// a re-export. Without the word an import is its module's own.</summary>
    public bool IsPublic { get; init; }
}
public abstract record ImportClause(Span Span) : Node(Span);
/// <remarks><c>import a.b { x, y }</c>. <see cref="NameSpans"/> is parallel to
/// <see cref="Names"/>: the clause is the one place an imported name stands that no use-site table
/// records, and an editor renaming the target has to edit exactly it.</remarks>
public sealed record ImportSelective(string[] Names, Span Span) : ImportClause(Span)
{
    public required Span[] NameSpans { get; init; }

    /// <summary><c>{ f as g }</c> (design/v5/spec/07 V3 I2): the name each item is bound under,
    /// by index — <c>null</c> where it keeps its own, and <c>null</c> as a whole when none is
    /// renamed.</summary>
    public string?[]? Renames { get; init; }

    /// <summary>The name item <paramref name="i"/> is bound under in the importing module.</summary>
    public string BoundName(int i) => Renames is { } renames && i < renames.Length && renames[i] is { } alias ? alias : Names[i];
}
public sealed record ImportAlias(string Alias, Span Span) : ImportClause(Span);       // import a.b as C

// --- generics ---
public sealed record GenericParam(string Name, TypeNode[] Constraints, Span Span) : Node(Span), INamedDecl // T, or T :: [I1, I2]
{
    public required Span NameSpan { get; init; }

    /// <summary><c>Rhs = Self</c> (design/v5/spec/03 T18): the argument the parameter takes when a
    /// use writes none; <c>null</c> when it must be written.</summary>
    public TypeNode? Default { get; init; }
}

// --- functions and members ---
public sealed record Param(bool IsParams, string Name, TypeNode Type, Expr? Default, Span Span) : Node(Span), INamedDecl
{
    public required Span NameSpan { get; init; }

    /// <summary>What sits on the parameter: <c>@callerExpr(actual)</c> (design/v5/spec/09 A11) —
    /// the one attribute a parameter takes.</summary>
    public AttributeNode[] Attributes { get; init; } = [];

    /// <summary><c>&amp;x: T</c> (design/v5/spec/03 T12): the parameter takes a place of the
    /// caller, which the function reads and writes, not a value.</summary>
    public bool IsPlace { get; init; }
}
/// <summary><c>throws E</c>, <c>throws [A, B]</c>, or bare <c>throws</c> (design/v5/spec/05 E2 K1,
/// K2; 08 D9): a SET of thrown types, written in brackets from two on (the list rule, 08 D5/D6).
/// <see cref="Types"/> empty is the bare form, which means <c>throws Error</c>.</summary>
public sealed record ThrowsClause(TypeNode[] Types, Span Span) : Node(Span);

/// <param name="IsStatic">A member without a receiver: no <c>this</c>, reachable only through
/// the type. Always <c>false</c> at top level.</param>
public sealed record FunctionDecl(
    VisibilityWord Visibility, bool IsMut, bool IsStatic, string Name, GenericParam[] Generics, Param[] Parameters,
    TypeNode? ReturnType, ThrowsClause? Throws, Block? Body, Span Span) : Decl(Span), INamedDecl // Body == null means abstract or declared with ';'
{
    public bool IsPublic => Visibility == VisibilityWord.Pub;

    /// <summary>On an interface member: <c>private fn</c> with a body (design/v5/spec/07 V2 S4) —
    /// a helper the interface's defaults call. No requirement, no slot, and no conformer answers
    /// or overrides it.</summary>
    public bool IsPrivateHelper => Visibility == VisibilityWord.Private && Body is not null;

    public required Span NameSpan { get; init; }

    /// <summary>Set on a top-level function, and (since 2.1) on a method of a struct, class,
    /// enum or extend block — where the sema admits only the row-less <c>@Deprecated</c>.
    /// Interface members stay attribute-free: the parser rejects the list there.</summary>
    public AttributeNode[] Attributes { get; init; } = [];

    /// <summary>Set on an <c>extern "abi" fn</c> declaration: a bodyless function whose
    /// implementation the runtime binds outside the program. <c>null</c> for every ordinary
    /// function.</summary>
    public ExternSpec? Extern { get; init; }
}

/// <summary>
/// The foreign side of an <c>extern</c> declaration: which ABI binds it and under which symbol.
/// </summary>
/// <param name="Abi">The ABI string — <c>"dotnet"</c> is the one this compiler knows.</param>
/// <param name="Symbol">The symbol named after <c>=</c>, or <c>null</c> when the function's own
/// name is the symbol.</param>
public sealed record ExternSpec(string Abi, string? Symbol, Span Span) : Node(Span)
{
    /// <summary>The ABI string literal as written, for the formatter.</summary>
    public required Span AbiSpan { get; init; }

    /// <summary>The symbol string literal as written, or <c>null</c> when none was.</summary>
    public Span? SymbolSpan { get; init; }
}

/// <summary>A <c>static let</c> constant in the body of a struct or class, reachable as
/// <c>Type.NAME</c>; syntactically the same binding as a module <c>let</c>.</summary>
/// <remarks>The name is the wrapped binding's. A symbol declares from THIS node rather than from the
/// binding inside it, so the two spans have to be reachable from here as well.</remarks>
public sealed record StaticBindingDecl(VisibilityWord Visibility, BindingStmt Binding, Span Span) : Decl(Span), INamedDecl
{
    public bool IsPublic => Visibility == VisibilityWord.Pub;

    public string Name => Binding.Name;

    public Span NameSpan => Binding.NameSpan;

    /// <summary>Since 2.1; the sema admits only <c>@Deprecated</c> on a member.</summary>
    public AttributeNode[] Attributes { get; init; } = [];
}

public sealed record FieldDecl(string Name, TypeNode Type, Expr? Default, Span Span) : Decl(Span), INamedDecl
{
    public required Span NameSpan { get; init; }

    /// <summary>Since 2.1; the sema admits only <c>@Deprecated</c> on a member.</summary>
    public AttributeNode[] Attributes { get; init; } = [];

    /// <summary>Declared <c>var name: T</c> (design/v5/spec/02 M2): the field may be written,
    /// through a root that may be written. Without the word a field is fixed once its value is
    /// built — for a struct and for a class alike (M9).</summary>
    public bool IsVar { get; init; }

    /// <summary><c>pub x: int</c>, <c>private var y: int</c> (design/v5/spec/07 V2 S0): a field
    /// follows the rule of every member. A variant's fields have none of their own.</summary>
    public VisibilityWord Visibility { get; init; }
}

// --- type declarations ---
public sealed record StructDecl(VisibilityWord Visibility, string Name, GenericParam[] Generics, TypeNode[] Interfaces, Decl[] Members, Span Span) : Decl(Span), INamedDecl
{
    public bool IsPublic => Visibility == VisibilityWord.Pub;

    /// <summary><c>:: [Walker by legs]</c> (design/v5/spec/04 D1): the field each entry of
    /// <c>Interfaces</c> delegates to, by index, <c>null</c> where the entry is conformed in the
    /// ordinary way — and <c>null</c> as a whole when no entry delegates.</summary>
    public string?[]? Delegates { get; init; }

    public required Span NameSpan { get; init; }

    public AttributeNode[] Attributes { get; init; } = [];
}

public sealed record ClassDecl(VisibilityWord Visibility, string Name, GenericParam[] Generics, TypeNode[] Interfaces, Decl[] Members, Span Span) : Decl(Span), INamedDecl
{
    public bool IsPublic => Visibility == VisibilityWord.Pub;

    /// <summary><c>:: [Walker by legs]</c> (design/v5/spec/04 D1): the field each entry of
    /// <c>Interfaces</c> delegates to, by index, <c>null</c> where the entry is conformed in the
    /// ordinary way — and <c>null</c> as a whole when no entry delegates.</summary>
    public string?[]? Delegates { get; init; }

    public required Span NameSpan { get; init; }

    public AttributeNode[] Attributes { get; init; } = [];
}

public sealed record EnumDecl(VisibilityWord Visibility, string Name, GenericParam[] Generics, TypeNode[] Interfaces, EnumVariant[] Variants, FunctionDecl[] Methods, Span Span) : Decl(Span), INamedDecl
{
    public bool IsPublic => Visibility == VisibilityWord.Pub;

    public required Span NameSpan { get; init; }

    /// <summary>The associated types the enum binds for its conformances (03 T6).</summary>
    public AssociatedTypeDecl[] Types { get; init; } = [];

    public AttributeNode[] Attributes { get; init; } = [];
}

public sealed record EnumVariant(string Name, TypeNode[]? TupleFields, FieldDecl[]? StructFields, Span Span) : Node(Span), INamedDecl // both null means a unit variant
{
    public required Span NameSpan { get; init; }
}

public sealed record InterfaceDecl(VisibilityWord Visibility, string Name, GenericParam[] Generics, TypeNode[] Interfaces, FunctionDecl[] Members, Span Span) : Decl(Span), INamedDecl
{
    public bool IsPublic => Visibility == VisibilityWord.Pub;

    public required Span NameSpan { get; init; }

    /// <summary>The associated types the interface declares, <c>type Item;</c> (design/v5/spec/03 T6).</summary>
    public AssociatedTypeDecl[] Types { get; init; } = [];

    /// <summary><c>sealed interface Shape</c> (04 D8): every conformer stands in the declaring
    /// module, and a match of type patterns over it is exhaustive without <c>_</c>.</summary>
    public bool IsSealed { get; init; }

    /// <summary>The constants it declares, <c>static let zero: Self;</c> (design/v5/spec/03 T5):
    /// every conformer answers them with a <c>static let</c> of its own.</summary>
    public StaticBindingDecl[] Statics { get; init; } = [];
}

public sealed record ExtendDecl(VisibilityWord Visibility, TypeNode Target, TypeNode[] Interfaces, FunctionDecl[] Methods, Span Span) : Decl(Span)
{
    public bool IsPublic => Visibility == VisibilityWord.Pub;

    /// <summary>The associated types a conformance block binds, <c>type Item = int;</c> (03 T6).</summary>
    public AssociatedTypeDecl[] Types { get; init; } = [];

    /// <summary><c>extend&lt;T :: [Display]&gt; List&lt;T&gt; { … }</c> (03 T7 X1): the block's own type
    /// parameters, bound by the receiver at every use.</summary>
    public GenericParam[] Generics { get; init; } = [];

    /// <summary>The constants the block adds to its type, <c>static let answer: int = 42;</c> — or
    /// answers for its interfaces (05 §6, §7).</summary>
    public StaticBindingDecl[] Statics { get; init; } = [];
}

/// <summary>
/// An associated type (design/v5/spec/03 T6): in an interface <c>type Item;</c> declares one,
/// <c>type Item = Self;</c> with a default; in a struct, class, enum or conformance block
/// <c>type Item = int;</c> binds it for that conformer. <see cref="Type"/> is the default or
/// the binding, <c>null</c> for a bare declaration.
/// </summary>
public sealed record AssociatedTypeDecl(string Name, TypeNode? Type, Span Span) : Decl(Span), INamedDecl
{
    public required Span NameSpan { get; init; }

    /// <summary>What every answer conforms to, in an interface's declaration —
    /// <c>type Iter :: [Iterator];</c> (10 B6) —, as a type parameter's constraints are written.</summary>
    public TypeNode[] Bounds { get; init; } = [];
}

// --- global bindings and type aliases ---
/// <inheritdoc cref="StaticBindingDecl"/>
public sealed record GlobalBindingDecl(VisibilityWord Visibility, BindingStmt Binding, Span Span) : Decl(Span), INamedDecl // 'let' or 'var' (07 V5 G5)
{
    public bool IsPublic => Visibility == VisibilityWord.Pub;

    public string Name => Binding.Name;

    public Span NameSpan => Binding.NameSpan;
}

public sealed record TypeAliasDecl(VisibilityWord Visibility, bool IsOpaque, string Name, TypeNode Aliased, Span Span) : Decl(Span), INamedDecl
{
    public bool IsPublic => Visibility == VisibilityWord.Pub;

    public required Span NameSpan { get; init; }
}

public sealed record ErrorDecl(Span Span) : Decl(Span); // recovery placeholder
