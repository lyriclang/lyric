using Lyric.AST;

namespace Lyric.Resolver;

/// <summary>
/// Registers `extend` blocks. Extension methods are NOT merged into the target type's member
/// table, which could not express import-bound visibility; they are collected here, as in C#'s
/// extension method model. The type checker consults the registry during member lookup and
/// filters by visibility: the declaring module is the current one or imported by it.
/// </summary>
public sealed class ExtensionRegistry
{
    private readonly List<ExtensionBlock> _blocks = new();
    private readonly Dictionary<TypeSymbol, List<ExtensionMethod>> _byTarget = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<ExtensionBlock> Blocks => _blocks;

    public void Add(ExtensionBlock block) => _blocks.Add(block);

    /// <summary>Takes a block back as if never declared: an implicit synthesis (04 D7 "Debug
    /// bei Bedarf") whose body does not check. The index is rebuilt, so no lookup finds it.</summary>
    public void Withdraw(ExtensionBlock block)
    {
        _blocks.Remove(block);
        BuildIndex();
    }

    /// <summary>Builds the lookup index after target resolution.</summary>
    public void BuildIndex()
    {
        _byTarget.Clear();
        _blockOf.Clear();
        foreach (var b in _blocks)
        {
            foreach (var m in b.Methods) _blockOf[m] = b;
            if (b.Target is null) continue;
            if (!_byTarget.TryGetValue(b.Target, out var list))
                _byTarget[b.Target] = list = new();
            foreach (var m in b.Methods)
                list.Add(new ExtensionMethod(m, b.Module, b));
        }
    }

    /// <summary>All extension methods registered for <paramref name="target"/>
    /// (unfiltered; the caller checks visibility).</summary>
    public IReadOnlyList<ExtensionMethod> MethodsFor(TypeSymbol target) =>
        _byTarget.TryGetValue(target, out var list) ? list : [];

    private readonly Dictionary<Symbol, ExtensionBlock> _blockOf = new(ReferenceEqualityComparer.Instance);

    /// <summary>The block a method was declared in.</summary>
    public ExtensionBlock? BlockOf(Symbol method) => _blockOf.TryGetValue(method, out var block) ? block : null;
}

/// <summary>An `extend` block with its method symbols and its declaring module.
/// <see cref="Target"/> is set in pass 3 (null when unresolvable).</summary>
public sealed class ExtensionBlock
{
    public ExtendDecl Decl { get; }
    public ModuleSymbol Module { get; }
    public SymbolTable MethodScope { get; } // the FunctionSymbols of the extend methods, for the body check and cross-calls
    public FunctionSymbol[] Methods { get; }
    public TypeSymbol? Target { get; set; }

    /// <summary>The block's own type parameters (03 T7 X1), declared in <see cref="MethodScope"/>.</summary>
    public GenericParamSymbol[] Generics { get; init; } = [];

    /// <summary>The target as a type, <c>List&lt;T&gt;</c> with the block's own parameters; resolved
    /// by the sema on first use. <c>null</c> until then and for a plain target.</summary>
    public Sema.LyrType? TargetType { get; set; }

    /// <summary>The target is a built-in constructor — <c>T[]</c>, <c>?T</c>, a tuple (03 T7 X2):
    /// no symbol of its own, matched by shape.</summary>
    public bool IsConstructorTarget { get; set; }

    /// <summary>The block's parameters a fixation of another's constraint names (05 §13 rule 2):
    /// <c>T</c> in <c>extend&lt;I :: [Iterator&lt;Item = T&gt;], T :: [Num]&gt; I</c>, the receiver's
    /// answer for <c>Item</c> once <c>I</c> is bound. Set by the checker.</summary>
    public (GenericParamSymbol Bound, GenericParamSymbol From, AssociatedTypeSymbol Member)[]? FixationBindings { get; set; }

    /// <summary>The target is the block's own parameter, <c>extend&lt;T :: [I]&gt; T</c> (04 D15): a
    /// blanket block, matched by its constraints alone.</summary>
    public bool IsBlanketTarget { get; set; }

    /// <summary>Synthesized unasked — the <c>Debug</c> every type gets where it can (04 D7). The
    /// sema checks it muted and withdraws it where the fields give no rendering.</summary>
    public bool IsImplicit { get; init; }

    public ExtensionBlock(ExtendDecl decl, ModuleSymbol module, SymbolTable methodScope, FunctionSymbol[] methods)
    {
        Decl = decl;
        Module = module;
        MethodScope = methodScope;
        Methods = methods;
    }
}

/// <param name="Block">The block that declares it: a conformance block (<c>extend T :: [I]</c>)
/// scopes its members to the conformance (design/v5/spec/04 D3), an inherent one does not.</param>
public readonly record struct ExtensionMethod(FunctionSymbol Symbol, ModuleSymbol Module, ExtensionBlock Block)
{
    public bool InConformanceBlock => Block.Decl.Interfaces.Length > 0;
}
