using Lyric.AST;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Ir.Lowering;

/// <summary>
/// The MONOMORPHIZED INSTANCES of generic functions.
///
/// <para>One <see cref="IrFunction"/> per concrete type argument tuple: <c>id&lt;int&gt;</c> and
/// <c>id&lt;string&gt;</c> are two functions, not one with a type parameter. The IR therefore stays
/// FULLY MONOMORPHIC — the verifier, the bytecode format and the VM learn nothing about generics, they
/// only see more functions.</para>
///
/// <para>MONOMORPHIC RATHER THAN GENERIC AT RUNTIME. C# reifies generics and needs a JIT that produces
/// code per instantiation; Java erases them and pays with boxing at every boundary. Both assume the
/// runtime knows types, and a Lyric value carries no type tag. Rust and C++ do the same for the same
/// reason.</para>
///
/// <para>The price is code duplication per instantiation. It is visible and bounded: one instance per
/// type tuple actually used, not per possible one.</para>
/// </summary>
internal sealed class InstanceTable
{
    /// <summary>A requested instance that has not been lowered yet.</summary>
    private readonly record struct Pending(
        FunctionDecl Decl, string Name, FunctionId Id, TypeSymbol? Receiver,
        IReadOnlyDictionary<GenericParamSymbol, LyrType> Substitution,
        GenericInstance? Owner = null, LyrType? ReceiverType = null);

    private readonly List<Pending> _pending = new();

    /// <summary>
    /// How many instances one module may produce before the lowering calls it a runaway.
    ///
    /// <para>Monomorphization terminates only when the set of instantiations is finite, and a
    /// program can ask for one that is not: a method returning a type built from its own — an
    /// interface method answering <c>Iterator&lt;T[]&gt;</c> — demands the instance for the next
    /// element type, and that one for the one after. Without a cap the compiler does not fail, it
    /// simply never finishes, which is the worst of the three outcomes.</para>
    ///
    /// <para>The number is far above anything a real module reaches; what it buys is a message
    /// with a name in it instead of a process to kill.</para>
    /// </summary>
    private const int MaxInstances = 20_000;

    /// <summary>What has already been requested, so two calls of <c>id(7)</c> get the same instance
    /// rather than producing two identical functions.</summary>
    private readonly Dictionary<string, FunctionId> _byKey = new(StringComparer.Ordinal);

    /// <summary>
    /// The module a generic declaration stands in, so its instances carry the module path the rest
    /// of the IR carries (<see cref="NameMangling.ForFunction"/>).
    ///
    /// <para>The name IS the key here, so an UNQUALIFIED one is not a cosmetic matter: two modules
    /// declaring <c>fn twice&lt;T&gt;</c> both ask for <c>twice&lt;int&gt;</c>, the second request
    /// finds the first one's id, and the call lands in the other module's body. The verifier cannot
    /// see it, because the table deduplicated before any function was built.</para>
    ///
    /// <para>Built once from the compilation — the same walk pass 1 makes — and indexed by the AST
    /// node, which is the one identity a symbol, an instance definition and a declaration all
    /// agree on.</para>
    /// </summary>
    private Dictionary<Node, string>? _declaringModules;

    private string Qualify(Node? declaration, string name)
    {
        if (declaration is null) return name;
        _declaringModules ??= BuildModuleIndex(_compilation);
        return _declaringModules.TryGetValue(declaration, out var module) ? $"{module}.{name}" : name;
    }

    /// <summary>Every declaration that can carry generics, with the module it stands in: the
    /// top-level ones, and the members of a type, because a generic type's method is requested
    /// through the type's declaration.</summary>
    private static Dictionary<Node, string> BuildModuleIndex(Compilation? compilation)
    {
        var index = new Dictionary<Node, string>(ReferenceEqualityComparer.Instance);
        if (compilation is null) return index;

        foreach (var module in compilation.Modules)
            foreach (var decl in compilation.AstOf(module).Declarations)
            {
                index[decl] = module.FullName;
                IEnumerable<Decl>? members = decl switch
                {
                    StructDecl s => s.Members,
                    ClassDecl c => c.Members,
                    EnumDecl e => e.Methods,
                    InterfaceDecl i => i.Members,
                    ExtendDecl x => x.Methods,
                    _ => null,
                };
                if (members is null) continue;
                foreach (var member in members) index[member] = module.FullName;
            }

        return index;
    }

    /// <summary>How far the lowering has come. The table is drained SEVERAL times — an instance can
    /// request a lambda, a lambda an instance — and without this mark everything would arise anew on
    /// every pass.</summary>
    private int _lowered;

    private readonly FunctionIds _ids;
    private readonly Compilation? _compilation;

    public InstanceTable(FunctionIds ids, Compilation? compilation = null)
    {
        _ids = ids;
        _compilation = compilation;
    }

    public bool IsEmpty => _pending.Count == 0;

