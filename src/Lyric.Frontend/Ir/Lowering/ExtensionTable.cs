using Lyric.AST;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Ir.Lowering;

/// <summary>
/// The USED extension methods of a module.
///
/// <para>Emphasis on used. Registering every method of every <c>extend</c> block is harmless only as
/// long as extensions live in user programs: whoever writes a block usually uses it. With the
/// <c>Display</c> extensions in <c>std.core</c> that tips over — <c>std.core</c> is always loaded, so
/// every program would carry five extension functions and four <c>std.string</c> imports, even a
/// <c>hello.lyr</c> that touches none of them.</para>
///
/// <para>The same rule applies here as for types and imports: ONLY WHAT IS ACTUALLY USED GOES INTO
/// THE BYTECODE. A declared but never called extension belongs in it as little as a declared but never
/// instantiated class.</para>
///
/// <para>WORKLIST RATHER THAN RECURSION, the same reasoning as in <see cref="LambdaTable"/>: the id is
/// assigned at REGISTRATION, so the caller can write its <c>call</c> immediately; lowering happens
/// afterwards, and the body may request further extensions. Recursion would have made the order in the
/// function list depend on the call nesting, and that list is index-bearing in the bytecode.</para>
/// </summary>
internal sealed class ExtensionTable
{
    private readonly record struct Pending(
        FunctionDecl Decl,
        string Name,
        FunctionId Id,
        TypeSymbol? Receiver,
        TypeNode? ReceiverTypeNode);

    private readonly List<Pending> _pending = new();

    /// <summary>Who already has an id. Without this map the same method would get a new one on every
    /// call, and the verifier rejects duplicate function names.</summary>
    private readonly Dictionary<FunctionSymbol, FunctionId> _requested =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>How far the lowering has come. The table is drained SEVERAL times — an extension can
    /// request a lambda, a lambda an extension — and without this mark everything would arise anew on
    /// every pass.</summary>
    private int _lowered;

    private readonly FunctionIds _ids;

    /// <summary>Every registered <c>extend</c> block, for the overload suffix. <c>null</c> only where a
    /// caller builds a table without a compilation; the names are then unsuffixed, which is what they
    /// were before overloaded extensions were told apart at all.</summary>
    private readonly ExtensionRegistry? _registry;

    public ExtensionTable(FunctionIds ids, ExtensionRegistry? registry = null)
    {
        _ids = ids;
        _registry = registry;
    }

    /// <summary>Has this method already been requested? Returns the id under which it is
    /// callable.</summary>
    public bool TryGet(FunctionSymbol symbol, out FunctionId id) =>
        _requested.TryGetValue(symbol, out id);

    /// <summary>
    /// Requests an extension method and returns the id under which it will be callable. Repeated
    /// requests for the same method return the same id.
    /// </summary>
    /// <param name="declaringModule">The module the <c>extend</c> block stands in, not the one of the
    /// target type. <c>extend string</c> may stand in any module.</param>
    /// <param name="target">The extended type. Needed as the SYMBOL rather than as its name, because
    /// the overload suffix is computed over every block that extends it.</param>
    public FunctionId Request(FunctionSymbol symbol, FunctionDecl decl, ModuleSymbol declaringModule,
        TypeSymbol target, TypeSymbol? receiver, TypeNode? receiverTypeNode)
    {
        if (_requested.TryGetValue(symbol, out var existing)) return existing;

        var id = _ids.Next();
        _requested[symbol] = id;
        _pending.Add(new Pending(decl,
            NameMangling.ForExtension(declaringModule, target.Name, decl.Name)
                + OverloadSuffixFor(declaringModule, target, decl),
            id, receiver, receiverTypeNode));
        return id;
    }

    /// <summary>
    /// The suffix that separates an extension method from its overloads, empty when the name is
    /// declared once — which keeps the bytes of every program without overloaded extensions where
    /// they were.
    ///
    /// <para>THE SET SPANS BLOCKS. §4.3a says several visible extensions offering one member are one
    /// overload set, so <c>extend Box { fn tell(n: int) }</c> and a second <c>extend Box</c> with
    /// <c>fn tell(s: string)</c> are two overloads of one name, not two names. Asking the declaring
    /// block's own scope would see one method per block and find nothing to separate — which is how
    /// both landed on <c>main.&lt;extend&gt;.Box.tell</c> and the verifier called it a duplicate.</para>
    ///
    /// <para>Filtered by the DECLARING module, because that module already stands in the name: two
    /// modules extending the same type never collided in the first place.</para>
    /// </summary>
    private string OverloadSuffixFor(ModuleSymbol declaringModule, TypeSymbol target, FunctionDecl decl)
    {
        if (_registry is null) return "";

        var siblings = _registry.MethodsFor(target)
            .Where(m => ReferenceEquals(m.Module, declaringModule) && m.Symbol.Name == decl.Name)
            .ToList();
        if (siblings.Count < 2) return "";

        // The ordinal counts the EARLIER declarations whose written signatures print alike; it is the
        // tie-breaker for the case the written form cannot separate. Same rule as for free functions
        // and methods, and the order is the registration order, which is block then declaration order.
        var mine = NameMangling.OverloadSuffix(decl.Parameters, 0);
        var ordinal = 0;
        foreach (var sibling in siblings)
        {
            if (sibling.Symbol.Declaration is not FunctionDecl other) continue;
            if (ReferenceEquals(other, decl)) break;
            if (NameMangling.OverloadSuffix(other.Parameters, 0) == mine) ordinal++;
        }

        return NameMangling.OverloadSuffix(decl.Parameters, ordinal);
    }

    /// <summary>
    /// Lowers all registered extensions, including the ones that arise while doing so. The loop runs
    /// over an index rather than an enumerator, because <see cref="_pending"/> can grow during the pass.
    /// </summary>
    public List<(FunctionId Id, IrFunction Function)> LowerAll(TypeResult types,
        IReadOnlyDictionary<FunctionSymbol, FunctionId> functions, ImportTable imports,
        TypeTable typeTable, GlobalTable globals, LambdaTable lambdas, InstanceTable instances)
    {
        var lowered = new List<(FunctionId, IrFunction)>();

        for (; _lowered < _pending.Count; _lowered++)
        {
            var p = _pending[_lowered];
            lowered.Add((p.Id, new FunctionLowerer(p.Decl, p.Name, types, functions, imports,
                typeTable, ModuleLowerer.NoSubstitution, globals, lambdas, instances, p.Receiver,
                receiverTypeNode: p.ReceiverTypeNode).Run()));
        }

        return lowered;
    }
}