    /// <summary>A type as an instance's name writes it: by the type, not by its name alone.</summary>
    private string NameOf(LyrType type) => InstanceNames.Of(type, _compilation);

    /// <summary>
    /// Requests an instance and returns its id: a new one the first time, the same one afterwards.
    ///
    /// <para>The id is settled immediately although the body is not lowered yet. That is what makes
    /// recursion possible: <c>fn depth&lt;T&gt;(n: int): int { return depth&lt;T&gt;(n - 1); }</c>
    /// requests itself and finds its own id already there.</para>
    /// </summary>
    /// <param name="owner">The generic instance the method belongs to, when it has one:
    /// <c>Iterator&lt;int&gt;.map&lt;string&gt;</c> takes its <c>T</c> from the interface instance
    /// and its <c>U</c> from the call, and needs BOTH bound to lower a body that mentions each.
    /// </param>
    public FunctionId Request(FunctionSymbol symbol, FunctionDecl decl, string baseName,
        TypeSymbol? receiver, IReadOnlyList<LyrType> typeArguments, TypeTable typeTable,
        Core.Span span, GenericInstance? owner = null)
    {
        if (symbol.Generics.Length != typeArguments.Count)
            throw new UnsupportedConstructException(
                $"call to '{baseName}' supplies {typeArguments.Count} type argument(s), "
                + $"but it declares {symbol.Generics.Length}", span);

        // The name IS the key: it carries the module path, the type arguments and, for a method, the
        // owning instance, is therefore unique, and a human can read off a disassembly which instance
        // is in front of them.
        var name = owner is { } owning
            ? Qualify(owning.Definition.Declaration,
                  $"{owning.Definition.Name}<{string.Join(", ", owning.Arguments.Select(NameOf))}>")
              + $".{symbol.Name}<{string.Join(", ", typeArguments.Select(NameOf))}>"
            : Qualify(decl, baseName)
              + $"<{string.Join(", ", typeArguments.Select(NameOf))}>";
        if (_byKey.TryGetValue(name, out var existing)) return existing;

        // A type parameter still open means the inference did not get through at the call site, and then
        // there is no instance that could be built.
        for (var i = 0; i < typeArguments.Count; i++)
            if (typeArguments[i] is TypeParamType or Sema.ErrorType)
                throw new UnsupportedConstructException(
                    $"call to '{baseName}': type argument {i} is not concrete "
                    + $"('{TypeFacts.Display(typeArguments[i])}')", span);

        var substitution = new Dictionary<GenericParamSymbol, LyrType>(
            ReferenceEqualityComparer.Instance);

        // The OWNER's parameters first, so a body may mention both: in 'Iterator<int>.map<string>'
        // the T comes from the instance and the U from the call. A method's own parameter wins a
        // name collision, which is the scoping the source has.
        if (owner is { } instance)
        {
            for (var i = 0; i < instance.Definition.Generics.Length && i < instance.Arguments.Length; i++)
                substitution[instance.Definition.Generics[i]] = instance.Arguments[i];
            // An interface's member for a value of the interface: 'Self' is the interface (04 D9).
            if (instance.Definition.SelfParam is { } self) substitution[self] = instance;
        }
        // The same for a non-generic interface, which has no owner instance: a helper or a generic
        // member reached through its value.
        if (receiver is { Kind: TypeSymbolKind.Interface, SelfParam: { } ownSelf } && !substitution.ContainsKey(ownSelf))
            substitution[ownSelf] = new NamedRef(receiver);

        for (var i = 0; i < symbol.Generics.Length; i++)
            substitution[symbol.Generics[i]] = typeArguments[i];

        Guard(name, span);

        var id = _ids.Next();
        _byKey[name] = id;
        _pending.Add(new Pending(decl, name, id, receiver, substitution, owner));
        return id;
    }

    /// <summary>Stops a monomorphization that does not terminate, and names the instance it was
    /// asked for when it gave up — that name is the shape of the recursion.</summary>
    private void Guard(string name, Core.Span span)
    {
        if (_pending.Count < MaxInstances) return;

        throw new UnsupportedConstructException(
            $"the monomorphization does not terminate: {_pending.Count} instances and still "
            + $"asking for '{name}'. A method whose result type is built from its own element "
            + "type demands an instance for the next one, and so on without end — write it as a "
            + "free function, which is instantiated per use rather than per instance", span);
    }

    /// <summary>
    /// Requests a METHOD OF A TYPE INSTANCE: <c>Box&lt;int&gt;.get</c>.
    ///
    /// <para>The substitution comes from the type rather than from the call — <c>get()</c> has no type
    /// parameters of its own, its <c>T</c> is that of <c>Box</c>. Hence a separate request rather than
    /// the same one as for generic functions.</para>
    /// </summary>
    /// <summary>
    /// A member of a generic <c>extend</c> block for one receiver (03 T7 X1): the block's
    /// parameters as the receiver bound them, 'this' the receiver's instance. Keyed by the
    /// receiver, as a method of a generic type is.
    /// </summary>
    public FunctionId RequestExtension(FunctionSymbol method, FunctionDecl decl, ExtensionBlock block,
        Dictionary<GenericParamSymbol, LyrType> substitution, LyrType receiver, Core.Span span)
    {
        var name = Qualify(decl, $"<extend>.{NameOf(receiver)}.{method.Name}");
        if (_byKey.TryGetValue(name, out var existing)) return existing;
        Guard(name, span);
        var id = _ids.Next();
        _byKey[name] = id;
        // 'this' is the receiver: the instance for a named target, the shape itself for a
        // built-in constructor (03 T7 X2), where no symbol stands — and whatever a blanket block
        // (04 D15) is reached on, an instance included, which no target symbol names.
        _pending.Add(new Pending(decl, name, id, method.IsStatic ? null : block.Target,
            substitution, receiver as GenericInstance,
            method.IsStatic || (receiver is GenericInstance && block.Target is not null) ? null : receiver));
        return id;
    }

    /// <summary>
    /// An interface's default for one conformer (04 D9): 'Self' the conformer, 'this' its value,
    /// the call direct — one instance per default and conformer, as for a block's member. The
    /// interface has no parameters of its own here; a generic one's defaults go through its value.
    /// </summary>
    public FunctionId RequestDefault(FunctionSymbol method, FunctionDecl decl, TypeSymbol iface,
        LyrType conformer, Core.Span span)
    {
        var name = Qualify(decl, $"<default>.{iface.Name}.{NameOf(conformer)}.{method.Name}");
        if (_byKey.TryGetValue(name, out var existing)) return existing;
        Guard(name, span);
        var substitution = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);
        if (iface.SelfParam is { } self) substitution[self] = conformer;
        var id = _ids.Next();
        _byKey[name] = id;
        _pending.Add(new Pending(decl, name, id, null, substitution, null, conformer));
        return id;
    }

    public FunctionId RequestMethod(FunctionSymbol method, FunctionDecl decl,
        GenericInstance owner, Core.Span span)
    {
        var ownerName = Qualify(owner.Definition.Declaration,
            $"{owner.Definition.Name}<{string.Join(", ", owner.Arguments.Select(NameOf))}>");
        var name = $"{ownerName}.{method.Name}";
        if (_byKey.TryGetValue(name, out var existing)) return existing;

        var substitution = new Dictionary<GenericParamSymbol, LyrType>(
            ReferenceEqualityComparer.Instance);
        for (var i = 0; i < owner.Definition.Generics.Length && i < owner.Arguments.Length; i++)
            substitution[owner.Definition.Generics[i]] = owner.Arguments[i];
        // An interface's member for a value of the interface: 'Self' is the interface (04 D9).
        if (owner.Definition.SelfParam is { } self) substitution[self] = owner;

        Guard(name, span);

        var id = _ids.Next();
        _byKey[name] = id;
        // A STATIC method gets no 'this'. 'Owner' stays set all the same: its 'T' is that of the type,
        // even when no receiver brings it along.
        _pending.Add(new Pending(decl, name, id, method.IsStatic ? null : owner.Definition,
            substitution, owner));
        return id;
    }

    /// <summary>
    /// Lowers all requested instances AS A WORKLIST, because an instance can request further ones while
    /// being lowered: <c>id&lt;T&gt;</c> calls <c>wrap&lt;T&gt;</c>, and only here is it settled which
    /// <c>T</c> was meant.
    /// </summary>
    public List<(FunctionId Id, IrFunction Function)> LowerAll(TypeResult types,
        IReadOnlyDictionary<FunctionSymbol, FunctionId> functions, ImportTable imports,
        TypeTable typeTable, GlobalTable globals, LambdaTable lambdas)
    {
        var lowered = new List<(FunctionId, IrFunction)>();

        for (; _lowered < _pending.Count; _lowered++)
        {
            var p = _pending[_lowered];
            // A generic coroutine is two functions per instance, as a written one is (06 N2): the
            // factory under the instance's name, the body behind it in the instance's terms.
            if (ModuleLowerer.CoroutineReturn(p.Decl) is not null)
            {
                lowered.AddRange(CoroutineFactory.Split(FunctionLowerer.ForCoroutineBody(p.Decl, $"{p.Name}.<body>",
                    p.Receiver, types, functions, imports, typeTable, p.Substitution, globals, lambdas, this,
                    p.Owner, receiverType: p.ReceiverType), p.Name, p.Id, _ids, p.Decl.Span));
                continue;
            }
            lowered.Add((p.Id, new FunctionLowerer(p.Decl, p.Name, types, functions, imports, typeTable,
                p.Substitution, globals, lambdas, this, p.Receiver, p.Owner, receiverType: p.ReceiverType).Run()));
        }

        return lowered;
    }
}
