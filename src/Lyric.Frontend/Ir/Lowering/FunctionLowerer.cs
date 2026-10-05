using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Ir.Lowering;

/// <summary>
/// Lowers ONE function from the type-checked AST into an <see cref="IrFunction"/>. One object per
/// function, like <c>IrVerifier.FunctionVerifier</c>, because slots, blocks and the loop stack are
/// function-local and die with the function.
///
/// <para>STATEMENTS RETURN A <c>bool</c>: "does control flow fall through?" That is the load-bearing
/// signature decision. Without it one cannot decide whether a merge block may be created, and a merge
/// block without predecessors is unreachable, which the verifier rejects — deliberately, as there is no
/// <c>SimplifyCfg</c> pass. For the same reason <see cref="LowerStatements"/> stops as soon as a
/// statement does not fall through: code after a <c>return</c> must not produce a block.</para>
///
/// <para>VALUES CROSSING BLOCK BOUNDARIES TRAVEL THROUGH LOCALS, NOT THROUGH TEMPS. A temp is defined
/// exactly once and therefore cannot carry "the result from two branches". An if expression and
/// <c>&amp;&amp;</c>/<c>||</c> therefore create a synthetic local, write into it in both branches and
/// read it in the merge block. That is why this IR needs no <c>Phi</c>: the target is a stack VM with
/// local slots, and a phi would have to become store/load again at emission.</para>
///
/// <para>A construct the IR cannot express is valid Lyric and therefore a DIAGNOSTIC
/// (<c>LYR-IR0001</c>) with file, line and column rather than a crash — see
/// <see cref="UnsupportedConstructException"/>. Internal inconsistencies stay separate and keep
/// throwing <see cref="InternalCompilationException"/>.</para>
/// </summary>
internal sealed class FunctionLowerer
{
    private static readonly IrType VoidType = new IrScalarType(IrScalar.Void);
    private static readonly IrType BoolType = new IrScalarType(IrScalar.Bool);

    private readonly FunctionDecl? _decl;
    private readonly string _name;
    private readonly TypeResult _types;
    private readonly IReadOnlyDictionary<FunctionSymbol, FunctionId> _functions;
    private readonly ImportTable _imports;
    private readonly TypeTable _typeTable;
    private readonly GlobalTable _globals;

    /// <summary>The receiver's slot, always 0, or <c>null</c> for a free or static function.</summary>
    private readonly LocalId? _thisSlot;
    private IrType? _thisType;

    /// <summary>The receiver type of this function; a lambda in its body inherits it when it captures
    /// <c>this</c>.</summary>
    private readonly TypeSymbol? _receiver;

    /// <summary>The type instance this method belongs to, set when <c>Box&lt;int&gt;.get</c> is being
    /// lowered here. <c>this</c> is then of the INSTANCE's type rather than the definition's:
    /// <c>Box</c> alone has no layout, only <c>Box&lt;int&gt;</c> has one.</summary>
    private readonly GenericInstance? _ownerInstance;

    /// <summary>The type arguments of the instance being lowered. The hook sits in
    /// <see cref="LowerType"/>, so the worklist monomorphization only has to fill the map rather than
    /// rebuild the whole expression path.</summary>
    /// <remarks>Not readonly for one reader: a generic type's FIELD DEFAULT, which is lowered
    /// at the construction site and as code of the instance being built
    /// (<see cref="UnderInstance"/>).</remarks>
    private IReadOnlyDictionary<GenericParamSymbol, LyrType> _substitution;

    /// <summary>
    /// Lowers what belongs to an INSTANCE of a generic type from inside another function: the
    /// instance's arguments are the substitution while it is lowered, over this function's own.
    /// A field default names its type's parameters — <c>items: List&lt;T&gt; = List&lt;T&gt;.new()</c> —,
    /// and the function that writes <c>Bag&lt;int&gt; { }</c> knows no such <c>T</c>.
    /// </summary>
    private TResult UnderInstance<TResult>(GenericInstance? instance, Func<TResult> lower)
    {
        if (instance is null || instance.Definition.Generics.Length != instance.Arguments.Length
            || instance.Arguments.Length == 0) return lower();
        var outer = _substitution;
        var own = new Dictionary<GenericParamSymbol, LyrType>(outer, ReferenceEqualityComparer.Instance);
        for (var i = 0; i < instance.Arguments.Length; i++) own[instance.Definition.Generics[i]] = instance.Arguments[i];
        _substitution = own;
        try { return lower(); }
        finally { _substitution = outer; }
    }

    /// <summary>
    /// Temps holding a FRESHLY BUILT value: the result of a <c>newobj</c> or of a call.
    ///
    /// <para>Relevant for structs only, and there the line between correct and wasteful: a value nobody
    /// else holds does not have to be copied when bound. Without this distinction every
    /// <c>let p = P { … };</c> would get a <c>structcopy</c> directly behind its <c>newobj</c> — correct,
    /// but obviously pointless and visible in every disassembly.</para>
    /// </summary>
    /// <summary>
    /// What a call in a <c>?.</c> chain sees differently, and nothing else.
    ///
    /// <para><c>_chainReceivers</c> maps the RECEIVER expression to the already unwrapped value:
    /// <c>LowerExprOrVoid</c> returns it instead of evaluating <c>b</c> a second time.
    /// <c>_chainResults</c> maps the CALL expression to the method's return type, because the sema gave
    /// the expression the chain type (<c>?int</c> instead of <c>int</c>).</para>
    ///
    /// <para>On the node rather than as a parameter, because otherwise both facts would have to be
    /// threaded through LowerVirtualCall, LowerGenericMethodCall, LowerConstraintCall and
    /// LowerImportCall — four signatures for an exception none of the four cares about. Nested chains
    /// (<c>a?.f(b?.g())</c>) carry different nodes and therefore do not get in each other's way; the
    /// entries are removed again after the call.</para>
    /// </summary>
    private readonly Dictionary<Expr, TempId> _chainReceivers =
        new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<Expr, IrType> _chainResults =
        new(ReferenceEqualityComparer.Instance);

    private readonly HashSet<TempId> _fresh = new();

    /// <summary>The lifted lambdas of the module. A lambda in the body registers here and gets its id
    /// immediately, long before its own body is lowered.</summary>
    private readonly LambdaTable _lambdas;

    /// <summary>The monomorphized instances of the module. A call to a generic function requests its own
    /// here and gets an id immediately.</summary>
    private readonly InstanceTable _instances;

    /// <summary>
    /// Slots holding a CELL rather than a value: per slot the type of the cell and the type of what lies
    /// inside it.
    ///
    /// <para>Such a slot behaves unremarkably for the whole rest of the lowering: only
    /// <see cref="LoadValue"/> and <see cref="StoreValue"/> know about it, and they are the only places
    /// writing <c>ldloc</c> and <c>stloc</c> on named variables. Without this bundling each of the
    /// roughly fifteen access sites would have to ask the question itself, and the one that forgets lets
    /// the closure and the function see different values.</para>
    /// </summary>
    private readonly Dictionary<LocalId, (TypeId Cell, IrType Value)> _cells = new();

    /// <summary>
    /// Slots holding the caller's PLACE rather than a value — a place parameter's (03 T12) — with
    /// the type of the value at the place. Like a cell, known only to <see cref="LoadValue"/> and
    /// <see cref="StoreValue"/>: the rest of the lowering reads and writes the name as any other.
    /// </summary>
    private readonly Dictionary<LocalId, IrType> _places = new();

    /// <summary>While lowering a lambda: which environment field holds which captured symbol. Empty
    /// outside a lambda.</summary>
    private readonly Dictionary<Symbol, int> _captureFields =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>The environment's slot, always 0 when there is one, and its type.</summary>
    private readonly LocalId? _envSlot;
    private readonly TypeId? _envType;

    /// <summary>The lambda currently being lowered, or <c>null</c> for a written function. It carries
    /// the body, because a lambda has no <see cref="FunctionDecl"/>.</summary>
    private readonly LambdaExpr? _lambda;

    private readonly SlotAllocator _slots = new();
    private readonly List<IrBlock> _blocks = new();
    private readonly BlockBuilder _b;
    private readonly Stack<LoopScope> _loops = new();
    private readonly IrType _returnType;

    public FunctionLowerer(FunctionDecl decl, string name, TypeResult types,
        IReadOnlyDictionary<FunctionSymbol, FunctionId> functions,
        ImportTable imports,
        TypeTable typeTable,
        IReadOnlyDictionary<GenericParamSymbol, LyrType> substitution,
        GlobalTable globals,
        LambdaTable lambdas,
        InstanceTable instances,
        TypeSymbol? receiver = null,
        GenericInstance? ownerInstance = null,
        TypeNode? receiverTypeNode = null,
        bool coroutineBody = false,
        IrType? returnTypeOverride = null,
        LyrType? receiverType = null)
    {
        _ownerInstance = ownerInstance;
        _instances = instances;
        _lambdas = lambdas;
        _receiver = receiver;
        _globals = globals;
        _decl = decl;
        _name = name;
        _types = types;
        _functions = functions;
        _imports = imports;
        _typeTable = typeTable;
        _substitution = substitution;
        _b = new BlockBuilder(_blocks);

        // A coroutine BODY is an ordinary function containing 'yield' ops: the yielded values
        // travel through the suspension, never through 'ret', and what it returns is the
        // coroutine's result (06 A1) — void for 'Coroutine<Y>'. Everything else about it —
        // parameters, locals, defers, lambdas — is the ordinary machinery, which is the point:
        // the frame lives on the coroutine's own stack like any other. Its coroutine is the
        // written return type, in this instance's terms.
        if (coroutineBody)
        {
            CoroutineType = LowerDeclaredReturnType() as IrCoroutineType
                ?? throw Bug($"'{name}' is lowered as a coroutine body but returns no coroutine");
            _coroutineYield = CoroutineType.Yield;
        }
        // The caller's error slot (01 L5 E1): a function whose CALL may throw — and a coroutine's
        // body, whose error the runtime keeps for the pull that ran into it (05 E10, M6).
        // A body has it also for the 'Cancelled' its yields throw when close() unwinds it (06 A5).
        _irThrows = coroutineBody
            ? ThrownHere(types.DeclaredThrows(decl)).Length > 0 || CancelledClass() is not null
            : types.ThrowsAtCall(decl) && ThrownHere(types.DeclaredThrows(decl)).Length > 0;
        // A synthetic function — a comptime site's evaluator — has no written return type; the
        // sema type of its expression is handed in lowered instead.
        _returnType = CoroutineType?.Result ?? returnTypeOverride ?? LowerDeclaredReturnType();

        // The receiver is parameter 0 and is allocated BEFORE the declared parameters: the IR's parameter
        // convention is positional, and a later slot would be a wrong-slot read in the VM. CIL takes the
        // same route with 'this'.
        if (receiver is not null || receiverType is not null)
        {
            // An enum receiver is the enum type rather than one of its variants; which one is present is
            // decided by the 'match' in the body.
            // In an interface default method 'this' is the interface type itself — which implementation
            // is behind it is known only at runtime. A 'this.foo()' inside therefore becomes a callvirt.
            // An extension brings its receiver type along as a written TypeNode and lets it run through
            // the same lowering as any parameter type. The detour over 'receiver.Kind' below cannot do
            // that: 'extend int' and 'extend string' target builtins that have no layout entry, and every
            // case there would invent an object for them. A scalar as parameter 0 is nothing new — every
            // free function has that. This is why an inherent extension needs no boxing.
            // A block on a built-in constructor (03 T7 X2) brings its receiver as a TYPE — 'T[]'
            // at the block's parameters, substituted for this instance — where no symbol stands.
            _thisType = receiverType is { } shape
                ? _typeTable.Lower(SubstituteType(shape), SpanOfDecl(decl))
                : receiverTypeNode is { } written
                ? _typeTable.Lower(written)
                : _ownerInstance is { } owner
                ? _typeTable.InstanceType(owner, SpanOfDecl(decl))
                : receiver!.Kind switch
            {
                TypeSymbolKind.Enum => _typeTable.EnumOf(receiver),
                TypeSymbolKind.Interface => _typeTable.InterfaceOf(receiver),
                // The receiver of a struct method is the value itself. That it is a copy was arranged by
                // the caller, so a 'mut fn' mutates only this copy.
                TypeSymbolKind.Struct => _typeTable.StructOf(receiver),
                _ => _typeTable.RefTo(receiver),
            };
            _thisSlot = _slots.Declare("this", _thisType);
        }

        // Parameter convention: the first ParamCount locals ARE the parameters, in order. Without it the
        // IR carries parameter types nowhere and a call would not be type-checkable.
        foreach (var p in decl.Parameters)
        {
            // 'params' and default values are purely CALL-SITE matters: the callee sees an ordinary T[]
            // and an ordinary parameter. Both are materialized where the call stands — see
            // MaterializeArguments.
            //
            // The alternative, letting the callee build its own defaults, would mean lowering a default
            // expression once per function rather than once per call; for 'params' the signature would
            // additionally become variadic, and this IR cannot do that. C# decides at the call site for
            // the same reason.

            if (_types.RefOf(p) is not ParameterSymbol ps)
                throw Bug($"parameter '{p.Name}' was not bound by the type checker");
            var valueType = LowerValueType(ps.Type, p.Span);
            // '&n: int' (03 T12): the slot holds the caller's place, and the name is read and
            // written through it.
            if (ps.IsPlace) _places[_slots.DeclareFor(ps, new IrPlaceType(valueType))] = valueType;
            else _slots.DeclareFor(ps, valueType);
        }
        Parameters = _slots.Locals.ToArray();
    }

    /// <summary>
    /// The lowerer for a LIFTED LAMBDA.
    ///
    /// <para>A factory of its own rather than a synthetic <see cref="FunctionDecl"/>: the
    /// GlobalInitializer may build one, because its statements are real AST nodes. That would not work
    /// here — the sema bound the <c>LambdaParam</c> nodes to their symbols, and rebuilt <c>Param</c>
    /// nodes would be different objects without a binding. The detour would have needed a second symbol
    /// resolution.</para>
    /// </summary>
    public static FunctionLowerer ForLambda(LambdaExpr lambda, string name,
        IReadOnlyList<Symbol> captures, bool capturesThis, IrType environmentType,
        TypeSymbol? receiver, TypeResult types,
        IReadOnlyDictionary<FunctionSymbol, FunctionId> functions, ImportTable imports,
        TypeTable typeTable, GlobalTable globals, LambdaTable lambdas, InstanceTable instances,
        IReadOnlyDictionary<GenericParamSymbol, LyrType>? substitution = null) =>
        new(lambda, name, captures, capturesThis, environmentType, receiver, types, functions,
            imports, typeTable, globals, lambdas, instances, substitution);

    private FunctionLowerer(LambdaExpr lambda, string name, IReadOnlyList<Symbol> captures,
        bool capturesThis, IrType environmentType, TypeSymbol? receiver, TypeResult types,
        IReadOnlyDictionary<FunctionSymbol, FunctionId> functions, ImportTable imports,
        TypeTable typeTable, GlobalTable globals, LambdaTable lambdas, InstanceTable instances,
        IReadOnlyDictionary<GenericParamSymbol, LyrType>? substitution = null)
    {
        _instances = instances;
        _lambda = lambda;
        _receiver = receiver;
        _name = name;
        _types = types;
        _functions = functions;
        _imports = imports;
        _typeTable = typeTable;
        _globals = globals;
        _lambdas = lambdas;

        // The substitution of the ENCLOSING function. With 'NoSubstitution' every lambda in a
        // monomorphized instance breaks: '(a: T, b: T) => …' in 'sortList<T>' makes the lowering abort
        // with "type parameter 'T' is not supported".
        //
        // A lambda is no generic context of its own; it inherits the one of its body. That it is lowered
        // as a separate function is an implementation decision and must not change the types.
        _substitution = substitution ?? ModuleLowerer.NoSubstitution;
        _b = new BlockBuilder(_blocks);

        if (_types.IsGeneratorLambda(lambda) && _types.TypeOf(lambda) is FnType { Return: CoroutineOf made })
        {
            // A generator lambda (08 Y11 F5) is lowered as a coroutine's body: its yields suspend,
            // its returns give the result, and it has the error slot — for what the coroutine's
            // pulls throw, and for the Cancelled its yields throw at close (06 A5).
            CoroutineType = (IrCoroutineType)LowerType(made, lambda.Span);
            _coroutineYield = CoroutineType.Yield;
            _returnType = CoroutineType.Result;
            _irThrows = made.Throws is { } pulled && ThrownHere([pulled]).Length > 0 || CancelledClass() is not null;
        }
        else
        {
            // A lambda throws what its type says (05 E2 K3), in the enclosing instance's terms: a set
            // naming the enclosing function's 'E' is empty where 'E = never' (K4).
            _irThrows = _types.TypeOf(lambda) is FnType thrower && ThrownHere(thrower.Throws).Length > 0;

            _returnType = _types.TypeOf(lambda) is FnType fn
                ? LowerType(fn.Return, lambda.Span)
                : throw Bug("lambda has no function type");
        }

        // The environment is parameter 0, the same position 'this' occupies on a method. A closure call
        // is therefore an ordinary call, and the VM needs no second frame setup for 'callind'.
        if (environmentType is IrRefType env)
        {
            _envType = env.Type;
            _envSlot = _slots.Declare("<env>", environmentType);

            for (var i = 0; i < captures.Count; i++) _captureFields[captures[i]] = i;

            // 'this' lies behind the named captures when it is captured: it is no symbol, having no
            // declaration, so it needs a place of its own rather than an entry in the same map.
            if (capturesThis)
            {
                // The type the environment holds it as (02 M8 C2): a class as its reference,
                // a struct as a copy of the value — read off the environment's layout, not
                // rebuilt from the receiver, which would make a struct's 'this' a reference.
                _thisType = _typeTable.Defs[env.Type.Value].FieldTypes[captures.Count];
                _capturedThisField = captures.Count;
            }
        }

        foreach (var p in lambda.Parameters)
        {
            // The implicit 'it' of a trailing lambda in a context that takes nothing: the
            // checker dropped it, so it has no symbol and no slot.
            if (p.Implicit && _types.RefOf(p) is null) continue;
            if (_types.RefOf(p) is not ParameterSymbol ps)
                throw Bug($"lambda parameter '{p.Name}' was not bound by the type checker");
            var valueType = LowerValueType(ps.Type, p.Span);
            // '&n' (03 T12): the slot holds the caller's place, as a declaration's does.
            if (ps.IsPlace) _places[_slots.DeclareFor(ps, new IrPlaceType(valueType))] = valueType;
            else _slots.DeclareFor(ps, valueType);
            _lambdaParameterCount++;
        }
        Parameters = _slots.Locals.ToArray();
    }

    /// <summary>The parameters the lambda really has — its declared ones minus a dropped
    /// implicit 'it'.</summary>
    private int _lambdaParameterCount;

    /// <summary>
    /// The lowerer for the BODY OF A COROUTINE: an ordinary function whose <c>yield</c>
    /// statements become suspension ops and whose return is the coroutine's result. Parameters
    /// and locals live where they do in any function — the body runs on the coroutine's own
    /// stack, so nothing has to move into an object to survive a suspension. Built before it
    /// runs, it already knows what the factory is made of: its <see cref="CoroutineType"/> and
    /// its <see cref="Parameters"/>, in the instance's terms for a generic one.
    /// </summary>
    public static FunctionLowerer ForCoroutineBody(FunctionDecl decl, string name, TypeSymbol? receiver,
        TypeResult types, IReadOnlyDictionary<FunctionSymbol, FunctionId> functions, ImportTable imports,
        TypeTable typeTable, IReadOnlyDictionary<GenericParamSymbol, LyrType> substitution,
        GlobalTable globals, LambdaTable lambdas, InstanceTable instances,
        GenericInstance? owner = null, TypeNode? receiverTypeNode = null, LyrType? receiverType = null) =>
        new(decl, name, types, functions, imports, typeTable, substitution, globals, lambdas, instances,
            receiver, owner, receiverTypeNode, coroutineBody: true, receiverType: receiverType);

    /// <summary>The coroutine a body belongs to (<see cref="ForCoroutineBody"/>); null for any
    /// other function.</summary>
    public IrCoroutineType? CoroutineType { get; }

    /// <summary>The parameters as the IR has them — the receiver first — known before the body is
    /// lowered.</summary>
    public IReadOnlyList<IrLocal> Parameters { get; } = [];

    /// <summary>The field index of the captured <c>this</c> in the environment, when captured.</summary>
    private readonly int? _capturedThisField;

    // ------------------------------------------------------------------ coroutines

    /// <summary>What the chain yields when a COROUTINE BODY is being lowered here, <c>null</c>
    /// otherwise. The one thing the body mode still decides: <c>yield</c> ops carry it as the
    /// annotation §10a rule 3 compares at runtime.</summary>
    private readonly IrType? _coroutineYield;

    private bool InCoroutine => _coroutineYield is not null;


    // ------------------------------------------------------------------ closures

    /// <summary>
    /// A lambda: build the environment, register the function, produce the fat pointer.
    ///
    /// <para>The lifted function is NOT lowered here — it is only registered and gets its id immediately.
    /// That is the condition for a recursive or nested lambda to work at all: its <c>mkclosure</c> is
    /// settled before its body exists.</para>
    /// </summary>
    /// <summary>
    /// <c>(1, "a")</c> — an object with one field per element.
    ///
    /// <para>The same sequence as for a struct initializer, except that the fields go by position rather
    /// than by name. An opcode of its own would be a second way to build an object.</para>
    /// </summary>
    /// <summary><c>a..b</c> as a value (03 T13 A3): the std.core struct holding its bounds, a
    /// fresh value like a tuple. In a <c>for</c> head the literal never gets here.</summary>
    private TempId LowerRangeValue(RangeExpr expr)
    {
        if (LowerType(_types.TypeOf(expr), expr.Span) is not IrStructType type)
            throw Bug("range value has no struct type");
        var layout = _typeTable.Defs[type.Type.Value];
        var dest = _slots.NewTemp(type);
        _b.Emit(new NewObject(dest, type.Type, type, expr.Span));
        _b.Emit(new StoreField(dest, type.Type, new FieldId(0), LowerExprAs(expr.Low, layout.FieldTypes[0]), expr.Span));
        _b.Emit(new StoreField(dest, type.Type, new FieldId(1), LowerExprAs(expr.High, layout.FieldTypes[1]), expr.Span));
        _fresh.Add(dest);
        return dest;
    }

    /// <summary>
    /// <c>p with { x = 3, pos.y = 4 }</c> (02 M6): a copy of <c>p</c>, then one store per field
    /// into the copy — through the structs a path names, aliased where they lie in the copy.
    /// The values are expressions over <c>p</c> and see the old values (W4).
    /// </summary>
    private TempId LowerWith(WithExpr expr)
    {
        if (LowerType(_types.TypeOf(expr), expr.Span) is not IrStructType type)
            throw Bug("'with' on a value that is not a struct");
        var copy = CopyStructValue(LowerExpr(expr.Target), type, expr.Span);

        foreach (var field in expr.Fields)
        {
            var (place, placeType) = (copy, type);
            for (var i = 0; i < field.Path.Length - 1; i++)
            {
                var layout = _typeTable.Defs[placeType.Type.Value];
                var index = Array.IndexOf(layout.FieldNames, field.Path[i]);
                var inner = (IrStructType)layout.FieldTypes[index];
                var loaded = _slots.NewTemp(inner);
                _b.Emit(new LoadField(loaded, place, placeType.Type, new FieldId(index), inner, field.Span));
                (place, placeType) = (loaded, inner);
            }
            var last = _typeTable.Defs[placeType.Type.Value];
            var at = Array.IndexOf(last.FieldNames, field.Path[^1]);
            var value = LowerExprAs(field.Value, last.FieldTypes[at]);
            _b.Emit(new StoreField(place, placeType.Type, new FieldId(at), value, field.Span));
        }
        return copy;
    }

    private TempId LowerTupleLiteral(TupleLitExpr expr)
    {
        if (LowerType(_types.TypeOf(expr), expr.Span) is not IrStructType type)
            throw Bug("tuple literal has no tuple type");

        var layout = _typeTable.Defs[type.Type.Value];

        var dest = _slots.NewTemp(type);
        _b.Emit(new NewObject(dest, type.Type, type, expr.Span));

        for (var i = 0; i < expr.Elements.Length; i++)
        {
            var value = LowerExprAs(expr.Elements[i], layout.FieldTypes[i]);
            _b.Emit(new StoreField(dest, type.Type, new FieldId(i), value, expr.Span));
        }

        _fresh.Add(dest);
        return dest;
    }

    private TempId LowerLambda(LambdaExpr lambda)
    {
        if (LowerType(_types.TypeOf(lambda), lambda.Span) is not IrFunctionType signature)
            throw Bug("lambda has no function type");

        var (captured, capturesThis) = _types.CapturesOf(lambda);

        // The values for the environment are evaluated HERE, in the enclosing frame — that is the core of
        // "captures on creation": a later call sees the state of now. For a boxed 'var' that state is the
        // CELL rather than its content, and that is exactly how both sides share the same variable.
        var fieldTypes = new IrType[captured.Count + (capturesThis ? 1 : 0)];
        var fieldNames = new string[fieldTypes.Length];
        var values = new TempId[fieldTypes.Length];

        for (var i = 0; i < captured.Count; i++)
        {
            var symbol = captured[i];
            fieldNames[i] = symbol.Name;
            (fieldTypes[i], values[i]) = LoadCaptured(symbol, lambda.Span);
        }

        if (capturesThis)
        {
            var slot = _thisSlot ?? throw Bug("lambda captures 'this' outside a method");
            var thisType = _thisType ?? throw Bug("captured 'this' has no type");
            var value = _slots.NewTemp(thisType);
            _b.Emit(new LoadLocal(value, slot, thisType, lambda.Span));

            fieldNames[^1] = "this";
            fieldTypes[^1] = thisType;
            values[^1] = value;
        }

        // Without captures there is no environment and no allocation — the common case for a filter such
        // as '(x) => x > 0'.
        IrType environment = fieldTypes.Length == 0
            ? VoidType
            : _typeTable.EnvironmentFor(_name, fieldTypes, fieldNames);

        var target = _lambdas.Register(lambda, _name, captured, capturesThis, environment,
            ReceiverForLambda(), _substitution);

        TempId? env = null;
        if (environment is IrRefType envType)
        {
            var instance = _slots.NewTemp(environment);
            _b.Emit(new NewObject(instance, envType.Type, environment, lambda.Span));
            for (var i = 0; i < values.Length; i++)
                _b.Emit(new StoreField(instance, envType.Type, new FieldId(i), values[i], lambda.Span));
            env = instance;
        }

        var dest = _slots.NewTemp(signature);
        _b.Emit(new MakeClosure(dest, target, env, signature, lambda.Span));
        return dest;
    }

    /// <summary>
    /// The value that goes into the environment for a captured symbol.
    ///
    /// <para>For a cell that is the cell itself rather than its content; otherwise the closure would have
    /// ended up with a copy and the sharing would silently have become by-value.</para>
    ///
    /// <para>When a symbol is captured that the ENCLOSING function already captured, it lies in its
    /// environment rather than in a slot. Nested lambdas therefore resolve their captures through the
    /// same chain an ordinary identifier takes.</para>
    /// </summary>
    private (IrType Type, TempId Value) LoadCaptured(Symbol symbol, Core.Span span)
    {
        if (_slots.TryLookup(symbol, out var slot))
        {
            var type = _slots.TypeOfLocal(slot); // for a cell: the cell type, which is what is wanted
            var value = _slots.NewTemp(type);
            _b.Emit(new LoadLocal(value, slot, type, span));
            return (type, value);
        }

        if (_captureFields.TryGetValue(symbol, out var field) && _envSlot is { } envSlot)
        {
            var envType = _slots.TypeOfLocal(envSlot);
            var fieldType = _typeTable.Defs[_envType!.Value.Value].FieldTypes[field];

            var holder = _slots.NewTemp(envType);
            _b.Emit(new LoadLocal(holder, envSlot, envType, span));

            var value = _slots.NewTemp(fieldType);
            _b.Emit(new LoadField(value, holder, _envType.Value, new FieldId(field), fieldType, span));
            return (fieldType, value);
        }

        throw Bug($"captured symbol '{symbol.Name}' is neither a slot nor an environment field");
    }

    /// <summary>The receiver type an inner lambda inherits when it captures <c>this</c>.</summary>
    private TypeSymbol? ReceiverForLambda() => _receiver;

    // ------------------------------------------------------------------ cells

    /// <summary>
    /// Reads a named variable. When it lives in a cell, the access goes through the cell's field, here
    /// exactly as in every closure sharing it.
    /// </summary>
    private TempId LoadValue(LocalId slot, Core.Span span)
    {
        if (_places.TryGetValue(slot, out var held))
        {
            var read = _slots.NewTemp(held);
            _b.Emit(new LoadPlace(read, PlaceIn(slot, span), held, span));
            return read;
        }

        if (!_cells.TryGetValue(slot, out var cell))
        {
            var plain = _slots.TypeOfLocal(slot);
            var direct = _slots.NewTemp(plain);
            _b.Emit(new LoadLocal(direct, slot, plain, span));
            return direct;
        }

        var holder = _slots.NewTemp(_slots.TypeOfLocal(slot));
        _b.Emit(new LoadLocal(holder, slot, _slots.TypeOfLocal(slot), span));

        var dest = _slots.NewTemp(cell.Value);
        _b.Emit(new LoadField(dest, holder, cell.Cell, new FieldId(0), cell.Value, span));
        return dest;
    }

    /// <summary>Writes a named variable, into its slot or into its cell.</summary>
    private void StoreValue(LocalId slot, TempId value, Core.Span span)
    {
        if (_places.ContainsKey(slot))
        {
            _b.Emit(new StorePlace(PlaceIn(slot, span), value, span));
            return;
        }

        if (!_cells.TryGetValue(slot, out var cell))
        {
            _b.Emit(new StoreLocal(slot, value, span));
            return;
        }

        var holder = _slots.NewTemp(_slots.TypeOfLocal(slot));
        _b.Emit(new LoadLocal(holder, slot, _slots.TypeOfLocal(slot), span));
        _b.Emit(new StoreField(holder, cell.Cell, new FieldId(0), value, span));
    }

    /// <summary>The type of the VALUE in a slot, so for a cell its content rather than the cell. Every
    /// place that uses <c>TypeOfLocal</c> to type a value has to ask this question.</summary>
    private IrType ValueTypeOf(LocalId slot) =>
        _places.TryGetValue(slot, out var held) ? held
        : _cells.TryGetValue(slot, out var cell) ? cell.Value : _slots.TypeOfLocal(slot);

    /// <summary>The place a place parameter's slot holds (03 T12).</summary>
    private TempId PlaceIn(LocalId slot, Core.Span span)
    {
        var type = _slots.TypeOfLocal(slot);
        var place = _slots.NewTemp(type);
        _b.Emit(new LoadLocal(place, slot, type, span));
        return place;
    }

    // ------------------------------------------------------------------ places

    /// <summary>
    /// The place a marked argument hands a place parameter (03 §2.3a): a variable — in its slot,
    /// in the cell a closure shares, at module level, or the place the function holds itself,
    /// handed on — the receiver of a <c>mut fn</c>, a field, an element. The sema proved it a
    /// place the call may write; what is made here is its address, of the value's own type.
    /// </summary>
    private TempId LowerPlace(Expr operand)
    {
        var span = operand.Span;
        TempId Address(IrType value, Func<TempId, IrOp> make)
        {
            var dest = _slots.NewTemp(new IrPlaceType(value));
            _b.Emit(make(dest));
            return dest;
        }

        switch (operand)
        {
            case IdentifierExpr name when GlobalOf(name) is { } global:
            {
                var (id, type) = _globals.Resolve(global, span);
                return Address(type, dest => new AddrGlobal(dest, id, type, span));
            }
            case IdentifierExpr name when TryCapturedCell(name, out var shared, out var sharedType, out var sharedValue):
                return Address(sharedValue, dest => new AddrField(dest, shared, sharedType, new FieldId(0), sharedValue, span));
            case IdentifierExpr name:
            {
                var slot = ResolveLocalTarget(name, "a place argument");
                if (_places.ContainsKey(slot)) return PlaceIn(slot, span);
                if (_cells.TryGetValue(slot, out var cell))
                {
                    var holder = _slots.NewTemp(_slots.TypeOfLocal(slot));
                    _b.Emit(new LoadLocal(holder, slot, _slots.TypeOfLocal(slot), span));
                    return Address(cell.Value, dest => new AddrField(dest, holder, cell.Cell, new FieldId(0), cell.Value, span));
                }
                var type = _slots.TypeOfLocal(slot);
                return Address(type, dest => new AddrLocal(dest, slot, type, span));
            }
            // 'normalize(&this)' in a 'mut fn' (03 T12): the receiver's place, which is the caller's.
            case ThisExpr when _thisSlot is { } self && _thisType is { } selfType:
                return Address(selfType, dest => new AddrLocal(dest, self, selfType, span));
            case ThisExpr:
                throw NotSupported("'&this' inside a lambda", span);
            case MemberExpr member:
            {
                var (obj, type, field, fieldType) = ResolveFieldAccess(member);
                return Address(fieldType, dest => new AddrField(dest, obj, type, field, fieldType, span));
            }
            case IndexExpr indexed:
            {
                var (array, index, element) = ResolveIndexAccess(indexed);
                return Address(element, dest => new AddrElem(dest, array, index, element, span));
            }
            default:
                throw Bug($"'&' of a {operand.GetType().Name} reached the lowering — the sema refuses what is no place");
        }
    }

    /// <summary>
    /// The place <c>++</c> and <c>--</c> read and write (03 §2.2): a variable — in its slot, its
    /// cell, at module level, or through the place a place parameter holds — a field, an element of
    /// an array. What leads to it is evaluated here, once, as for a compound assignment:
    /// <c>next().n++</c> calls <c>next</c> once.
    /// </summary>
    private (IrType Type, Func<TempId> Load, Action<TempId> Store) AccessOf(Expr target, Span span)
    {
        switch (target)
        {
            case MemberExpr member:
            {
                var (obj, type, field, fieldType) = ResolveFieldAccess(member);
                return (fieldType, () =>
                {
                    var loaded = _slots.NewTemp(fieldType);
                    _b.Emit(new LoadField(loaded, obj, type, field, fieldType, span));
                    return loaded;
                }, value => _b.Emit(new StoreField(obj, type, field, value, span)));
            }
            case IndexExpr indexed when TypeOfExpr(indexed.Target) is IrArrayType or IrSliceType or IrInlineArrayType:
            {
                var (array, index, element) = ResolveIndexAccess(indexed);
                return (element, () =>
                {
                    var loaded = _slots.NewTemp(element);
                    _b.Emit(new LoadElem(loaded, array, index, element, span));
                    return loaded;
                }, value => _b.Emit(new StoreElem(array, index, value, span)));
            }
            // The read and the write with one index, which a container's get and set would each
            // evaluate — the question a compound assignment on one leaves open as well.
            case IndexExpr:
                throw NotSupported("'++' or '--' on an element of a container (only of arrays)", target.Span);
            case IdentifierExpr name when GlobalOf(name) is { } global:
            {
                var (id, type) = _globals.Resolve(global, span);
                return (type, () =>
                {
                    var loaded = _slots.NewTemp(type);
                    _b.Emit(new LoadGlobal(loaded, id, type, span));
                    return loaded;
                }, value => _b.Emit(new StoreGlobal(id, value, span)));
            }
            case IdentifierExpr when TryCapturedCell(target, out var cell, out var cellType, out var held):
                return (held, () =>
                {
                    var loaded = _slots.NewTemp(held);
                    _b.Emit(new LoadField(loaded, cell, cellType, new FieldId(0), held, span));
                    return loaded;
                }, value => _b.Emit(new StoreField(cell, cellType, new FieldId(0), value, span)));
            default:
            {
                var slot = ResolveLocalTarget(target, "increment/decrement");
                return (ValueTypeOf(slot), () => LoadValue(slot, span), value => StoreValue(slot, value, span));
            }
        }
    }

    public IrFunction Run()
    {
        CompilerPosition.At("lowering", _name, _decl?.Span ?? _lambda!.Span);

        // A lambda has an expression OR a block instead of a body. The expression case is the common one
        // and needs no 'return' in the source; it is inserted here.
        if (_lambda is not null) return RunLambda();

        if (_decl!.Body is null) throw Bug("function has no body");

        // The function body is itself a scope with its own defers.
        if (LowerScope(_decl.Body))
        {
            // Control flow ran out of the body. For void that is the normal case and needs the implicit
            // 'ret'. For non-void the sema's return coverage (LYR-SEM0017) proved that every path returns,
            // so this point is reached only through a diverging construct such as 'while (true) { }',
            // whose exit edge never fires. 'unreachable' is the honest encoding of that.
            _b.Seal(IsVoid(_returnType)
                ? new Return(null, _decl.Body.Span)
                : new Unreachable(_decl.Body.Span));
        }

        // The receiver counts as a parameter: it occupies slot 0 and is passed as argument 0 at the call
        // site. Without it here the parameter convention is violated, and the verifier reports exactly
        // that ("call passes 2 arg(s), expected 1").
        return new IrFunction(_name, _returnType, _decl.Parameters.Length + (_thisSlot is null ? 0 : 1),
            _slots.Locals, _slots.Temps, _blocks)
        {
            Entry = new BlockId(0), Throws = _irThrows,
            ReceiverByRef = _thisSlot is not null && _thisType is IrStructType or IrEnumType,
            Cleanup = _cleanup,
        };
    }

    /// <summary>
    /// The body of a lifted lambda. Two forms: an expression IS the return value, a block delivers
    /// through <c>return</c>.
    /// </summary>
    private IrFunction RunLambda()
    {
        // '((k, v)) => …': a pattern parameter is taken apart before the body runs, from the
        // slot the caller filled — the same compiler a 'let' pattern uses, and it cannot fail.
        foreach (var p in _lambda!.Parameters)
        {
            if (p.Pattern is not { } pattern || _types.RefOf(p) is not ParameterSymbol ps) continue;
            if (!_slots.TryLookup(ps, out var slot)) continue;
            var type = _slots.TypeOfLocal(slot);
            var value = _slots.NewTemp(type);
            _b.Emit(new LoadLocal(value, slot, type, p.Span));
            LowerPattern(pattern, value, type,
                () => throw Bug("an irrefutable parameter pattern asked for a failure path"), assumeMatch: true);
        }

        switch (_lambda!.Body)
        {
            case Block block:
                _tailSink = new TailSink(null, null, AsReturn: true); // a tail is the lambda's return
                if (LowerScope(block))
                    _b.Seal(IsVoid(_returnType)
                        ? new Return(null, block.Span)
                        : new Unreachable(block.Span));
                break;

            case Expr expr:
                // A void context discards the value: '() => doStuff()' is allowed and only calls.
                if (IsVoid(_returnType))
                {
                    if (ComesOut(() => LowerExprOrVoid(expr))) _b.Seal(new Return(null, expr.Span));
                }
                // A body that gives no value — '(n: int): int => unreachable()' — ends as it stands.
                else if (Diverges(expr)) LowerDiverging(expr);
                else
                {
                    // … and so does one an operand of which gives none: nothing statement-like
                    // stands around a lambda's expression body to end with it.
                    TempId value = default;
                    if (ComesOut(() => value = LowerExprAs(expr, _returnType))) _b.Seal(new Return(value, expr.Span));
                }
                break;

            default:
                throw Bug($"lambda body is neither an expression nor a block ({_lambda.Body.GetType().Name})");
        }

        // The environment counts as parameter 0, the same arithmetic as for the receiver.
        return new IrFunction(_name, _returnType,
            _lambdaParameterCount + (_envSlot is null ? 0 : 1),
            _slots.Locals, _slots.Temps, _blocks)
        {
            Entry = new BlockId(0), Throws = _irThrows, Cleanup = _cleanup,
        };
    }

    // ------------------------------------------------------------------ statements

    /// <summary>Lowers the statements of a block. Returns false as soon as control flow ends; the
    /// remaining statements are then unreachable and are discarded.</summary>
    private bool LowerStatements(Block block)
    {
        foreach (var stmt in block.Statements)
            if (!LowerStmt(stmt)) return false;
        return true;
    }

    /// <summary>Where a value block's tail goes: into a match arm's result slot, out of a lambda
    /// as its return, or nowhere (a block arm of a match statement). Set by the owner of the block
    /// around its <see cref="LowerScope"/>; the tail is the last statement, so the scope's defers
    /// run AFTER the value is taken — a store before the exit, or the return path's own drain.</summary>
    private TailSink? _tailSink;

    private readonly record struct TailSink(LocalId? Slot, IrType? Type, bool AsReturn);

    private bool LowerTail(TailExprStmt tail)
    {
        var sink = _tailSink ?? throw Bug($"a tail expression without a value position at {tail.Span}");
        if (Diverges(tail.Expr)) { LowerDiverging(tail.Expr); return false; }

        if (sink.AsReturn)
        {
            // Exactly what 'return tail;' lowers to: the value first, then every pending defer.
            var returned = IsVoid(_returnType) ? LowerExprOrVoidDiscarding(tail.Expr) : LowerExprAs(tail.Expr, _returnType);
            if (EmitLeavingDefers(_defers.Count)) _b.Seal(new Return(returned, tail.Span));
            return false;
        }

        if (sink.Slot is { } slot && sink.Type is { } type)
        {
            _b.Emit(new StoreLocal(slot, LowerExprAs(tail.Expr, type), tail.Span));
            return true;
        }

        LowerExprOrVoid(tail.Expr); // a match statement's arm: the value goes nowhere
        return !_b.IsSealed;
    }

    private TempId? LowerExprOrVoidDiscarding(Expr expr)
    {
        LowerExprOrVoid(expr);
        return null;
    }

    /// <summary>true means control flow falls through, false means the block is sealed. An expression
    /// in the statement that gives no value (05 E12) ends the statement where it stands.</summary>
    private bool LowerStmt(Stmt stmt)
    {
        try { return LowerStmtCore(stmt); }
        catch (Diverged) { return false; }
    }

    /// <summary>A value position met an expression that gives no value (05 E12) — a throw, a panic, a
    /// call of a function that returns 'never' — which sealed its block: the rest of the enclosing
    /// expression never runs, so its lowering stops, and the statement around it reports that control
    /// flow ended (<see cref="LowerStmt"/>). A construct that branches before such an operand handles
    /// it itself, as '??', 'if', 'match' and the short circuits do — its other path goes on.</summary>
    private sealed class Diverged : Exception;

    private bool LowerStmtCore(Stmt stmt)
    {
        switch (stmt)
        {
            // A nested block is a scope of its own: its defers run at its end rather than only at the
            // end of the function.
            case Block b: return LowerScope(b);
            case BindingStmt b: return LowerBinding(b);
            case DestructuringStmt d: return LowerDestructuring(d);
            case LetPatternStmt lp: return LowerLetPattern(lp);
            // 'panic(…)' has the return type 'never' and seals its block. An expression can therefore
            // end control flow, and the return value has to report that, or the caller later tries to
            // seal the same block a second time.
            case ExprStmt e:
                if (Diverges(e.Expr)) { LowerDiverging(e.Expr); return false; }
                LowerExprOrVoid(e.Expr);
                return !_b.IsSealed;
            case TailExprStmt tail: return LowerTail(tail);

            // Only in the synthetic global initializer (see GlobalInitializer).
            case GlobalInitStmt g: LowerGlobalInit(g); return true;
            case IfStmt s: return LowerIf(s);
            case WhileStmt s: return LowerWhile(s);
            case DoWhileStmt s: return LowerDoWhile(s);
            case ReturnStmt s: return LowerReturn(s);
            case BreakStmt s: return LowerBreak(s);
            case ContinueStmt s: return LowerContinue(s);

            case ForInStmt s: return LowerForIn(s);
            case MatchStmt s:
                LowerMatch(s.Scrutinee, s.Arms, null, s.Span);
                return _matchFellThrough;
            case TryStmt s: return LowerTry(s);
            case ThrowStmt s: return LowerThrow(s);
            // 'defer' only registers; LowerScope places the bodies at the exits.
            case DeferStmt s: _defers.Peek().Add(s); _cleanup = true; return true;
            case YieldStmt s: return LowerYield(s);
            case ErrorStmt s: throw Bug($"error statement reached lowering at {s.Span}");

            default: throw Bug($"unhandled statement {stmt.GetType().Name}");
        }
    }


    // ------------------------------------------------------------------ exceptions and defer

    /// <summary>
    /// The <c>defer</c> statements accumulated per scope, outermost scope at the bottom.
    ///
    /// <para><c>defer</c> registers nothing at runtime: which bodies are due is settled at compile time,
    /// so the lowering places them directly at every exit. A runtime stack, as in Go, would need closures
    /// and would cost something on every path, including where there is nothing to do. The price is code
    /// duplication per exit.</para>
    /// </summary>
    private readonly Stack<List<DeferStmt>> _defers = new();

    /// <summary>Whether a defer — a <c>using</c>'s close among them — registers anywhere in the
    /// function: for a coroutine's body, what dropping it without <c>close()</c> skips (06 A5).</summary>
    private bool _cleanup;

    // --- the error path (design/v5/spec/05 E1-E4, E9; 01 L5) ----------------------------------
    //
    // A throw site — a 'throw', a call of a throwing function — has a LANDING: the defers of every
    // scope it leaves, innermost and latest first, then the catch dispatch of the innermost try
    // around it, or the function's bottom. Each step is a block of the CFG; nothing unwinds. The
    // chains are shared: a step is keyed by the scope and how many of its defers are registered,
    // which fixes everything below it, so two sites with the same pending defers share one chain
    // (L5 E3, the cleanup labels of Zig's emission).

    /// <summary>Whether this function takes the caller's error slot (<see cref="IrFunction.Throws"/>).</summary>
    private readonly bool _irThrows;

    /// <summary>A try whose body is being lowered: the defer depth at which it stands, and its
    /// dispatch block once a site needed one.</summary>
    private sealed class TryFrame(int deferDepth)
    {
        public int DeferDepth { get; } = deferDepth;
        public BlockId? Dispatch { get; set; }
    }

    private readonly Stack<TryFrame> _tryFrames = new();
    private readonly Dictionary<(List<DeferStmt> Scope, int Count, object? End), BlockId> _chains = new();
    private BlockId? _bottom;

    /// <summary>How many of a scope's defers a landing runs, where not all of them: while the scope's
    /// defers run on a normal exit, those registered before the one running (05 E7: they run all
    /// the same); none, once they all ran. Keyed by the scope's list itself.</summary>
    private readonly Dictionary<List<DeferStmt>, int> _deferLimits = new(ReferenceEqualityComparer.Instance);

    /// <summary>A defer body running with an error in flight: the try and defer depths it began at —
    /// a site deeper in either is the body's own business — and the block where an error of its
    /// own is merged into the stashed one, once a site needs it.</summary>
    private sealed class Suppressing(int tryDepth, int deferDepth)
    {
        public int TryDepth { get; } = tryDepth;
        public int DeferDepth { get; } = deferDepth;
        public BlockId? Merge { get; set; }
    }

    private readonly Stack<Suppressing> _suppressing = new();
    private int _stashes;

    /// <summary>The bindings of the clauses being lowered whose bodies throw them again, and the
    /// stash each one's record waits in for that throw (05 E6 O3).</summary>
    private readonly Dictionary<LocalSymbol, int> _kept = new(ReferenceEqualityComparer.Instance);

    /// <summary>The error edges and throws lowered so far: a defer body on the error path that adds
    /// none cannot fail, and needs no stash.</summary>
    private int _errorSites;

    /// <summary>The calls whose error edge stands: a call lowered twice — the optional call lowers
    /// its unwrapped self — gets one edge.</summary>
    private readonly HashSet<CallExpr> _errorEdged = new(ReferenceEqualityComparer.Instance);

    /// <summary>The root of what is thrown as an interface type (05 E6 O1).</summary>
    private IrInterfaceType ErrorType(Span span) =>
        LowerType(ErrorRoot(span), span) as IrInterfaceType
            ?? throw Bug("std.core's Error did not lower to an interface");

    private NamedRef ErrorRoot(Span span) =>
        _typeTable.Compilation.FindModule(["std", "core"])?.Members.LookupLocal("Error") is TypeSymbol { Kind: TypeSymbolKind.Interface } root
            ? new NamedRef(root)
            : throw NotSupported("an error without std.core's 'Error'", span);

    /// <summary>
    /// Where an error at this point goes: the pending defers above the innermost try (or all of
    /// them), then that try's dispatch (or the bottom). Inside a defer body that runs with an error
    /// in flight — and not inside a try of its own — the body's own scopes, then the merge into the
    /// stashed error (05 E7). Built on demand, shared by key.
    /// </summary>
    private BlockId ErrorLanding(Span span)
    {
        object? end = _tryFrames.Count > 0 ? _tryFrames.Peek() : null;
        var floor = _tryFrames.Count > 0 ? _tryFrames.Peek().DeferDepth : 0;
        if (_suppressing.Count > 0 && _tryFrames.Count <= _suppressing.Peek().TryDepth)
        {
            end = _suppressing.Peek();
            floor = _suppressing.Peek().DeferDepth;
        }
        var scopes = _defers.ToArray(); // innermost first
        var pending = new List<List<DeferStmt>>();
        for (var i = 0; i < scopes.Length - floor; i++)
            if (CountOf(scopes[i]) > 0) pending.Add(scopes[i]);
        return Chain(pending, 0, pending.Count > 0 ? CountOf(pending[0]) : 0, end, span);
    }

    /// <summary>How many of a scope's defers a landing runs (<see cref="_deferLimits"/>).</summary>
    private int CountOf(List<DeferStmt> scope) =>
        _deferLimits.TryGetValue(scope, out var limit) ? limit : scope.Count;

    /// <summary>The landing from the <paramref name="count"/>-th defer of the
    /// <paramref name="at"/>-th pending scope down, ending at <paramref name="end"/>: a try's
    /// dispatch, a defer body's merge, or (null) the bottom.</summary>
    private BlockId Chain(List<List<DeferStmt>> pending, int at, int count, object? end, Span span)
    {
        if (at == pending.Count) return end switch
        {
            TryFrame frame => frame.Dispatch ??= _b.NewBlock(),
            Suppressing running => running.Merge ??= _b.NewBlock(),
            _ => Bottom(span),
        };
        if (count == 0) return Chain(pending, at + 1, at + 1 < pending.Count ? CountOf(pending[at + 1]) : 0, end, span);

        var scope = pending[at];
        if (_chains.TryGetValue((scope, count, end), out var shared)) return shared;

        var step = _b.NewBlock();
        _chains[(scope, count, end)] = step;
        var resume = _b.CurrentId;
        _b.SwitchTo(step);
        // The body runs where it was registered, with an error in flight (05 E7). Where it can fail,
        // that error is stashed while it runs — the body's own calls report through the same slot,
        // and a call that went well leaves it as it was — and an error of the body's own is
        // appended to the stashed one as suppressed, the first winning, before the chain goes on
        // with the stashed one. A body that cannot fail runs as it stands.
        var body = new Suppressing(_tryFrames.Count, _defers.Count);
        var sites = _errorSites;
        _suppressing.Push(body);
        bool open;
        try { open = LowerStmt(scope[count - 1].Body); }
        finally { _suppressing.Pop(); }
        int? stash = _errorSites == sites ? null : _stashes++;
        if (stash is { } aside) _b.Prepend(step, new StashError(aside, span));
        BlockId? next = null;
        if (open && !_b.IsSealed)
        {
            next = Chain(pending, at, count - 1, end, span);
            if (stash is { } back) _b.Emit(new RestoreError(back, span));
            _b.Seal(new Branch(next.Value, span));
        }
        if (body.Merge is { } merge)
        {
            next ??= Chain(pending, at, count - 1, end, span);
            _b.SwitchTo(merge);
            _b.Emit(new SuppressError(stash!.Value, span));
            _b.Seal(new Branch(next.Value, span));
        }
        _b.SwitchTo(resume);
        return step;
    }

    /// <summary>The end of every chain no try catches: the error leaves through the caller's slot —
    /// or, in a function that throws nothing, cannot arrive at all: the sema proved every site
    /// covered (05 K8), and a dispatch whose clauses do not end in a catch-all keeps an edge here.</summary>
    private BlockId Bottom(Span span)
    {
        if (_bottom is { } known) return known;
        var bottom = _b.NewBlock();
        var resume = _b.CurrentId;
        _b.SwitchTo(bottom);
        _b.Seal(_irThrows ? new Propagate(span) : new Unreachable(span));
        _b.SwitchTo(resume);
        _bottom = bottom;
        return bottom;
    }

    /// <summary>A recorded set in this instance's terms (05 E2 K4): substituted, <c>never</c> dropped
    /// — a call through a <c>fn() throws E</c> throws nothing where <c>E = never</c>, and the sema,
    /// which checks a generic body once, recorded the <c>E</c>.</summary>
    private LyrType[] ThrownHere(LyrType[] set) => TypeChecker.ThrownAfter(set.Select(SubstituteType));

    /// <summary>The edge after a call that may fail (01 L5 E3): the error to the landing, the value
    /// on in a fresh block.</summary>
    private void ErrorEdge(Span span)
    {
        _errorSites++;
        var landing = ErrorLanding(span);
        var next = _b.NewBlock();
        _b.Seal(new ErrorBranch(landing, next, span));
        _b.SwitchTo(next);
    }

    /// <summary>'throw e' (05 E1, E3): the value as an Error interface value — a class as it is, a
    /// struct or an enum boxed, an interface value re-tabled — into flight, then to the landing. A
    /// clause's own binding thrown again is the same error going on (E6 O3): its record — where it
    /// was first thrown, what was suppressed into it — goes back into flight from its stash.</summary>
    private void Raise(Expr thrown, Span span)
    {
        _errorSites++;
        if (thrown is IdentifierExpr && _types.RefOf(thrown) is LocalSymbol caught && _kept.TryGetValue(caught, out var stash))
        {
            var again = ErrorLanding(span);
            _b.Emit(new RestoreError(stash, span));
            _b.Seal(new Branch(again, span));
            return;
        }
        var value = Coerce(LowerExpr(thrown), TypeOfExpr(thrown), ErrorType(span), span);
        var landing = ErrorLanding(span);
        _b.Seal(new Throw(value, landing, span));
    }

    /// <summary>'throw' in value position: the same terminator as the statement, and no value —
    /// the type is 'never'. Whoever lowers the enclosing expression asks <see cref="Diverges"/>
    /// first, and so never asks this one for a temp it cannot give.</summary>
    private TempId? LowerThrowExpr(ThrowExpr expr)
    {
        Raise(expr.Value, expr.Span);
        return null;
    }

    /// <summary>Does this expression have the type 'never' — a 'throw', a call to 'panic' or to a
    /// function declared 'never', an 'if' or 'match' whose every arm diverges? Such an expression
    /// seals the block when lowered, and the position holding it must not store or branch after it.</summary>
    private bool Diverges(Expr expr) => _types.TypeOf(expr) is NeverType;

    /// <summary>Lowers a diverging expression for its effect. After it the block is sealed; a
    /// lowering that did not seal (a never-declared user function, whose call is an ordinary
    /// 'call') gets the 'unreachable' the sema's type promises.</summary>
    private void LowerDiverging(Expr expr)
    {
        ComesOut(() => LowerExprOrVoid(expr));
        if (!_b.IsSealed) _b.Seal(new Unreachable(expr.Span));
    }

    /// <summary>
    /// One PATH of an expression that has several — a branch of an 'if', an arm, the right of
    /// '??' or of a short circuit, the call behind '?.', the operand under a 'try' — lowered
    /// where the builder stands. Answers whether control comes out of it.
    ///
    /// <para>A path can end inside itself: an operand of it gives no value. 'if (c) wrap(todo())
    /// else 5' — the branch is a call and typed as one, and its argument ends it. That unwinds
    /// through the lowering of the path as <see cref="Diverged"/>, and it stops HERE: the other
    /// paths of the expression still run. Passed on, it took the whole expression with it and
    /// left those paths' blocks without an end — "has no terminator", an internal error, for
    /// every such program.</para>
    ///
    /// <para>An expression none of whose paths comes out throws <see cref="Diverged"/> itself,
    /// for whatever holds IT.</para>
    /// </summary>
    private bool ComesOut(Action path)
    {
        try { path(); }
        catch (Diverged) { return false; }
        return !_b.IsSealed;
    }

    private bool LowerThrow(ThrowStmt stmt)
    {
        // The defers of the scopes the throw leaves are the landing's (05 E7, L5 E3) — not inlined
        // here, as a 'return' inlines them.
        Raise(stmt.Value, stmt.Span);
        return false;
    }

    /// <summary>
    /// <c>try { … } catch (e: T) { … }</c> (05 E4, E9): the body's sites land — after the defers
    /// they leave inside it — on ONE dispatch block, which tests the in-flight error against the
    /// clauses in order: the first that covers it takes it off and runs; a catch-all ends the chain;
    /// when no clause matches, the error goes on to the landing outside this try. The clauses are
    /// lowered outside the try, so a throw in one is not caught by its sisters (E9 C6).
    /// </summary>
    private bool LowerTry(TryStmt stmt)
    {
        var frame = new TryFrame(_defers.Count);
        _tryFrames.Push(frame);
        bool bodyFallsThrough;
        try { bodyFallsThrough = LowerScope(stmt.Body); }
        finally { _tryFrames.Pop(); }

        var open = new List<BlockId>();
        if (bodyFallsThrough) open.Add(_b.CurrentId);

        // Nothing in the body can fail: no dispatch, the clauses are dead (the sema warned, SEM0139).
        if (frame.Dispatch is { } dispatch)
            open.AddRange(Dispatch(dispatch, stmt.Catches, stmt.Span, clause => LowerScope(clause.Body)));

        if (open.Count == 0) return false;
        var merge = _b.NewBlock();
        foreach (var id in open) _b.SealBlock(id, new Branch(merge, stmt.Span));
        _b.SwitchTo(merge);
        return true;
    }

    /// <summary>
    /// A try's dispatch (05 E9): the in-flight error tested against the clauses in order — the first
    /// that covers it takes it off, binds it under its own type and runs <paramref name="lowerBody"/>;
    /// a catch-all ends the chain. No clause took it: the error goes on to the landing outside — the
    /// defers there, then the next try or the bottom. Answers the blocks the bodies fall out of.
    /// </summary>
    private List<BlockId> Dispatch(BlockId dispatch, CatchClause[] clauses, Span span, Func<CatchClause, bool> lowerBody)
    {
        var open = new List<BlockId>();
        _b.SwitchTo(dispatch);
        var errorType = ErrorType(span);
        var error = _slots.NewTemp(errorType);
        _b.Emit(new CurrentError(error, errorType, span));
        var root = ErrorRoot(span);

        var caughtAll = false;
        foreach (var clause in clauses)
        {
            var symbol = clause.BindingName is null ? null
                : _types.RefOf(clause) as LocalSymbol
                  ?? throw Bug($"catch binding at {clause.Span} was not bound by the type checker");
            // What the clause tests: its type, or each type of its set (C5); nothing — it takes
            // everything — without a type, or where the root is among them.
            LyrType[] tested = clause.BindingTypes.Length > 0
                ? _types.CatchSet(clause) ?? throw Bug($"the set at {clause.Span} was not resolved by the type checker")
                : clause.BindingType is not null
                    ? [_types.CatchType(clause) ?? throw Bug($"the type at {clause.Span} was not resolved by the type checker")]
                    : [];
            if (tested.Any(t => LyrType.Equal(t, root))) tested = [];

            BlockId? next = null;
            if (tested.Length > 0)
            {
                // A test per type, any of them taking the error.
                var take = _b.NewBlock();
                next = _b.NewBlock();
                for (var i = 0; i < tested.Length; i++)
                {
                    var test = _slots.NewTemp(BoolType);
                    _b.Emit(new TypeTest(test, error, TestTarget(tested[i], clause.Span).Target, clause.Span));
                    var otherwise = i == tested.Length - 1 ? next.Value : _b.NewBlock();
                    _b.Seal(new CondBranch(test, take, otherwise, clause.Span));
                    _b.SwitchTo(otherwise);
                }
                _b.SwitchTo(take);
                Take(clause, symbol);
                if (symbol is not null && clause.BindingType is not null)
                {
                    // The value under its own type: a class's object, a struct's or an enum's
                    // payload out of the box, an interface value re-tabled.
                    var (target, bindingType) = TestTarget(tested[0], clause.Span);
                    var bound = _slots.NewTemp(bindingType);
                    _b.Emit(new Downcast(bound, error, target, bindingType, clause.Span));
                    _b.Emit(new StoreLocal(_slots.DeclareFor(symbol, bindingType), bound, clause.Span));
                }
                // A set's binding is the Error value itself; what it may hold is the sema's (K7).
                else if (symbol is not null)
                    _b.Emit(new StoreLocal(_slots.DeclareFor(symbol, errorType), error, clause.Span));
            }
            else
            {
                Take(clause, symbol);
                if (symbol is not null)
                    _b.Emit(new StoreLocal(_slots.DeclareFor(symbol, errorType), error, clause.Span));
            }

            if (lowerBody(clause)) open.Add(_b.CurrentId);
            if (symbol is not null) _kept.Remove(symbol);

            if (next is null) { caughtAll = true; break; } // a catch-all ends the chain (E9 C3)
            _b.SwitchTo(next.Value);
        }

        // No clause took it: the error goes on — the defers outside, then the next try or the
        // bottom.
        if (!caughtAll) _b.Seal(new Branch(ErrorLanding(span), span));
        return open;
    }

    /// <summary>A clause takes the error off the slot: the record dropped — or, where the body throws
    /// the binding again, kept aside for that throw (05 E6 O3), which is one store more.</summary>
    private void Take(CatchClause clause, LocalSymbol? symbol)
    {
        if (symbol is not null && ThrowsAgain(clause.Body, symbol))
        {
            var stash = _stashes++;
            _kept[symbol] = stash;
            _b.Emit(new StashError(stash, clause.Span));
        }
        else _b.Emit(new ClearError(clause.Span));
    }

    /// <summary>Does this code throw the binding itself — not under another name, and not from a
    /// lambda, whose body is a function of its own?</summary>
    private bool ThrowsAgain(Node node, LocalSymbol binding) => node switch
    {
        LambdaExpr => false,
        ThrowStmt { Value: IdentifierExpr id } when ReferenceEquals(_types.RefOf(id), binding) => true,
        ThrowExpr { Value: IdentifierExpr id } when ReferenceEquals(_types.RefOf(id), binding) => true,
        _ => AstChildren.Of(node).Any(child => ThrowsAgain(child, binding)),
    };

    /// <summary>A caught type as a type test's target, and as the binding's IR type.</summary>
    private (TypeId Target, IrType Type) TestTarget(LyrType caught, Span span)
    {
        var type = LowerType(caught, span);
        var target = type switch
        {
            IrRefType r => r.Type,
            IrStructType st => st.Type,
            IrEnumType en => en.Type,
            IrInterfaceType i => i.Type,
            _ => throw NotSupported($"catching '{TypeFacts.Display(caught)}'", span),
        };
        return (target, type);
    }

    /// <summary>
    /// The <c>try</c> family as an expression (05 E4). A mark is its operand: the sites in it got
    /// their edges where they were lowered. The other forms open a frame, as the block does, so the
    /// operand's errors land on ONE dispatch: <c>try!</c> panics there, <c>try?</c> clears the error
    /// and gives <c>null</c>, and the expression form asks its clauses, each delivering the value
    /// from its value block. Nothing under it can fail: no dispatch, the operand's value alone.
    /// </summary>
    private TempId? LowerTryExpr(TryExpr expr) => expr switch
    {
        { Kind: TryKind.Force } => LowerTryForce(expr),
        { Kind: TryKind.Optional } => LowerTryOptional(expr),
        { Catches.Length: > 0 } => LowerTryCatch(expr),
        _ => LowerExprOrVoid(expr.Value),
    };

    /// <summary>The operand under a frame of its own, so its sites land on the frame's dispatch.</summary>
    private T UnderFrame<T>(TryFrame frame, Func<T> lower)
    {
        _tryFrames.Push(frame);
        try { return lower(); }
        finally { _tryFrames.Pop(); }
    }

    /// <summary><c>try! e</c> (05 E4, E8): the value is the operand's; an error ends the program as a
    /// panic with its message. The dispatch has no way back, so the value needs no slot.</summary>
    private TempId? LowerTryForce(TryExpr expr)
    {
        var frame = new TryFrame(_defers.Count);
        // The operand may end where it stands; the dispatch of the sites before that still gets
        // its end, and then the expression ends as the operand did.
        TempId? value = null;
        var ended = false;
        try { value = UnderFrame(frame, () => LowerExprOrVoid(expr.Value)); }
        catch (Diverged) { ended = true; }
        if (frame.Dispatch is { } dispatch) _b.SealBlock(dispatch, new PanicError(expr.Span));
        if (ended) throw new Diverged();
        return value;
    }

    /// <summary><c>try? e</c> (05 E4): the operand's value wrapped, or <c>null</c> when it fails — the
    /// error cleared and dropped. Never flattened: a <c>?T</c> operand is wrapped once more. Over an
    /// expression without a value it stands as a statement (the sema saw to it) and only drops the
    /// error.</summary>
    private TempId? LowerTryOptional(TryExpr expr)
    {
        if (Diverges(expr.Value))
            throw NotSupported("a 'try?' over an expression that never gives a value", expr.Span,
                "its value could only ever be null, and '?never' has no layout yet");

        var frame = new TryFrame(_defers.Count);
        var type = TypeOfExpr(expr);
        if (IsVoid(type))
        {
            // The operand's own way may end inside it; what is left then is an error's way.
            if (!ComesOut(() => UnderFrame(frame, () => LowerExprOrVoid(expr.Value))))
            {
                if (frame.Dispatch is not { } only) throw new Diverged();
                _b.SwitchTo(only);
                _b.Emit(new ClearError(expr.Span));
                return null;
            }
            if (frame.Dispatch is not { } drop) return null;
            var after = _b.NewBlock();
            _b.Seal(new Branch(after, expr.Span));
            _b.SwitchTo(drop);
            _b.Emit(new ClearError(expr.Span));
            _b.Seal(new Branch(after, expr.Span));
            _b.SwitchTo(after);
            return null;
        }

        var inner = ((IrOptionalType)type).Inner;
        TempId value = default;
        if (!ComesOut(() => value = UnderFrame(frame, () => LowerExprAs(expr.Value, inner))))
        {
            // The operand ended where it stands. What is left is the way of an error thrown
            // before that: the expression is null there, and it is the only way out.
            if (frame.Dispatch is not { } only) throw new Diverged();
            _b.SwitchTo(only);
            _b.Emit(new ClearError(expr.Span));
            var absent = _slots.NewTemp(type);
            _b.Emit(new OptNone(absent, inner, expr.Span));
            return absent;
        }
        var some = _slots.NewTemp(type);
        _b.Emit(new OptSome(some, value, inner, expr.Span));
        if (frame.Dispatch is not { } dispatch) return some;

        var slot = _slots.DeclareSynthetic("try", type);
        _b.Emit(new StoreLocal(slot, some, expr.Span));
        var merge = _b.NewBlock();
        _b.Seal(new Branch(merge, expr.Span));
        _b.SwitchTo(dispatch);
        _b.Emit(new ClearError(expr.Span));
        var none = _slots.NewTemp(type);
        _b.Emit(new OptNone(none, inner, expr.Span));
        _b.Emit(new StoreLocal(slot, none, expr.Span));
        _b.Seal(new Branch(merge, expr.Span));
        _b.SwitchTo(merge);
        var dest = _slots.NewTemp(type);
        _b.Emit(new LoadLocal(dest, slot, type, expr.Span));
        return dest;
    }

    /// <summary><c>try e catch (x: A) v</c> (05 E4): the operand under a frame; its dispatch asks the
    /// clauses in order as the block form's does, and the clause that takes the error delivers the
    /// value from its value block. No clause takes it: the error goes on to the landing outside.
    /// A form whose every part leaves has the type 'never' and no slot.</summary>
    private TempId? LowerTryCatch(TryExpr expr)
    {
        IrType? type = _types.TypeOf(expr) is NeverType ? null : TypeOfExpr(expr);
        if (type is not null && IsVoid(type)) type = null;
        LocalId? slot = type is null ? null : _slots.DeclareSynthetic("try", type);

        var frame = new TryFrame(_defers.Count);
        // The operand's way; one that ends inside it leaves the clauses' ways standing.
        ComesOut(() => UnderFrame(frame, () =>
        {
            if (Diverges(expr.Value)) { LowerDiverging(expr.Value); return 0; }
            var value = type is null ? LowerExprOrVoid(expr.Value) : LowerExprAs(expr.Value, type);
            if (slot is { } s && value is { } v) _b.Emit(new StoreLocal(s, v, expr.Value.Span));
            return 0;
        }));

        var open = new List<BlockId>();
        if (!_b.IsSealed) open.Add(_b.CurrentId);
        if (frame.Dispatch is { } dispatch)
            open.AddRange(Dispatch(dispatch, expr.Catches, expr.Span, clause =>
            {
                var savedSink = _tailSink;
                _tailSink = new TailSink(slot, type, AsReturn: false);
                try { return LowerScope(clause.Body); }
                finally { _tailSink = savedSink; }
            }));

        // Every part leaves: nothing comes out. The type says so where each part gives no value
        // as a whole ('never'); where one ended inside itself, this is where the expression ends.
        if (open.Count == 0) return Diverges(expr) ? null : throw new Diverged();
        var merge = _b.NewBlock();
        foreach (var id in open) _b.SealBlock(id, new Branch(merge, expr.Span));
        _b.SwitchTo(merge);
        if (slot is not { } result) return null;
        var dest = _slots.NewTemp(type!);
        _b.Emit(new LoadLocal(dest, result, type!, expr.Span));
        return dest;
    }

    /// <summary>
    /// A scope with its own <c>defer</c>s: lower the body, then the registered bodies in LIFO order.
    /// </summary>
    private bool LowerScope(Block block)
    {
        // Whether this scope has defers stands in its OWN statements; a defer in a nested block belongs
        // there. Asking in advance saves every defer-free scope the extra block boundary, and that is
        // nearly all of them.
        var hasDefers = block.Statements.Any(st => st is DeferStmt or BindingStmt { Cleanup: not null });
        if (!hasDefers) return LowerPlainScope(block);
        _defers.Push(new List<DeferStmt>());
        try
        {
            // The normal exit runs the bodies here, inline, latest first, while the scope still
            // stands; an error leaving the scope reaches them through its landing (ErrorLanding),
            // which reads the same list.
            return LowerStatements(block) && EmitLeavingDefers(1);
        }
        finally
        {
            _defers.Pop();
        }
    }

    /// <summary>A scope without a <c>defer</c>: nothing to guard, nothing to clean up.</summary>
    private bool LowerPlainScope(Block block)
    {
        _defers.Push(new List<DeferStmt>());
        try
        {
            return LowerStatements(block);
        }
        finally
        {
            _defers.Pop();
        }
    }

    /// <summary>
    /// The defers of the scopes a normal exit leaves — the top <paramref name="count"/> scopes,
    /// innermost first, each scope's latest first (LIFO): a scope's end, a <c>return</c> (all of
    /// them), a <c>break</c> or <c>continue</c> (those above the loop). Each body runs where its
    /// defer was registered — under the trys around its scope, not those inside it — and should
    /// one throw (05 E7), the error leaves like any other: the defers registered before it run all
    /// the same, then those of the scopes below, never again one that ran, then the try around or
    /// the bottom. Answers whether the exit is still open: a body that always throws ends it.
    /// </summary>
    private bool EmitLeavingDefers(int count)
    {
        // Over a COPY rather than over the stack itself: lowering a defer body enters a scope and
        // pushes onto exactly this stack. Stack<T>.ToArray() yields top to bottom, innermost first.
        var scopes = _defers.ToArray();
        var hidden = new Stack<TryFrame>();
        var limited = new List<List<DeferStmt>>();
        try
        {
            for (var k = 0; k < count && k < scopes.Length; k++)
            {
                var scope = scopes[k];
                var depth = scopes.Length - k;
                while (_tryFrames.Count > 0 && _tryFrames.Peek().DeferDepth >= depth) hidden.Push(_tryFrames.Pop());
                limited.Add(scope);
                for (var i = scope.Count - 1; i >= 0; i--)
                {
                    _deferLimits[scope] = i;
                    if (!LowerStmt(scope[i].Body)) return false;
                }
                _deferLimits[scope] = 0;
            }
            return true;
        }
        finally
        {
            foreach (var scope in limited) _deferLimits.Remove(scope);
            while (hidden.Count > 0) _tryFrames.Push(hidden.Pop());
        }
    }

    private bool LowerBinding(BindingStmt binding)
    {
        if (_types.RefOf(binding) is not LocalSymbol local)
            throw Bug($"binding '{binding.Name}' was not bound by the type checker");

        var type = LowerValueType(local.Type, binding.Span);

        // A captured 'var' lives in a cell, so the slot holds the cell, and it has to exist BEFORE anyone
        // writes into it. Hence the newobj here rather than at the first assignment: a 'var n: int;'
        // without an initializer is written later, and the cell would not be there yet.
        if (_types.IsBoxed(local))
        {
            var cellType = _typeTable.CellOf(type);
            var slotForCell = _slots.DeclareFor(local, cellType);
            _cells[slotForCell] = (cellType.Type, type);

            var cell = _slots.NewTemp(cellType);
            _b.Emit(new NewObject(cell, cellType.Type, cellType, binding.Span));
            _b.Emit(new StoreLocal(slotForCell, cell, binding.Span));

            if (binding.Initializer is not null)
                StoreValue(slotForCell, LowerExprAs(binding.Initializer, type), binding.Span);

            return true;
        }

        var slot = _slots.DeclareFor(local, type);

        // Without an initializer the slot stays unwritten: the definite-assignment analysis proved that
        // every read sees an assignment.
        if (binding.Initializer is not null)
            _b.Emit(new StoreLocal(slot, LowerExprAs(binding.Initializer, type), binding.Span));

        // 'using let' (05 E7 R2): its close joins the scope's one LIFO list, registered once the
        // value stands — a failure in the initializer has nothing to close yet.
        if (binding.Cleanup is { } cleanup) _defers.Peek().Add(cleanup);
        return true;
    }

    /// <summary>
    /// <c>let (a, b) = pair;</c> — evaluate the value once, then bind field by field.
    ///
    /// <para>Evaluating ONCE is the actual statement: <c>let (a, b) = f();</c> must not call <c>f</c>
    /// twice. The tuple therefore lands in a temp first and the bindings read from it rather than from
    /// the expression.</para>
    /// </summary>
    private bool LowerDestructuring(DestructuringStmt stmt)
    {
        if (LowerType(_types.TypeOf(stmt.Initializer), stmt.Span) is not IrStructType type)
            throw Bug("destructuring a value that is not a tuple");

        var source = LowerExprAs(stmt.Initializer, type);
        // Irrefutable by the sema's proof: no test is emitted, so the failure path is never asked
        // for — and asking for it is a lowering bug rather than a program error.
        LowerPattern(stmt.Pattern, source, type,
            () => throw Bug("an irrefutable destructuring pattern asked for a failure path"),
            assumeMatch: true);
        return true;
    }

    private bool LowerReturn(ReturnStmt stmt)
    {
        // A bare 'return;' in a coroutine body is the ordinary void return below (§10): the
        // body is a void function since format 4.0, and the chain's end is the frame ending —
        // the interpreter marks exhaustion when this frame pops through the resume boundary.
        // A valued return in a coroutine is LYR-SEM0039 and never reaches this point.

        // 'return match (…) { … }' whose every arm leaves: the sema typed the value 'never', so
        // there is nothing to return — the arms already did. Lowered for its effect, sealed by it.
        if (stmt.Value is { } diverging && Diverges(diverging)) { LowerDiverging(diverging); return false; }

        // A value of type 'void' returned where the function returns nothing — an instance at
        // 'T = void' (03 §9.1): the expression runs for its effect, and the return carries nothing.
        if (stmt.Value is { } nothing && IsVoid(_returnType))
        {
            LowerExprOrVoid(nothing);
            if (!_b.IsSealed && EmitLeavingDefers(_defers.Count)) _b.Seal(new Return(null, stmt.Span));
            return false;
        }

        // The return value is evaluated BEFORE the defer bodies: a 'defer' must not change the value a
        // 'return' has already determined. Go behaves the same way.
        var returned = stmt.Value is null ? null : (TempId?)LowerExprAs(stmt.Value, _returnType);
        if (EmitLeavingDefers(_defers.Count)) _b.Seal(new Return(returned, stmt.Span));
        return false;
    }

    private bool LowerBreak(BreakStmt stmt)
    {
        var loop = TargetLoop(stmt.Label, "break", stmt.Span);
        // A value goes into the loop's slot first, as a 'return' takes its value before the defers
        // run (05 E11).
        if (stmt.Value is { } value && loop.ValueSlot is { } slot)
            _b.Emit(new StoreLocal(slot, LowerExprAs(value, loop.ValueType!), value.Span));
        // A break leaves every scope between it and the loop it names — their defers run first,
        // innermost first, down to the depth the target loop was entered at (§7.5).
        if (EmitLeavingDefers(_defers.Count - loop.DeferDepth)) _b.Seal(new Branch(loop.BreakTarget, stmt.Span));
        return false;
    }

    private bool LowerContinue(ContinueStmt stmt)
    {
        var loop = TargetLoop(stmt.Label, "continue", stmt.Span);
        // continue ends the iteration — the same exit path
        if (EmitLeavingDefers(_defers.Count - loop.DeferDepth)) _b.Seal(new Branch(loop.ContinueTarget, stmt.Span));
        return false;
    }

    /// <summary>The innermost loop, or the enclosing one carrying the label. The stack enumerates
    /// innermost first, so the first match is the nearest — and the sema refused a label that
    /// repeats an enclosing one, so nearest and only coincide.</summary>
    private LoopScope TargetLoop(string? label, string keyword, Span span)
    {
        if (_loops.Count == 0) throw Bug($"'{keyword}' outside a loop at {span}");
        if (label is null) return _loops.Peek();
        return _loops.FirstOrDefault(l => l.Label == label)
               ?? throw Bug($"'{keyword} {label}' without a loop of that label at {span}");
    }

    /// <summary>
    /// The merge block is created only once at least one branch falls through. For
    /// <c>if (c) { return 1; } else { return 2; }</c> none arises: it would have no predecessors and the
    /// verifier would report it as unreachable.
    /// </summary>
    private bool LowerIf(IfStmt stmt)
    {
        if (stmt.Condition is LetCondExpr letCond) return LowerIfLet(stmt, letCond);

        var condition = LowerExpr(stmt.Condition);
        var thenBlock = _b.NewBlock();

        if (stmt.Else is null)
        {
            // Without an else the false branch is the merge block; it is reachable through the false edge
            // and may therefore arise immediately.
            var merge = _b.NewBlock();
            _b.Seal(new CondBranch(condition, thenBlock, merge, stmt.Span));

            // Through LowerScope, not LowerStatements: the branch is a scope of its own, and a
            // 'defer' in it belongs to it (§7.5). Lowered as bare statements, the defer was
            // registered on the ENCLOSING scope — it ran at that scope's exit whether or not the
            // branch had been taken, and inside a scope without defers of its own it never ran.
            _b.SwitchTo(thenBlock);
            if (LowerScope(stmt.Then)) _b.Seal(new Branch(merge, stmt.Then.Span));

            _b.SwitchTo(merge);
            return true;
        }

        var elseBlock = _b.NewBlock();
        _b.Seal(new CondBranch(condition, thenBlock, elseBlock, stmt.Span));

        _b.SwitchTo(thenBlock);
        var thenFallsThrough = LowerScope(stmt.Then);
        var thenExit = _b.CurrentId; // after nested control flow this is no longer thenBlock

        _b.SwitchTo(elseBlock);
        var elseFallsThrough = LowerStmt(stmt.Else); // a block or an else-if
        var elseExit = _b.CurrentId;

        if (!thenFallsThrough && !elseFallsThrough) return false;

        var mergeBlock = _b.NewBlock();
        if (thenFallsThrough) _b.SealBlock(thenExit, new Branch(mergeBlock, stmt.Then.Span));
        if (elseFallsThrough) _b.SealBlock(elseExit, new Branch(mergeBlock, stmt.Else.Span));

        _b.SwitchTo(mergeBlock);
        return true;
    }

    /// <summary>
    /// <c>for (x in e) { … }</c> — a loop over <c>next()</c>.
    ///
    /// <para>The body runs as long as <c>next()</c> yields a value; <c>null</c> ends the loop. That is
    /// the entire protocol, and it is the same for a user-written iterator as for the built-in forms,
    /// where the compiler obtains the iterator by building an adapter from <c>std.iter</c>.</para>
    ///
    /// <para>ONE CALL, NOT THREE. The alternative — check <c>hasNext()</c>, then fetch <c>next()</c> —
    /// asks the same question twice and can fall out of step between the two. Rust and Python use one
    /// call for the same reason.</para>
    /// </summary>
    private bool LowerForIn(ForInStmt stmt)
    {
        if (_types.RefOf(stmt) is not LocalSymbol loopVar)
            throw Bug($"loop variable '{stmt.Variable}' was not bound by the type checker");

        if (stmt.Pattern is null && stmt.Iterable is RangeExpr literal
            && SubstituteType(_types.TypeOf(stmt.Iterable)) is RangeOf)
            return LowerCountedRange(stmt, literal, loopVar);

        if (_types.ForInOf(stmt) is { } protocol)
            return LowerForInProtocol(stmt, protocol, loopVar);

        if (_types.IsIndexed(stmt))
            return LowerIndexed(stmt, loopVar);

        var (iterator, iteratorType, owner, yieldOverride) = BuildIterator(stmt);
        var elementType = LowerType(loopVar.Type, stmt.Span);
        // What the iterator PRODUCES: the range adapters carry i64/u64 regardless of the
        // range's width, and the value converts back to the element type below.
        var yieldType = yieldOverride ?? elementType;

        // The iterator lives in a slot: it is read on every pass and changes while doing so, and a temp
        // would no longer be valid after the first block.
        var slot = _slots.DeclareSynthetic("iter", iteratorType);
        _b.Emit(new StoreLocal(slot, iterator, stmt.Span));

        var condBlock = _b.NewBlock();
        _b.Seal(new Branch(condBlock, stmt.Span));

        _b.SwitchTo(condBlock);
        var current = _slots.NewTemp(iteratorType);
        _b.Emit(new LoadLocal(current, slot, iteratorType, stmt.Span));

        var optional = new IrOptionalType(yieldType);
        var produced = _slots.NewTemp(optional);
        EmitNextCall(produced, current, iteratorType, owner, optional, stmt.Span);

        var hasValue = _slots.NewTemp(BoolType);
        _b.Emit(new OptIsSome(hasValue, produced, stmt.Span));

        var bodyBlock = _b.NewBlock();
        var exitBlock = _b.NewBlock(); // before the body: 'break' needs its target
        _b.Seal(new CondBranch(hasValue, bodyBlock, exitBlock, stmt.Span));

        _b.SwitchTo(bodyBlock);

        // The 'optget' cannot panic: it stands behind the 'optissome' that carried the proof — the same
        // division of labour as in flow narrowing.
        var value = _slots.NewTemp(yieldType);
        _b.Emit(new OptGet(value, produced, yieldType, stmt.Span));

        // A range over a smaller width gets its values back at that width; the values fit by
        // construction (the bounds came from the element type).
        if (!yieldType.Equals(elementType))
        {
            var narrowed = _slots.NewTemp(elementType);
            _b.Emit(new Lyric.Ir.Convert(narrowed, yieldType, elementType, value, stmt.Span));
            value = narrowed;
        }

        var variable = _slots.DeclareFor(loopVar, elementType);
        _b.Emit(new StoreLocal(variable, value, stmt.Span));

        // 'for ((k, v) in …)': the element is taken apart at the top of every iteration. The
        // checker proved the pattern cannot fail, so no failure path is ever asked for.
        if (stmt.Pattern is { } pattern)
            LowerPattern(pattern, value, elementType,
                () => throw Bug("an irrefutable loop pattern asked for a failure path"), assumeMatch: true);

        _loops.Push(new LoopScope(_b, condBlock, exitBlock)
            { DeferDepth = _defers.Count, Label = stmt.Label });
        // Through LowerScope, not LowerStatements: the loop body is a SCOPE, and a defer in it
        // runs at every iteration's end (§7.5) — registered into the enclosing function it ran
        // once, with the last iteration's values (the 2.0.1 bug).
        if (LowerScope(stmt.Body)) _b.Seal(new Branch(condBlock, stmt.Body.Span));
        _loops.Pop();

        _b.SwitchTo(exitBlock);
        return true;
    }

    /// <summary>
    /// <c>for (x in xs)</c> over an array, a view or an inline array in Lyric 5 (design/v5/spec/10
    /// B6 I10, COL-09 B): the index loop. The sequence is evaluated once into a hidden slot — an
    /// inline array copied there, as a binding copies it —, its length read once, and the element
    /// at the index read at every pass: a write to a later element of an array is seen. No
    /// iterator and no optional per element, so an element that is itself optional walks.
    /// </summary>
    private bool LowerIndexed(ForInStmt stmt, LocalSymbol loopVar)
    {
        var span = stmt.Span;
        var i64 = new IrScalarType(IrScalar.I64);
        var sequenceType = TypeOfExpr(stmt.Iterable);
        var element = ElementOf(sequenceType)
                      ?? throw Bug($"an index loop over a '{TypeFacts.Display(_types.TypeOf(stmt.Iterable))}'");
        var sequence = _slots.DeclareSynthetic("each", sequenceType);
        _b.Emit(new StoreLocal(sequence, LowerExprAs(stmt.Iterable, sequenceType), span));
        var whole = _slots.NewTemp(sequenceType);
        _b.Emit(new LoadLocal(whole, sequence, sequenceType, span));
        var length = _slots.NewTemp(i64);
        _b.Emit(new ArrayLen(length, whole, span));
        var count = _slots.DeclareSynthetic("count", i64);
        _b.Emit(new StoreLocal(count, length, span));
        var index = _slots.DeclareSynthetic("index", i64);
        _b.Emit(new StoreLocal(index, IntConstant(0, span), span));

        var condBlock = _b.NewBlock();
        _b.Seal(new Branch(condBlock, span));
        _b.SwitchTo(condBlock);
        var at = _slots.NewTemp(i64);
        _b.Emit(new LoadLocal(at, index, i64, span));
        var bound = _slots.NewTemp(i64);
        _b.Emit(new LoadLocal(bound, count, i64, span));
        var goesOn = _slots.NewTemp(BoolType);
        _b.Emit(new BinOp(goesOn, IrBinKind.Lt, BoolType, at, bound, span));
        var bodyBlock = _b.NewBlock();
        var stepBlock = _b.NewBlock();
        var exitBlock = _b.NewBlock();
        _b.Seal(new CondBranch(goesOn, bodyBlock, exitBlock, span));

        _b.SwitchTo(bodyBlock);
        var current = _slots.NewTemp(sequenceType);
        _b.Emit(new LoadLocal(current, sequence, sequenceType, span));
        var value = _slots.NewTemp(element);
        _b.Emit(new LoadElem(value, current, at, element, span));
        var variable = _slots.DeclareFor(loopVar, element);
        _b.Emit(new StoreLocal(variable, value, span));
        if (stmt.Pattern is { } pattern)
            LowerPattern(pattern, value, element,
                () => throw Bug("an irrefutable loop pattern asked for a failure path"), assumeMatch: true);

        _loops.Push(new LoopScope(_b, stepBlock, exitBlock) { DeferDepth = _defers.Count, Label = stmt.Label });
        if (LowerScope(stmt.Body)) _b.Seal(new Branch(stepBlock, stmt.Body.Span));
        _loops.Pop();

        _b.SwitchTo(stepBlock);
        var again = _slots.NewTemp(i64);
        _b.Emit(new LoadLocal(again, index, i64, span));
        var next = _slots.NewTemp(i64);
        _b.Emit(new BinOp(next, IrBinKind.Add, i64, again, IntConstant(1, span), span));
        _b.Emit(new StoreLocal(index, next, span));
        _b.Seal(new Branch(condBlock, span));

        _b.SwitchTo(exitBlock);
        return true;
    }

    /// <summary>
    /// <c>for (x in e)</c> over Lyric 5's protocol (design/v5/spec/10 B6 I2): the iterator from
    /// <c>e.iter()</c> into the loop's hidden variable, then <c>next()</c> on it before every pass,
    /// the body while it answers a value — the two calls lowered as the calls the checker recorded.
    /// </summary>
    private bool LowerForInProtocol(ForInStmt stmt, TypeResult.ForInProtocol protocol, LocalSymbol loopVar)
    {
        var cursorType = LowerType(SubstituteType(protocol.Cursor.Type), stmt.Span);
        var iterator = LowerExpr(protocol.IterCall);
        var cursor = _slots.DeclareFor(protocol.Cursor, cursorType);
        _b.Emit(new StoreLocal(cursor, iterator, stmt.Span));

        // A Closeable iterator's close (10 B6 I6): a defer in a scope of the loop's own, below
        // the loop's depth, so break and continue stay inside it — the exit runs it once, a return
        // or an error leaving the loop unwinds it.
        if (protocol.Close is { } close) _defers.Push(new List<DeferStmt> { close });
        try { return LowerProtocolLoop(stmt, protocol, loopVar, cursor, cursorType); }
        finally { if (protocol.Close is not null) _defers.Pop(); }
    }

    private bool LowerProtocolLoop(ForInStmt stmt, TypeResult.ForInProtocol protocol, LocalSymbol loopVar,
        LocalId cursor, IrType cursorType)
    {

        var condBlock = _b.NewBlock();
        _b.Seal(new Branch(condBlock, stmt.Span));
        _b.SwitchTo(condBlock);
        var produced = LowerExpr(protocol.NextCall);
        var hasValue = _slots.NewTemp(BoolType);
        _b.Emit(new OptIsSome(hasValue, produced, stmt.Span));
        var bodyBlock = _b.NewBlock();
        var exitBlock = _b.NewBlock();
        _b.Seal(new CondBranch(hasValue, bodyBlock, exitBlock, stmt.Span));

        _b.SwitchTo(bodyBlock);
        var elementType = LowerType(SubstituteType(loopVar.Type), stmt.Span);
        var value = _slots.NewTemp(elementType);
        _b.Emit(new OptGet(value, produced, elementType, stmt.Span));
        var variable = _slots.DeclareFor(loopVar, elementType);
        _b.Emit(new StoreLocal(variable, value, stmt.Span));
        if (stmt.Pattern is { } pattern)
            LowerPattern(pattern, value, elementType,
                () => throw Bug("an irrefutable loop pattern asked for a failure path"), assumeMatch: true);

        _loops.Push(new LoopScope(_b, condBlock, exitBlock) { DeferDepth = _defers.Count, Label = stmt.Label });
        if (LowerScope(stmt.Body)) _b.Seal(new Branch(condBlock, stmt.Body.Span));
        _loops.Pop();

        _b.SwitchTo(exitBlock);
        return protocol.Close is null || EmitLeavingDefers(1);
    }

    /// <summary>
    /// <c>for (i in a..b)</c> and <c>a..=b</c> over a range LITERAL: a counted loop on the element
    /// type, no iterator object (design/v5/spec/13, M2 — the Lyric 5 backend has no runtime
    /// object to hand a range to, and 03 A3 makes a range a value the compiler knows). The bounds
    /// are read once, before the loop. An inclusive range ends on equality with its last value
    /// BEFORE the step, so <c>..= MAX</c> never computes <c>MAX + 1</c> (the 2.0.1 bug, §7.2);
    /// an exclusive one steps only while <c>i &lt; last</c>, where <c>i + 1</c> fits. <c>continue</c>
    /// lands on the step, <c>break</c> after the loop.
    /// </summary>
    private bool LowerCountedRange(ForInStmt stmt, RangeExpr range, LocalSymbol loopVar)
    {
        var span = stmt.Span;
        var elementType = LowerType(loopVar.Type, span);
        var low = LowerExprAs(range.Low, elementType);
        var high = LowerExprAs(range.High, elementType);

        var cursor = _slots.DeclareSynthetic("range", elementType);
        var last = _slots.DeclareSynthetic("last", elementType);
        _b.Emit(new StoreLocal(cursor, low, span));
        _b.Emit(new StoreLocal(last, high, span));

        var condBlock = _b.NewBlock();
        _b.Seal(new Branch(condBlock, span));

        _b.SwitchTo(condBlock);
        var current = _slots.NewTemp(elementType);
        _b.Emit(new LoadLocal(current, cursor, elementType, span));
        var bound = _slots.NewTemp(elementType);
        _b.Emit(new LoadLocal(bound, last, elementType, span));
        var goesOn = _slots.NewTemp(BoolType);
        _b.Emit(new BinOp(goesOn, range.IsInclusive ? IrBinKind.Le : IrBinKind.Lt, BoolType, current, bound, span));

        var bodyBlock = _b.NewBlock();
        var stepBlock = _b.NewBlock();
        var exitBlock = _b.NewBlock();
        _b.Seal(new CondBranch(goesOn, bodyBlock, exitBlock, span));

        _b.SwitchTo(bodyBlock);
        var variable = _slots.DeclareFor(loopVar, elementType);
        _b.Emit(new StoreLocal(variable, current, span));

        _loops.Push(new LoopScope(_b, stepBlock, exitBlock)
            { DeferDepth = _defers.Count, Label = stmt.Label });
        if (LowerScope(stmt.Body)) _b.Seal(new Branch(stepBlock, stmt.Body.Span));
        _loops.Pop();

        _b.SwitchTo(stepBlock);
        var again = _slots.NewTemp(elementType);
        _b.Emit(new LoadLocal(again, cursor, elementType, span));
        if (range.IsInclusive)
        {
            var end = _slots.NewTemp(elementType);
            _b.Emit(new LoadLocal(end, last, elementType, span));
            var done = _slots.NewTemp(BoolType);
            _b.Emit(new BinOp(done, IrBinKind.Eq, BoolType, again, end, span));
            var stepOn = _b.NewBlock();
            _b.Seal(new CondBranch(done, exitBlock, stepOn, span));
            _b.SwitchTo(stepOn);
        }
        var one = _slots.NewTemp(elementType);
        _b.Emit(new Const(one, elementType, new IntConst(1), span));
        var next = _slots.NewTemp(elementType);
        _b.Emit(new BinOp(next, IrBinKind.Add, elementType, again, one, span));
        _b.Emit(new StoreLocal(cursor, next, span));
        _b.Seal(new Branch(condBlock, span));

        _b.SwitchTo(exitBlock);
        return true;
    }

    /// <summary>
    /// Obtains the iterator for a <c>for-in</c> head.
    ///
    /// <para>A value that satisfies <c>Iterator&lt;T&gt;</c> itself is used directly. The built-in forms
    /// get an adapter from <c>std.iter</c>: they have no declaration a conformance could hang on.</para>
    /// </summary>
    // 'Yield' is what the ITERATOR produces when that differs from the loop variable's element
    // type — the range adapters carry i64/u64 while the range may be over a smaller width; the
    // loop converts at the edges. Null everywhere else.
    private (TempId Value, IrType Type, GenericInstance? Owner, IrType? Yield) BuildIterator(ForInStmt stmt)
    {
        // Substituted, because a 'for-in' can stand in a monomorphized instance:
        // 'fn total<T :: [P]>(xs: T[]) { for (x in xs) … }'. Without the substitution the ArrayIterator
        // would be interned with the type PARAMETER, and the type table would look for a class named 'T'.
        var source = SubstituteType(_types.TypeOf(stmt.Iterable));

        // 'Iterable<T>' first: the container SAYS how to walk it and yields a fresh cursor on every call.
        // Two loops over the same list therefore do not disturb each other — if the list were its own
        // iterator, they would.
        //
        // The return type is 'Iterator<T>', so an INTERFACE, and 'next()' therefore goes through
        // callvirt. That is the price of the decoupling and the same route an iterator takes that is
        // available through its interface.
        if (_types.Iterable is { } iterable
            && TypeFacts.SymbolOf(source) is { } carrier
            && Conformance.Implements(carrier, iterable, _typeTable.Binding)
            && carrier.Members.LookupLocal("iter") is FunctionSymbol iterMethod
            && iterMethod.Declaration is FunctionDecl iterDecl)
        {
            var target = source is GenericInstance owning
                ? _instances.RequestMethod(iterMethod, iterDecl, owning, stmt.Span)
                : TryResolveFunction(iterMethod, out var direct)
                    ? direct
                    : throw NotSupported($"'{carrier.Name}.iter' was not lowered", stmt.Span);

            // 'Iterator<T>' with the CONCRETE element type rather than the definition: a generic instance
            // has its own slot table, and 'callvirt' reads the index from it. Where the element type
            // comes from is long known to the sema — it stands on the symbol of the loop variable, and
            // deriving it again here would be a second truth.
            var element = _types.RefOf(stmt) is LocalSymbol bound
                ? SubstituteType(bound.Type)
                : throw Bug($"for-in at {stmt.Span} has no bound loop variable");

            var iteratorDefinition = _types.IteratorInterface ?? throw NotSupported(
                "iterating (std.iter is not on the module path)", stmt.Span);

            var cursorType = new IrInterfaceType(
                _typeTable.Intern(iteratorDefinition, [element]));

            var cursor = _slots.NewTemp(cursorType);
            _b.Emit(new Call(cursor, target, [LowerExpr(stmt.Iterable)], stmt.Span));
            return (cursor, cursorType, null, null);
        }

        if (source is ArrayOf array)
        {
            var owner = new GenericInstance(
                _types.ArrayIterator ?? throw NotSupported(
                    "iterating an array (std.iter is not on the module path)", stmt.Span),
                [array.Element]);

            var type = _typeTable.Intern(owner.Definition, owner.Arguments);
            var instance = _slots.NewTemp(new IrRefType(type));
            _b.Emit(new NewObject(instance, type, new IrRefType(type), stmt.Span));
            _b.Emit(new StoreField(instance, type, new FieldId(0), LowerExpr(stmt.Iterable), stmt.Span));
            _b.Emit(new StoreField(instance, type, new FieldId(1), IntConstant(0, stmt.Span), stmt.Span));
            return (instance, new IrRefType(type), owner, null);
        }

        // A string is walked over its code points, since a 'char' IS a code point. The adapter gets them
        // as an array: 'toChars' extracts them ONCE. An iterator calling 'charAt' instead would have to
        // count from the front on every step and would make the loop quadratic; that is not visible in a
        // 'for (c in s)'.
        if (source is PrimitiveType { Kind: PrimitiveKind.String })
        {
            var symbol = _types.StringIterator ?? throw NotSupported(
                "iterating a string (std.iter is not on the module path)", stmt.Span);

            var type = _typeTable.Intern(symbol);
            // The compiler-bound edge behind 'for (c in s)'. Bound to the private twin since 2.0:
            // the deprecated pub form went with the cut, the native stayed.
            var chars = CallHelper("std.string.rawToChars", stmt.Span, LowerExpr(stmt.Iterable));

            var instance = _slots.NewTemp(new IrRefType(type));
            _b.Emit(new NewObject(instance, type, new IrRefType(type), stmt.Span));
            _b.Emit(new StoreField(instance, type, new FieldId(0), chars, stmt.Span));
            _b.Emit(new StoreField(instance, type, new FieldId(1), IntConstant(0, stmt.Span),
                stmt.Span));
            return (instance, new IrRefType(type), null, null);
        }

        if (source is RangeOf ro && stmt.Iterable is RangeExpr range)
        {
            // Four adapters, not one. Folding 'a..=b' into 'a..b+1' was the 2.0.1 bug — at the
            // type's maximum the '+1' wraps and the loop runs zero times (§7.2); the inclusive
            // adapters carry a done flag instead of arithmetic on the bound. And a full-width
            // uint range cannot ride the SIGNED adapters — a bound beyond 2^63 reinterprets
            // and the comparison calls a range crossing the sign bit empty — so uint has its
            // own pair. Smaller widths embed into the carrier order-preserving; the loop head
            // converts the yielded value back (Yield below).
            var unsigned = ro.Element is PrimitiveType { Kind: PrimitiveKind.Uint };
            var symbol = (range.IsInclusive, unsigned) switch
            {
                (false, false) => _types.RangeIterator,
                (true, false) => _types.InclusiveRangeIterator,
                (false, true) => _types.UnsignedRangeIterator,
                (true, true) => _types.InclusiveUnsignedRangeIterator,
            } ?? throw NotSupported("iterating a range (std.iter is not on the module path)", stmt.Span);

            var type = _typeTable.Intern(symbol);
            var carrierType = new IrScalarType(unsigned ? IrScalar.U64 : IrScalar.I64);
            var boundType = LowerType(ro.Element, stmt.Span);

            TempId Bound(Expr e)
            {
                var raw = LowerExprAs(e, boundType);
                if (boundType.Equals(carrierType)) return raw;
                var widened = _slots.NewTemp(carrierType);
                _b.Emit(new Lyric.Ir.Convert(widened, boundType, carrierType, raw, stmt.Span));
                return widened;
            }

            var low = Bound(range.Low);
            var high = Bound(range.High);

            var instance = _slots.NewTemp(new IrRefType(type));
            _b.Emit(new NewObject(instance, type, new IrRefType(type), stmt.Span));
            _b.Emit(new StoreField(instance, type, new FieldId(0), low, stmt.Span));
            _b.Emit(new StoreField(instance, type, new FieldId(1), high, stmt.Span));
            if (range.IsInclusive)
            {
                var notDone = _slots.NewTemp(BoolType);
                _b.Emit(new Const(notDone, BoolType, new BoolConst(false), stmt.Span));
                _b.Emit(new StoreField(instance, type, new FieldId(2), notDone, stmt.Span));
            }
            return (instance, new IrRefType(type), null, carrierType);
        }

        if (source is PrimitiveType { Kind: PrimitiveKind.String })
            throw NotSupported(
                "iterating a string (std.iter has no adapter for it yet — a string has no "
                + "'length' to walk with)", stmt.Span);

        // A user-written iterator is used directly.
        var own = LowerType(source, stmt.Span);
        return (LowerExpr(stmt.Iterable), own,
            SubstituteType(source) as GenericInstance, null);
    }

    /// <summary>The <c>next()</c> call: virtual when the iterator is available through its interface,
    /// otherwise directly on the instance.</summary>
    private void EmitNextCall(TempId dest, TempId iterator, IrType iteratorType,
        GenericInstance? owner, IrType returns, Core.Span span)
    {
        // When the iterator is available through its interface, only the runtime decides which
        // implementation runs — the one case in which 'for-in' dispatches dynamically.
        if (iteratorType is IrInterfaceType iface)
        {
            var slots = _typeTable.MethodSlotsOf(iface.Type);
            _b.Emit(new CallVirt(dest, iface.Type, Array.IndexOf(slots, "next"), [iterator],
                returns, span));
            return;
        }

        var declaring = owner?.Definition ?? IteratorSymbolOf(iteratorType, span);
        if (declaring.Members.LookupLocal("next") is not FunctionSymbol method
            || method.Declaration is not FunctionDecl decl)
            throw NotSupported($"'{declaring.Name}' has no 'next' to iterate with", span);

        // A concrete iterator is called directly: which function runs is settled, and a callvirt would
        // only consult a table whose answer the compiler already knows.
        var target = owner is not null
            ? _instances.RequestMethod(method, decl, owner, span)
            : TryResolveFunction(method, out var direct)
                ? direct
                : throw NotSupported($"'{declaring.Name}.next' was not lowered", span);

        _b.Emit(new Call(dest, target, [iterator], span));
    }

    /// <summary>The symbol behind a non-generic iterator value.</summary>
    private TypeSymbol IteratorSymbolOf(IrType type, Core.Span span)
    {
        if (type is IrRefType reference)
            foreach (var (symbol, id) in _typeTable.Interned)
                if (id == reference.Type) return symbol;

        throw NotSupported("iterating a value that is not an object", span);
    }

    private TempId IntConstant(long value, Core.Span span)
    {
        var type = new IrScalarType(IrScalar.I64);
        var dest = _slots.NewTemp(type);
        _b.Emit(new Const(dest, type, new IntConst(unchecked((ulong)value)), span));
        return dest;
    }

    private bool LowerWhile(WhileStmt stmt)
    {
        if (stmt.Condition is LetCondExpr letCond) return LowerWhileLet(stmt, letCond);

        var condBlock = _b.NewBlock();
        _b.Seal(new Branch(condBlock, stmt.Span));

        _b.SwitchTo(condBlock);
        var condition = LowerExpr(stmt.Condition);
        var condExit = _b.CurrentId; // the condition may have produced blocks itself (&&, ||)

        var bodyBlock = _b.NewBlock();
        var exitBlock = _b.NewBlock(); // has to stand before the body: 'break' needs its target
        _b.SealBlock(condExit, new CondBranch(condition, bodyBlock, exitBlock, stmt.Condition.Span));

        _b.SwitchTo(bodyBlock);
        _loops.Push(new LoopScope(_b, condBlock, exitBlock) { DeferDepth = _defers.Count, Label = stmt.Label });
        // Through LowerScope, not LowerStatements: the loop body is a SCOPE, and a defer in it
        // runs at every iteration's end (§7.5) — registered into the enclosing function it ran
        // once, with the last iteration's values (the 2.0.1 bug).
        if (LowerScope(stmt.Body)) _b.Seal(new Branch(condBlock, stmt.Body.Span));
        _loops.Pop();

        _b.SwitchTo(exitBlock);
        return true; // always reachable through the condition's false edge
    }

    // ------------------------------------------------------------------ binding conditions

    /// <summary>
    /// <c>if (let P = e) { … } else { … }</c>: the pattern compiler tests and binds, and its failure
    /// target IS the else branch (or the merge, without one). No bool temp exists — the pattern's
    /// tests branch directly, as a match arm's do.
    /// </summary>
    private bool LowerIfLet(IfStmt stmt, LetCondExpr letCond)
    {
        var type = TypeOfExpr(letCond.Initializer);
        var value = LowerExpr(letCond.Initializer);

        BlockId? elseBlock = null;
        BlockId Fail() => elseBlock ??= _b.NewBlock();
        LowerPattern(letCond.Pattern, value, type, Fail, assumeMatch: false);

        var thenBlock = _b.NewBlock();
        _b.Seal(new Branch(thenBlock, stmt.Span));
        _b.SwitchTo(thenBlock);
        var thenFallsThrough = LowerScope(stmt.Then); // a scope: its defers run at its end (§7.5)
        var thenExit = _b.CurrentId;

        // The pattern cannot fail (the sema warned): the else branch is dead and gets no block,
        // because a block nobody can reach is what the verifier refuses.
        if (elseBlock is not { } onFail) return thenFallsThrough;

        _b.SwitchTo(onFail);
        var elseFallsThrough = stmt.Else is null || LowerStmt(stmt.Else);
        var elseExit = _b.CurrentId;

        if (!thenFallsThrough && !elseFallsThrough) return false;

        var mergeBlock = _b.NewBlock();
        if (thenFallsThrough) _b.SealBlock(thenExit, new Branch(mergeBlock, stmt.Then.Span));
        if (elseFallsThrough) _b.SealBlock(elseExit, new Branch(mergeBlock, stmt.Span));
        _b.SwitchTo(mergeBlock);
        return true;
    }

    /// <summary>
    /// <c>loop { … }</c> (05 E11): the body, and back to its start. Only a <c>break</c> leaves — with
    /// a value through a synthetic local the exit reads. The exit exists once a break was lowered
    /// (<see cref="LoopScope"/>): a loop nothing leaves has none, and nothing after it runs — its
    /// type is 'never', or its breaks stood only where no path goes.
    /// </summary>
    private TempId? LowerLoop(LoopExpr loop)
    {
        var valueType = _types.TypeOf(loop) is NeverType ? null : TypeOfExpr(loop);
        LocalId? slot = valueType is not null && !IsVoid(valueType) ? _slots.DeclareSynthetic("loop", valueType) : null;

        var head = _b.NewBlock();
        _b.Seal(new Branch(head, loop.Span));
        _b.SwitchTo(head);
        var scope = new LoopScope(_b, head)
            { DeferDepth = _defers.Count, Label = loop.Label, ValueSlot = slot, ValueType = valueType };
        _loops.Push(scope);
        try { if (LowerScope(loop.Body)) _b.Seal(new Branch(head, loop.Body.Span)); }
        finally { _loops.Pop(); }

        if (!scope.BreakRequested)
        {
            if (slot is not null) throw new Diverged(); // where a value was wanted, the statement ends
            return null;
        }
        _b.SwitchTo(scope.BreakTarget);
        if (slot is not { } kept) return null;
        var dest = _slots.NewTemp(valueType!);
        _b.Emit(new LoadLocal(dest, kept, valueType!, loop.Span));
        return dest;
    }

    /// <summary>
    /// <c>while (let P = e) { … }</c>: the initializer is evaluated and the pattern tried before
    /// every iteration; a miss leaves the loop. The exit block arises on demand — from the
    /// pattern's failure path or from a <c>break</c> — so an irrefutable pattern without a break
    /// creates no unreachable block.
    /// </summary>
    private bool LowerWhileLet(WhileStmt stmt, LetCondExpr letCond)
    {
        var condBlock = _b.NewBlock();
        _b.Seal(new Branch(condBlock, stmt.Span));
        _b.SwitchTo(condBlock);

        var loop = new LoopScope(_b, condBlock) { DeferDepth = _defers.Count };
        var type = TypeOfExpr(letCond.Initializer);
        var value = LowerExpr(letCond.Initializer);
        LowerPattern(letCond.Pattern, value, type, () => loop.BreakTarget, assumeMatch: false);

        var bodyBlock = _b.NewBlock();
        _b.Seal(new Branch(bodyBlock, stmt.Span));
        _b.SwitchTo(bodyBlock);
        _loops.Push(loop);
        if (LowerScope(stmt.Body)) _b.Seal(new Branch(condBlock, stmt.Body.Span));
        _loops.Pop();

        if (!loop.BreakRequested) return false; // the loop never ends: nothing behind it is reachable
        _b.SwitchTo(loop.BreakTarget);
        return true;
    }

    /// <summary>
    /// <c>let P = e;</c> and <c>let P = e else { … };</c> — the value once into a temp, then the
    /// pattern compiler binds; with an else, its failure path runs the block, which the sema
    /// proved leaves. Without one the pattern is irrefutable and no test is emitted.
    /// </summary>
    private bool LowerLetPattern(LetPatternStmt stmt)
    {
        var type = TypeOfExpr(stmt.Initializer);
        var value = LowerExprAs(stmt.Initializer, type);

        BlockId? elseBlock = null;
        LowerPattern(stmt.Pattern, value, type, () => elseBlock ??= _b.NewBlock(),
            assumeMatch: stmt.Else is null);
        if (elseBlock is not { } onFail) return true; // nothing could fail: no else path exists

        var after = _b.NewBlock();
        _b.Seal(new Branch(after, stmt.Span));

        _b.SwitchTo(onFail);
        // The block leaves on every path (LYR-SEM0098); should it fall through regardless, the
        // continuation would run with unbound names, and 'unreachable' is the honest terminator.
        if (LowerScope(stmt.Else!)) _b.Seal(new Unreachable(stmt.Else!.Span));

        _b.SwitchTo(after);
        return true;
    }

    /// <summary>
    /// <c>do { … } while (cond);</c> — the body runs at least once, the condition stands behind it.
    ///
    /// <para>That makes it the only loop whose condition can be unreachable. If the body terminates on
    /// every path (<c>do { return 1; } while (true);</c>) nobody arrives at it, and the verifier rejects
    /// an unreachable block, because there is no <c>SimplifyCfg</c> pass.</para>
    ///
    /// <para>The blocks therefore arise ON DEMAND (see <see cref="LoopScope"/>). The question is "did
    /// anyone jump here", not "does the body fall through": a <c>break</c> reaches the exit even from a
    /// body that does not fall through.</para>
    /// </summary>
    private bool LowerDoWhile(DoWhileStmt stmt)
    {
        var bodyBlock = _b.NewBlock();
        _b.Seal(new Branch(bodyBlock, stmt.Span));

        _b.SwitchTo(bodyBlock);
        var loop = new LoopScope(_b) { DeferDepth = _defers.Count, Label = stmt.Label };

        // The targets a 'break' or 'continue' really reaches are reserved HERE, before the body:
        // created on demand from inside a try or a defer scope they would land in that region's
        // block range and put the code after the loop under its handler. See LoopScope.Reserve.
        // A jump naming ANOTHER label leaves this loop and reserves nothing.
        if (Flow.ReachesJump(stmt.Body, wantContinue: true, stmt.Label, _types)) loop.Reserve(continueTarget: true);
        if (Flow.ReachesJump(stmt.Body, wantContinue: false, stmt.Label, _types)) loop.Reserve(continueTarget: false);

        _loops.Push(loop);
        var fallsThrough = LowerScope(stmt.Body);
        _loops.Pop();

        // The condition is needed when the body falls through OR a 'continue' jumps to it. Otherwise it
        // does not exist, and neither does the false edge to the exit.
        if (fallsThrough || loop.ContinueRequested)
        {
            var condBlock = loop.ContinueTarget;
            if (fallsThrough) _b.Seal(new Branch(condBlock, stmt.Body.Span));

            _b.SwitchTo(condBlock);
            var condition = LowerExpr(stmt.Condition);
            _b.Seal(new CondBranch(condition, bodyBlock, loop.BreakTarget, stmt.Condition.Span));
        }

        // No exit: the loop is never left. Control flow does not fall through here, and that is what
        // this method reports upwards, rather than leaving a block nobody enters.
        if (!loop.BreakRequested) return false;

        _b.SwitchTo(loop.BreakTarget);
        return true;
    }

    // ------------------------------------------------------------------ expressions

    private TempId LowerExpr(Expr expr) =>
        LowerExprOrVoid(expr) ?? (Diverges(expr) ? Diverge(expr) : throw Bug($"expression at {expr.Span} produced no value"));

    /// <summary>An expression of type 'never' where a value is wanted: its block ends with the
    /// 'unreachable' its type promises — a call of a never-declared function is an ordinary call and
    /// does not seal — and the enclosing expression stops (<see cref="Diverged"/>).</summary>
    private TempId Diverge(Expr expr)
    {
        if (!_b.IsSealed) _b.Seal(new Unreachable(expr.Span));
        throw new Diverged();
    }

    /// <summary>Returns null for an expression that is worth nothing — a call of a void function,
    /// an 'if' or a 'match' whose every branch is one — and for one that gives no value and has
    /// sealed its block. Otherwise a temp.</summary>
    private TempId? LowerExprOrVoid(Expr expr) =>
        _chainReceivers.TryGetValue(expr, out var alreadyUnwrapped) ? alreadyUnwrapped : expr switch
    {
        IntLiteralExpr e => LowerIntLiteral(e),
        FloatLiteralExpr e => LowerFloatLiteral(e),
        BoolLiteralExpr e => EmitConst(new BoolConst(e.Value), TypeOfExpr(e), e.Span),
        CharLiteralExpr e => EmitConst(new CharConst(e.CodePoint), TypeOfExpr(e), e.Span),
        StringLiteralExpr e => EmitConst(new StringConst(e.Value), TypeOfExpr(e), e.Span),
        IdentifierExpr e => LowerIdentifier(e),
        TypePathExpr e => LowerInstantiatedFunction(e),
        ImplicitMemberExpr e => LowerImplicitMember(e),
        UnaryExpr e => LowerUnary(e),
        PostfixExpr e => LowerPostfix(e),
        BinaryExpr e => LowerBinary(e),
        AssignExpr e => LowerAssign(e),
        CastExpr e => LowerCast(e),
        TypeTestExpr typeTest => LowerTypeTest(typeTest),
        CallExpr e => LowerCall(e),
        IfExpr e => LowerIfExpr(e),
        BlockExpr e => LowerBlockExpr(e),
        LoopExpr e => LowerLoop(e),

        InterpolatedStringExpr e => LowerInterpolatedString(e),

        NullLiteralExpr e => LowerNull(e),
        LambdaExpr e => LowerLambda(e),
        TupleLitExpr e => LowerTupleLiteral(e),
        WithExpr e => LowerWith(e),
        MatchExpr e => LowerMatchExpr(e),
        MemberExpr e => LowerFieldRead(e),
        IndexExpr e => LowerIndexRead(e),
        ArrayLitExpr e => LowerArrayLiteral(e),
        StructInitExpr e => LowerObjectInit(e),
        RangeExpr e => LowerRangeValue(e),
        ComptimeExpr e => LowerComptime(e),
        ThrowExpr e => LowerThrowExpr(e),
        TryExpr e => LowerTryExpr(e),
        ThisExpr e => LowerThis(e),
        AtIdentifierExpr e => throw NotSupported($"attribute '{e.Name}'", e.Span),
        ErrorExpr e => throw Bug($"error expression reached lowering at {e.Span}"),

        _ => throw Bug($"unhandled expression {expr.GetType().Name}")
    };

    private TempId LowerIntLiteral(IntLiteralExpr expr)
    {
        var type = TypeOfExpr(expr);

        // An untyped integer literal in float context IS a float value; it is not converted. A
        // `let f: float = 5;` therefore has to become a FloatConst — an IntConst with a float type would
        // be malformed, and the verifier says so.
        if (type is IrScalarType { Kind: IrScalar.F32 or IrScalar.F64 })
            return EmitConst(new FloatConst(expr.Value), type, expr.Span);

        // The encoding of IntConst is two's complement, zero-extended to 64 bits. The parser yields the
        // magnitude; a minus sign is a UnaryExpr(Neg) of its own.
        return EmitConst(new IntConst(expr.Value), type, expr.Span);
    }

    private TempId LowerFloatLiteral(FloatLiteralExpr expr)
    {
        var type = TypeOfExpr(expr);
        // f32 has to be narrowed here: a const of type f32 whose value is no f32 value would be
        // malformed, and the verifier reports it. The narrowing belongs in the lowering, so the value in
        // the bytecode is deterministically the same.
        var value = type is IrScalarType { Kind: IrScalar.F32 } ? (float)expr.Value : expr.Value;
        return EmitConst(new FloatConst(value), type, expr.Span);
    }

    private TempId LowerIdentifier(IdentifierExpr expr)
    {
        var symbol = _types.RefOf(expr) ?? throw Bug($"identifier '{expr.Name}' is unbound");

        // A parameter of the CALLEE, named in a default that is being lowered at this call site
        // (04 D5 F2): the argument materialized for it.
        if (symbol is ParameterSymbol && _argumentOverrides.TryGetValue(symbol, out var passed)) return passed;

        // A module 'let' has no frame slot but a global one.
        if (TryLowerGlobalIdentifier(expr) is { } global) return global;

        // In a lifted lambda a captured symbol lies in the environment rather than in a slot. Slots are
        // asked first: a local symbol of the same name IS a different symbol, and reference equality
        // keeps the two apart.
        if (!_slots.TryLookup(symbol, out var slot))
        {
            if (_captureFields.ContainsKey(symbol))
            {
                var (capturedType, capturedValue) = LoadCaptured(symbol, expr.Span);

                // When the captured thing is a cell, its content is what is at issue here rather than
                // the cell itself: the cell is a carrier, not a value of the program.
                if (capturedType is IrRefType reference && _typeTable.IsCell(reference.Type))
                {
                    var inner = _typeTable.Defs[reference.Type.Value].FieldTypes[0];
                    var unwrapped = _slots.NewTemp(inner);
                    _b.Emit(new LoadField(unwrapped, capturedValue, reference.Type, new FieldId(0),
                        inner, expr.Span));
                    return Narrow(expr, unwrapped, inner);
                }

                return Narrow(expr, capturedValue, capturedType);
            }

            // A declared function as a VALUE: 'map(o, double)' rather than
            // 'map(o, (n: int) => double(n))'.
            //
            // It is a closure without an environment, nothing more. 'MakeClosure' takes its environment
            // optionally — the common case '(x) => x > 0' captures nothing — and the VM decides from the
            // 'HasEnvironment' bit whether slot 0 is occupied.
            //
            // 'Reachability' already knows 'MakeClosure' as a root, so a function referenced only this
            // way does not fall victim to the reachability analysis.
            //
            // A selective import binds its name to a shell — under the function's own name or
            // another, from the module that declares it or one that passes it on (07 V3 I2, I4) —
            // and the function beneath is what the name means, here as at a call.
            var meant = symbol;
            while (meant is ImportBindingSymbol shell) meant = shell.Target;
            if (meant is FunctionSymbol function)
            {
                // BEFORE the type computation: `TypeOfExpr` on a generic signature throws itself, with
                // "type parameter 'T' reached lowering unsubstituted" — a message about the compiler's
                // internals rather than about the program.
                if (function.Declaration is FunctionDecl { Generics.Length: > 0 })
                    throw NotSupported(
                        $"a generic function ('{expr.Name}') as a value — the type arguments have "
                        + "no call site to come from; wrap it in a lambda", expr.Span);

                if (TypeOfExpr(expr) is not IrFunctionType signature
                    || !TryResolveFunction(function, out var target))
                    throw NotSupported($"reference to '{expr.Name}' as a value", expr.Span);

                var closure = _slots.NewTemp(signature);
                _b.Emit(new MakeClosure(closure, target, null, signature, expr.Span));
                return closure;
            }

            throw NotSupported($"reference to '{expr.Name}' (only parameters, locals and constants)",
                expr.Span);
        }

        return Narrow(expr, LoadValue(slot, expr.Span), ValueTypeOf(slot));
    }

    /// <summary>
    /// Flow narrowing: after <c>if (x != null)</c> the sema says the type of x is T, while the place in
    /// memory still holds ?T — the narrowing is a statement about control flow, not about memory. It is
    /// redeemed here: the lowerer unwraps where the sema expects T.
    ///
    /// <para>That this is sound was proven by the sema, which narrows only where it excluded null. The
    /// <c>optget</c> can therefore never panic; it is the materialization of a proof already made.</para>
    ///
    /// <para>Pulled out when captures arrived: a captured <c>?T</c> needs the same narrowing as a local
    /// one, and two copies of the same four lines would have been two places it could have been missing
    /// from.</para>
    /// </summary>
    private TempId Narrow(Expr expr, TempId value, IrType type)
    {
        // One 'optget' per level the sema narrowed away: a '??T' proven present once is a '?T',
        // twice a 'T' (03 T4 O3). The slot keeps its declared depth; the expression has its own.
        var wanted = OptionalDepth(TypeOfExpr(expr));
        while (type is IrOptionalType option && OptionalDepth(type) > wanted)
        {
            var narrowed = _slots.NewTemp(option.Inner);
            _b.Emit(new OptGet(narrowed, value, option.Inner, expr.Span));
            (value, type) = (narrowed, option.Inner);
        }

        // 'if (x is Circle)' (03 T11): the sema says 'Circle' where the slot holds the interface
        // value — the downcast is the materialization of the test that guards the branch.
        if (type is IrInterfaceType && TypeOfExpr(expr) is { } proven && proven is not IrOptionalType && !proven.Equals(type))
        {
            var cast = _slots.NewTemp(proven);
            _b.Emit(new Downcast(cast, value, TargetIdOf(proven, expr.Span), proven, expr.Span));
            if (proven is IrStructType or IrInlineArrayType) _fresh.Add(cast); // a copy out of the box (04 D12)
            return cast;
        }
        return value;
    }

    /// <summary>The type a test names, as the entry a descriptor identifies.</summary>
    private TypeId TargetIdOf(IrType type, Span span) => type switch
    {
        IrRefType r => r.Type,
        IrStructType s => s.Type,
        IrEnumType e => e.Type,
        IrInterfaceType i => i.Type,
        _ => throw NotSupported($"a type test against '{type}'", span),
    };

    /// <summary><c>x is T</c> (03 T11): a bool off the interface value's table.</summary>
    private TempId LowerTypeTest(TypeTestExpr test)
    {
        var value = LowerExpr(test.Operand);
        if (TypeOfExpr(test.Operand) is not IrInterfaceType)
            throw NotSupported("'is' on a value that is no interface value", test.Span);
        var target = _typeTable.Lower(test.Type);
        var dest = _slots.NewTemp(BoolType);
        _b.Emit(new TypeTest(dest, value, TargetIdOf(target, test.Span), test.Span));
        return dest;
    }

    /// <summary>How many optional levels a type has: 0 for <c>T</c>, 2 for <c>??T</c>.</summary>
    private static int OptionalDepth(IrType type)
    {
        var depth = 0;
        for (; type is IrOptionalType option; type = option.Inner) depth++;
        return depth;
    }

    /// <summary>The four orderings off a <c>?Ordering</c>: present and the right tag — read in
    /// a branch, since the tag of an absent answer is nothing to read.</summary>
    private TempId LowerOrderingAnswer(BinaryOp op, TempId answer, IrOptionalType answered, IrEnumType ordering, Span span)
    {
        var result = _slots.DeclareSynthetic("ordered", BoolType);
        var present = _slots.NewTemp(BoolType);
        _b.Emit(new OptIsSome(present, answer, span));
        var some = _b.NewBlock();
        var none = _b.NewBlock();
        var merge = _b.NewBlock();
        _b.Seal(new CondBranch(present, some, none, span));

        _b.SwitchTo(none);
        StoreValue(result, EmitConst(new BoolConst(false), BoolType, span), span);
        _b.Seal(new Branch(merge, span));

        _b.SwitchTo(some);
        var inner = _slots.NewTemp(answered.Inner);
        _b.Emit(new OptGet(inner, answer, answered.Inner, span));
        var tag = TagOf(inner, span);
        // '<' is Less, '>' is Greater; '<=' is anything but Greater, '>=' anything but Less.
        var (variant, equal) = op switch
        {
            BinaryOp.Lt => ("Less", true),
            BinaryOp.Gt => ("Greater", true),
            BinaryOp.Le => ("Greater", false),
            _ => ("Less", false),
        };
        var wanted = EmitConst(new IntConst((ulong)_typeTable.TagOf(ordering.Type, variant, span)), new IrScalarType(IrScalar.I64), span);
        var holds = _slots.NewTemp(BoolType);
        _b.Emit(new BinOp(holds, equal ? IrBinKind.Eq : IrBinKind.Ne, BoolType, tag, wanted, span));
        StoreValue(result, holds, span);
        _b.Seal(new Branch(merge, span));

        _b.SwitchTo(merge);
        return LoadValue(result, span);
    }

    private TempId LowerUnary(UnaryExpr expr)
    {
        if (expr.Operator is UnaryOp.Place) return LowerPlace(expr.Operand);

        // '-v' and '~v' on a conforming type ARE their calls (04 D6).
        if (_types.OperatorCallOf(expr) is { } desugaredUnary)
            return LowerCall(desugaredUnary) ?? throw Bug($"operator method for '{expr.Operator}' returned no value");

        if (expr.Operator is UnaryOp.PreInc or UnaryOp.PreDec)
            return LowerIncDec(expr.Operand, expr.Operator is UnaryOp.PreInc,
                yieldOldValue: false, expr.Span);

        // '-128' as an int8 is one literal, not a negation of 128, which no int8 holds: the sema
        // typed both nodes with the adapted type (3.1), and the value folds here, into the
        // two's-complement bits at the type's width, so no checked negation ever runs on a
        // magnitude the type has no room for. A float negates as an instruction; nothing
        // overflows there.
        if (expr.Operator is UnaryOp.Neg && expr.Operand is IntLiteralExpr literal
            && TypeOfExpr(expr) is IrScalarType { Kind: not (IrScalar.F32 or IrScalar.F64) } integer)
        {
            var bits = unchecked(0UL - literal.Value);
            var width = integer.Kind switch
            {
                IrScalar.I8 or IrScalar.U8 => 8,
                IrScalar.I16 or IrScalar.U16 => 16,
                IrScalar.I32 or IrScalar.U32 => 32,
                _ => 64,
            };
            if (width < 64) bits &= (1UL << width) - 1;
            return EmitConst(new IntConst(bits), integer, expr.Span);
        }

        var operand = LowerExpr(expr.Operand);
        var type = TypeOfExpr(expr);
        var dest = _slots.NewTemp(type);
        _b.Emit(new UnOp(dest, IrUnKindExtensions.FromAst(expr.Operator), type, operand, expr.Span));
        return dest;
    }

    private TempId LowerPostfix(PostfixExpr expr) => expr.Operator switch
    {
        PostfixOp.Inc => LowerIncDec(expr.Operand, increment: true, yieldOldValue: true, expr.Span),
        PostfixOp.Dec => LowerIncDec(expr.Operand, increment: false, yieldOldValue: true, expr.Span),
        PostfixOp.ForceUnwrap => LowerForceUnwrap(expr.Operand, expr.Span),
        _ => throw Bug($"unhandled postfix operator {expr.Operator}")
    };

    /// <summary><c>++</c> and <c>--</c> in both positions: prefix yields the new value, postfix the old.
    /// Both write the same store.</summary>
    private TempId LowerIncDec(Expr target, bool increment, bool yieldOldValue, Span span)
    {
        var (type, load, store) = AccessOf(target, span);
        var oldValue = load();

        var one = EmitConst(OneFor(type, span), type, span);
        var newValue = _slots.NewTemp(type);
        _b.Emit(new BinOp(newValue, increment ? IrBinKind.Add : IrBinKind.Sub, type,
            oldValue, one, span));
        store(newValue);

        return yieldOldValue ? oldValue : newValue;
    }

    private TempId LowerBinary(BinaryExpr expr)
    {
        if (expr.Operator is BinaryOp.LogicalAnd or BinaryOp.LogicalOr)
            return LowerShortCircuit(expr);
        if (expr.Operator is BinaryOp.Coalesce)
            return LowerCoalesce(expr);
        if (TryLowerNullTest(expr) is { } nullTest)
            return nullTest;

        // An operator on a conforming type IS a method call, built and checked by the sema. Lowering
        // the stored call routes through the ordinary dispatch, so every receiver shape — plain,
        // generic instance, extension, constraint — behaves exactly as the written call would. The
        // operands are the REAL operand nodes, lowered once, here.
        //
        // What to make of the result follows from the operator on this node: '==' is the call
        // itself, '!=' negates it, and the four orderings read the SIGN of what 'compare' answered —
        // against zero, with the same comparison instruction an 'int < int' emits.
        // '[x] * 64' as a 'T[64]' (10 C7): the inline array, its one element repeated.
        if (TypeOfExpr(expr) is IrInlineArrayType repeated
            && expr is { Operator: BinaryOp.Mul, Left: ArrayLitExpr { Elements: [var one] } })
        {
            var element = LowerExprAs(one, repeated.Element);
            var built = _slots.NewTemp(repeated);
            _b.Emit(new NewInline(built, repeated.Element, repeated.Length, [element], Repeat: true, expr.Span));
            _fresh.Add(built);
            return built;
        }

        if (_types.OperatorCallOf(expr) is { } desugared)
        {
            var value = LowerCall(desugared)
                        ?? throw Bug($"operator method for '{expr.Operator}' returned no value");

            // 'compare' answers a '?Ordering' (04 D6): '<' is '.Less', '<=' is not '.Greater', and
            // so on; 'null' — the two are not ordered — makes every one of the four false.
            if (expr.Operator is BinaryOp.Lt or BinaryOp.Le or BinaryOp.Gt or BinaryOp.Ge
                && TypeOfExpr(desugared) is IrOptionalType { Inner: IrEnumType ordering } answered)
                return LowerOrderingAnswer(expr.Operator, value, answered, ordering, expr.Span);

            switch (expr.Operator)
            {
                // Equality and arithmetic ARE their calls; nothing follows.
                case BinaryOp.Eq or BinaryOp.Add or BinaryOp.Sub or BinaryOp.Mul or BinaryOp.Div or BinaryOp.Rem
                    or BinaryOp.BitAnd or BinaryOp.BitOr or BinaryOp.BitXor or BinaryOp.Shl or BinaryOp.Shr:
                    return value;

                case BinaryOp.Ne:
                    var negated = _slots.NewTemp(BoolType);
                    _b.Emit(new UnOp(negated, IrUnKind.Not, BoolType, value, expr.Span));
                    return negated;

                default:
                    var zero = EmitConst(new IntConst(0),
                        new IrScalarType(IrScalar.I64), expr.Span);
                    return EmitBinary(IrBinKindExtensions.FromAst(expr.Operator),
                        TypeOfExpr(expr), value, zero, expr.Span);
            }
        }

        var kind = IrBinKindExtensions.FromAst(expr.Operator);
        var lhs = LowerExpr(expr.Left);
        var rhs = LowerExpr(expr.Right);
        return EmitBinary(kind, TypeOfExpr(expr), lhs, rhs, expr.Span);
    }

    /// <summary>
    /// Applies a binary operator to two lowered operands of <paramref name="type"/>.
    ///
    /// <para>THE ONLY PLACE THAT DECIDES WHAT AN OPERATOR BECOMES. <c>a + b</c> and <c>a += b</c> are the
    /// same operator on the same types and have to reach the same instruction; when the compound paths
    /// emitted their own <c>BinOp</c>, <c>s += "x"</c> produced <c>add string</c>, which no release build
    /// rejects and the VM evaluates as an integer addition of two references.</para>
    ///
    /// <para><paramref name="type"/> is the type of the RESULT, which for a comparison is <c>bool</c>
    /// while the operands are not — hence the comparison guard on the string branch.</para>
    /// </summary>
    private TempId EmitBinary(IrBinKind kind, IrType type, TempId lhs, TempId rhs, Span span)
    {
        // xs + ys and xs * n are built-in language semantics but NO BinOp: the add opcode would otherwise
        // stay polymorphic and would have to dispatch on the type at runtime — the same reasoning as for
        // string + string, only with an instruction of its own instead of a call.
        if (type is IrArrayType array)
        {
            var built = _slots.NewTemp(type);
            _b.Emit(kind switch
            {
                IrBinKind.Add => new ArrayConcat(built, lhs, rhs, array.Element, span),
                IrBinKind.Mul => new ArrayRepeat(built, lhs, rhs, array.Element, span),
                _ => throw NotSupported($"'{IrNames.Bin(kind)}' on arrays", span),
            });
            return built;
        }

        // '+' and '*' are overloaded for string; that is built-in semantics but NO BinOp — the add opcode
        // would otherwise be polymorphic and would have to dispatch on the type at runtime. It lowers to
        // a call in std.string, exactly as the f-string lowering assembles its parts.
        if (!kind.IsComparison() && type is IrScalarType { Kind: IrScalar.String })
            return kind switch
            {
                IrBinKind.Add => CallHelper("std.string.concat", span, lhs, rhs),
                IrBinKind.Mul => CallHelper("std.string.repeat", span, lhs, rhs),
                _ => throw NotSupported($"'{IrNames.Bin(kind)}' on strings", span),
            };

        var dest = _slots.NewTemp(type);
        _b.Emit(new BinOp(dest, kind, type, lhs, rhs, span));
        return dest;
    }

    /// <summary>
    /// <c>a &amp;&amp; b</c> and <c>a || b</c>: the right operand may run only conditionally, so control
    /// flow. The result travels through a synthetic local, because a temp may be defined only once.
    /// </summary>
    private TempId LowerShortCircuit(BinaryExpr expr)
    {
        var isAnd = expr.Operator is BinaryOp.LogicalAnd;
        var slot = _slots.DeclareSynthetic(isAnd ? "and" : "or", BoolType);

        var left = LowerExpr(expr.Left);
        _b.Emit(new StoreLocal(slot, left, expr.Left.Span));

        var rhsBlock = _b.NewBlock();
        var mergeBlock = _b.NewBlock();
        // '&&' evaluates the right side only on true, '||' only on false; the edges are swapped.
        _b.Seal(isAnd
            ? new CondBranch(left, rhsBlock, mergeBlock, expr.Span)
            : new CondBranch(left, mergeBlock, rhsBlock, expr.Span));

        // A right side that gives no value ('ok || fail("…")') seals its own block: the merge then
        // has the short edge alone, and the slot holds the left side's value.
        _b.SwitchTo(rhsBlock);
        if (Diverges(expr.Right)) LowerDiverging(expr.Right);
        else if (ComesOut(() => _b.Emit(new StoreLocal(slot, LowerExpr(expr.Right), expr.Right.Span))))
            _b.Seal(new Branch(mergeBlock, expr.Right.Span));

        _b.SwitchTo(mergeBlock);
        var dest = _slots.NewTemp(BoolType);
        _b.Emit(new LoadLocal(dest, slot, BoolType, expr.Span));
        return dest;
    }

    /// <summary>Like <see cref="LowerShortCircuit"/>, but with two writing branches. A branch
    /// that comes out has stored its value and reaches the merge; one that gives no value — as a
    /// whole ('else throw e'), or because an operand in it does — ends where it stands, and the
    /// merge has the other as its only predecessor. An 'if' nobody takes a value from — both
    /// branches give none (05 E12), or are worth nothing, as two calls of void functions are —
    /// has no slot.</summary>
    private TempId? LowerIfExpr(IfExpr expr)
    {
        var sema = _types.TypeOf(expr);
        var type = sema is NeverType || TypeFacts.IsVoid(sema) ? null : TypeOfExpr(expr);
        LocalId? slot = type is null ? null : _slots.DeclareSynthetic("if", type);

        var condition = LowerExpr(expr.Condition);
        var thenBlock = _b.NewBlock();
        var elseBlock = _b.NewBlock();
        _b.Seal(new CondBranch(condition, thenBlock, elseBlock, expr.Span));

        _b.SwitchTo(thenBlock);
        var thenExit = Lowered(expr.Then);
        _b.SwitchTo(elseBlock);
        var elseExit = Lowered(expr.Else);

        // Neither branch comes out: no merge — it would have no predecessor — and the expression
        // ends where it stands.
        if (thenExit is null && elseExit is null) throw new Diverged();

        var mergeBlock = _b.NewBlock();
        if (thenExit is { } t) _b.SealBlock(t, new Branch(mergeBlock, expr.Then.Span));
        if (elseExit is { } e) _b.SealBlock(e, new Branch(mergeBlock, expr.Else.Span));

        _b.SwitchTo(mergeBlock);
        if (slot is not { } result || type is null) return null;
        var dest = _slots.NewTemp(type);
        _b.Emit(new LoadLocal(dest, result, type, expr.Span));
        return dest;

        // A branch, where the builder stands: the block control leaves it from, or null.
        //
        // `LowerExprAs` rather than `LowerExpr`: the branch type need not be the result type.
        // `if (c) 5 else null` is `?int`, and both branches need the target type — the `null`
        // because it has none of its own, and the `5` because it has to be wrapped.
        BlockId? Lowered(Expr branch)
        {
            if (Diverges(branch)) { LowerDiverging(branch); return null; }
            var comesOut = ComesOut(() =>
            {
                if (slot is { } target && type is not null) _b.Emit(new StoreLocal(target, LowerExprAs(branch, type), branch.Span));
                else LowerExprOrVoid(branch);
            });
            return comesOut ? _b.CurrentId : null;
        }
    }


    /// <summary>
    /// A value block as an expression (08 Y4): its statements in a scope of their own — its
    /// defers run at its end — and its tail into a slot, which is the block's value. A block
    /// nobody takes a value from has no slot: it is worth nothing, or it leaves. One that does
    /// not come out — it leaves, or an operand in it gives no value — ends where it stands.
    /// </summary>
    private TempId? LowerBlockExpr(BlockExpr expr)
    {
        var sema = _types.TypeOf(expr);
        var type = sema is NeverType || TypeFacts.IsVoid(sema) ? null : TypeOfExpr(expr);
        LocalId? slot = type is null ? null : _slots.DeclareSynthetic("block", type);

        var savedSink = _tailSink;
        _tailSink = new TailSink(slot, type, AsReturn: false);
        bool comesOut;
        try { comesOut = LowerScope(expr.Block); }
        finally { _tailSink = savedSink; }
        if (!comesOut) throw new Diverged();

        if (slot is not { } result || type is null) return null;
        var dest = _slots.NewTemp(type);
        _b.Emit(new LoadLocal(dest, result, type, expr.Span));
        return dest;
    }

    /// <summary>
    /// <c>&amp;&amp;=</c>, <c>||=</c> and <c>??=</c> on anything that can be loaded and stored:
    /// a local, a field, an element.
    ///
    /// <para>ALL THREE ARE SHORT-CIRCUIT, which is the whole reason they are not
    /// <c>x = x op e</c>: <c>b &amp;&amp;= f()</c> must not call <c>f</c> when <c>b</c> is already
    /// false, and <c>o ??= f()</c> must not call it when <c>o</c> already holds a value. An
    /// assignment that evaluates its right side regardless is a different program.</para>
    ///
    /// <para>One helper for the three targets because they differ in exactly one place — which way
    /// the test branches — and §2 lists the operators in one production. Three copies of a
    /// four-block diamond is how the versions that existed came to disagree: <c>??=</c> worked on a
    /// local and was an internal error on a field and on an element, and the short-circuit pair was
    /// an internal error on all three.</para>
    ///
    /// <para>The caller has already evaluated the receiver and the index, so <paramref name="load"/>
    /// and <paramref name="store"/> touch them once each however often they are invoked — which is
    /// what keeps <c>xs[next()] ??= v</c> from calling <c>next</c> twice.</para>
    /// </summary>
    private TempId LowerShortCircuitAssign(AssignExpr expr, IrType type,
        Func<TempId> load, Action<TempId> store)
    {
        var current = load();

        TempId test;
        if (expr.Operator is BinaryOp.Coalesce)
        {
            if (type is not IrOptionalType)
                throw NotSupported("'??=' on a non-optional target", expr.Span,
                    LoweringDiagnostics.NeverNull);

            test = _slots.NewTemp(BoolType);
            _b.Emit(new OptIsSome(test, current, expr.Span));
        }
        else
        {
            if (type is not IrScalarType { Kind: IrScalar.Bool })
                throw NotSupported(
                    $"'{(expr.Operator is BinaryOp.LogicalAnd ? "&&=" : "||=")}' on a non-bool target",
                    expr.Span);

            test = current;
        }

        var assign = _b.NewBlock();
        var merge = _b.NewBlock();

        // '&&=' assigns when the target is TRUE; '||=' and '??=' assign when it is not. That one
        // line is the whole difference between the three.
        _b.Seal(expr.Operator is BinaryOp.LogicalAnd
            ? new CondBranch(test, assign, merge, expr.Span)
            : new CondBranch(test, merge, assign, expr.Span));

        _b.SwitchTo(assign);
        if (ComesOut(() => store(LowerExprAs(expr.Value, type)))) _b.Seal(new Branch(merge, expr.Span));

        _b.SwitchTo(merge);
        return load();
    }

    private TempId LowerAssign(AssignExpr expr)
    {
        // 'this = value' in a 'mut fn' of a struct replaces the caller's value as a whole
        // (02 M4): 'this' is the caller's place (M5), and the store reaches it.
        if (expr.Target is ThisExpr && expr.Operator is null && _thisSlot is { } self && _thisType is { } selfType)
        {
            var replacement = LowerExprAs(expr.Value, selfType);
            _b.Emit(new StoreLocal(self, replacement, expr.Span));
            return replacement;
        }

        if (expr.Target is MemberExpr member) return LowerFieldAssign(member, expr);
        // 'x[k] = v' through IndexSet (04 D6): the value first, as an element's store has it, then
        // the desugared 'x.setIndex(k, v)' with that value, which is the assignment's.
        if (expr.Target is IndexExpr && _types.OperatorCallOf(expr) is { } setCall)
        {
            var stored = LowerExpr(expr.Value);
            _chainReceivers[expr.Value] = stored;
            try { LowerCall(setCall); }
            finally { _chainReceivers.Remove(expr.Value); }
            return stored;
        }
        if (expr.Target is IndexExpr indexed) return LowerElementAssign(indexed, expr);

        if (TryCapturedCell(expr.Target, out var cell, out var cellType, out var cellValueType))
            return LowerCapturedAssign(expr, cell, cellType, cellValueType);

        // A module-level 'var' (07 V5 G5): the global slot, read and written as a local is.
        if (expr.Target is IdentifierExpr globalName && GlobalOf(globalName) is { } global)
            return LowerGlobalAssign(expr, global);

        var slot = ResolveLocalTarget(expr.Target, "assignment");

        if (expr.Operator is null)
        {
            // The slot type is the expected shape; otherwise 'var d: Damageable; d = p;' would put a bare
            // class reference into an interface slot.
            var value = LowerExprAs(expr.Value, ValueTypeOf(slot));
            StoreValue(slot, value, expr.Span);
            return value;
        }

        if (expr.Operator is BinaryOp.Coalesce or BinaryOp.LogicalAnd or BinaryOp.LogicalOr)
        {
            var slotType = ValueTypeOf(slot);
            return LowerShortCircuitAssign(expr, slotType,
                () => LoadValue(slot, expr.Target.Span),
                value => StoreValue(slot, value, expr.Span));
        }

        // Through the operator interface when the sema desugared one: the stored call lowers the
        // real operand nodes — the identifier receiver loads once, exactly like the read below.
        if (_types.OperatorCallOf(expr) is { } operatorCall)
        {
            var combined = LowerCall(operatorCall)
                ?? throw Bug("operator compound returned no value");
            StoreValue(slot, combined, expr.Span);
            return combined;
        }

        var type = ValueTypeOf(slot);
        var current = LoadValue(slot, expr.Target.Span);

        var operand = LowerExpr(expr.Value);
        var result = EmitBinary(IrBinKindExtensions.FromAst(expr.Operator.Value), type,
            current, operand, expr.Span);
        StoreValue(slot, result, expr.Span);
        return result;
    }

    /// <summary>
    /// <c>resume co</c> — continue the coroutine and fetch the next value.
    ///
    /// <para>An ordinary <c>callind</c>: the coroutine value IS a function value over the state object.
    /// The jump table in the body makes the call continue where the last <c>yield</c> stopped; from here
    /// it looks like any other call, and that is the whole point of the transformation.</para>
    /// </summary>
    /// <summary>
    /// A <c>comptime</c> site. With the evaluator's values in hand it is a constant; in the
    /// hoisting pass and in a check it is the inner expression, computed where it stands — the
    /// same value, so the two lowerings agree by construction. See <see cref="ComptimeTable"/>.
    /// </summary>
    private TempId? LowerComptime(ComptimeExpr expr)
    {
        if (_typeTable.Comptime is { Values: { } values })
        {
            var index = _types.ComptimeSites.IndexOf(expr);
            if (index < 0 || !values.TryGetValue(index, out var constant))
                throw Bug($"comptime site at {expr.Span} has no evaluated value");
            return EmitConst(constant, TypeOfExpr(expr), expr.Span);
        }

        return LowerExpr(expr.Inner);
    }

    /// <summary>
    /// <c>co.next()</c> (06 N2 A2): one instruction — <c>?Y</c>, or for a coroutine that yields
    /// nothing whether it stopped at a yield. A coroutine whose type throws throws through its
    /// pulls (05 E10): the error its body ended with comes out here, on the error path like a
    /// call's.
    /// </summary>
    private TempId LowerCoroutineNext(MemberExpr member, CoroutineOf type, Core.Span span)
    {
        var yield = LowerType(type.Yield, span);
        var coroutine = LowerExpr(member.Target);
        var result = IsVoid(yield) ? BoolType : new IrOptionalType(yield);
        var dest = _slots.NewTemp(result);
        var throws = _types.ThrownByPull(member) is { } pulled && ThrownHere([pulled]).Length > 0;
        _b.Emit(new ResumePull(dest, coroutine, yield, span) { Throws = throws });
        _fresh.Add(dest);
        if (throws && !_b.IsSealed) ErrorEdge(span);
        return dest;
    }

    /// <summary>
    /// <c>co.close()</c> (06 A5): the runtime resumes a suspended coroutine to be unwound, and the
    /// <c>Cancelled</c> its body ends with is dropped; what else escapes the body comes out here, on
    /// the error path like a pull's.
    /// </summary>
    private void LowerCoroutineClose(MemberExpr member, Core.Span span)
    {
        var coroutine = LowerExpr(member.Target);
        var cancelled = CancelledClass() ?? throw Bug("'close()' of a coroutine checked without std.task's Cancelled");
        var throws = _types.ThrownByPull(member) is { } thrown && ThrownHere([thrown]).Length > 0;
        _b.Emit(new CoroutineClose(coroutine, ((IrRefType)_typeTable.RefTo(cancelled)).Type, span) { Throws = throws });
        if (throws && !_b.IsSealed) ErrorEdge(span);
    }

    /// <summary>std.task's <c>Cancelled</c>, when the compilation has it (06 A5).</summary>
    private TypeSymbol? CancelledClass() =>
        _typeTable.Compilation.FindModule(["std", "task"])?.Members.LookupLocal("Cancelled") is TypeSymbol { Kind: TypeSymbolKind.Class } cancelled
            ? cancelled : null;

    /// <summary>'throw Cancelled {}' at a yield that close() resumed: a new object, as the Error it
    /// is thrown as, to the landing of the yield's scope.</summary>
    private void RaiseCancelled(TypeSymbol cancelled, Core.Span span)
    {
        _errorSites++;
        var type = _typeTable.RefTo(cancelled);
        var made = _slots.NewTemp(type);
        _b.Emit(new NewObject(made, ((IrRefType)type).Type, type, span));
        var value = Coerce(made, type, ErrorType(span), span);
        _b.Seal(new Throw(value, ErrorLanding(span), span));
    }

    /// <summary><c>co.result()</c> and <c>co.isDone()</c> (06 A2, A4): what the body returned —
    /// <c>?R</c> — and whether it has ended, without pulling.</summary>
    private TempId LowerCoroutineQuery(MemberExpr member, CoroutineOf type, Core.Span span)
    {
        var coroutine = LowerExpr(member.Target);
        if (member.Member == "isDone")
        {
            var done = _slots.NewTemp(BoolType);
            _b.Emit(new CoroutineDone(done, coroutine, span));
            return done;
        }
        var result = LowerType(type.Result, span);
        var dest = _slots.NewTemp(new IrOptionalType(result));
        _b.Emit(new CoroutineResult(dest, coroutine, result, span));
        _fresh.Add(dest);
        return dest;
    }

    /// <summary>
    /// <c>yield</c> (06 N2): one op, and control CONTINUES at the next instruction when the
    /// coroutine is pulled again — the suspension is the runtime's business, not the CFG's.
    /// Inside a coroutine body the value is checked against the yield type here, statically;
    /// outside it (§10a) the value is typed as what it is, and the running coroutine meets it at
    /// run time. A yield of the body is where <c>close()</c> finds the coroutine suspended
    /// (06 A5): resumed to be unwound, it throws <c>Cancelled</c> there, and the error path runs
    /// the defers on the way out of the body.
    /// </summary>
    private bool LowerYield(YieldStmt stmt)
    {
        if (InCoroutine)
        {
            var value = stmt.Value is null
                ? null
                : (TempId?)LowerExprAs(stmt.Value, _coroutineYield!);
            _b.Emit(new YieldSuspend(value, _coroutineYield!, Dynamic: false, stmt.Span));
            UnwindAtClose(stmt.Span);
            return true;
        }

        var dynamicValue = stmt.Value is null ? null : (TempId?)LowerExpr(stmt.Value);
        var siteType = stmt.Value is null ? VoidType : TypeOfExpr(stmt.Value);
        _b.Emit(new YieldSuspend(dynamicValue, siteType, Dynamic: true, stmt.Span));
        UnwindAtClose(stmt.Span);
        return true;
    }

    /// <summary>After a yield: where close() resumed the coroutine to be unwound (06 A5), the yield
    /// throws <c>Cancelled</c>, and the error path runs the defers on the way out.</summary>
    private void UnwindAtClose(Core.Span span)
    {
        if (CancelledClass() is not { } cancelled) return;
        var closing = _slots.NewTemp(BoolType);
        _b.Emit(new CoroutineClosing(closing, span));
        var unwind = _b.NewBlock();
        var go = _b.NewBlock();
        _b.Seal(new CondBranch(closing, unwind, go, span));
        _b.SwitchTo(unwind);
        RaiseCancelled(cancelled, span);
        _b.SwitchTo(go);
    }

    /// <summary>
    /// An assignment to a captured cell. The same three forms as on the slot path, except that reading
    /// and writing happen where the variable really lives.
    /// </summary>
    private TempId LowerCapturedAssign(AssignExpr expr, TempId cell, TypeId cellType, IrType type)
    {
        if (expr.Operator is null)
        {
            var assigned = LowerExprAs(expr.Value, type);
            _b.Emit(new StoreField(cell, cellType, new FieldId(0), assigned, expr.Span));
            return assigned;
        }

        if (expr.Operator is BinaryOp.Coalesce or BinaryOp.LogicalAnd or BinaryOp.LogicalOr)
            throw NotSupported($"'{expr.Operator}=' on a captured variable", expr.Span);

        if (_types.OperatorCallOf(expr) is { } operatorCall)
        {
            var combined = LowerCall(operatorCall)
                ?? throw Bug("operator compound returned no value");
            _b.Emit(new StoreField(cell, cellType, new FieldId(0), combined, expr.Span));
            return combined;
        }

        var current = _slots.NewTemp(type);
        _b.Emit(new LoadField(current, cell, cellType, new FieldId(0), type, expr.Target.Span));

        var operand = LowerExpr(expr.Value);
        var result = EmitBinary(IrBinKindExtensions.FromAst(expr.Operator.Value), type,
            current, operand, expr.Span);
        _b.Emit(new StoreField(cell, cellType, new FieldId(0), result, expr.Span));
        return result;
    }

    /// <summary>
    /// <c>obj.f = v</c> and <c>obj.f += v</c>.
    ///
    /// <para>THE OBJECT IS EVALUATED EXACTLY ONCE. For <c>+=</c> that is the difference between right and
    /// wrong as soon as the target expression has side effects: <c>next().f += 1</c> must not call
    /// <c>next()</c> twice. The reference is therefore lowered once into a temp and reused for reading
    /// and writing.</para>
    /// </summary>
    private TempId LowerFieldAssign(MemberExpr member, AssignExpr expr)
    {
        var (obj, type, field, fieldType) = ResolveFieldAccess(member);

        if (expr.Operator is null)
        {
            var assigned = LowerExprAs(expr.Value, fieldType);
            _b.Emit(new StoreField(obj, type, field, assigned, expr.Span));
            return assigned;
        }

        if (expr.Operator is BinaryOp.Coalesce or BinaryOp.LogicalAnd or BinaryOp.LogicalOr)
            return LowerShortCircuitAssign(expr, fieldType,
                () =>
                {
                    var loaded = _slots.NewTemp(fieldType);
                    _b.Emit(new LoadField(loaded, obj, type, field, fieldType, member.Span));
                    return loaded;
                },
                value => _b.Emit(new StoreField(obj, type, field, value, expr.Span)));

        var current = _slots.NewTemp(fieldType);
        _b.Emit(new LoadField(current, obj, type, field, fieldType, member.Span));

        var operand = LowerExpr(expr.Value);
        var result = EmitBinary(IrBinKindExtensions.FromAst(expr.Operator.Value), fieldType,
            current, operand, expr.Span);
        _b.Emit(new StoreField(obj, type, field, result, expr.Span));
        return result;
    }

    /// <summary><c>this</c> is slot 0. That it exists was checked by the sema (<c>LYR-SEM0008</c> in a
    /// static method), so a missing slot is a bug here.</summary>
    private TempId LowerThis(ThisExpr expr)
    {
        // Inside a lambda 'this' is a captured value (02 M8 C2): read from the environment, at
        // the field behind the named captures. A class's 'this' is the shared reference, a
        // struct's the copy taken when the closure was made.
        if (_thisSlot is null && _capturedThisField is { } field && _envSlot is { } envSlot && _envType is { } envType
            && _thisType is { } captured)
        {
            var env = _slots.NewTemp(new IrRefType(envType));
            _b.Emit(new LoadLocal(env, envSlot, new IrRefType(envType), expr.Span));
            var self = _slots.NewTemp(captured);
            _b.Emit(new LoadField(self, env, envType, new FieldId(field), captured, expr.Span));
            return self;
        }

        if (_thisSlot is not { } slot || _thisType is not { } type)
            throw Bug($"'this' reached lowering outside an instance method at {expr.Span}");

        var dest = _slots.NewTemp(type);
        _b.Emit(new LoadLocal(dest, slot, type, expr.Span));
        return dest;
    }

    /// <summary>
    /// <c>xs[i] = v</c> and <c>xs[i] += v</c>.
    ///
    /// <para>Array AND index are evaluated exactly once — for <c>+=</c> that is the difference between
    /// right and wrong as soon as either has side effects: <c>xs[next()] += 1</c> must not call
    /// <c>next()</c> twice.</para>
    /// </summary>
    private TempId LowerElementAssign(IndexExpr indexed, AssignExpr expr)
    {
        // 'xs[i] = v' on a container is 'Indexable<T>.set(i, v)'. A compound assignment ('xs[i] += 1') is
        // NOT covered here and reports as a scope boundary: it would need a read and a write with the
        // same index, and whether the index may be evaluated twice is a language question the spec does
        // not answer.
        if (TypeOfExpr(indexed.Target) is not (IrArrayType or IrSliceType or IrInlineArrayType))
        {
            if (expr.Operator is not null)
                throw NotSupported("compound assignment on a container (only on arrays)",
                    expr.Span);

            var stored = LowerExpr(expr.Value);
            if (LowerIndexableCall(indexed, "set", stored) is null)
                ResolveIndexAccess(indexed); // reports the scope boundary with the type name
            return stored;
        }

        var (array, index, element) = ResolveIndexAccess(indexed);

        if (expr.Operator is null)
        {
            // Through the EXPECTED type rather than bare: otherwise 'xs[i] = null' on a '(?T)[]' has no
            // target type for 'null' to take its shape from, and a 'T' in a '?T' slot would stay
            // unwrapped. The same rule as for 'stloc' in LowerAssign.
            var assigned = LowerExprAs(expr.Value, element);
            _b.Emit(new StoreElem(array, index, assigned, expr.Span));
            return assigned;
        }

        if (expr.Operator is BinaryOp.Coalesce or BinaryOp.LogicalAnd or BinaryOp.LogicalOr)
            return LowerShortCircuitAssign(expr, element,
                () =>
                {
                    var loaded = _slots.NewTemp(element);
                    _b.Emit(new LoadElem(loaded, array, index, element, indexed.Span));
                    return loaded;
                },
                value => _b.Emit(new StoreElem(array, index, value, expr.Span)));

        var current = _slots.NewTemp(element);
        _b.Emit(new LoadElem(current, array, index, element, indexed.Span));

        var operand = LowerExpr(expr.Value);
        var result = EmitBinary(IrBinKindExtensions.FromAst(expr.Operator.Value), element,
            current, operand, expr.Span);
        _b.Emit(new StoreElem(array, index, result, expr.Span));
        return result;
    }

    // ------------------------------------------------------------------ enums

    /// <summary>
    /// The enum entry a value belongs to, or a scope boundary.
    ///
    /// <para>THE TYPE IS ASKED, NOT THE SYMBOL. <c>TypeFacts.SymbolOf</c> yields the definition for a
    /// <c>GenericInstance</c> and throws the type arguments away, which would land
    /// <c>Opt&lt;int&gt;</c> and <c>Opt&lt;string&gt;</c> on the same entry. The sema type of the
    /// expression carries them, so it comes from there.</para>
    /// </summary>
    private IrEnumType RequireEnum(Expr expr) => RequireEnum(_types.TypeOf(expr), expr.Span);

    private IrEnumType RequireEnum(LyrType type, Span span) => SubstituteType(type) switch
    {
        NamedRef { Symbol: { Kind: TypeSymbolKind.Enum } symbol } => _typeTable.EnumOf(symbol),
        GenericInstance { Definition.Kind: TypeSymbolKind.Enum } instance
            => new IrEnumType(_typeTable.Intern(instance.Definition, instance.Arguments)),
        _ => throw NotSupported($"'{TypeFacts.Display(type)}' is not an enum", span),
    };

    /// <summary><c>Shape.Circle(2.0)</c> and <c>Shape.Empty</c> — a tuple variant and a unit variant.
    /// The struct form <c>Triangle { a = … }</c> goes through <see cref="LowerObjectInit"/>.</summary>
    /// <param name="constructed">The expression whose type is the constructed INSTANCE: for
    /// <c>Shape.Circle(2.0)</c> the call, for the unit variant <c>Shape.Empty</c> the member itself. It
    /// does not stand at the target — <c>Opt.Some(5)</c> names its type arguments nowhere, and
    /// the sema resolved them from the context.</param>
    /// <summary><c>.Red</c> as a value: the variant of the enum the position expected, which the
    /// sema bound; or a static constant reached the same way.</summary>
    private TempId LowerImplicitMember(ImplicitMemberExpr expr)
    {
        if (_types.RefOf(expr) is GlobalSymbol constant) return LowerGlobalRead(constant, expr.Span);
        if (_types.RefOf(expr) is EnumVariantSymbol) return LowerVariantCall(expr.Name, [], expr, expr.Span);
        throw Bug($"implicit member '.{expr.Name}' is bound to nothing the lowering knows");
    }

    private TempId LowerVariantCall(string variantName, Expr[] arguments, Expr constructed, Span span)
    {
        // Which INSTANCE is constructed stands in the call's result type rather than at the target:
        // 'Opt.Some(5)' in a position with an expected 'Opt<int>' names the arguments nowhere, but the
        // sema resolved them. The target need not be written at all ('.Some(5)').
        var enumType = RequireEnum(_types.TypeOf(constructed), span);
        var variant = _typeTable.VariantOf(enumType.Type, variantName, span);
        var layout = _typeTable.Defs[variant.Value];

        // Against the FIELD'S type, as an argument is lowered against its parameter: the payload
        // of 'Result<?int, E>.Ok(4)' is a '?int' and the 4 has to be wrapped, a 'null' has no
        // type of its own, and a struct payload is a copy — lowered bare it would share the slot
        // array with its source. Lowered bare, the verifier refused the 'newvariant' ("field 0 is
        // i64, expected ?i64"). Slot 0 is the tag, so the payload starts at 1.
        var fields = new TempId[arguments.Length];
        for (var i = 0; i < arguments.Length; i++)
            fields[i] = LowerExprAs(arguments[i], layout.FieldTypes[i + 1]);

        var dest = _slots.NewTemp(enumType);
        _b.Emit(new NewVariant(dest, variant, enumType.Type, fields, span));
        return dest;
    }

    /// <summary><c>Shape.Tri { a = 3, b = 4 }</c>. As with an object literal, writing happens in LAYOUT
    /// order while evaluation happens in source order, except that slot 0 is the tag and the payload
    /// fields start at 1.</summary>
    private TempId LowerStructVariant(StructInitExpr expr)
    {
        var variantName = expr.Path[^1];
        var enumType = RequireEnum(_types.TypeOf(expr), expr.Span);
        var variant = _typeTable.VariantOf(enumType.Type, variantName, expr.Span);
        var layout = _typeTable.Defs[variant.Value];

        // Adapted to the declared field type, the same step an object initializer takes — see
        // LowerVariantCall for what a raw payload costs.
        var values = new Dictionary<string, TempId>(StringComparer.Ordinal);
        foreach (var field in expr.Fields)
        {
            var index = Array.IndexOf(layout.FieldNames, field.Name);
            if (index < 0) throw NotSupported($"unknown field '{field.Name}' in a variant initializer", field.Span);
            values[field.Name] = LowerExprAs(field.Value, layout.FieldTypes[index]); // wraps into ?T, copies a struct
        }

        var fields = new TempId[layout.FieldNames.Length - 1];
        for (var i = 1; i < layout.FieldNames.Length; i++)
        {
            if (!values.TryGetValue(layout.FieldNames[i], out var value))
                throw NotSupported($"initializer omits field '{layout.FieldNames[i]}'", expr.Span);
            fields[i - 1] = value;
        }

        var dest = _slots.NewTemp(enumType);
        _b.Emit(new NewVariant(dest, variant, enumType.Type, fields, expr.Span));
        return dest;
    }

    private TypeSymbol RefEnumSymbol(Expr target, Span span)
    {
        var bound = _types.RefOf(target);
        if (bound is ImportBindingSymbol import) bound = import.Target;
        if (bound is TypeSymbol { Kind: TypeSymbolKind.Enum } symbol) return symbol;

        throw NotSupported("variant construction on something that is not an enum", span);
    }

    /// <summary>Whether the last lowered <c>match</c> continues behind itself. A return value would be
    /// cleaner, but <see cref="LowerMatch"/> already yields the result temp of the expression case, and a
    /// second channel for a question only the statement case asks would have widened every call
    /// site.</summary>
    private bool _matchFellThrough = true;

    /// <summary>
    /// <c>match</c> as an expression and as a statement — the same code, only the result slot is
    /// missing in the statement case. Over enums AND over scalars.
    ///
    /// <para>NO OPCODE OF ITS OWN. A <c>match</c> branches over a sequence of tests like any other
    /// case distinction — over its tag for an enum, over the value itself otherwise, and into its
    /// payloads for a nested pattern (<see cref="LowerPattern"/>). A jump table would be an
    /// optimization, not semantics; the arms are tried in the order written (§7.6).</para>
    ///
    /// <para>The last arm is taken unchecked when it has no guard: the sema proved exhaustiveness
    /// (<c>LYR-SEM0050</c>). A guard can still fail, and then the failure path becomes
    /// <c>unreachable</c>: reachable in the CFG, impossible at runtime.</para>
    /// </summary>
    private TempId? LowerMatch(Expr scrutinee, MatchArm[] arms, IrType? resultType, Span span)
    {
        var scrutineeType = TypeOfExpr(scrutinee);
        var value = LowerExpr(scrutinee);
        var slot = resultType is null ? (LocalId?)null : _slots.DeclareSynthetic("match", resultType);

        // For an enum the tag is read ONCE, in the entry block, and every arm compares against
        // that temp: the entry dominates every arm, so the reuse is legal, and it keeps the
        // per-arm cost at one comparison — what the tag-first lowering before the pattern
        // compiler paid as well.
        var outerTag = _scrutineeTag; // a match inside an arm must not forget the outer one
        _scrutineeTag = null;
        if (scrutineeType is IrEnumType)
        {
            var tag = _slots.NewTemp(new IrScalarType(IrScalar.I64));
            _b.Emit(new EnumTag(tag, value, span));
            _scrutineeTag = (value, tag);
        }

        // The merge block arises ONLY when an arm needs it. If none falls through — every arm returns,
        // throws or jumps — there is no control flow behind the 'match', and a created block would be
        // unreachable from the entry. The verifier rejects exactly that, and rightly: a block nobody can
        // reach is either dead or a lowering error.
        BlockId? merge = null;

        for (var i = 0; i < arms.Length; i++)
        {
            var arm = arms[i];
            var last = i == arms.Length - 1;

            // The last arm is not tested: the sema proved exhaustiveness (LYR-SEM0050), so it matches
            // when none before it did. Its bindings still run — the pattern compiler unwraps and
            // narrows without testing when told the match is certain. Only an or-pattern keeps
            // its tests, because WHICH alternative matched decides what is bound.
            //
            // With a guard that does not hold: a guard can fail, and then a failure path is needed. It
            // becomes 'unreachable' — reachable in the CFG, impossible at runtime.
            var unconditional = last && arm.Guard is null;

            BlockId? next = null;
            BlockId Fail()
            {
                next ??= _b.NewBlock();
                return next.Value;
            }

            // Tests and bindings come in one pass, and before the guard: 'n if n > 0' needs 'n'.
            LowerPattern(arm.Pattern, value, scrutineeType, Fail, assumeMatch: unconditional);

            // A guard may end its arm where it stands: an operand of it gives no value. The
            // arms after it are reached through the tests before it, as ever.
            var reached = true;
            if (arm.Guard is { } guard)
            {
                TempId test = default;
                reached = ComesOut(() => test = LowerExpr(guard));
                if (reached)
                {
                    var guarded = _b.NewBlock();
                    _b.Seal(new CondBranch(test, guarded, Fail(), guard.Span));
                    _b.SwitchTo(guarded);
                }
            }

            if (reached && LowerArm(arm, value, slot, resultType))
            {
                merge ??= _b.NewBlock();
                _b.Seal(new Branch(merge.Value, arm.Span));
            }

            if (next is { } fallthrough)
            {
                _b.SwitchTo(fallthrough);
                // After the last arm the failure path is impossible at runtime, because the sema proved
                // exhaustiveness. In the CFG it is reachable and therefore needs a terminator, and
                // 'unreachable' is exactly that statement.
                if (last) _b.Seal(new Unreachable(span));
            }
            else if (unconditional)
            {
                // Nothing to do: the last arm tested nothing, so no block waits for a terminator.
            }
            else if (!last)
            {
                // An arm before the last one that could not fail: whatever follows it is dead at
                // runtime, and a block for it would be unreachable from the entry — which the
                // verifier refuses. The remaining arms are not lowered at all.
                break;
            }
        }

        _scrutineeTag = outerTag;

        // No arm falls through — every one returns, throws or jumps. Control flow then ends here and the
        // merge block is unreachable.
        //
        // That is not an edge case but the usual pattern for a 'match' as a statement:
        // 'match (e) { A => { return 1; }, B => { return 2; } }'.
        if (merge is not { } after)
        {
            // No arm falls through: control flow ends here. The caller learns that through
            // _matchFellThrough and does not seal a second time.
            _matchFellThrough = false;
            return null;
        }

        _b.SwitchTo(after);
        _matchFellThrough = true;
        if (slot is not { } result || resultType is null) return null;

        var dest = _slots.NewTemp(resultType);
        _b.Emit(new LoadLocal(dest, result, resultType, span));
        return dest;
    }

    // ------------------------------------------------------------------ pattern compiler

    /// <summary>The enum scrutinee of the match being lowered and its tag, read once in the entry
    /// block. <see cref="TagOf"/> reuses it instead of reading the tag again per arm.</summary>
    private (TempId Value, TempId Tag)? _scrutineeTag;

    /// <summary>
    /// Compiles one pattern against a value: emits the tests it needs, branching to
    /// <paramref name="onFail"/> when one fails, and stores every name it binds — in ONE recursive
    /// pass, so a pattern nests to any depth. On return the current block is the one in which the
    /// whole pattern has matched and all its bindings are in their slots.
    ///
    /// <para>This is a backtracking automaton rather than a decision tree: the arms are tried in
    /// the order written (§7.6), each arm tests its pattern left to right, and a failed test jumps
    /// to the next arm. Bindings written before a later test failed are dead stores, which is
    /// harmless — the slots belong to the arm alone. A decision tree would test each column once
    /// but duplicate arm bodies and reorder guards; the language promises the written order.</para>
    ///
    /// <para><paramref name="assumeMatch"/> says the sema proved the pattern matches (the last arm
    /// of an exhaustive match, or an irrefutable binding): tests are then skipped and only the
    /// unwrapping and binding remain. It does not survive into an or-pattern, whose alternatives
    /// must be told apart, except for the last alternative.</para>
    ///
    /// <para>A BRANCH RATHER THAN A bool TEMP, and that is no matter of style: a range needs two
    /// comparisons, an or-pattern arbitrarily many, and combining them into a value would mean
    /// <c>and</c>/<c>or</c> on <c>bool</c> — both are integral in this IR, and the verifier says
    /// so. The same solution as for <c>&amp;&amp;</c> and <c>||</c>.</para>
    /// </summary>
    private void LowerPattern(Pattern pattern, TempId value, IrType valueType, Func<BlockId> onFail,
        bool assumeMatch)
    {
        switch (pattern)
        {
            case WildcardPattern or ErrorPattern:
                return;

            case BindingPattern binding when _types.RefOf(pattern) is LocalSymbol local:
            {
                var slotType = LowerType(local.Type, binding.Span);

                // When a binding takes the rest of a '?T' at the top of a match (or an if-let), the
                // sema gives the name the NARROWED type 'T': the value has to be present, and here
                // that is a test. Nested inside a payload the sema binds '?T' as it is, and the
                // slot type says so — then nothing is unwrapped.
                if (valueType is IrOptionalType optional && slotType is not IrOptionalType)
                    value = UnwrapPresent(value, optional, onFail, assumeMatch, binding.Span);

                BindLocal(binding, local, value, slotType, binding.Span);
                return;
            }

            case BindingPattern other:
                throw Bug($"pattern binding '{other.Name}' was not bound by the type checker");

            // 'c: Circle' (03 T11): the test, then the value out of the interface value as the
            // name's type. The last arm of an exhaustive match is not tested; a type pattern is
            // never the one that makes a match exhaustive, so the test always stands here.
            case TypePattern tp:
            {
                if (valueType is IrOptionalType optionalIface)
                    value = UnwrapPresent(value, optionalIface, onFail, assumeMatch, tp.Span);
                var ifaceType = valueType is IrOptionalType oi ? oi.Inner : valueType;
                if (ifaceType is not IrInterfaceType)
                    throw NotSupported("a type pattern on a value that is no interface value", tp.Span);
                var target = _typeTable.Lower(tp.Type);
                var targetId = TargetIdOf(target, tp.Span);
                if (!assumeMatch)
                {
                    var holds = _slots.NewTemp(BoolType);
                    _b.Emit(new TypeTest(holds, value, targetId, tp.Span));
                    var matched = _b.NewBlock();
                    _b.Seal(new CondBranch(holds, matched, onFail(), tp.Span));
                    _b.SwitchTo(matched);
                }
                if (tp.Name is not null && _types.RefOf(tp) is LocalSymbol tested)
                {
                    var cast = _slots.NewTemp(target);
                    _b.Emit(new Downcast(cast, value, targetId, target, tp.Span));
                    if (target is IrStructType) _fresh.Add(cast);
                    BindLocal(tp, tested, cast, target, tp.Span);
                }
                return;
            }

            // 'null' as a pattern is NO comparison but the question of a value's presence — the same
            // answer as for 'x == null' (TryLowerNullTest). A real equality comparison would need a
            // null value as an operand, and there is none.
            case LiteralPattern { Literal: NullLiteralExpr } nullPattern:
            {
                if (valueType is not IrOptionalType)
                    throw NotSupported("'null' pattern on a non-optional", nullPattern.Span, LoweringDiagnostics.NeverNull);
                if (assumeMatch) return;

                var isSome = _slots.NewTemp(BoolType);
                _b.Emit(new OptIsSome(isSome, value, nullPattern.Span));
                var matched = _b.NewBlock();
                _b.Seal(new CondBranch(isSome, onFail(), matched, nullPattern.Span));
                _b.SwitchTo(matched);
                return;
            }

            case LiteralPattern literal:
            {
                if (valueType is IrOptionalType optional)
                    value = UnwrapPresent(value, optional, onFail, assumeMatch, literal.Span);
                if (assumeMatch) return;

                var scalar = valueType is IrOptionalType o ? o.Inner : valueType;
                var expected = LowerExprAs(literal.Literal, scalar);
                var matches = _slots.NewTemp(BoolType);
                _b.Emit(new BinOp(matches, IrBinKind.Eq, BoolType, value, expected, literal.Span));
                var matched = _b.NewBlock();
                _b.Seal(new CondBranch(matches, matched, onFail(), literal.Span));
                _b.SwitchTo(matched);
                return;
            }

            // 'lo <= v' and then 'v <= hi': two blocks rather than one combination.
            case RangePattern range:
            {
                if (valueType is IrOptionalType optional)
                    value = UnwrapPresent(value, optional, onFail, assumeMatch, range.Span);
                if (assumeMatch) return;

                var scalar = valueType is IrOptionalType o ? o.Inner : valueType;
                var low = LowerExprAs(range.Low, scalar);
                var atLeast = _slots.NewTemp(BoolType);
                _b.Emit(new BinOp(atLeast, IrBinKind.Ge, BoolType, value, low, range.Span));
                var upper = _b.NewBlock();
                _b.Seal(new CondBranch(atLeast, upper, onFail(), range.Span));
                _b.SwitchTo(upper);

                var high = LowerExprAs(range.High, scalar);
                var atMost = _slots.NewTemp(BoolType);
                _b.Emit(new BinOp(atMost, range.IsInclusive ? IrBinKind.Le : IrBinKind.Lt,
                    BoolType, value, high, range.Span));
                var matched = _b.NewBlock();
                _b.Seal(new CondBranch(atMost, matched, onFail(), range.Span));
                _b.SwitchTo(matched);
                return;
            }

            // Field by field: every element is loaded and compiled against its own sub-pattern.
            // The value is evaluated ONCE — it already sits in a temp — so 'let (a, b) = f();' calls
            // f once, whatever the pattern does with the parts.
            case TuplePattern tuple:
            {
                if (valueType is IrOptionalType optional)
                    value = UnwrapPresent(value, optional, onFail, assumeMatch, tuple.Span);
                var tupleType = valueType is IrOptionalType o ? o.Inner : valueType;
                if (tupleType is not IrStructType { Type: var tupleId })
                    throw Bug("tuple pattern on a value that is not a tuple");

                var layout = _typeTable.Defs[tupleId.Value];
                for (var i = 0; i < tuple.Elements.Length; i++)
                {
                    // '_' binds nothing, so the field is not even read: an 'ldfld' whose result
                    // nobody uses would be dead code in the bytecode.
                    if (tuple.Elements[i] is WildcardPattern) continue;

                    var fieldType = layout.FieldTypes[i];
                    var element = _slots.NewTemp(fieldType);
                    _b.Emit(new LoadField(element, value, tupleId, new FieldId(i), fieldType, tuple.Span));
                    LowerPattern(tuple.Elements[i], element, fieldType, onFail, assumeMatch);
                }
                return;
            }

            // A variant of an enum: tag test, then 'enumas' narrows, after which every payload
            // field is an ordinary 'ldfld' with the variant's layout — and each field's pattern
            // is compiled recursively, so 'Neg(Neg(x))' and 'Add(Lit(0), r)' are nothing special.
            case VariantPattern variant when _types.RefOf(pattern) is EnumVariantSymbol:
            {
                var variantType = EmitVariantTest(variant.Path[^1], ref value, ref valueType,
                    onFail, assumeMatch, variant.Span);
                if (variant.TupleElements is null && variant.StructFields is null) return;

                var narrowed = _slots.NewTemp(new IrRefType(variantType));
                _b.Emit(new EnumAs(narrowed, value, variantType, variant.Span));
                var layout = _typeTable.Defs[variantType.Value];

                if (variant.TupleElements is { } elements)
                    for (var i = 0; i < elements.Length; i++)
                        LowerFieldPattern(elements[i], null, narrowed, variantType,
                            new FieldId(i + 1), layout.FieldTypes[i + 1], onFail, assumeMatch);

                if (variant.StructFields is { } fields)
                    foreach (var field in fields)
                    {
                        var index = Array.IndexOf(layout.FieldNames, field.Name);
                        if (index < 0) throw NotSupported($"unknown field '{field.Name}' in a pattern", field.Span);
                        LowerFieldPattern(field.Pattern, field, narrowed, variantType,
                            new FieldId(index), layout.FieldTypes[index], onFail, assumeMatch);
                    }
                return;
            }

            // A field pattern over a STRUCT or CLASS: the scrutinee's type already IS the
            // pattern's type, so there is no tag to test — the pattern says which fields to read,
            // and a sub-pattern with a test ('P { x = 0, y }') is compiled like any other (§7.6).
            case VariantPattern { StructFields: { } structFields } destructure:
            {
                if (valueType is IrOptionalType optional)
                    value = UnwrapPresent(value, optional, onFail, assumeMatch, destructure.Span);
                var holderType = valueType is IrOptionalType o ? o.Inner : valueType;

                // A class is a reference and a struct a value, and both carry their layout id: the
                // difference decides how the value travels, not how a field is read out of it.
                var holder = holderType switch
                {
                    IrRefType reference => reference.Type,
                    IrStructType structure => structure.Type,
                    _ => throw NotSupported(
                        "a field pattern over a value that carries no fields here", destructure.Span),
                };

                var layout = _typeTable.Defs[holder.Value];
                foreach (var field in structFields)
                {
                    var index = Array.IndexOf(layout.FieldNames, field.Name);
                    if (index < 0) throw NotSupported($"unknown field '{field.Name}' in a pattern", field.Span);
                    LowerFieldPattern(field.Pattern, field, value, holder,
                        new FieldId(index), layout.FieldTypes[index], onFail, assumeMatch);
                }
                return;
            }

            case VariantPattern unresolved:
                throw Bug($"variant pattern '{string.Join('.', unresolved.Path)}' was not resolved by the type checker");

            // An array asks about its LENGTH first — exactly, or at least, depending on whether
            // a rest stands among the elements — and only then about the elements at the fixed
            // positions. The ones before the rest are counted from the front, the ones after it
            // from the back, so '[first, .., last]' needs no arithmetic on the rest's size.
            case ArrayPattern array:
            {
                if (valueType is IrOptionalType optional)
                    value = UnwrapPresent(value, optional, onFail, assumeMatch, array.Span);
                var arrayType = valueType is IrOptionalType o ? o.Inner : valueType;
                if (ElementOf(arrayType) is not { } elementType)
                    throw Bug("array pattern on a value that is not an array");

                var restIndex = Array.FindIndex(array.Elements, e => e is RestPattern);
                var before = restIndex < 0 ? array.Elements : array.Elements[..restIndex];
                var after = restIndex < 0 ? [] : array.Elements[(restIndex + 1)..];
                var fixedCount = before.Length + after.Length;

                var i64 = new IrScalarType(IrScalar.I64);
                var length = _slots.NewTemp(i64);
                _b.Emit(new ArrayLen(length, value, array.Span));

                // '[..]' and '[..rest]' test nothing: every array has at least no elements.
                var needsTest = !assumeMatch && (restIndex < 0 || fixedCount > 0);
                if (needsTest)
                {
                    var wanted = EmitConst(new IntConst((ulong)fixedCount), i64, array.Span);
                    var fits = _slots.NewTemp(BoolType);
                    _b.Emit(new BinOp(fits, restIndex < 0 ? IrBinKind.Eq : IrBinKind.Ge, BoolType,
                        length, wanted, array.Span));
                    var matched = _b.NewBlock();
                    _b.Seal(new CondBranch(fits, matched, onFail(), array.Span));
                    _b.SwitchTo(matched);
                }

                for (var i = 0; i < before.Length; i++)
                {
                    if (before[i] is WildcardPattern) continue;
                    var index = EmitConst(new IntConst((ulong)i), i64, before[i].Span);
                    var element = _slots.NewTemp(elementType);
                    _b.Emit(new LoadElem(element, value, index, elementType, before[i].Span));
                    LowerPattern(before[i], element, elementType, onFail, assumeMatch);
                }

                for (var i = 0; i < after.Length; i++)
                {
                    if (after[i] is WildcardPattern) continue;
                    var fromEnd = EmitConst(new IntConst((ulong)(after.Length - i)), i64, after[i].Span);
                    var index = _slots.NewTemp(i64);
                    _b.Emit(new BinOp(index, IrBinKind.Sub, i64, length, fromEnd, after[i].Span));
                    var element = _slots.NewTemp(elementType);
                    _b.Emit(new LoadElem(element, value, index, elementType, after[i].Span));
                    LowerPattern(after[i], element, elementType, onFail, assumeMatch);
                }

                if (restIndex >= 0 && array.Elements[restIndex] is RestPattern { Name: not null } named)
                    BindNamedRest(named, value, elementType, length, before.Length, after.Length);
                return;
            }

            case RestPattern lone:
                throw Bug($"a rest pattern outside an array pattern at {lone.Span}");

            // Every alternative gets its own attempt; the first that matches wins, and all of them
            // arrive in one block with the same names bound: the sema pointed every alternative's
            // bindings at the first alternative's symbols, so they share slots.
            case OrPattern or:
            {
                var joined = _b.NewBlock();
                for (var i = 0; i < or.Alternatives.Length; i++)
                {
                    var lastAlternative = i == or.Alternatives.Length - 1;
                    BlockId? nextAlternative = null;
                    BlockId FailAlternative()
                    {
                        if (lastAlternative) return onFail();
                        nextAlternative ??= _b.NewBlock();
                        return nextAlternative.Value;
                    }

                    LowerPattern(or.Alternatives[i], value, valueType, FailAlternative,
                        assumeMatch && lastAlternative);
                    _b.Seal(new Branch(joined, or.Span));

                    if (lastAlternative) break;
                    if (nextAlternative is not { } fallthrough) break; // this alternative cannot fail: the rest is dead
                    _b.SwitchTo(fallthrough);
                }
                _b.SwitchTo(joined);
                return;
            }

            default:
                throw NotSupported($"a {pattern.GetType().Name} in a pattern", pattern.Span);
        }
    }

    /// <summary>Loads one field of a payload or a struct and compiles the sub-pattern against it.
    /// The short form <c>{ n }</c> has no sub-pattern: the field name IS the binding, and the sema
    /// bound it on the <see cref="FieldPattern"/> node itself.</summary>
    private void LowerFieldPattern(Pattern? sub, FieldPattern? field, TempId obj, TypeId holder,
        FieldId fieldId, IrType type, Func<BlockId> onFail, bool assumeMatch)
    {
        if (sub is WildcardPattern) return;
        var span = sub?.Span ?? field!.Span;

        var loaded = _slots.NewTemp(type);
        _b.Emit(new LoadField(loaded, obj, holder, fieldId, type, span));

        if (sub is not null)
        {
            LowerPattern(sub, loaded, type, onFail, assumeMatch);
            return;
        }

        if (_types.RefOf(field!) is not LocalSymbol local)
            throw Bug($"pattern binding '{field!.Name}' was not bound by the type checker");
        BindLocal(field, local, loaded, LowerType(local.Type, span), span);
    }

    /// <summary>The value present, or a jump to <paramref name="onFail"/>: 'optissome' decides,
    /// 'optget' unwraps. With the match already proven the unwrap stands alone — the 'optget'
    /// cannot panic then, the proof is in the arms before it.</summary>
    private TempId UnwrapPresent(TempId value, IrOptionalType optional, Func<BlockId> onFail,
        bool assumeMatch, Span span)
    {
        if (!assumeMatch)
        {
            var isSome = _slots.NewTemp(BoolType);
            _b.Emit(new OptIsSome(isSome, value, span));
            var present = _b.NewBlock();
            _b.Seal(new CondBranch(isSome, present, onFail(), span));
            _b.SwitchTo(present);
        }

        var unwrapped = _slots.NewTemp(optional.Inner);
        _b.Emit(new OptGet(unwrapped, value, optional.Inner, span));
        return unwrapped;
    }

    /// <summary>Tests that an enum value (or an optional enum, which is unwrapped first) carries
    /// the named variant. Returns the variant's layout id; <paramref name="value"/> and
    /// <paramref name="valueType"/> are the unwrapped enum afterwards.</summary>
    private TypeId EmitVariantTest(string variantName, ref TempId value, ref IrType valueType,
        Func<BlockId> onFail, bool assumeMatch, Span span)
    {
        if (valueType is IrOptionalType optional)
        {
            value = UnwrapPresent(value, optional, onFail, assumeMatch, span);
            valueType = optional.Inner;
        }

        if (valueType is not IrEnumType { Type: var enumId })
            throw NotSupported("a variant pattern over a value that is not an enum", span);

        var variantType = _typeTable.VariantOf(enumId, variantName, span);
        if (assumeMatch) return variantType;

        var expected = EmitConst(new IntConst((ulong)_typeTable.TagOf(enumId, variantName, span)),
            new IrScalarType(IrScalar.I64), span);

        // The Type field of a comparison is its RESULT type (bool); the emitter looks the operand
        // type up in the temp table, because signed and unsigned are different opcodes.
        var matches = _slots.NewTemp(BoolType);
        _b.Emit(new BinOp(matches, IrBinKind.Eq, BoolType, TagOf(value, span), expected, span));
        var matched = _b.NewBlock();
        _b.Seal(new CondBranch(matches, matched, onFail(), span));
        _b.SwitchTo(matched);
        return variantType;
    }

    /// <summary>The tag of an enum value: the one read in the match entry when this is the
    /// scrutinee itself, a fresh 'enumtag' for a payload reached by nesting.</summary>
    private TempId TagOf(TempId value, Span span)
    {
        if (_scrutineeTag is { } known && known.Value == value) return known.Tag;
        var tag = _slots.NewTemp(new IrScalarType(IrScalar.I64));
        _b.Emit(new EnumTag(tag, value, span));
        return tag;
    }

    /// <summary>Stores a bound value into the local's slot, declaring the slot on first sight.
    ///
    /// <para>A STRUCT is a value, and binding it takes a copy — the same thing <c>let q = p;</c>
    /// does. Without this the binding aliased what it was read from, so mutating the original
    /// through its own name changed what the pattern had bound: measured at 99 where an ordinary
    /// <c>let</c> answered 1. A freshly built value has no other owner and needs no copy.</para>
    ///
    /// <para>A captured <c>var</c> binding (a mutable destructuring) lives in a cell, like every
    /// other boxed local; the slot then holds the cell and the value goes through it.</para></summary>
    private void BindLocal(Node node, LocalSymbol local, TempId value, IrType type, Span span)
    {
        if (type is IrStructType structType && !_fresh.Contains(value))
            value = CopyStructValue(value, structType, span);

        if (_slots.TryLookup(local, out var existing))
        {
            StoreValue(existing, value, span);
            return;
        }

        if (_types.IsBoxed(local))
        {
            var cellType = _typeTable.CellOf(type);
            var slotForCell = _slots.DeclareFor(local, cellType);
            _cells[slotForCell] = (cellType.Type, type);
            var cell = _slots.NewTemp(cellType);
            _b.Emit(new NewObject(cell, cellType.Type, cellType, span));
            _b.Emit(new StoreLocal(slotForCell, cell, span));
            StoreValue(slotForCell, value, span);
            return;
        }

        var slot = _slots.DeclareFor(local, type);
        _b.Emit(new StoreLocal(slot, value, span));
    }

    /// <summary>
    /// A 'match' as an expression. One nobody takes a value from has no slot: its arms all leave
    /// ('never' — nothing flows out, and 'never' has no IR type to give a slot), or each is worth
    /// nothing, as a call of a void function is. Where no arm comes out, neither does the match.
    /// </summary>
    private TempId? LowerMatchExpr(MatchExpr expr)
    {
        var sema = _types.TypeOf(expr);
        var type = sema is NeverType || TypeFacts.IsVoid(sema) ? null : TypeOfExpr(expr);
        var value = LowerMatch(expr.Scrutinee, expr.Arms, type, expr.Span);
        if (!_matchFellThrough) throw new Diverged();
        return value;
    }

    /// <summary>Lowers the body of an arm. Returns whether it falls through.</summary>
    private bool LowerArm(MatchArm arm, TempId value, LocalId? slot, IrType? resultType)
    {
        if (arm.Body is Expr expr)
        {
            // '_ => throw e' or '_ => panic(…)': the arm diverges, stores nothing, and does not
            // fall through to the merge — exactly like a block arm ending in a 'throw'. And so
            // does one an operand of which gives no value.
            if (Diverges(expr)) { LowerDiverging(expr); return false; }
            return ComesOut(() =>
            {
                var produced = resultType is null ? LowerExprOrVoid(expr) : LowerExprAs(expr, resultType);
                if (slot is { } target && produced is { } v) _b.Emit(new StoreLocal(target, v, arm.Span));
            });
        }

        // A block arm: its tail, when it has one, lands in the result slot before the scope's
        // defers run and the arm falls through to the merge.
        var savedSink = _tailSink;
        _tailSink = new TailSink(slot, resultType, AsReturn: false);
        try { return LowerScope((Block)arm.Body); }
        finally { _tailSink = savedSink; }
    }
    // ------------------------------------------------------------------ optionals

    /// <summary>
    /// An expression at a position with an EXPECTED TYPE. Two things happen only here: <c>null</c> gets
    /// its type, having none of its own — the sema gives it <c>NullType</c> — and a <c>T</c> is wrapped
    /// into <c>?T</c>, because the language allows that direction implicitly.
    /// </summary>
    private TempId LowerExprAs(Expr expr, IrType expected)
    {
        // A marked argument (03 §2.3a) is its place, whatever the path to the call wrote down as
        // the parameter's type: the sema let the mark through to a place parameter only.
        if (expr is UnaryExpr { Operator: UnaryOp.Place } mark) return LowerPlace(mark.Operand);

        if (expr is NullLiteralExpr)
        {
            if (expected is not IrOptionalType option)
                throw NotSupported("'null' outside an optional context", expr.Span);

            var none = _slots.NewTemp(expected);
            _b.Emit(new OptNone(none, option.Inner, expr.Span));
            return none;
        }

        // A value of type 'void' where a place must hold one (03 §9.1) — passed, stored, wrapped:
        // a place that holds one gives its unit; a call of a function that returns nothing runs for
        // its effect and gives the unit; then it coerces as any value does, into '?void' say.
        if (!IsVoid(expected) && SubstituteType(_types.TypeOf(expr)) is PrimitiveType { Kind: PrimitiveKind.Void })
        {
            var unit = LowerExprOrVoid(expr) ?? EmitConst(new BoolConst(false), TypeTable.Unit, expr.Span);
            return Coerce(unit, TypeTable.Unit, expected, expr.Span);
        }

        return Coerce(LowerExpr(expr), TypeOfExpr(expr), expected, expr.Span);
    }

    /// <summary>A <c>null</c> without an expected type. Should never occur: every position where
    /// <c>null</c> is valid knows its target type and goes through <see cref="LowerExprAs"/>.</summary>
    private TempId LowerNull(NullLiteralExpr expr) =>
        throw NotSupported("'null' in a position without an expected type", expr.Span);

    /// <summary>
    /// <c>x != null</c> and <c>x == null</c> are NO comparisons but the question of a value's presence —
    /// exactly what <c>optissome</c> answers. A real comparison would demand a <c>null</c> value on the
    /// stack, and there is none: "no value" is an empty reference, not an operand.
    /// </summary>
    private TempId? TryLowerNullTest(BinaryExpr expr)
    {
        if (expr.Operator is not (BinaryOp.Eq or BinaryOp.Ne)) return null;

        var (option, _) = expr.Right is NullLiteralExpr ? (expr.Left, expr.Right)
            : expr.Left is NullLiteralExpr ? (expr.Right, expr.Left)
            : (null, null);
        if (option is null) return null;

        var value = LowerExpr(option);
        if (TypeOfExpr(option) is not IrOptionalType) throw NotSupported("null test on a non-optional", expr.Span, LoweringDiagnostics.NeverNull);

        var isSome = _slots.NewTemp(BoolType);
        _b.Emit(new OptIsSome(isSome, value, expr.Span));
        if (expr.Operator is BinaryOp.Ne) return isSome;

        // '== null' is the negation. 'not' is the only opcode without a type tag: bool only.
        var isNone = _slots.NewTemp(BoolType);
        _b.Emit(new UnOp(isNone, IrUnKind.Not, BoolType, isSome, expr.Span));
        return isNone;
    }

    /// <summary>
    /// Adapts a value to the type its position expects. The language knows two implicit transitions, and
    /// both are materialized here:
    ///
    /// <para><c>T</c> to <c>?T</c> is implicit and becomes <c>optsome</c>.</para>
    ///
    /// <para>A class or enum value to its interface becomes <c>mkiface</c>: the interface value is a fat
    /// pointer carrying the concrete type, and that is settled at compile time exactly here. Later, at
    /// the <c>callvirt</c>, nobody knows which class it was any more, because an object carries no type
    /// tag.</para>
    ///
    /// <para>The order is not arbitrary: for <c>?SomeInterface</c> the interface has to arise first and
    /// the optional around it second, or a class reference would be wrapped and <c>optget</c> would yield
    /// something no <c>callvirt</c> can run on.</para>
    /// </summary>
    private TempId Coerce(TempId value, IrType from, IrType to, Span span)
    {
        // The optional levels the target has beyond the value: each becomes one 'optsome', from
        // the inside out — a 'T' where '??T' is expected is wrapped twice, a '?T' once (03 T4
        // O1). 'target' is the target with those levels taken off, the type the value itself
        // has to reach first.
        var missing = Math.Max(0, OptionalDepth(to) - OptionalDepth(from));
        var target = to;
        for (var i = 0; i < missing; i++) target = ((IrOptionalType)target).Inner;
        var source = from;

        // The third implicit transition, a lossless integer widening (design/v5/spec/03 T1c):
        // the sema admitted 'int8' where 'int' is expected, and the value changes representation
        // here, as a convert — the same instruction 'as' emits, on a pair the rule allows. Only
        // where the scalar kinds differ; the sema vouches that they differ by a widening.
        if (target is IrScalarType { Kind: var wanted } && source is IrScalarType { Kind: var given }
            && wanted != given && from is not IrOptionalType && WidensScalar(given, wanted))
        {
            var widened = _slots.NewTemp(target);
            _b.Emit(new Lyric.Ir.Convert(widened, source, target, value, span));
            value = widened;
            from = target;
        }

        // An array where a view is expected gives a view of itself, whole (03 T13 A2); an inline
        // array too, where the sema found it in the heap (A4).
        if (target is IrSliceType view && source is IrArrayType or IrInlineArrayType && from is not IrOptionalType)
        {
            value = ViewOf(value, view.Element, span);
            from = target;
        }

        // An inline array is a value like a struct (A4): copied at its binding point.
        if (target is IrInlineArrayType inlineValue && from is not IrOptionalType && !_fresh.Contains(value))
        {
            var copy = _slots.NewTemp(inlineValue);
            _b.Emit(new CopyValue(copy, value, inlineValue, span));
            _fresh.Add(copy);
            value = copy;
        }

        // Value semantics. The binding point is where a struct value gets a new home; that is where the
        // copy happens, and only there. A freshly built value does not need it: it has no other owner to
        // detach from.
        if (target is IrStructType value_ && from is not IrOptionalType && !_fresh.Contains(value))
            value = CopyStructValue(value, value_, span);

        // A child interface value where a parent is expected (04 D10): the parent's table, found
        // through the concrete type's conformance list — the same op as the downcast to an
        // interface, since both read that list.
        if (target is IrInterfaceType parentIface && source is IrInterfaceType childIface
            && from is not IrOptionalType && childIface.Type != parentIface.Type)
        {
            var up = _slots.NewTemp(parentIface);
            _b.Emit(new Downcast(up, value, parentIface.Type, parentIface, span));
            value = up;
            from = parentIface;
        }

        if (target is IrInterfaceType iface && source is not IrInterfaceType
            && from is not IrOptionalType)
        {
            // The way behind an interface is a binding point too: a struct is copied there, or the
            // interface value would share the slot array with its source and a mutation through the
            // interface would hit the original.
            if (source is IrStructType boxed && !_fresh.Contains(value))
                value = CopyStructValue(value, boxed, span);

            value = MakeInterfaceValue(value, source, iface, span);
            from = iface;
        }

        // Wrap, innermost level first: the level at depth 'missing - 1 - i' of 'to'.
        for (var i = missing - 1; i >= 0; i--)
        {
            var level = to;
            for (var j = 0; j < i; j++) level = ((IrOptionalType)level).Inner;
            var wrapped = _slots.NewTemp(level);
            _b.Emit(new OptSome(wrapped, value, ((IrOptionalType)level).Inner, span));
            value = wrapped;
        }
        return value;
    }

    /// <summary>The IR side of <see cref="TypeFacts.Widens"/>: the same table on scalar kinds.</summary>
    private static bool WidensScalar(IrScalar from, IrScalar to)
    {
        if (from == IrScalar.F32 && to == IrScalar.F64) return true;
        static (int Bits, bool Signed)? Shape(IrScalar kind) => kind switch
        {
            IrScalar.I8 => (8, true), IrScalar.I16 => (16, true), IrScalar.I32 => (32, true), IrScalar.I64 => (64, true),
            IrScalar.U8 => (8, false), IrScalar.U16 => (16, false), IrScalar.U32 => (32, false), IrScalar.U64 => (64, false),
            _ => null,
        };
        if (Shape(from) is not { } s || Shape(to) is not { } t) return false;
        if (s.Signed && !t.Signed) return false;
        return t.Bits > s.Bits;
    }

    /// <summary>
    /// Creates an independent copy of a struct value.
    ///
    /// <para>The copy is recursive at runtime over nested structs and shallow over everything else: a
    /// field of type <c>class</c> or <c>T[]</c> carries a reference, and that is shared. The value is
    /// copied, not the world behind it.</para>
    /// </summary>
    private TempId CopyStructValue(TempId value, IrStructType type, Span span)
    {
        var dest = _slots.NewTemp(type);
        _b.Emit(new StructCopy(dest, value, type.Type, span));
        _fresh.Add(dest);
        return dest;
    }

    /// <summary>Lifts an object reference to its interface type.</summary>
    private TempId MakeInterfaceValue(TempId value, IrType concrete, IrInterfaceType iface,
        Span span)
    {
        var concreteId = concrete switch
        {
            IrRefType r => r.Type,
            IrStructType v => v.Type,
            IrEnumType e => e.Type,
            _ => throw NotSupported(
                "a value of this type cannot be used through an interface "
                + "(only classes, structs and enums)",
                span),
        };

        var dest = _slots.NewTemp(iface);
        _b.Emit(new MakeInterface(dest, value, concreteId, iface.Type, span));
        return dest;
    }

    private TempId LowerForceUnwrap(Expr operand, Span span)
    {
        var value = LowerExpr(operand);
        if (TypeOfExpr(operand) is not IrOptionalType option)
            throw NotSupported("'!' on a non-optional", span, LoweringDiagnostics.NeverNull);

        // The one unwrap that can be wrong: the program says the value is there (03 T4 O4).
        var dest = _slots.NewTemp(option.Inner);
        _b.Emit(new OptGet(dest, value, option.Inner, span) { Checked = true });
        return dest;
    }

    /// <summary>
    /// <c>a ?? b</c> — the right side is evaluated ONLY when there is no value on the left. Hence a
    /// branch rather than an instruction, exactly as for <c>&amp;&amp;</c> and <c>||</c>: a stack machine
    /// cannot transport an unevaluated expression.
    /// </summary>
    private TempId LowerCoalesce(BinaryExpr expr)
    {
        var type = TypeOfExpr(expr);
        var slot = _slots.DeclareSynthetic("coalesce", type);

        var option = LowerExpr(expr.Left);
        if (TypeOfExpr(expr.Left) is not IrOptionalType left)
            throw NotSupported("'??' on a non-optional", expr.Span, LoweringDiagnostics.NeverNull);

        var test = _slots.NewTemp(BoolType);
        _b.Emit(new OptIsSome(test, option, expr.Span));

        var whenSome = _b.NewBlock();
        var whenNone = _b.NewBlock();
        var merge = _b.NewBlock();
        _b.Seal(new CondBranch(test, whenSome, whenNone, expr.Span));

        _b.SwitchTo(whenSome);
        var unwrapped = _slots.NewTemp(left.Inner);
        _b.Emit(new OptGet(unwrapped, option, left.Inner, expr.Span));
        _b.Emit(new StoreLocal(slot, Coerce(unwrapped, left.Inner, type, expr.Span), expr.Span));
        _b.Seal(new Branch(merge, expr.Span));

        _b.SwitchTo(whenNone);
        // 'x ?? throw e': the absent path throws and never reaches the merge.
        if (Diverges(expr.Right)) LowerDiverging(expr.Right);
        else if (ComesOut(() =>
                 {
                     var fallback = LowerExpr(expr.Right);
                     _b.Emit(new StoreLocal(slot, Coerce(fallback, TypeOfExpr(expr.Right), type, expr.Span), expr.Span));
                 }))
            _b.Seal(new Branch(merge, expr.Span));

        _b.SwitchTo(merge);
        var dest = _slots.NewTemp(type);
        _b.Emit(new LoadLocal(dest, slot, type, expr.Span));
        return dest;
    }

    /// <summary>
    /// <c>x ??= v</c> — assigns only when <c>x</c> has no value.
    ///
    /// <para>Like <c>??</c> a branch rather than an opcode: the right side is evaluated only then, and an
    /// unevaluated expression cannot be transported on a stack machine. The only difference from
    /// <c>??</c> is that the result goes back into the existing slot rather than into a new one.</para>
    /// </summary>
    private TempId LowerCoalesceAssign(LocalId slot, AssignExpr expr)
    {
        var type = _slots.TypeOfLocal(slot);
        if (type is not IrOptionalType option)
            throw NotSupported("'??=' on a non-optional target", expr.Span, LoweringDiagnostics.NeverNull);

        var current = _slots.NewTemp(type);
        _b.Emit(new LoadLocal(current, slot, type, expr.Target.Span));

        var test = _slots.NewTemp(BoolType);
        _b.Emit(new OptIsSome(test, current, expr.Span));

        var whenNone = _b.NewBlock();
        var merge = _b.NewBlock();
        // When there is already a value, the slot stays untouched, and the right side does not run.
        _b.Seal(new CondBranch(test, merge, whenNone, expr.Span));

        _b.SwitchTo(whenNone);
        if (ComesOut(() => _b.Emit(new StoreLocal(slot, LowerExprAs(expr.Value, type), expr.Span))))
            _b.Seal(new Branch(merge, expr.Span));

        _b.SwitchTo(merge);
        var result = _slots.NewTemp(type);
        _b.Emit(new LoadLocal(result, slot, type, expr.Span));
        return result;
    }

    /// <summary>
    /// <c>a?.b</c> — a field access that does not happen when there is no value.
    ///
    /// <para>The result is always an optional: when <c>a</c> has no value it is <c>optnone</c>, otherwise
    /// the unwrapped field in <c>optsome</c>. That too is a branch — the field access must NOT run on an
    /// empty reference, and an opcode could not express that without branching itself.</para>
    /// </summary>
    /// <summary>
    /// <c>b?.get()</c> — the receiver is optional, so the result is too.
    ///
    /// <para>THE CALL RUNS THROUGH THE SAME RESOLUTION AS ANY OTHER, only with an already unwrapped
    /// receiver. A path of its own here would have to answer virtual dispatch, natives, extensions and
    /// generics a second time.</para>
    /// </summary>
    private TempId LowerOptionalCall(CallExpr expr, MemberExpr callee)
    {
        if (TypeOfExpr(expr) is not IrOptionalType result)
            throw NotSupported("'?.' with a call whose result is not an optional", expr.Span);

        if (TypeOfExpr(callee.Target) is not IrOptionalType target)
            throw NotSupported("'?.' on a non-optional", expr.Span, LoweringDiagnostics.NeverNull);

        // The method's actual return type. 'TypeOfExpr(expr)' is no good for that: the sema gave the call
        // the chain type '?int', while the method yields 'int'.
        if (_types.TypeOf(callee) is not Sema.Optional { Inner: FnType signature })
            throw NotSupported("'?.' with a call on something that is not a method", expr.Span);

        var returned = LowerType(signature.Return, expr.Span);

        var slot = _slots.DeclareSynthetic("chain", result);
        var option = LowerExpr(callee.Target);

        var test = _slots.NewTemp(BoolType);
        _b.Emit(new OptIsSome(test, option, expr.Span));

        var whenSome = _b.NewBlock();
        var whenNone = _b.NewBlock();
        var merge = _b.NewBlock();
        _b.Seal(new CondBranch(test, whenSome, whenNone, expr.Span));

        _b.SwitchTo(whenSome);
        var unwrapped = _slots.NewTemp(target.Inner);

        // Cannot panic: the branch stands behind the 'optissome'. The same division of labour as for the
        // field access and for flow narrowing.
        _b.Emit(new OptGet(unwrapped, option, target.Inner, expr.Span));

        // The call then runs through LowerCall like any other. What it sees differently is exactly two
        // things, and both hang on the AST node rather than on a parameter chain: the receiver is already
        // unwrapped, and the return type is the method's.
        //
        // A call path of its own would have to answer virtual dispatch, generics, constraints, natives
        // and extensions a second time. A first attempt did that through a special case in the 'switch'
        // and promptly hid the generics detection, so 'b?.get()' on a 'Box<int>' was reported as
        // 'external or bodiless'.
        // The call's path. An argument of it may give no value and end it where it stands; the
        // receiver's absence is the other path, and it still runs.
        var called = ComesOut(() =>
        {
            TempId? produced;
            _chainReceivers[callee.Target] = unwrapped;
            _chainResults[expr] = returned;
            try
            {
                produced = LowerCall(expr);
            }
            finally
            {
                _chainReceivers.Remove(callee.Target);
                _chainResults.Remove(expr);
            }

            if (produced is not { } value)
                throw NotSupported("'?.' with a call that returns nothing", expr.Span);

            // When the METHOD itself already yields an optional ('fn empty(): ?int'), its result is already
            // the result type: the sema collapsed '??int' to '?int'. Wrapping a second time would create a
            // level the language does not have.
            var stored = value;
            if (returned is not IrOptionalType)
            {
                stored = _slots.NewTemp(result);
                _b.Emit(new OptSome(stored, value, result.Inner, expr.Span));
            }

            _b.Emit(new StoreLocal(slot, stored, expr.Span));
        });
        if (called) _b.Seal(new Branch(merge, expr.Span));

        _b.SwitchTo(whenNone);
        var none = _slots.NewTemp(result);
        _b.Emit(new OptNone(none, result.Inner, expr.Span));
        _b.Emit(new StoreLocal(slot, none, expr.Span));
        _b.Seal(new Branch(merge, expr.Span));

        _b.SwitchTo(merge);
        var dest = _slots.NewTemp(result);
        _b.Emit(new LoadLocal(dest, slot, result, expr.Span));
        return dest;
    }

    private TempId LowerOptionalMember(MemberExpr expr)
    {
        var resultType = TypeOfExpr(expr);
        if (resultType is not IrOptionalType result)
            throw NotSupported("'?.' whose result is not an optional", expr.Span);

        if (TypeOfExpr(expr.Target) is not IrOptionalType target)
            throw NotSupported("'?.' on a non-optional", expr.Span, LoweringDiagnostics.NeverNull);

        var slot = _slots.DeclareSynthetic("chain", resultType);
        var option = LowerExpr(expr.Target);

        var test = _slots.NewTemp(BoolType);
        _b.Emit(new OptIsSome(test, option, expr.Span));

        var whenSome = _b.NewBlock();
        var whenNone = _b.NewBlock();
        var merge = _b.NewBlock();
        _b.Seal(new CondBranch(test, whenSome, whenNone, expr.Span));

        _b.SwitchTo(whenSome);
        var unwrapped = _slots.NewTemp(target.Inner);
        _b.Emit(new OptGet(unwrapped, option, target.Inner, expr.Span));

        // The 'optget' cannot panic: the branch stands behind the 'optissome', so the proof is made. The
        // same division of labour as in flow narrowing.
        var (type, field, fieldType) = ResolveFieldOn(target.Inner, expr);
        var value = _slots.NewTemp(fieldType);
        _b.Emit(new LoadField(value, unwrapped, type, field, fieldType, expr.Span));

        // When the FIELD itself is optional ('w: ?int'), its value is already the result type: the sema
        // collapsed '??int' to '?int'. Wrapping a second time would create a level the language does not
        // have.
        var stored = value;
        if (fieldType is not IrOptionalType)
        {
            stored = _slots.NewTemp(resultType);
            _b.Emit(new OptSome(stored, value, result.Inner, expr.Span));
        }

        _b.Emit(new StoreLocal(slot, stored, expr.Span));
        _b.Seal(new Branch(merge, expr.Span));

        _b.SwitchTo(whenNone);
        var none = _slots.NewTemp(resultType);
        _b.Emit(new OptNone(none, result.Inner, expr.Span));
        _b.Emit(new StoreLocal(slot, none, expr.Span));
        _b.Seal(new Branch(merge, expr.Span));

        _b.SwitchTo(merge);
        var dest = _slots.NewTemp(resultType);
        _b.Emit(new LoadLocal(dest, slot, resultType, expr.Span));
        return dest;
    }

    /// <summary>The field index and type on the unwrapped carrier. Separate from
    /// <c>ResolveFieldAccess</c>, because there the carrier expression itself is lowered; here it is
    /// already unwrapped.</summary>
    private (TypeId Type, FieldId Field, IrType FieldType) ResolveFieldOn(IrType carrier,
        MemberExpr expr)
    {
        if (_types.TypeOf(expr.Target) is not Optional option
            || TypeFacts.SymbolOf(option.Inner) is not { } named)
            throw NotSupported($"'?.{expr.Member}' on " +
                               $"'{TypeFacts.Display(_types.TypeOf(expr.Target))}'", expr.Span);

        var type = _typeTable.Intern(named);
        var field = _typeTable.FieldOf(named, expr.Member, expr.Span);
        return (type, field, _typeTable.Defs[type.Value].FieldTypes[field.Value]);
    }

    // ------------------------------------------------------------------ arrays

    /// <summary><c>[a, b, c]</c> — one instruction rather than three stores. The values lie on the stack
    /// in source order at the <c>newarr</c>.</summary>
    private TempId LowerArrayLiteral(ArrayLitExpr expr)
    {
        // '[a, b, c]' as a 'T[3]' (03 T13 A4): the inline array, a fresh value.
        if (TypeOfExpr(expr) is IrInlineArrayType inline)
        {
            var parts = new TempId[expr.Elements.Length];
            for (var i = 0; i < expr.Elements.Length; i++)
                parts[i] = LowerExprAs(expr.Elements[i], inline.Element);
            var built = _slots.NewTemp(inline);
            _b.Emit(new NewInline(built, inline.Element, inline.Length, parts, Repeat: false, expr.Span));
            _fresh.Add(built);
            return built;
        }

        if (TypeOfExpr(expr) is not IrArrayType type)
            throw NotSupported("array literal of a non-array type", expr.Span);

        // AS the element type, not merely lowered: the element position is a context like any
        // other, so a class becomes an interface value here, a 'null' becomes the empty optional,
        // and a literal adapts to the width the sema settled on. Lowered bare, an element carried
        // its own type into a slot declared for another one — malformed IR for the interface case,
        // and no context at all for a 'null' the sema had long accepted.
        var elements = new TempId[expr.Elements.Length];
        for (var i = 0; i < expr.Elements.Length; i++)
            elements[i] = LowerExprAs(expr.Elements[i], type.Element);

        var dest = _slots.NewTemp(type);
        _b.Emit(new NewArray(dest, type.Element, elements, expr.Span));
        return dest;
    }

    private TempId LowerIndexRead(IndexExpr expr)
    {
        // A type's own index (04 D6): the call the sema desugared, 'x.index(k)'.
        if (_types.OperatorCallOf(expr) is { } indexCall)
            return LowerCall(indexCall) ?? throw Bug("'index' returned no value");
        // A container from std.collections goes through 'Indexable<T>.get(i)' — the same division of
        // labour as for 'for-in': the compiler knows ONE built-in form, the array, and everything else
        // runs through the interface.
        if (LowerIndexableCall(expr, "get", null) is { } viaInterface) return viaInterface;
        if (expr.Index is SliceRangeExpr range) return LowerSlice(expr, range);

        var (array, index, element) = ResolveIndexAccess(expr);
        var dest = _slots.NewTemp(element);
        _b.Emit(new LoadElem(dest, array, index, element, expr.Span));
        return dest;
    }

    /// <summary>
    /// <c>xs[i]</c> and <c>xs[i] = v</c> on a type satisfying <c>Indexable&lt;T&gt;</c>, as a call to
    /// <c>get</c> or <c>set</c>. Returns <c>null</c> when the carrier is an array; the built-in route
    /// through <c>ldelem</c> and <c>stelem</c> then applies.
    ///
    /// <para>The call goes DIRECTLY rather than virtually: the receiver type is statically settled, and
    /// for a generic instance the monomorphization has produced the method anyway. That is the same gain
    /// as in constraint dispatch — an interface does not automatically mean a vtable.</para>
    /// </summary>
    private TempId? LowerIndexableCall(IndexExpr expr, string method, TempId? value)
    {
        if (TypeOfExpr(expr.Target) is IrArrayType or IrSliceType or IrInlineArrayType) return null;

        var carrier = SubstituteType(_types.TypeOf(expr.Target));
        if (TypeFacts.SymbolOf(carrier) is not { } owner) return null;
        if (owner.Members.LookupLocal(method) is not FunctionSymbol symbol) return null;
        if (symbol.Declaration is not FunctionDecl declaration) return null;

        var target = carrier is GenericInstance instance
            ? _instances.RequestMethod(symbol, declaration, instance, expr.Span)
            : TryResolveFunction(symbol, out var direct)
                ? direct
                : throw NotSupported($"'{owner.Name}.{method}' was not lowered", expr.Span);

        var receiver = LowerExpr(expr.Target);
        var index = LowerExprAs(expr.Index, new IrScalarType(IrScalar.I64));

        var arguments = value is { } stored
            ? new[] { receiver, index, stored }
            : new[] { receiver, index };

        // 'set' yields void, 'get' the element type.
        if (value is { } assigned)
        {
            _b.Emit(new Call(null, target, arguments, expr.Span));
            return assigned;
        }

        var dest = _slots.NewTemp(TypeOfExpr(expr));
        _b.Emit(new Call(dest, target, arguments, expr.Span));
        _fresh.Add(dest);
        return dest;
    }

    /// <summary>The shared part of reading and writing. <c>[i]</c> is built in on <c>T[]</c> only;
    /// everything else goes through the <c>Indexable&lt;T&gt;</c> interface.</summary>
    private (TempId Array, TempId Index, IrType Element) ResolveIndexAccess(IndexExpr expr)
    {
        if (ElementOf(TypeOfExpr(expr.Target)) is not { } element)
            throw NotSupported($"indexing a '{TypeFacts.Display(_types.TypeOf(expr.Target))}' " +
                               "(only arrays; other containers need the Indexable interface)",
                expr.Span);

        var target = LowerExpr(expr.Target);
        return (target, LowerIndexValue(expr.Index, target), element);
    }

    /// <summary>The element type of an array or of a view of one; null for anything else.</summary>
    private static IrType? ElementOf(IrType type) => type switch
    {
        IrArrayType a => a.Element,
        IrSliceType s => s.Element,
        IrInlineArrayType ia => ia.Element,
        _ => null,
    };

    /// <summary>
    /// <c>xs[a..b]</c>: a view of the elements from <c>a</c> to <c>b</c> (03 T13 A2), of an
    /// array or of a view — checked against the source's length, no copy. An open bound is the
    /// start or the length; an inclusive end is one past its bound. The source is evaluated once,
    /// before the bounds, so <c>^n</c> reads its length.
    /// </summary>
    private TempId LowerSlice(IndexExpr expr, SliceRangeExpr range)
    {
        if (ElementOf(TypeOfExpr(expr.Target)) is not { } element)
            throw NotSupported($"a view of a '{TypeFacts.Display(_types.TypeOf(expr.Target))}'", expr.Span);
        var i64 = new IrScalarType(IrScalar.I64);
        var source = LowerExpr(expr.Target);
        var low = range.Low is null ? EmitConst(new IntConst(0), i64, range.Span) : LowerIndexValue(range.Low, source);
        TempId high;
        if (range.High is null)
        {
            high = _slots.NewTemp(i64);
            _b.Emit(new ArrayLen(high, source, range.Span));
        }
        else
        {
            high = LowerIndexValue(range.High, source);
            if (range.IsInclusive)
                high = EmitBinary(IrBinKind.Add, i64, high, EmitConst(new IntConst(1), i64, range.Span), range.Span);
        }
        var dest = _slots.NewTemp(new IrSliceType(element));
        _b.Emit(new MakeSlice(dest, source, low, high, element, expr.Span));
        return dest;
    }

    /// <summary>An array where a view of it is expected gives a view of itself, whole (A2).</summary>
    private TempId ViewOf(TempId array, IrType element, Span span)
    {
        var i64 = new IrScalarType(IrScalar.I64);
        var length = _slots.NewTemp(i64);
        _b.Emit(new ArrayLen(length, array, span));
        var dest = _slots.NewTemp(new IrSliceType(element));
        _b.Emit(new MakeSlice(dest, array, EmitConst(new IntConst(0), i64, span), length, element, span));
        return dest;
    }

    /// <summary>The index as an <c>int</c>: a narrower one widened, <c>^n</c> as the length of
    /// the indexed value less <c>n</c> (03 T14 N6) — the value is evaluated once, before.</summary>
    private TempId LowerIndexValue(Expr index, TempId target)
    {
        if (index is not UnaryExpr { Operator: UnaryOp.FromEnd } fromEnd)
            return LowerExprAs(index, new IrScalarType(IrScalar.I64));
        var length = _slots.NewTemp(new IrScalarType(IrScalar.I64));
        _b.Emit(new ArrayLen(length, target, fromEnd.Span));
        var n = LowerExprAs(fromEnd.Operand, new IrScalarType(IrScalar.I64));
        return EmitBinary(IrBinKind.Sub, new IrScalarType(IrScalar.I64), length, n, fromEnd.Span);
    }

    private TempId LowerArrayLength(Expr array, Span span)
    {
        var value = LowerExpr(array);
        var dest = _slots.NewTemp(new IrScalarType(IrScalar.I64));
        _b.Emit(new ArrayLen(dest, value, span));
        return dest;
    }

    // ------------------------------------------------------------------ objects

    /// <summary>
    /// <c>Account { owner = a, balance = b }</c> becomes one <c>newobj</c> and one <c>storefield</c> per
    /// field.
    ///
    /// <para>WRITING HAPPENS IN DECLARATION ORDER, NOT IN WRITE ORDER. The initializers may stand in any
    /// order in the source, but the layout is the declaration, and only a fixed order makes the bytecode
    /// deterministic. The values are nevertheless evaluated in SOURCE order — with side effects that is
    /// the order the reader expects.</para>
    /// </summary>
    private TempId LowerObjectInit(StructInitExpr expr)
    {
        // 'Shape.Tri { a = 3, b = 4 }' and 'Ev<int>.Hit { … }' are struct variants. They look like an
        // object literal but are a variant construction and therefore go through newvariant. Which
        // INSTANCE is meant stands in the type of the expression.
        if (SubstituteType(_types.TypeOf(expr)) is NamedRef { Symbol.Kind: TypeSymbolKind.Enum }
            or GenericInstance { Definition.Kind: TypeSymbolKind.Enum })
            return LowerStructVariant(expr);

        // An initializer for an instance of a generic type ('Box<int> { v = 3 }'): the type arguments
        // decide the layout, so they have to be present when interning — and through the own
        // substitution, in case the calling function is itself an instance.
        TypeId type;
        TypeSymbol declaring;
        GenericInstance? built = null;
        if (SubstituteType(_types.TypeOf(expr)) is GenericInstance instance
            && instance.Definition.Kind is TypeSymbolKind.Class or TypeSymbolKind.Struct)
        {
            type = _typeTable.Intern(instance.Definition, instance.Arguments);
            declaring = instance.Definition;
            built = instance;
        }
        else if (_types.TypeOf(expr) is NamedRef
                 { Symbol.Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct } named)
        {
            type = _typeTable.Intern(named.Symbol);
            declaring = named.Symbol;
        }
        else
        {
            throw NotSupported($"initializer for '{TypeFacts.Display(_types.TypeOf(expr))}' " +
                               "(only classes and structs are lowered)", expr.Span);
        }

        var layout = _typeTable.Defs[type.Value];

        // Evaluate all values first, in source order, then write in layout order.
        var values = new Dictionary<string, TempId>(StringComparer.Ordinal);
        foreach (var field in expr.Fields)
        {
            if (values.ContainsKey(field.Name))
                throw Bug($"duplicate initializer for '{field.Name}' reached lowering");
            // Adapt to the declared field type: a field of an interface type takes a class only as a fat
            // pointer.
            var fieldIndex = Array.IndexOf(layout.FieldNames, field.Name);
            values[field.Name] = fieldIndex >= 0
                ? LowerExprAs(field.Value, layout.FieldTypes[fieldIndex])
                : LowerExpr(field.Value);
        }

        // An omitted field gets its default, evaluated HERE at the construction site rather than once at
        // the type. A default is an expression, and storing it in the layout would mean writing an
        // expression into a type table.
        //
        // Without a default it stays an error: a silent zero value would be a guess, and the sema knows
        // no rule that allows it.
        var declaredFields = declaring.Declaration switch
        {
            ClassDecl c => c.Members.OfType<FieldDecl>().ToArray(),
            StructDecl v => v.Members.OfType<FieldDecl>().ToArray(),
            _ => [],
        };

        foreach (var field in declaredFields)
        {
            if (values.ContainsKey(field.Name)) continue;

            var index = Array.IndexOf(layout.FieldNames, field.Name);

            // A '?T' field without a written default is 'null' (02 M14 I1).
            if (field.Default is null && index >= 0 && layout.FieldTypes[index] is IrOptionalType absent)
            {
                var none = _slots.NewTemp(absent);
                _b.Emit(new OptNone(none, absent.Inner, expr.Span));
                values[field.Name] = none;
                continue;
            }

            if (field.Default is null)
                throw NotSupported($"initializer omits field '{field.Name}', which has no default",
                    expr.Span);

            // As code of the instance that is built: the default may name the type's parameters.
            values[field.Name] = UnderInstance(built, () => index >= 0
                ? LowerExprAs(field.Default, layout.FieldTypes[index])
                : LowerExpr(field.Default));
        }

        foreach (var name in layout.FieldNames)
            if (!values.ContainsKey(name))
                throw Bug($"field '{name}' of '{declaring.Name}' has neither a value nor a default");

        // A struct value is the same slot array as a class object at runtime, so 'newobj' serves both.
        // The difference lies solely in the binding points.
        IrType result = _typeTable.IsStruct(type)
            ? new IrStructType(type)
            : new IrRefType(type);
        var dest = _slots.NewTemp(result);
        _b.Emit(new NewObject(dest, type, result, expr.Span));

        for (var i = 0; i < layout.FieldNames.Length; i++)
            _b.Emit(new StoreField(dest, type, new FieldId(i), values[layout.FieldNames[i]], expr.Span));

        // Freshly built: this value belongs to nobody yet, and a copy when binding would be ballast.
        _fresh.Add(dest);
        return dest;
    }

    /// <summary>Fills a global slot. Occurs only in the synthetic initializer: in user source there is no
    /// assignment to a global, because they are all <c>let</c>.</summary>
    private void LowerGlobalInit(GlobalInitStmt stmt)
    {
        var (id, type) = _globals.Resolve(stmt.Symbol, stmt.Span);
        var value = LowerExprAs(stmt.Binding.Initializer!, type);
        _b.Emit(new StoreGlobal(id, value, stmt.Span));
    }

    private GlobalSymbol? GlobalOf(IdentifierExpr expr)
    {
        var symbol = _types.RefOf(expr);
        if (symbol is ImportBindingSymbol import) symbol = import.Target;
        return symbol as GlobalSymbol;
    }

    private TempId LowerGlobalAssign(AssignExpr expr, GlobalSymbol global)
    {
        var (id, type) = _globals.Resolve(global, expr.Span);
        TempId Load()
        {
            var loaded = _slots.NewTemp(type);
            _b.Emit(new LoadGlobal(loaded, id, type, expr.Target.Span));
            return loaded;
        }
        if (expr.Operator is null)
        {
            var value = LowerExprAs(expr.Value, type);
            _b.Emit(new StoreGlobal(id, value, expr.Span));
            return value;
        }
        if (expr.Operator is BinaryOp.Coalesce or BinaryOp.LogicalAnd or BinaryOp.LogicalOr)
            return LowerShortCircuitAssign(expr, type, Load, v => _b.Emit(new StoreGlobal(id, v, expr.Span)));
        if (_types.OperatorCallOf(expr) is { } operatorCall)
        {
            var combined = LowerCall(operatorCall) ?? throw Bug("operator compound returned no value");
            _b.Emit(new StoreGlobal(id, combined, expr.Span));
            return combined;
        }
        var result = EmitBinary(IrBinKindExtensions.FromAst(expr.Operator.Value), type, Load(), LowerExpr(expr.Value), expr.Span);
        _b.Emit(new StoreGlobal(id, result, expr.Span));
        return result;
    }

    /// <summary>A global slot through a bare name: a module <c>let</c> in the own or an imported
    /// module.</summary>
    private TempId? TryLowerGlobalIdentifier(IdentifierExpr expr)
    {
        var symbol = _types.RefOf(expr);
        if (symbol is ImportBindingSymbol import) symbol = import.Target;
        if (symbol is not GlobalSymbol global) return null;
        // A module 'let' narrows like a local (03 §3.2): the read unwraps what the test proved.
        var type = _globals.ConstantOf(global)?.Type ?? _globals.Resolve(global, expr.Span).Type;
        return Narrow(expr, LowerGlobalRead(global, expr.Span), type);
    }

    /// <summary>A global slot: a module <c>let</c> or a <c>static let</c>. Both are the same in the
    /// bytecode; the difference is only where the name is visible.</summary>
    private TempId LowerGlobalRead(GlobalSymbol symbol, Span span)
    {
        // A constant, a 'static let' of a literal, is the literal where it is read (GlobalTable).
        if (_globals.ConstantOf(symbol) is { } constant) return LowerExprAs(constant.Literal, constant.Type);
        var (id, type) = _globals.Resolve(symbol, span);
        var dest = _slots.NewTemp(type);
        _b.Emit(new LoadGlobal(dest, id, type, span));
        return dest;
    }

    /// <summary>
    /// A method as a value. A static one is a function value without an environment, like a
    /// free function. An instance method is a BOUND closure (08 Y11 F10): its environment holds
    /// the receiver — the object shared, a struct copied (02 M8 C2) — and its code is a function
    /// built here that takes the receiver out of the environment and calls the method with it.
    /// </summary>
    private TempId LowerMethodValue(MemberExpr expr, FunctionSymbol method)
    {
        if (method.Declaration is FunctionDecl { Generics.Length: > 0 })
            throw NotSupported($"a generic method ('{method.Name}') as a value — wrap it in a lambda", expr.Span);
        if (TypeOfExpr(expr) is not IrFunctionType signature)
            throw NotSupported($"'{method.Name}' as a value", expr.Span);
        if (!TryResolveFunction(method, out var target))
            throw NotSupported($"reference to '{method.Name}' as a value", expr.Span);

        // A function of a module, named through the module ('util.twice'), has no receiver
        // either: its target names a module, not a value.
        if (method.Declaration is FunctionDecl { IsStatic: true }
            || _types.TypeOf(expr.Target) is NonValueType { Symbol: ModuleSymbol })
        {
            var direct = _slots.NewTemp(signature);
            _b.Emit(new MakeClosure(direct, target, null, signature, expr.Span));
            return direct;
        }

        var receiverType = SubstituteType(_types.TypeOf(expr.Target));
        if (receiverType is GenericInstance)
            throw NotSupported($"a method of a generic instance ('{method.Name}') as a value — wrap it in a lambda", expr.Span);
        var receiver = LowerType(receiverType, expr.Target.Span);
        var envType = _typeTable.EnvironmentFor(_name, [receiver], ["this"]);
        var env = _slots.NewTemp(envType);
        _b.Emit(new NewObject(env, envType.Type, envType, expr.Span));
        _b.Emit(new StoreField(env, envType.Type, new FieldId(0), LowerExprAs(expr.Target, receiver), expr.Span));

        var name = _lambdas.BuiltName(_name, "bound:" + method.Name);
        var throws = _types.TypeOf(expr) is FnType bound && ThrownHere(bound.Throws).Length > 0;
        var code = _lambdas.RegisterBuilt(_ => BuildBoundMethod(name, envType, receiver, signature, target, throws));
        var dest = _slots.NewTemp(signature);
        _b.Emit(new MakeClosure(dest, code, env, signature, expr.Span));
        return dest;
    }

    /// <summary>The code of a bound method value: environment first, then the parameters; the
    /// receiver is read from the environment and the method called with it. A method that throws
    /// passes its error on through the closure's slot (05 E2, 03 T17).</summary>
    private static IrFunction BuildBoundMethod(string name, IrRefType envType, IrType receiver,
        IrFunctionType signature, FunctionId target, bool throws)
    {
        var locals = new List<IrLocal> { new(new LocalId(0), "<env>", envType) };
        for (var i = 0; i < signature.Parameters.Length; i++)
            locals.Add(new IrLocal(new LocalId(i + 1), $"p{i}", signature.Parameters[i]));
        var temps = new List<IrTemp>();
        TempId Temp(IrType type) { var t = new TempId(temps.Count); temps.Add(new IrTemp(t, type)); return t; }

        var blocks = new List<IrBlock>();
        var b = new BlockBuilder(blocks);
        var env = Temp(envType);
        b.Emit(new LoadLocal(env, new LocalId(0), envType, default));
        var self = Temp(receiver);
        b.Emit(new LoadField(self, env, envType.Type, new FieldId(0), receiver, default));
        var args = new TempId[signature.Parameters.Length + 1];
        args[0] = self;
        for (var i = 0; i < signature.Parameters.Length; i++)
        {
            args[i + 1] = Temp(signature.Parameters[i]);
            b.Emit(new LoadLocal(args[i + 1], new LocalId(i + 1), signature.Parameters[i], default));
        }
        var isVoid = signature.Return is IrScalarType { Kind: IrScalar.Void };
        TempId? result = isVoid ? null : Temp(signature.Return);
        b.Emit(new Call(result, target, args, default));
        if (throws)
        {
            var fail = b.NewBlock();
            var next = b.NewBlock();
            b.Seal(new ErrorBranch(fail, next, default));
            b.SwitchTo(fail);
            b.Seal(new Propagate(default));
            b.SwitchTo(next);
        }
        b.Seal(new Return(result, default));

        return new IrFunction(name, signature.Return, locals.Count, locals, temps, blocks) { Entry = new BlockId(0), Throws = throws };
    }

    private TempId LowerFieldRead(MemberExpr expr)
    {
        // 'T.zero' (05 §7 rule 2): the interface's constant through a constraint. Under this
        // instance's substitution T is a type, and the read is that type's own constant.
        if (_types.RefOf(expr) is GlobalSymbol
            && (_types.RefOf(expr.Target) as GenericParamSymbol
                ?? (_types.TypeOf(expr.Target) as NonValueType)?.Symbol as GenericParamSymbol) is { } parameter
            && _substitution.TryGetValue(parameter, out var boundTo))
        {
            var owner = TypeFacts.SymbolOf(boundTo) ?? _typeTable.BuiltinSymbolOf(boundTo);
            return LowerGlobalRead(
                (owner is null ? null : _typeTable.StaticOf(owner, expr.Member))
                ?? throw NotSupported($"the constant '{expr.Member}' of '{TypeFacts.Display(boundTo)}' through a constraint", expr.Span),
                expr.Span);
        }

        // 'P.ZERO' is not a field read but a constant read: a 'static let' is a global slot rather than
        // an object slot.
        if (_types.RefOf(expr) is GlobalSymbol constant) return LowerGlobalRead(constant, expr.Span);

        // 'Shape.Empty' is a unit variant. It looks like a member access but is a construction without
        // arguments.
        if (_types.RefOf(expr) is EnumVariantSymbol)
            return LowerVariantCall(expr.Member, [], expr, expr.Span);

        // 'obj.method' and 'Type.staticFn' as VALUES (08 Y11 F10, 03 T17).
        if (_types.RefOf(expr) is FunctionSymbol method && !expr.IsOptional)
            return LowerMethodValue(expr, method);

        // 'a?.b' accesses only when 'a' has a value.
        if (expr.IsOptional) return LowerOptionalMember(expr);

        var (obj, type, field, fieldType) = ResolveFieldAccess(expr);
        var dest = _slots.NewTemp(fieldType);
        _b.Emit(new LoadField(dest, obj, type, field, fieldType, expr.Span));
        return dest;
    }

    /// <summary>The shared part of reading and writing: evaluate the object and determine the type, the
    /// field index and the field type.</summary>
    private (TempId Object, TypeId Type, FieldId Field, IrType FieldType) ResolveFieldAccess(MemberExpr expr)
    {
        // The receiver may be an instance of a generic type ('Box<int>'). It then decides the layout
        // rather than the definition — 'Box<int>' and 'Box<string>' have different field types at the
        // same position.
        var target = SubstituteType(_types.TypeOf(expr.Target));

        // A tuple's element, by position or by the label the sema resolved (03 T16): the
        // layout's fields are the positions.
        if (target is Sema.TupleOf tuple)
        {
            var position = tuple.Labels is { } labels ? Array.IndexOf(labels, expr.Member) : -1;
            if (position < 0) position = int.Parse(expr.Member, System.Globalization.CultureInfo.InvariantCulture);
            var tupleType = (IrStructType)LowerType(tuple, expr.Span);
            var tupleLayout = _typeTable.Defs[tupleType.Type.Value];
            return (LowerExpr(expr.Target), tupleType.Type, new FieldId(position), tupleLayout.FieldTypes[position]);
        }

        var declaring = target switch
        {
            NamedRef { Symbol.Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct } n => n.Symbol,
            GenericInstance { Definition.Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct } g
                => g.Definition,
            _ => throw NotSupported($"member access '.{expr.Member}' on " +
                                    $"'{TypeFacts.Display(target)}'", expr.Span),
        };

        var obj = LowerExpr(expr.Target);
        var type = target is GenericInstance instance
            ? _typeTable.Intern(instance.Definition, instance.Arguments)
            : _typeTable.Intern(declaring);

        // Index and field type both come from the layout of THIS instance. Going through the symbol
        // would not work: 'Box' alone has no layout, only 'Box<int>' has one.
        var layout = _typeTable.Defs[type.Value];
        var index = Array.IndexOf(layout.FieldNames, expr.Member);
        if (index < 0)
            throw NotSupported($"'{declaring.Name}' has no field '{expr.Member}'", expr.Span);

        return (obj, type, new FieldId(index), layout.FieldTypes[index]);
    }

    private TempId LowerCast(CastExpr expr)
    {
        // A non-numeric cast IS the conversion call the sema stored — same seam as the operators.
        // The operand is the call's receiver and lowers exactly once, in there.
        if (_types.OperatorCallOf(expr) is { } conversion)
            return LowerCall(conversion)
                   ?? throw Bug("conversion method returned no value");

        var from = TypeOfExpr(expr.Operand);
        var to = TypeOfExpr(expr);
        var operand = LowerExpr(expr.Operand);

        // 'x as int' for x: int is legal Lyric but yields no meaningful opcode. The lowering elides the
        // identity; the verifier rejects it.
        if (IrType.Equal(from, to)) return operand;

        var dest = _slots.NewTemp(to);
        _b.Emit(new Lyric.Ir.Convert(dest, from, to, operand, expr.Span));
        return dest;
    }

    /// <summary>
    /// A call through a FUNCTION VALUE: <c>f(1)</c>, where <c>f</c> is a closure.
    ///
    /// <para>No default argument and no <c>params</c>: both are call-site transformations needing the
    /// DECLARATION of the callee, and a function value has none. The type <c>fn(int) -&gt; int</c> says
    /// "one argument", and there is nothing more to know. C# draws the same line at delegates, for the
    /// same reason.</para>
    /// </summary>
    /// <summary>
    /// A generic method of an interface, monomorphized: <c>Iterator&lt;int&gt;.map&lt;string&gt;</c>.
    ///
    /// <para>Not a <c>callvirt</c>, because there is no slot to call: a slot holds one function
    /// and this is one per instantiation. The receiver is lifted all the same — the method's
    /// <c>this</c> is the interface type, exactly as for an ordinary default — and what follows is
    /// a direct call to the instance.</para>
    ///
    /// <para>The same trade Rust makes for a provided method with type parameters of its own: it
    /// exists where the concrete type is known and is unavailable through a dynamic value. What is
    /// bought with it is chaining — <c>xs.iter().map(f).take(3)</c> — and what is paid is that such
    /// a method can never be dispatched dynamically.</para>
    /// </summary>
    /// <summary>The base name of a generic function's instance where no receiver holds it: a
    /// block's member by the block's target, a static of a type's body by the type, a free
    /// function by itself. The name is the instance table's key
    /// (<see cref="InstanceTable.Request"/>), so it tells apart what the program tells apart.</summary>
    private string StaticBaseName(FunctionSymbol symbol, string calleeName) =>
        _typeTable.BlockOf(symbol) is { Target: { } blockTarget } ? $"<extend>.{blockTarget.Name}.{calleeName}"
        : TypeTable.TypeDeclaring(symbol) is { } declaring ? $"{declaring.Name}.{calleeName}"
        : calleeName;

    private TempId? LowerGenericInterfaceMethod(MemberExpr member, CallExpr expr, TypeSymbol iface,
        FunctionSymbol method, TypeId interfaceId, TempId receiver)
    {
        if (method.Declaration is not FunctionDecl declaration)
            throw NotSupported($"call to '{member.Member}' (no declaration)", expr.Span);

        // An abstract generic member has no body to instantiate for the interface's value. What
        // comes here is one its type leaves to a field (05 §5): the forwarder would be one
        // function per instantiation, and is not built. Asked here, an instance of nothing was
        // requested and the compiler failed on it ("function has no body").
        if (declaration.Body is null)
            throw NotSupported($"the generic member '{member.Member}' of '{iface.Name}' where its type forwards it "
                               + "to a field — write it on the type, calling the field's", expr.Span);

        var typeArguments = SubstitutedTypeArguments(expr);

        var owning = _typeTable.InstanceOf(interfaceId);
        var target = _instances.Request(method, declaration, $"{iface.Name}.{method.Name}",
            iface, typeArguments, _typeTable, expr.Span, owning);

        // The arguments are written in the member''s terms and need BOTH sides bound: the
        // interface''s T from the instance, the member''s own B from the call. Without them a
        // parameter such as ''right: Iterator<B>'' lowers to an unsubstituted type, and the value
        // arrives as the class reference it is rather than lifted into the interface — which the
        // verifier catches one step later, at the field the body stores it into.
        var mapping = new Dictionary<string, LyrType>(StringComparer.Ordinal);
        if (owning is { } instance)
            for (var i = 0; i < Math.Min(instance.Definition.Generics.Length,
                     instance.Arguments.Length); i++)
                mapping[instance.Definition.Generics[i].Name] = instance.Arguments[i];
        for (var i = 0; i < Math.Min(method.Generics.Length, typeArguments.Length); i++)
            mapping[method.Generics[i].Name] = typeArguments[i];

        var supplied = MaterializeArguments(declaration, ArgumentsOf(expr), member.Member, expr.Span,
            mapping);
        var args = new TempId[supplied.Length + 1];
        args[0] = receiver;
        Array.Copy(supplied, 0, args, 1, supplied.Length);

        var returns = TypeOfExpr(expr);
        if (IsVoid(returns))
        {
            _b.Emit(new Call(null, target, args, expr.Span));
            return null;
        }

        var result = _slots.NewTemp(returns);
        _b.Emit(new Call(result, target, args, expr.Span));
        _fresh.Add(result);
        return result;
    }

    /// <summary>
    /// A method call on an instance of a generic type.
    ///
    /// <para>The method is monomorphized PER TYPE INSTANCE: <c>Box&lt;int&gt;.get</c> and
    /// <c>Box&lt;string&gt;.get</c> are two functions. The substitution comes from the TYPE rather than
    /// from the call — <c>get()</c> has no type parameters of its own, its <c>T</c> is that of
    /// <c>Box</c>.</para>
    /// </summary>
    /// <summary>
    /// What the instance makes of its definition's type parameters, by name.
    ///
    /// <para>A parameter written <c>T</c> is a NAME until the instance says otherwise, and the
    /// declaration alone cannot see that. Without this, an argument whose parameter type only
    /// BECOMES optional through the substitution — <c>or(fallback: T)</c> on a
    /// <c>Holder&lt;?int&gt;</c> — is passed as the bare scalar its literal lowered to, and the
    /// malformed store into the optional slot surfaces one step later, in the caller, with no
    /// line to point at. Every path that calls a method OF an instance needs it.</para>
    /// </summary>
    private static Dictionary<string, LyrType> InstanceSubstitution(GenericInstance owner,
        FunctionSymbol? method = null, IReadOnlyList<LyrType>? methodArguments = null)
    {
        var mapping = new Dictionary<string, LyrType>(StringComparer.Ordinal);
        for (var i = 0; i < Math.Min(owner.Definition.Generics.Length, owner.Arguments.Length); i++)
            mapping[owner.Definition.Generics[i].Name] = owner.Arguments[i];

        // The METHOD's own parameters afterwards, so its 'U' wins a collision with the type's 'U'.
        // That is the scoping the source has, and the one InstanceTable.Request builds for the body
        // -- the two have to agree, or a parameter type would be materialized under one binding and
        // read under the other.
        if (method is not null && methodArguments is not null)
            for (var i = 0; i < Math.Min(method.Generics.Length, methodArguments.Count); i++)
                mapping[method.Generics[i].Name] = methodArguments[i];

        return mapping;
    }

    /// <summary>
    /// The type arguments the sema settled for this call, with any that are still a parameter OF
    /// THE CALLING INSTANCE resolved through its substitution.
    ///
    /// <para>In <c>fn wrap&lt;T&gt;(b: Box&lt;T&gt;)</c> the call <c>b.map&lt;T&gt;(x)</c> names the
    /// caller's <c>T</c>, and which type that is only the enclosing instantiation knows. Left
    /// unresolved it reaches <see cref="InstanceTable.Request"/> as a bare parameter, which refuses
    /// it -- correctly, since there is no instance to build for a name.</para>
    /// </summary>
    private LyrType[] SubstitutedTypeArguments(Node expr) =>
        _types.TypeArgumentsOf(expr).Select(SubstituteType).ToArray();

    /// <summary>
    /// <c>ident&lt;int&gt;</c> as a value (design/v5/spec/03 T17): the instance the sema settled,
    /// requested as a call would request it, then a closure without an environment around it —
    /// the same value a monomorphic function gives, and at the C level one more thunk.
    /// </summary>
    private TempId? LowerInstantiatedFunction(TypePathExpr expr)
    {
        var bound = _types.RefOf(expr);
        if (bound is ImportBindingSymbol binding) bound = binding.Target;
        if (bound is not FunctionSymbol { Declaration: FunctionDecl declaration } symbol
            || symbol.Generics.Length == 0)
            throw Bug($"a type path in value position reached lowering at {expr.Span}");

        // A generic native has a row per signature and no code of its own to point at (see
        // ImportTable.DeclareTemplate); a value needs the code.
        if (_imports.IsGenericNative(symbol) || declaration.Body is null)
            throw NotSupported($"the native generic '{symbol.Name}' as a value — wrap it in a lambda", expr.Span);

        // Without a receiver, as its call requests it: a free function has none, and the sema
        // lets only a static method through this route. Under the name its call gives it, so the
        // value and the call are one instance.
        var target = _instances.Request(symbol, declaration, StaticBaseName(symbol, symbol.Name), null,
            SubstitutedTypeArguments(expr), _typeTable, expr.Span);
        if (TypeOfExpr(expr) is not IrFunctionType signature)
            throw Bug($"an instantiated function without a function type at {expr.Span}");

        var closure = _slots.NewTemp(signature);
        _b.Emit(new MakeClosure(closure, target, null, signature, expr.Span));
        return closure;
    }

    private TempId? LowerGenericMethodCall(MemberExpr member, GenericInstance owner, CallExpr expr)
    {
        if (_types.RefOf(member) is not FunctionSymbol method)
            throw NotSupported($"call to '{member.Member}' on " +
                               $"'{TypeFacts.Display(owner)}'", expr.Span);

        if (method.Declaration is not FunctionDecl declaration)
            throw NotSupported($"call to '{member.Member}' (no declaration)", expr.Span);

        // A plain interface's member bound on the instance — written 'Walker.walk(c)' (05 §4), or
        // a default the type leaves to the interface: what the instance's type has for the
        // interface, found as a call through a constraint finds it at this instance — its own,
        // its block's, the default's instance. Asked for as a method of the instance, the
        // interface's member has no body, and its default ran past the member the type wrote.
        if (TypeTable.InterfaceOwning(method) is { Generics.Length: 0 })
            return LowerConstraintCall(member, owner, expr);

        // An interface's default on the instance: the default's instance for it (04 D9). As a
        // method of the type it was lowered with the interface's 'this' and failed the verifier.
        if (DirectDefault(member) is { } onInstance)
            return LowerDefaultCall(member, onInstance, owner, expr);

        // A GENERIC METHOD ON A GENERIC TYPE takes its T from the instance and its U from the
        // call, and needs BOTH bound. RequestMethod knows only the first, so 'Result<T,E>.map<U>'
        // ended at "a non-primitive field type" — a sentence about a field, reported at a method's
        // return type. The two-sided request has existed all along; this path never asked for it.
        var methodArguments = method.Generics.Length > 0
            ? SubstitutedTypeArguments(expr) : [];

        var target = method.Generics.Length > 0
            ? _instances.Request(method, declaration, $"{owner.Definition.Name}.{method.Name}",
                method.IsStatic ? null : owner.Definition, methodArguments, _typeTable,
                expr.Span, owner)
            : _instances.RequestMethod(method, declaration, owner, expr.Span);

        var receiver = LowerExpr(member.Target);

        // UNDER THE INSTANCE'S SUBSTITUTION. A parameter written 'T' is the instance's argument
        // here, and only the call site knows which: with 'T = ?int' an int LITERAL argument
        // lowered to a bare scalar, and the store into the optional slot was caught a step later
        // as "store of t3 (i64) into l9 (?i64)" — malformed IR with no line to point at. The
        // interface path below has carried this mapping all along; this one had not.
        var supplied = MaterializeArguments(declaration, ArgumentsOf(expr), member.Member, expr.Span,
            InstanceSubstitution(owner, method, methodArguments));

        // MaterializeArguments yields already lowered values including defaults and 'params'; the
        // receiver comes before them, as in every method call.
        var args = new TempId[supplied.Length + 1];
        args[0] = receiver;
        Array.Copy(supplied, 0, args, 1, supplied.Length);

        // The written return type is in the DECLARATION's terms and is lowered in the instance's;
        // for a generic method the sema's type of the call already carries both bindings, which is
        // the route the interface twin above takes for exactly this reason.
        var returns = method.Generics.Length > 0
            ? TypeOfExpr(expr)
            : ReturnTypeOfInstanceMethod(declaration, owner, expr.Span);
        if (IsVoid(returns))
        {
            _b.Emit(new Call(null, target, args, expr.Span));
            return null;
        }

        var dest = _slots.NewTemp(returns);
        _b.Emit(new Call(dest, target, args, expr.Span));
        _fresh.Add(dest);
        return dest;
    }

    /// <summary>
    /// <c>Pair&lt;int&gt;.of(3)</c> — a static method on a generic instance.
    ///
    /// <para>Apart from the missing receiver this is <see cref="LowerGenericMethodCall"/>: the same
    /// monomorphization request, the same substitution of the return type. It stands separately only
    /// because the receiver is parameter 0, and there is none here.</para>
    /// </summary>
    private TempId? LowerGenericStaticCall(MemberExpr member, GenericInstance owner, CallExpr expr)
    {
        if (_types.RefOf(member) is not FunctionSymbol method)
            throw NotSupported($"call to '{member.Member}' on " +
                               $"'{TypeFacts.Display(owner)}'", expr.Span);

        if (method.Declaration is not FunctionDecl declaration)
            throw NotSupported($"call to '{member.Member}' (no declaration)", expr.Span);

        // A static of a generic 'extend' block (03 T7 X1): 'Box<int>.default()' from
        // 'extend<T :: [Default]> Box<T>'. The block's parameters are bound by the named instance
        // and the method is an instance of its own, as the instance form is — the sema already
        // answered the call's type in the instance's terms.
        if (method.Generics.Length == 0 && _typeTable.BlockOf(method) is { Target: not null, Generics.Length: > 0 } block)
        {
            var map = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);
            if (block.TargetType is not { } pattern || !TypeFacts.Match(pattern, owner, map))
                throw NotSupported($"'{TypeFacts.Display(owner)}' does not match the block's target", expr.Span);
            var blockTarget = _instances.RequestExtension(method, declaration, block, map, owner, expr.Span);
            var byName = map.ToDictionary(kv => kv.Key.Name, kv => kv.Value, StringComparer.Ordinal);
            var blockArgs = MaterializeArguments(declaration, ArgumentsOf(expr), member.Member, expr.Span, byName);
            var blockReturns = TypeOfExpr(expr);
            if (IsVoid(blockReturns))
            {
                _b.Emit(new Call(null, blockTarget, blockArgs, expr.Span));
                return null;
            }
            var blockDest = _slots.NewTemp(blockReturns);
            _b.Emit(new Call(blockDest, blockTarget, blockArgs, expr.Span));
            _fresh.Add(blockDest);
            return blockDest;
        }

        // Generic on both sides here too: 'Box<int>.make<string>()'. A static method carries no
        // receiver, which is the only difference -- the request says so by passing none.
        var methodArguments = method.Generics.Length > 0
            ? SubstitutedTypeArguments(expr) : [];

        var target = method.Generics.Length > 0
            ? _instances.Request(method, declaration, $"{owner.Definition.Name}.{method.Name}",
                method.IsStatic ? null : owner.Definition, methodArguments, _typeTable,
                expr.Span, owner)
            : _instances.RequestMethod(method, declaration, owner, expr.Span);

        // Under the instance's substitution, for the same reason as the instance-method path:
        // 'Box<?int>.of(3)' writes its parameter 'T', which is a '?int' here, and the 3 has to
        // be wrapped. A STATIC call needs it just as much — the receiver is absent, the type
        // arguments are not.
        var args = MaterializeArguments(declaration, ArgumentsOf(expr), member.Member, expr.Span,
            InstanceSubstitution(owner, method, methodArguments));

        var returns = method.Generics.Length > 0
            ? TypeOfExpr(expr)
            : ReturnTypeOfInstanceMethod(declaration, owner, expr.Span);
        if (IsVoid(returns))
        {
            _b.Emit(new Call(null, target, args, expr.Span));
            return null;
        }

        var dest = _slots.NewTemp(returns);
        _b.Emit(new Call(dest, target, args, expr.Span));
        _fresh.Add(dest);
        return dest;
    }

    /// <summary>The return type of a method seen from the instance: the <c>T</c> in <c>fn get(): T</c> is
    /// the type argument of the receiver.</summary>
    private IrType ReturnTypeOfInstanceMethod(FunctionDecl declaration, GenericInstance owner,
        Core.Span span) =>
        declaration.ReturnType is null
            ? VoidType
            : LowerWithOwner(declaration.ReturnType, owner, span);

    /// <summary>
    /// Lowers a written type in the context of a type instance — the substitution goes to the TYPE TABLE
    /// rather than rebuilding a second resolution here.
    /// </summary>
    /// <remarks>
    /// <para>A partial copy here was three times too short: first only the bare case
    /// (<c>fn get(): T</c>), then <c>?T</c>, then <c>T[]</c> — and <c>Iterator&lt;T&gt;</c> was still
    /// missing. Every method returning a GENERIC type was therefore not lowerable:
    /// <c>fn iter(): Iterator&lt;T&gt;</c> is the signature <c>Set&lt;T&gt;</c> failed on.</para>
    /// <para>The same answer as in <see cref="LowerSubstituted"/>: the table can do it completely — it
    /// uses the same stack when lowering the members of a generic instance. It only had to learn that a
    /// substitution applies here.</para>
    /// </remarks>
    private IrType LowerWithOwner(TypeNode node, GenericInstance owner, Core.Span span)
    {
        var mapping = new Dictionary<string, LyrType>(StringComparer.Ordinal);
        var n = Math.Min(owner.Definition.Generics.Length, owner.Arguments.Length);

        for (var i = 0; i < n; i++)
            // Through the OWN substitution: an argument of the instance may itself be a type parameter of
            // the calling function ('Box<T>' in 'wrap<T>').
            mapping[owner.Definition.Generics[i].Name] = SubstituteType(owner.Arguments[i]);

        using var scope = _typeTable.PushSubstitution(mapping);
        return _typeTable.Lower(node);
    }

    /// <summary>
    /// A method call on a TYPE PARAMETER WITH A CONSTRAINT.
    ///
    /// <para>The sema binds <c>x.price()</c> to the interface declaration — that is all it knows in a
    /// generic function. In an INSTANCE the substituted type is settled and with it the method that
    /// really runs: the dynamic dispatch becomes a direct call.</para>
    ///
    /// <para>That is the gain of monomorphization, which Rust and C++ collect the same way, and the
    /// reason a constraint needs no vtable here. A value available through its interface
    /// (<c>let p: P = item;</c>) still goes through <c>callvirt</c>; those are two different questions
    /// and therefore two paths.</para>
    /// </summary>
    /// <summary>The interface entry an instance of a generic type is lifted into, as the type
    /// DECLARES it: for <c>ArrayIterator&lt;T&gt; :: [Iterator&lt;T&gt;]</c> and the instance
    /// <c>ArrayIterator&lt;string&gt;</c>, the entry of <c>Iterator&lt;string&gt;</c>. The written
    /// node stands in the definition's terms, so the instance's arguments have to be in scope
    /// while it is resolved.</summary>
    private IrInterfaceType InterfaceAsDeclaredBy(GenericInstance owner, TypeSymbol iface,
        Core.Span span)
    {
        var mapping = new Dictionary<string, LyrType>(StringComparer.Ordinal);
        var n = Math.Min(owner.Definition.Generics.Length, owner.Arguments.Length);
        for (var i = 0; i < n; i++)
            mapping[owner.Definition.Generics[i].Name] = SubstituteType(owner.Arguments[i]);

        using var scope = _typeTable.PushSubstitution(mapping);
        return _typeTable.InterfaceAsDeclared(owner.Definition, iface, span);
    }

    /// <summary>The interface entry a constrained receiver is lifted into.
    ///
    /// <para>The constraint stands in the GENERIC function's terms and may name its own type
    /// parameter: <c>T :: [Mul&lt;int, T&gt;]</c>. In an instance that <c>T</c> is settled, and
    /// the entry is <c>Mul&lt;int, Vec2&gt;</c> — resolving the node as written would look for a
    /// type called <c>T</c> and report "this type argument is not supported".</para></summary>
    private IrInterfaceType LiftTargetOf(TypeNode constraint, Core.Span span)
    {
        if (_substitution.Count == 0) return _typeTable.InterfaceOf(constraint, span);
        using var scope = _typeTable.PushSubstitution(NamedSubstitution());
        return _typeTable.InterfaceOf(constraint, span);
    }

    /// <summary>The concrete type's member a call names: its own, or its blocks' — the block of
    /// the interface the call's member belongs to (<see cref="BlockMemberFor"/>).</summary>
    private FunctionSymbol? GenericImplementationOf(LyrType concrete, MemberExpr member)
    {
        if (TypeFacts.SymbolOf(concrete) is { } owner)
            return owner.Members.LookupLocal(member.Member) as FunctionSymbol ?? BlockMemberFor(owner, member);
        return _typeTable.BuiltinSymbolOf(concrete) is { } builtin ? BlockMemberFor(builtin, member) : null;
    }

    /// <summary>
    /// The member a type's blocks give FOR the interface a call's member belongs to (04 D3): the
    /// one the conformance check settled, where that stands in a block. A name does not say it —
    /// two blocks of one type may each give a <c>greet</c>, one for <c>Greeter</c> and one for
    /// <c>Waver</c>, and the first block that has the name answered every call through a
    /// constraint, <c>T :: [Waver]</c> included. Where nothing single was settled — several
    /// conformances to one interface, a member of no interface — the name decides, as before.
    /// </summary>
    private FunctionSymbol? BlockMemberFor(TypeSymbol owner, MemberExpr member) =>
        _types.RefOf(member) is FunctionSymbol promised
        && TypeTable.InterfaceOwning(promised) is { } iface
        && _types.ConformanceImpl(owner, iface, member.Member, null) is { } settled
        && _typeTable.BlockOf(settled) is not null
            ? settled
            : _typeTable.ExtensionMethod(owner, member.Member);

    /// <summary>A generic member's implementation for the concrete receiver of a constraint call:
    /// a generic type's block member by the block's route, else the instance a direct call
    /// requests — the type's name before the member's, a builtin's block target where no symbol
    /// holds it.</summary>
    private TempId? LowerGenericImplementationCall(MemberExpr member, FunctionSymbol implementation,
        FunctionDecl decl, LyrType concrete, CallExpr expr)
    {
        if (concrete is GenericInstance && _typeTable.BlockOf(implementation) is { } block)
            return LowerBlockMethodCall(member, implementation, block, concrete, expr);
        // In the body of a generic type: the instance's member at the call's own arguments, as a
        // call on the instance asks for it (LowerGenericMethodCall) — one instance for both.
        if (concrete is GenericInstance ofInstance)
        {
            var own = SubstitutedTypeArguments(expr);
            var member0 = _instances.Request(implementation, decl, $"{ofInstance.Definition.Name}.{implementation.Name}",
                ofInstance.Definition, own, _typeTable, expr.Span, ofInstance);
            var given = MaterializeArguments(decl, ArgumentsOf(expr), member.Member, expr.Span,
                InstanceSubstitution(ofInstance, implementation, own));
            var withReceiver = new TempId[given.Length + 1];
            withReceiver[0] = LowerExpr(member.Target);
            given.CopyTo(withReceiver, 1);
            var returns = TypeOfExpr(expr);
            if (IsVoid(returns))
            {
                _b.Emit(new Call(null, member0, withReceiver, expr.Span));
                return null;
            }
            var value = _slots.NewTemp(returns);
            _b.Emit(new Call(value, member0, withReceiver, expr.Span));
            _fresh.Add(value);
            return value;
        }
        var owner = TypeFacts.SymbolOf(concrete);
        var baseName = owner is not null ? $"{owner.Name}.{implementation.Name}"
            : _typeTable.BlockOf(implementation) is { Target: { } blockTarget } ? $"<extend>.{blockTarget.Name}.{implementation.Name}"
            : implementation.Name;
        var target = _instances.Request(implementation, decl, baseName, owner, SubstitutedTypeArguments(expr), _typeTable,
            expr.Span, receiverType: owner is null ? concrete : null);
        var passed = MaterializeArguments(decl, ArgumentsOf(expr), member.Member, expr.Span,
            NamedSubstitutionFor(implementation, _types.TypeArgumentsOf(expr)));
        var all = new TempId[passed.Length + 1];
        all[0] = LowerExpr(member.Target);
        passed.CopyTo(all, 1);
        var resultType = TypeOfExpr(expr);
        if (IsVoid(resultType))
        {
            _b.Emit(new Call(null, target, all, expr.Span));
            return null;
        }
        var result = _slots.NewTemp(resultType);
        _b.Emit(new Call(result, target, all, expr.Span));
        _fresh.Add(result);
        return result;
    }

    /// <summary>Does the receiver's constraint name an interface the concrete type conforms to
    /// more than once? Then the member name does not identify the implementation and the call has
    /// to go through the instance-keyed dispatch table.</summary>
    private bool ConstraintNeedsTheInstance(MemberExpr member, TypeSymbol owner)
    {
        if (ReceiverType(member.Target) is not TypeParamType parameter) return false;
        foreach (var constraint in parameter.Param.Constraints)
            if (_typeTable.ConstraintInterface(constraint) is { } constrained
                && _typeTable.InterfaceInChainProviding(constrained, member.Member) is not null
                && _typeTable.ConformsSeveralTimes(owner, constrained))
                return true;
        return false;
    }

    /// <summary>A block member on an instance receiver (03 T7): a generic block's method is
    /// requested for the receiver, a plain block's lowered as every extension is; the call is
    /// direct either way.</summary>
    private TempId? LowerBlockMethodCall(MemberExpr member, FunctionSymbol symbol, ExtensionBlock block,
        LyrType receiver, CallExpr expr, bool viewed = false)
    {
        if (symbol.Declaration is not FunctionDecl decl || decl.Body is null)
            throw NotSupported($"'{member.Member}' of the block on '{TypeFacts.Display(receiver)}' has no body", expr.Span);
        FunctionId target;
        Dictionary<string, LyrType>? byName = null;
        if (block.Generics.Length == 0 && !block.IsConstructorTarget && receiver is GenericInstance)
        {
            if (!TryResolveFunction(symbol, out target))
                throw NotSupported($"'{member.Member}' of the block on '{TypeFacts.Display(receiver)}' was not lowered", expr.Span);
        }
        else
        {
            var map = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);
            if (block.TargetType is not { } pattern || !TypeFacts.Match(pattern, receiver, map))
                throw NotSupported($"'{TypeFacts.Display(receiver)}' does not match the block's target", expr.Span);
            // A parameter only a fixation names is the receiver's answer (05 §13 rule 2).
            foreach (var (bound, from, assocMember) in block.FixationBindings ?? [])
                if (!map.ContainsKey(bound) && map.TryGetValue(from, out var source))
                    map[bound] = TypeChecker.ResolveAssociated(source, assocMember);
            // A generic method's own parameters (03 T7 X1), bound by the call beside the block's
            // bound by the receiver: 'mapped<U>' in 'extend<I :: [Iterator]> I'.
            var own = symbol.Generics.Length > 0 ? SubstitutedTypeArguments(expr) : [];
            for (var i = 0; i < symbol.Generics.Length && i < own.Length; i++) map[symbol.Generics[i]] = own[i];
            target = _instances.RequestExtension(symbol, decl, block, map, receiver, expr.Span, own);
            byName = map.ToDictionary(kv => kv.Key.Name, kv => kv.Value, StringComparer.Ordinal);
        }
        var passed = MaterializeArguments(decl, ArgumentsOf(expr), member.Member, expr.Span, byName);
        var all = new TempId[passed.Length + 1];
        all[0] = viewed
            ? ViewOf(LowerExpr(member.Target), ((IrArrayType)TypeOfExpr(member.Target)).Element, member.Span)
            : LowerExpr(member.Target);
        passed.CopyTo(all, 1);
        var resultType = TypeOfExpr(expr);
        if (IsVoid(resultType))
        {
            _b.Emit(new Call(null, target, all, expr.Span));
            return null;
        }
        var result = _slots.NewTemp(resultType);
        _b.Emit(new Call(result, target, all, expr.Span));
        _fresh.Add(result);
        return result;
    }

    /// <summary>The static member the constraint promised, on the type the parameter stands for:
    /// the type's own, or a static of a visible extend block — a builtin's through its symbol. Of
    /// several of the name (08 §1.2: `parse(s)` beside `parse(s, radix)`) the one the conformance
    /// chose, which the checker recorded.</summary>
    private TempId? LowerStaticConstraintCall(MemberExpr member, LyrType concrete, CallExpr expr)
    {
        var owner = TypeFacts.SymbolOf(concrete) ?? _typeTable.BuiltinSymbolOf(concrete);
        var chosen = owner is not null && _types.RefOf(member) is FunctionSymbol promised
                     && TypeTable.InterfaceOwning(promised) is { } iface
            ? _types.ConformanceImpl(owner, iface, member.Member, null)
            : null;
        var function = chosen
                       ?? owner?.Members.LookupLocal(member.Member) as FunctionSymbol
                       ?? (owner is null ? null : _typeTable.ExtensionMethod(owner, member.Member));
        if (function is not { IsStatic: true, Declaration: FunctionDecl declaration })
            throw NotSupported($"the static '{member.Member}' of '{TypeFacts.Display(concrete)}' through a constraint", expr.Span);

        FunctionId target;
        IReadOnlyDictionary<string, LyrType>? under = null;
        if (concrete is GenericInstance instance && _typeTable.BlockOf(function) is null && declaration.Body is not null)
        {
            // A static of a generic type's body: the instance's, as 'Crate<string>.make()' asks
            // for it (LowerGenericStaticCall) — one instance for both.
            var own = function.Generics.Length > 0 ? SubstitutedTypeArguments(expr) : [];
            target = function.Generics.Length > 0
                ? _instances.Request(function, declaration, $"{instance.Definition.Name}.{function.Name}", null, own,
                    _typeTable, expr.Span, instance)
                : _instances.RequestMethod(function, declaration, instance, expr.Span);
            under = InstanceSubstitution(instance, function, own);
        }
        else if (function.Generics.Length > 0 && declaration.Body is not null && concrete is not GenericInstance)
        {
            // A generic static (04 D9 whole; 'U.exact(v)', the review's M8a-7): its instance at the
            // call's type arguments, under the name a call through the type gives it — with
            // 'U = int8' it is the function 'int8.exact(v)' calls.
            target = _instances.Request(function, declaration, StaticBaseName(function, function.Name), null,
                SubstitutedTypeArguments(expr), _typeTable, expr.Span);
            under = NamedSubstitutionFor(function, _types.TypeArgumentsOf(expr));
        }
        else if (!TryResolveFunction(function, out target))
            throw NotSupported($"the static '{member.Member}' of '{TypeFacts.Display(concrete)}' through a constraint", expr.Span);

        var passed = MaterializeArguments(declaration, ArgumentsOf(expr), member.Member, expr.Span, under);
        var resultType = TypeOfExpr(expr);
        if (IsVoid(resultType))
        {
            _b.Emit(new Call(null, target, passed, expr.Span));
            return null;
        }
        var result = _slots.NewTemp(resultType);
        _b.Emit(new Call(result, target, passed, expr.Span));
        _fresh.Add(result);
        return result;
    }

    /// <summary>The member a call names, where it is the default of a non-generic interface —
    /// what <see cref="LowerDefaultCall"/> instantiates per conformer. <c>null</c> for anything
    /// else: an own member, an abstract one, a default of a generic interface — and, in the 4.x
    /// path, a generic default, which is lowered for the interface's value there
    /// (<see cref="LowerGenericInterfaceMethod"/>). In Lyric 5 a generic default is one instance
    /// per conformer and argument list (04 D9 whole): its interface has no value.</summary>
    private (FunctionSymbol Default, FunctionDecl Decl, TypeSymbol Iface)? DirectDefault(MemberExpr member) =>
        _types.RefOf(member) is FunctionSymbol { IsStatic: false } promised
        && (promised.Generics.Length == 0 || _types.Lyric5Modules)
        && promised.Declaration is FunctionDecl { Body: not null } decl
        && TypeTable.InterfaceOwning(promised) is { Generics.Length: 0 } iface
            ? (promised, decl, iface)
            : null;

    /// <summary>
    /// A call of a member its type leaves to a field (04 D1), where the type is known: the
    /// forwarder of the slot, called directly. <c>false</c> where the member is not delegated —
    /// or is one this does not build a direct call for: a generic interface's, whose rows are
    /// per instance.
    /// </summary>
    private bool TryLowerForwardedCall(MemberExpr member, TypeSymbol owner, LyrType receiver, TypeSymbol iface,
        CallExpr expr, out TempId? result)
    {
        result = null;
        if (iface.Generics.Length > 0 || _types.DelegationOf(owner, iface) is not { } field) return false;
        if (iface.Members.LookupLocal(member.Member) is not FunctionSymbol
            { Generics.Length: 0, IsStatic: false, Declaration: FunctionDecl { Body: null } promised })
            return false;

        var typeId = SubstituteType(receiver) is GenericInstance instance
            ? _typeTable.Intern(instance.Definition, instance.Arguments)
            : _typeTable.Intern(owner);
        var ifaceId = _typeTable.InterfaceOf(iface).Type;
        var slot = Array.IndexOf(_typeTable.MethodSlotsOf(ifaceId), member.Member);
        if (slot < 0) return false;

        var target = _lambdas.RequestForwarder(owner, typeId, iface, ifaceId, slot, field);
        var passed = MaterializeArguments(promised, ArgumentsOf(expr), member.Member, expr.Span);
        var all = new TempId[passed.Length + 1];
        all[0] = LowerExpr(member.Target);
        passed.CopyTo(all, 1);
        var resultType = TypeOfExpr(expr);
        if (IsVoid(resultType))
        {
            _b.Emit(new Call(null, target, all, expr.Span));
            return true;
        }
        var value = _slots.NewTemp(resultType);
        _b.Emit(new Call(value, target, all, expr.Span));
        _fresh.Add(value);
        result = value;
        return true;
    }

    /// <summary>Whether a conformer's own member stands in place of this generic default (04 D9
    /// whole): in Lyric 5, a member of the contract of a non-generic interface. The checker lets
    /// one be written exactly there (<c>LYR-SEM0082</c> elsewhere).</summary>
    private bool Replaceable(FunctionSymbol promised) =>
        _types.Lyric5Modules
        && promised.Declaration is FunctionDecl { Body: not null, IsPrivateHelper: false }
        && TypeTable.InterfaceOwning(promised) is { Generics.Length: 0 };

    /// <summary>
    /// An interface's default called on a conformer (04 D9): the default's instance for the
    /// conformer's type, 'Self' that type, the receiver passed as it is — a struct, an instance, a
    /// builtin, a shape alike, no value of the interface made, which a scalar or a constraint-only
    /// interface has none of.
    /// </summary>
    private TempId? LowerDefaultCall(MemberExpr member, (FunctionSymbol Default, FunctionDecl Decl, TypeSymbol Iface) found,
        LyrType conformer, CallExpr expr)
    {
        // A generic default's own arguments beside 'Self' (D9 whole): the call's, through this
        // instance's substitution.
        var own = found.Default.Generics.Length > 0 ? SubstitutedTypeArguments(expr) : [];
        var target = _instances.RequestDefault(found.Default, found.Decl, found.Iface, conformer, expr.Span, own);
        var under = new Dictionary<string, LyrType>(StringComparer.Ordinal) { ["Self"] = conformer };
        for (var i = 0; i < Math.Min(found.Default.Generics.Length, own.Length); i++)
            under[found.Default.Generics[i].Name] = own[i];
        var passed = MaterializeArguments(found.Decl, ArgumentsOf(expr), member.Member, expr.Span, under);
        var all = new TempId[passed.Length + 1];
        all[0] = LowerExpr(member.Target);
        passed.CopyTo(all, 1);
        var resultType = TypeOfExpr(expr);
        if (IsVoid(resultType))
        {
            _b.Emit(new Call(null, target, all, expr.Span));
            return null;
        }
        var result = _slots.NewTemp(resultType);
        _b.Emit(new Call(result, target, all, expr.Span));
        _fresh.Add(result);
        return result;
    }

    private TempId? LowerConstraintCall(MemberExpr member, LyrType concrete, CallExpr expr)
    {
        // An interface's private helper (07 V2 S4) is never overridden: a conformer's method of its
        // name is the conformer's own, which the defaults do not call — the helper's instance for
        // the type, as a default's is.
        if (DirectDefault(member) is { } helper && helper.Decl.IsPrivateHelper)
            return LowerDefaultCall(member, helper, concrete, expr);

        // The implementation stands in a shape's block or a blanket block (05 §13 rules 6, 8),
        // where no symbol of the type holds the conformance: the sema says which block gives it at
        // this instance, and the call is the block method's instance for the type.
        if (_types.RefOf(member) is FunctionSymbol promised
            && _types.ConformanceBlock?.Invoke(concrete, promised) is { } through)
        {
            if (through.Method is { } implementation)
                return LowerBlockMethodCall(member, implementation, through.Block, concrete, expr);
            // The block leaves the member to the interface's default: its instance for the type.
            return DirectDefault(member) is { } viaBlock
                ? LowerDefaultCall(member, viaBlock, concrete, expr)
                : throw NotSupported($"'{member.Member}' on '{TypeFacts.Display(concrete)}' as the default of a generic interface, through the block that gives the conformance", expr.Span);
        }

        // A generic member (04 D9): no slot — the concrete type's own, its instance at the call's
        // type arguments, named as a direct call on the type names it, so both reach one instance.
        // An abstract one has nothing else. A default's place is the conformer's own where it
        // wrote one (D9 whole); where it did not, the default's instance for the type follows
        // below.
        if (_types.RefOf(member) is FunctionSymbol { Generics.Length: > 0, Declaration: FunctionDecl promisedDecl } promisedGeneric
            && (promisedDecl.Body is null || Replaceable(promisedGeneric))
            && GenericImplementationOf(concrete, member) is { Declaration: FunctionDecl genericDecl } genericImplementation)
            return LowerGenericImplementationCall(member, genericImplementation, genericDecl, concrete, expr);

        // A BUILTIN as the substituted type: 'render(42)' with 'extend int :: [Display]'. Primitives have
        // no symbol in SymbolOf, and that stays so, because on it hangs the boundary that a scalar does
        // not fit into an interface slot, which would need boxing. It does not get in the way here: the
        // monomorphization substituted the type, the method is settled, and the call is direct. No fat
        // pointer ever arises, so no boxing is ever needed.
        if (TypeFacts.SymbolOf(concrete) is not { } owner)
        {
            if (_typeTable.BuiltinSymbolOf(concrete) is { } builtin
                && BlockMemberFor(builtin, member) is { } extension
                && extension.Declaration is FunctionDecl extensionDecl
                && TryResolveFunction(extension, out var extensionTarget))
            {
                var self = LowerExpr(member.Target);
                var passed = MaterializeArguments(extensionDecl, ArgumentsOf(expr), member.Member,
                    expr.Span);
                var all = new TempId[passed.Length + 1];
                all[0] = self;
                passed.CopyTo(all, 1);

                var resultType = TypeOfExpr(expr);
                if (IsVoid(resultType))
                {
                    _b.Emit(new Call(null, extensionTarget, all, expr.Span));
                    return null;
                }

                var result = _slots.NewTemp(resultType);
                _b.Emit(new Call(result, extensionTarget, all, expr.Span));
                _fresh.Add(result);
                return result;
            }

            // The interface's default, 'render(42)' reaching a member 'extend int :: [Display]'
            // leaves to it: the default's instance for the builtin (04 D9) — no boxing either.
            if (DirectDefault(member) is { } onBuiltin)
                return LowerDefaultCall(member, onBuiltin, concrete, expr);

            throw NotSupported($"call to '{member.Member}' on '{TypeFacts.Display(concrete)}'",
                expr.Span);
        }

        // An own member beats a default. When the concrete type does NOT have the method, it comes as a
        // default from the interface, and its 'this' is the interface type. No direct call leads there:
        // the receiver has to be lifted first, which is exactly what 'callvirt' does. The constraint
        // names the interface, so it is known.
        //
        // SEVERAL conformances to one interface take the same route, for a related reason: the
        // type then has two methods of one name, and an own member no longer identifies which
        // conformance it serves. The dispatch table is keyed by the interface INSTANCE and does
        // know, so the receiver is lifted and the row answers — after which the devirtualizer
        // turns it back into a direct call, to the target the constraint actually named.
        if (owner.Members.LookupLocal(member.Member) is not FunctionSymbol method
            || method.Declaration is not FunctionDecl declaration
            || ConstraintNeedsTheInstance(member, owner))
        {
            // A generic owner's block, 'extend<T :: [Integer]> Range<T> :: [Iterator]' (03 T7
            // X1): the block's method for the receiver's instance, a direct call.
            if (concrete is GenericInstance && !ConstraintNeedsTheInstance(member, owner)
                && BlockMemberFor(owner, member) is { } genericBlockMethod
                && _typeTable.BlockOf(genericBlockMethod) is { } genericBlock)
                return LowerBlockMethodCall(member, genericBlockMethod, genericBlock, concrete, expr);

            // The implementation stands in a conformance block, 'extend StrBox :: [Container]'
            // (05 §6): the same direct call as on a builtin above. A member typed by an
            // associated type (03 T6) has no slot to be lifted into, so the direct call is the
            // only route.
            if (concrete is NamedRef && !ConstraintNeedsTheInstance(member, owner)
                && BlockMemberFor(owner, member) is { } blockMethod
                && blockMethod.Declaration is FunctionDecl blockDecl
                && TryResolveFunction(blockMethod, out var blockTarget))
            {
                var passed = MaterializeArguments(blockDecl, ArgumentsOf(expr), member.Member, expr.Span);
                var all = new TempId[passed.Length + 1];
                all[0] = LowerExpr(member.Target);
                passed.CopyTo(all, 1);
                var resultType = TypeOfExpr(expr);
                if (IsVoid(resultType))
                {
                    _b.Emit(new Call(null, blockTarget, all, expr.Span));
                    return null;
                }
                var blockResult = _slots.NewTemp(resultType);
                _b.Emit(new Call(blockResult, blockTarget, all, expr.Span));
                _fresh.Add(blockResult);
                return blockResult;
            }

            // The interface's default, for the type (04 D9): its instance, direct. Several
            // conformances to one interface go by the instance, through the table below.
            if (!ConstraintNeedsTheInstance(member, owner) && DirectDefault(member) is { } ofType)
                return LowerDefaultCall(member, ofType, concrete, expr);

            // Left to a field (04 D1): the forwarder, direct, the receiver as it stands.
            if (!ConstraintNeedsTheInstance(member, owner)
                && _types.RefOf(member) is FunctionSymbol leftToAField
                && TypeTable.InterfaceOwning(leftToAField) is { } forwarding
                && TryLowerForwardedCall(member, owner, concrete, forwarding, expr, out var viaField))
                return viaField;

            if (ReceiverType(member.Target) is TypeParamType parameter)
                foreach (var constraint in parameter.Param.Constraints)
                    if (_typeTable.ConstraintInterface(constraint) is { } constrained
                        && _typeTable.InterfaceInChainProviding(constrained, member.Member)
                            is { } iface)
                    {
                        // The receiver is available as a class reference and 'callvirt' needs an interface
                        // value: lift first (mkiface), then call. The same as at every other place where
                        // a class moves into an interface slot.
                        //
                        // From the WRITTEN constraint rather than from the symbol when the member
                        // comes from the constrained interface itself: a generic interface has no
                        // entry of its own, only 'Source<int>' does, and lifting into the
                        // definition is what a default method of a generic interface used to die
                        // on. The same id then carries the callvirt, whose slot table also hangs
                        // on the instance.
                        var lift = ReferenceEquals(iface, constrained)
                            ? LiftTargetOf(constraint, expr.Span)
                            : _typeTable.InterfaceOf(iface);

                        var lifted = LowerExprAs(member.Target, lift);
                        return LowerVirtualCall(member, iface, expr, lift.Type, lifted);
                    }

            throw NotSupported(
                $"'{owner.Name}' has no '{member.Member}' — the constraint promises it, so this "
                + "is a lowering gap and not a program error", expr.Span);
        }

        // For a generic receiver the method belongs to the instance; otherwise it was lowered in pass 1
        // with all the others.
        var target = concrete is GenericInstance instance
            ? _instances.RequestMethod(method, declaration, instance, expr.Span)
            : TryResolveFunction(method, out var direct)
                ? direct
                : throw NotSupported($"'{owner.Name}.{member.Member}' was not lowered", expr.Span);

        var supplied = MaterializeArguments(declaration, ArgumentsOf(expr), member.Member, expr.Span,
            concrete is GenericInstance forArguments ? InstanceSubstitution(forArguments) : null);

        var args = new TempId[supplied.Length + 1];
        args[0] = LowerExpr(member.Target);
        Array.Copy(supplied, 0, args, 1, supplied.Length);

        var returns = concrete is GenericInstance owning
            ? ReturnTypeOfInstanceMethod(declaration, owning, expr.Span)
            : declaration.ReturnType is null ? VoidType : _typeTable.Lower(declaration.ReturnType);

        if (IsVoid(returns))
        {
            _b.Emit(new Call(null, target, args, expr.Span));
            return null;
        }

        var dest = _slots.NewTemp(returns);
        _b.Emit(new Call(dest, target, args, expr.Span));
        _fresh.Add(dest);
        return dest;
    }

    private TempId? LowerIndirectCall(CallExpr expr)
    {
        if (LowerType(_types.TypeOf(expr.Callee), expr.Callee.Span) is not IrFunctionType signature)
            throw Bug("indirect call on a non-function value");

        var callee = LowerExpr(expr.Callee);

        var args = new TempId[expr.Arguments.Length];
        for (var i = 0; i < args.Length; i++)
            args[i] = i < signature.Parameters.Length
                ? LowerExprAs(expr.Arguments[i], signature.Parameters[i])
                : LowerExpr(expr.Arguments[i]); // an arity error was already reported by the sema

        // A value whose type throws hands the call the caller's slot (03 T17); the edge follows in
        // LowerCall, from the same recorded set.
        var throws = ThrownHere(_types.CallThrows(expr)).Length > 0;
        if (IsVoid(signature.Return))
        {
            _b.Emit(new CallIndirect(null, callee, args, signature.Return, expr.Span) { Throws = throws });
            return null;
        }

        var dest = _slots.NewTemp(signature.Return);
        _b.Emit(new CallIndirect(dest, callee, args, signature.Return, expr.Span) { Throws = throws });

        // The result belongs to nobody yet; for a struct that saves the structcopy when binding, exactly
        // as for an ordinary call.
        _fresh.Add(dest);
        return dest;
    }

    /// <summary>A call, and — when its function throws (05 E1, 01 L5 E3) — the error edge after
    /// it. Once per call node.</summary>
    private TempId? LowerCall(CallExpr expr)
    {
        var value = LowerCallSite(expr);
        if (ThrownHere(_types.CallThrows(expr)).Length > 0 && !_b.IsSealed && _errorEdged.Add(expr)) ErrorEdge(expr.Span);
        return value;
    }

    private TempId? LowerCallSite(CallExpr expr)
    {
        // 'Point(1, 2)' IS the factory call the sema stored for it (08 Y9) — the seam of the
        // operators, for a callee that names a type.
        if (_types.OperatorCallOf(expr) is { } factory)
            return LowerCall(factory);

        // 'same(a, b)': identity is the comparison of two references (02 M10). The symbol is
        // the builtin exactly when no module declares it; a user function of the name shadows.
        if (expr.Callee is IdentifierExpr { Name: "same" } identity
            && _types.RefOf(identity) is FunctionSymbol { Declaration: FunctionDecl { Body: null, Span: var where } } builtin
            && where == default && !_functions.ContainsKey(builtin) && expr.Arguments.Length == 2)
        {
            var left = LowerExpr(expr.Arguments[0]);
            var right = LowerExpr(expr.Arguments[1]);
            var one = _slots.NewTemp(BoolType);
            _b.Emit(new BinOp(one, IrBinKind.Eq, BoolType, left, right, expr.Span));
            return one;
        }

        // 'b?.get()' — optional chaining with a call. The same branch as for the field access, except
        // that the 'some' branch holds a call rather than an 'ldfld'. The call itself lands back here
        // afterwards, with an unwrapped receiver.
        if (expr.Callee is MemberExpr { IsOptional: true } chained
            && !_chainReceivers.ContainsKey(chained.Target))
            return LowerOptionalCall(expr, chained);

        // 'co.next()' — the safe pull on a coroutine, built in like '.length' on an array. Before
        // the indirect-call check: the sema types the member as a function type with no symbol
        // behind it, which is exactly what the value-call test matches.
        // 'xs.length()' on an array or a view is built in: neither a field nor a method (03 T13 A1).
        if (expr.Callee is MemberExpr { Member: "length", IsOptional: false } length
            && TypeOfExpr(length.Target) is IrArrayType or IrSliceType or IrInlineArrayType)
            return LowerArrayLength(length.Target, expr.Span);

        if (expr.Callee is MemberExpr { Member: "next" } pull
            && SubstituteType(_types.TypeOf(pull.Target)) is CoroutineOf pulled)
            return LowerCoroutineNext(pull, pulled, expr.Span);
        if (expr.Callee is MemberExpr { Member: "result" or "isDone" } asked
            && SubstituteType(_types.TypeOf(asked.Target)) is CoroutineOf answering)
            return LowerCoroutineQuery(asked, answering, expr.Span);
        if (expr.Callee is MemberExpr { Member: "close" } closed
            && SubstituteType(_types.TypeOf(closed.Target)) is CoroutineOf)
        {
            LowerCoroutineClose(closed, expr.Span);
            return null;
        }

        // The receiver is parameter 0. For `p.get()` the 'p' therefore becomes the first argument; for
        // `P.new(…)` there is none and the call is an ordinary one. Both forms then run through the same
        // path — the difference lies solely in the argument list.
        TempId? receiver = null;

        // The receiver's TYPE, for a generic method: the instance needs it to give the body a
        // 'this' slot. Without it the call passed a receiver the callee had not declared, and the
        // module went out one argument heavier than its own signature -- see the request below.
        TypeSymbol? receiverOwner = null;
        string calleeName;
        Symbol? bound;

        // The callee is a VALUE rather than a declaration: a closure, a parameter of type
        // 'fn(…) -> …', a field, the result of another call.
        //
        // The type alone does NOT decide that: a declared function also has a function type, and so does
        // an enum variant with a payload ('Shape.Line(1.0)') — which is a constructor, not a value. What
        // makes the call indirect is the BINDING: it points at something HOLDING a function value, or at
        // nothing at all, as in 'mk()()'.
        //
        // Enumerated positively rather than negatively: a list of prohibitions would silently give the
        // wrong answer for every new kind of symbol, and in the dangerous direction.
        //
        // Through an import's shell, as everywhere: 'import app.util { handler }' names the
        // module's binding, and 'handler(4)' calls the function it holds.
        var called = _types.RefOf(expr.Callee);
        while (called is ImportBindingSymbol calledThrough) called = calledThrough.Target;
        if (_types.TypeOf(expr.Callee) is FnType
            && called is null or LocalSymbol or ParameterSymbol or FieldSymbol or GlobalSymbol)
            return LowerIndirectCall(expr);

        switch (expr.Callee)
        {
            // A member of a blanket block (04 D15): 'extend<T :: [I]> T' — the receiver binds the
            // block's parameter, and the method is an instance of its own, as on any generic block.
            case MemberExpr member
                when _types.RefOf(member) is FunctionSymbol blanketMember
                     && _typeTable.BlockOf(blanketMember) is { IsBlanketTarget: true } blanket:
                return LowerBlockMethodCall(member, blanketMember, blanket, SubstituteType(ReceiverType(member.Target)), expr);

            // Shape.Circle(2.0) and Opt<int>.Some(5) are tuple variants. Not a call but a construction,
            // and that holds regardless of how the target is written. The case therefore stands BEFORE
            // the static call: 'Opt<int>.Some' looks like a static method on an instance and is not.
            case MemberExpr member when _types.RefOf(member) is EnumVariantSymbol:
                return LowerVariantCall(member.Member, expr.Arguments, expr, expr.Span);

            // '.Num(3)': the variant of the enum the position expected (08 Y9).
            case ImplicitMemberExpr implied when _types.RefOf(implied) is EnumVariantSymbol:
                return LowerVariantCall(implied.Name, expr.Arguments, expr, expr.Span);

            // '.origin()': a static member reached the same way.
            case ImplicitMemberExpr implied:
                calleeName = implied.Name;
                bound = _types.RefOf(implied);
                break;

            // 'Pair<int>.of(3)' — a static method on a generic INSTANCE. The target here is not a value
            // but a type path; there is no receiver, but there is an instantiation. The case stands
            // first, because every case below asks the type of the target EXPRESSION, and a type path
            // has none.
            //
            // The instance is the one the receiver names — written, 'Pair<int>', or settled by the
            // call for a bare 'Pair' (03 T8): the sema wrote it onto the receiver either way.
            case MemberExpr member
                when _types.TypeOf(member.Target) is Sema.NonValueType { Instance: { } owner }:
                // Through the OWN substitution, as the instance-method dispatch below does: in
                // 'fn make<T>()' the call 'List<T>.empty()' names the CALLER's T, and which type
                // that is only the enclosing instantiation knows. Unsubstituted it reached the
                // type table as a bare parameter and threw.
                return LowerGenericStaticCall(member,
                    SubstituteType(owner) as GenericInstance ?? owner, expr);

            // The receiver is an interface value: which implementation runs is settled only at runtime.
            // That is the language's only dynamic dispatch.
            //
            // SUBSTITUTED rather than written, which is what the generic twin below already did.
            // A type argument may BE an interface — 'show<T :: [Named]>' called with a 'Named'
            // value — and then the written type is the parameter while the real receiver is an
            // interface. Without the substitution this case missed, the constraint case took it,
            // and it ended at "'Named.name' was not lowered": an interface method has no body to
            // call directly, which is the whole reason this case exists.
            case MemberExpr member
                when SubstituteType(ReceiverType(member.Target)) is NamedRef
                     { Symbol.Kind: TypeSymbolKind.Interface } iface:
                return LowerVirtualCall(member, iface.Symbol, expr);

            // A generic interface as the receiver ('Iterator<int>'): the same dynamic dispatch, except
            // that the slot table hangs on the INSTANCE — 'Iterator<int>' and 'Iterator<string>' are
            // different entries.
            case MemberExpr member
                when SubstituteType(ReceiverType(member.Target)) is GenericInstance
                     { Definition.Kind: TypeSymbolKind.Interface } genericIface:
                return LowerVirtualCall(member, genericIface, expr);

            // The receiver is a TYPE PARAMETER with a constraint: 'fn total<T :: [P]>(x: T)
            // { x.price(); }'. The sema binds 'price' to the interface, where it has no body. In an
            // instance T is settled, so there is a real method.
            case MemberExpr member
                when ReceiverType(member.Target) is TypeParamType parameter
                     && _substitution.ContainsKey(parameter.Param):
                return LowerConstraintCall(member, SubstituteType(ReceiverType(member.Target)),
                    expr);

            // The receiver's type is an associated type through a constraint, 'A.Iter' (10 B6):
            // under this instance's substitution it is the answer, and the call is that type's.
            case MemberExpr member
                when ReceiverType(member.Target) is AssocOf && _substitution.Count > 0:
                return LowerConstraintCall(member, SubstituteType(ReceiverType(member.Target)), expr);

            // A STATIC interface member through a constraint, 'T.parse(s)' (03 T5): under this
            // instance's substitution T is a type, and the call is that type's own static — direct.
            case MemberExpr member
                when (_types.RefOf(member.Target) as GenericParamSymbol
                      ?? (_types.TypeOf(member.Target) as NonValueType)?.Symbol as GenericParamSymbol) is { } parameter
                     && _substitution.TryGetValue(parameter, out var boundTo):
                return LowerStaticConstraintCall(member, boundTo, expr);

            // An INTERFACE'S member bound on a concrete receiver — the qualified form 'Walker.walk(d)'
            // (04 D2 R5) and a member delegated to a field (D1): the sema settled the member on
            // the interface, and the call goes through that interface's table, where the type's
            // implementation — own, in a block, a default, a forwarder — stands.
            case MemberExpr member
                when _types.RefOf(member) is FunctionSymbol promised
                     && ReceiverType(member.Target) is NamedRef
                         { Symbol: { Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct
                             or TypeSymbolKind.Enum } concrete }
                     && !ReferenceEquals(concrete.Members.LookupLocal(member.Member), promised)
                     && !BoundToExtension(member)
                     && _typeTable.InterfaceDeclaring(concrete, promised) is { } declaring:
            {
                // Own members win (05 §3), written 'Walker.walk(d)' too (§4): what the type has
                // for the interface — in its body, or in the block that declares the conformance
                // — is called as its member. Asked BEFORE the default: the default's instance
                // ran past the 'walk' the type wrote.
                if (declaring.Generics.Length == 0
                    && _types.ConformanceImpl(concrete, declaring, member.Member, null) is { } answered)
                {
                    calleeName = member.Member;
                    bound = answered;
                    receiver = LowerExpr(member.Target);
                    receiverOwner = concrete;
                    break;
                }

                // A default, reached plainly or written 'Walker.walk(d)': its instance for the
                // type (04 D9), direct — a constraint-only interface has no value to go through.
                if (DirectDefault(member) is { } declared)
                    return LowerDefaultCall(member, declared, SubstituteType(ReceiverType(member.Target)), expr);
                // A member the type leaves to a field (04 D1): its forwarder, called with the
                // receiver as it stands — a struct is the caller's place there, and a 'mut fn'
                // writes the field of THAT. Through the table, the struct went into a box first.
                if (TryLowerForwardedCall(member, concrete, ReceiverType(member.Target), declaring, expr, out var forwarded))
                    return forwarded;

                var into = _typeTable.InterfaceAsDeclared(concrete, declaring, expr.Span);
                return LowerVirtualCall(member, declaring, expr, into.Type,
                    LowerExprAs(member.Target, into));
            }

            // An INTERFACE DEFAULT method on a concrete receiver: 'it.isFree()', where 'isFree' belongs
            // to the interface rather than to the struct. Its 'this' is the interface type, so no direct
            // call leads there — the receiver is lifted (mkiface) and then called virtually. The same
            // route LowerConstraintCall takes.
            //
            // 'An own member beats a default' sits in the LookupLocal condition: when the concrete type
            // has the method itself, this case falls through to the direct call. A visible EXTENSION
            // beats it too (§5.4), and that half was missing: the question asked was whether the TYPE
            // carries the member, never what the sema had already BOUND.
            case MemberExpr member
                when ReceiverType(member.Target) is NamedRef
                     { Symbol: { Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct
                         or TypeSymbolKind.Enum } concrete }
                     && concrete.Members.LookupLocal(member.Member) is not FunctionSymbol
                     && !BoundToExtension(member)
                     && _typeTable.InterfaceProviding(concrete, member.Member) is { } provider:
            {
                // The default's instance for the type (04 D9), direct; a generic interface's goes
                // through its value below.
                if (DirectDefault(member) is { } onConcrete)
                    return LowerDefaultCall(member, onConcrete, SubstituteType(ReceiverType(member.Target)), expr);
                // As the concrete type DECLARES it: 'Iterator<int>', not 'Iterator'. A generic
                // interface has no entry of its own, and lifting into the definition is what a
                // default of one used to die on.
                var into = _typeTable.InterfaceAsDeclared(concrete, provider, expr.Span);
                return LowerVirtualCall(member, provider, expr, into.Type,
                    LowerExprAs(member.Target, into));
            }

            case MemberExpr member
                when ReceiverType(member.Target) is NamedRef
                     { Symbol.Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct
                         or TypeSymbolKind.Enum } named:
                calleeName = member.Member;
                bound = _types.RefOf(member);
                receiver = LowerExpr(member.Target);
                receiverOwner = named.Symbol;
                break;

            // A member of an 'extend' block on an instance of a generic type (03 T7 X1): the
            // block's parameters bound by the receiver, the method an instance of its own.
            case MemberExpr member
                when _types.RefOf(member) is FunctionSymbol blockMember
                     && _typeTable.BlockOf(blockMember) is { Target: not null } ownerBlock
                     && SubstituteType(ReceiverType(member.Target)) is GenericInstance onInstance:
                return LowerBlockMethodCall(member, blockMember, ownerBlock, onInstance, expr);

            // A member of a block on a built-in constructor (03 T7 X2): 'xs.sum()' on a 'T[]',
            // 'o.orElse(v)' on a '?T' — the shape binds the block's parameters.
            case MemberExpr member
                when _types.RefOf(member) is FunctionSymbol shapeMember
                     && _typeTable.BlockOf(shapeMember) is { } shapeBlock
                     && (shapeBlock.IsConstructorTarget || shapeBlock.Target is { Kind: TypeSymbolKind.Builtin, Name: "Slice" })
                     && SubstituteType(ReceiverType(member.Target)) is ArrayOf or SliceOf or InlineArrayOf or Optional or Sema.TupleOf:
            {
                // A view's member on an array (03 §5.2 rule 4): the array gives a view of itself.
                var shapeReceiver = SubstituteType(ReceiverType(member.Target));
                if (shapeReceiver is ArrayOf whole && shapeBlock.Target is { Kind: TypeSymbolKind.Builtin, Name: "Slice" })
                    return LowerBlockMethodCall(member, shapeMember, shapeBlock, new SliceOf(whole.Element), expr, viewed: true);
                return LowerBlockMethodCall(member, shapeMember, shapeBlock, shapeReceiver, expr);
            }

            // A GENERIC interface member on an instance of a generic type:
            // 'ArrayIterator<int>.zip<string>()'. It is not a member of the instance at all — it is
            // a default of 'Iterator<T>', monomorphized per (interface instance, type arguments) —
            // and the case below would ask the CLASS instance for it. What breaks there is the
            // member's own parameters: 'Iterator<(T, B)>' has a B the receiver's substitution never
            // mentions, and resolving the return type reports an unsupported type argument at the
            // interface's own declaration, which is not where the program is wrong.
            //
            // Restricted to GENERIC members on purpose. A non-generic default reaches the instance
            // path today and works there, and moving it would change the code generated for every
            // existing program without answering a question anybody asked.
            case MemberExpr member
                when SubstituteType(ReceiverType(member.Target)) is GenericInstance instanced
                     && instanced.Definition.Kind is TypeSymbolKind.Class or TypeSymbolKind.Struct
                     && instanced.Definition.Members.LookupLocal(member.Member) is not FunctionSymbol
                     && _types.RefOf(member) is FunctionSymbol { Generics.Length: > 0 }
                     && _typeTable.InterfaceProviding(instanced.Definition, member.Member)
                         is { } providing:
            {
                // A plain interface's generic default (D9 whole): its instance for this receiver.
                if (DirectDefault(member) is { } forInstance)
                    return LowerDefaultCall(member, forInstance, instanced, expr);
                var into = InterfaceAsDeclaredBy(instanced, providing, expr.Span);
                return LowerVirtualCall(member, providing, expr, into.Type,
                    LowerExprAs(member.Target, into));
            }

            // The receiver is an instance of a generic type: 'Box<int>.get()'. The method belongs to the
            // INSTANCE rather than to the definition, and its return type may be T.
            case MemberExpr member
                when SubstituteType(ReceiverType(member.Target)) is GenericInstance owner
                     && owner.Definition.Kind is TypeSymbolKind.Class or TypeSymbolKind.Struct or TypeSymbolKind.Enum:
                return LowerGenericMethodCall(member, owner, expr);

            // An extension on a builtin: 'n.double()' with 'extend int'. The receiver is a scalar and
            // therefore NO NamedRef; without this case it falls into the type or module branch below,
            // which attaches no receiver, and the verifier reports a call with one argument too few.
            //
            // A scalar as parameter 0 needs nothing new: no boxing, no fat pointer, no dispatch. Which
            // function runs is statically settled — that is the whole difference between an inherent
            // extension and one through an interface.
            // An interface's default on a builtin, '3.greet()' with 'extend int :: [Named]': its
            // instance for the builtin (04 D9), direct — a scalar has no value of the interface.
            case MemberExpr member
                when ReceiverType(member.Target) is PrimitiveType onBuiltin && DirectDefault(member) is { } builtinDefault:
                return LowerDefaultCall(member, builtinDefault, onBuiltin, expr);

            case MemberExpr member
                when ReceiverType(member.Target) is PrimitiveType
                     && _types.RefOf(member) is FunctionSymbol:
                calleeName = member.Member;
                bound = _types.RefOf(member);
                receiver = LowerExpr(member.Target);
                break;

            case MemberExpr member: // a type or module target: P.new(…), console.println(…)
                calleeName = member.Member;
                bound = _types.RefOf(member);
                break;

            case IdentifierExpr callee:
                calleeName = callee.Name;
                bound = _types.RefOf(callee);
                break;

            default:
                throw NotSupported("call target (only functions and methods)", expr.Callee.Span);
        }

        // A selective import binds through an ImportBindingSymbol; the actual target lies beneath it.
        // Without unwrapping, `import std.io.console { println };` looks different from a call in the
        // same module although it is the same function.
        if (bound is ImportBindingSymbol binding) bound = binding.Target;

        if (bound is not FunctionSymbol symbol)
            throw NotSupported($"call to '{calleeName}' (not a function or method)", expr.Span);

        // Natively backed, by the stdlib or the host: its own instruction type and its own index space.
        //
        // For a METHOD on a host type the import carries the receiver as parameter 0, the same convention
        // as every other method. It stands in 'receiver', because the call 'e.damage(30)' does not have
        // it in the argument list.
        if (_imports.IsNative(symbol))
            return LowerImportCall(_imports.Intern(symbol), WithDefaults(symbol, expr.Arguments), expr.Span, receiver);

        // 'panic' is a language builtin and therefore has no module it is declared in; the resolver puts
        // it into the root scope. It is bound like any other native, through its symbolic name.
        if (symbol.Name == "panic" && !_functions.ContainsKey(symbol))
        {
            var message = expr.Arguments.Length == 1
                ? LowerExprAs(expr.Arguments[0], new IrScalarType(IrScalar.String))
                : throw NotSupported("panic with other than one argument", expr.Span);

            CallHelper("std.core.panic", expr.Span, message);

            // panic never returns, its return type being 'never'. The block ends here: everything behind
            // it would be dead code, and the verifier rejects unreachable blocks.
            _b.Seal(new Unreachable(expr.Span));
            return null;
        }

        if (symbol.Declaration is not FunctionDecl generic)
            throw NotSupported($"call to '{calleeName}' (no declaration to read parameters from)",
                expr.Span);

        // A GENERIC NATIVE has no instance to build — it has a row per signature. The type
        // arguments go into the declared signature and the result is interned; see
        // ImportTable.DeclareTemplate.
        if (symbol.Generics.Length > 0 && _imports.IsGenericNative(symbol))
            return LowerGenericImportCall(symbol, expr, calleeName);

        // Generic: not the declaration is called but an INSTANCE of it. Which one is said by the type
        // arguments the sema inferred at the call site; deriving them a second time would be a second
        // truth about the same question.
        FunctionId target;
        if (symbol.Generics.Length > 0)
        {
            // A type argument may itself BE a type parameter when the calling function is already an
            // instance: in 'wrap<T>' the call 'id(x)' calls the instance 'id<T>', and which T that is is
            // known only to the own substitution.
            var typeArguments = SubstitutedTypeArguments(expr);

            // THE RECEIVER'S TYPE GOES WITH IT. It used to be 'null' here unconditionally, so a
            // generic METHOD on a plain class was monomorphized without a 'this' slot: the call
            // pushed a receiver and the callee declared one parameter fewer than it was given.
            // The frontend was happy -- 'check' passes -- and the module the writer produced was
            // then refused by its own reader with "block at 0 ends with 1 value(s) on the stack".
            // Only 'check --emit' or a real build ever asked.
            //
            // And its class goes into the instance's name: 'TaskScope.spawn<int, Oops>' and a free
            // 'spawn<int, Oops>' of the same module are two functions. Under the bare name they
            // were one key, and the free function's call landed on the method, one argument short.
            //
            // A block's member on a builtin or through a type's name has no holder here: the block's
            // target goes into the name, or 'int8.exact<int>' and 'uint8.exact<int>' are one key
            // and the second call lands on the first block's function.
            //
            // And a STATIC function of a type's body has no holder either, through the type's name
            // or — inside the type — through none: its type goes into the name, or 'Small.make<int>',
            // 'Tiny.make<int>' and a free 'make<int>' are one key, and 'Thread.spawn<int, never>'
            // is the task 'spawn<int, never>' whenever a program writes both.
            var instanceName = receiverOwner is { } holder && !calleeName.StartsWith(holder.Name + ".", StringComparison.Ordinal)
                ? $"{holder.Name}.{calleeName}"
                : receiverOwner is null ? StaticBaseName(symbol, calleeName) : calleeName;
            // A builtin's value has no symbol to be the receiver: its type is, for the 'this'.
            var builtinReceiver = receiverOwner is null && receiver is not null && expr.Callee is MemberExpr { Target: var on }
                ? SubstituteType(ReceiverType(on))
                : null;
            target = _instances.Request(symbol, generic, instanceName, receiverOwner,
                typeArguments, _typeTable, expr.Span, receiverType: builtinReceiver);
        }
        else if (!TryResolveFunction(symbol, out target))
        {
            throw NotSupported($"call to '{calleeName}' (external or bodiless)", expr.Span);
        }

        if (symbol.Declaration is not FunctionDecl declaration)
            throw NotSupported($"call to '{calleeName}' (no declaration to read parameters from)",
                expr.Span);

        // The type arguments of this call site, by name — the form in which the type table keeps
        // substitutions. Only that way can the parameter type 'Iterator<T>' become 'Iterator<int>' and
        // the coercion from class to interface arise.
        var calleeSubstitution = symbol.Generics.Length > 0
            ? NamedSubstitutionFor(symbol, _types.TypeArgumentsOf(expr)) : null;

        var supplied = MaterializeArguments(declaration, ArgumentsOf(expr), calleeName, expr.Span,
            calleeSubstitution);

        // The receiver comes first: the order is the IR's parameter convention and has to match the one
        // in which FunctionLowerer allocated the slots.
        var offset = receiver is null ? 0 : 1;
        var args = new TempId[supplied.Length + offset];
        if (receiver is { } self) args[0] = self;
        supplied.CopyTo(args, offset);

        var returnType = TypeOfExpr(expr);
        if (IsVoid(returnType))
        {
            _b.Emit(new Call(null, target, args, expr.Span));
            return null;
        }

        var dest = _slots.NewTemp(returnType);
        _b.Emit(new Call(dest, target, args, expr.Span));
        _fresh.Add(dest);
        return dest;
    }

    /// <summary>
    /// The argument list as the callee expects it: exactly one value per declared parameter.
    ///
    /// <para>Here the two forms arise that look different at the source than in the IR: a <c>params</c>
    /// array and an omitted default.</para>
    ///
    /// <para>The default expression is evaluated AT THE CALL SITE rather than once at the callee, the
    /// same choice as in C#. Otherwise it would have to be lowered in a context where the caller's
    /// arguments are not visible.</para>
    /// </summary>
    /// <summary>The arguments of a call in parameter order: as the sema arranged them when the call
    /// names some (04 D5), as written otherwise.</summary>
    private Expr?[] ArgumentsOf(CallExpr expr)
    {
        if (expr.Parenthesized is { } grouped)
            foreach (var (argument, written) in grouped) _writtenWith[argument] = written;
        return _types.ArrangedArgumentsOf(expr) ?? expr.Arguments;
    }

    /// <summary>An argument written in parentheses of its own, with the span that holds them —
    /// the text '@callerExpr' quotes is what the call wrote, parentheses included.</summary>
    private readonly Dictionary<Expr, Span> _writtenWith = new(ReferenceEqualityComparer.Instance);

    /// <summary>The argument materialized for an earlier parameter of the call whose default is
    /// being lowered (04 D5 F2): a default runs per call, in the callee's scope, and may read the
    /// parameters before it — their symbols are the callee's, and here they mean the temps.</summary>
    private readonly Dictionary<Symbol, TempId> _argumentOverrides = new(ReferenceEqualityComparer.Instance);

    /// <param name="provided">In parameter order; a <c>null</c> is a parameter the call leaves to
    /// its default.</param>
    private TempId[] MaterializeArguments(FunctionDecl callee, Expr?[] provided, string name,
        Span span, IReadOnlyDictionary<string, LyrType>? calleeSubstitution = null)
    {
        var parameters = callee.Parameters;
        var args = new TempId[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];

            // 'params xs: T[]' collects everything from here on. The sema allows it only on the last
            // parameter, so the rest really is the rest.
            if (parameter.IsParams)
            {
                args[i] = CollectVariadic(parameter, provided, i, span, calleeSubstitution);
                return args;
            }

            if (i < provided.Length && provided[i] is { } given)
            {
                args[i] = LowerArgument(given, parameter, calleeSubstitution);
                continue;
            }

            // '@callerExpr(p)' (09 A11): where the call leaves this parameter out, the text it
            // wrote for 'p' — what an assertion names (10 X1).
            if (_types.CallerExprOf(parameter) is { } target && _types.SourceText is { } text
                && Array.FindIndex(parameters, q => q.Name == target) is var written and >= 0
                && written < provided.Length && provided[written] is { } argument)
            {
                var quoted = _writtenWith.GetValueOrDefault(argument, argument.Span);
                args[i] = EmitConst(new StringConst(text(quoted)), new IrScalarType(IrScalar.String), span);
                continue;
            }

            if (parameter.Default is { } fallback)
            {
                // Per call, in the callee's scope: an earlier parameter the default names is the
                // argument just materialized for it, for as long as this default is lowered.
                var bound = new List<Symbol>();
                for (var j = 0; j < i; j++)
                    if (_types.RefOf(parameters[j]) is ParameterSymbol earlier && _argumentOverrides.TryAdd(earlier, args[j]))
                        bound.Add(earlier);
                try { args[i] = LowerArgument(fallback, parameter, calleeSubstitution); }
                finally { foreach (var symbol in bound) _argumentOverrides.Remove(symbol); }
                continue;
            }

            // The sema checked the arity; landing here would mean it slipped through.
            throw Bug($"call to '{name}' at {span} passes {provided.Length} argument(s) but " +
                      $"parameter '{parameter.Name}' has no default");
        }

        if (provided.Length > parameters.Length)
            throw Bug($"call to '{name}' at {span} passes {provided.Length} argument(s) for " +
                      $"{parameters.Length} parameter(s)");

        return args;
    }

    /// <summary>
    /// The remaining arguments as an array: <c>sum(1, 2, 3)</c> becomes <c>sum([1, 2, 3])</c>.
    ///
    /// <para>A SPREAD ARGUMENT IS THE ARRAY: <c>sum(xs...)</c> hands on <c>xs</c> itself, no copy
    /// — that is how one variadic function hands its arguments to another. The call says so
    /// (the review's A2); an array without the dots is one element, like any other argument.</para>
    /// </summary>
    private TempId CollectVariadic(Param parameter, Expr?[] provided, int from, Span span,
        IReadOnlyDictionary<string, LyrType>? calleeSubstitution = null)
    {
        // Under the callee's substitution, as LowerArgument lowers a parameter type: in
        // 'fn count<T>(xs: T...)' only the call knows which T the array holds.
        IrType lowered;
        using (calleeSubstitution is null ? null : _typeTable.PushSubstitution(calleeSubstitution))
            lowered = _typeTable.Lower(parameter.Type);
        if (lowered is not IrArrayType array)
            throw NotSupported($"'params {parameter.Name}' whose type is not an array",
                parameter.Span);

        var rest = provided.Length > from ? provided[from..].OfType<Expr>().ToArray() : [];

        if (rest is [var spread] && _types.IsSpread(spread)) return LowerExprAs(spread, array);

        var elements = new TempId[rest.Length];
        for (var i = 0; i < elements.Length; i++)
            elements[i] = LowerExprAs(rest[i], array.Element);

        var dest = _slots.NewTemp(array);
        _b.Emit(new NewArray(dest, array.Element, elements, span));
        return dest;
    }

    /// <summary>
    /// An argument adapted to the DECLARED parameter type.
    ///
    /// <para>Without this step a class stays a class even when the parameter is an interface, and the
    /// callee gets a bare reference instead of a fat pointer. The verifier catches that, but as a type
    /// mismatch deep in the callee rather than as what it is: a missing coercion at the call site.</para>
    /// </summary>
    private TempId LowerArgument(Expr argument, Param parameter,
        IReadOnlyDictionary<string, LyrType>? calleeSubstitution = null)
    {
        // Only the lowering of the parameter type is shielded: a type this compiler build does not know
        // is reported by the function itself anyway, and complaining twice would be noise. The coercion
        // stands DELIBERATELY outside: inside the try, the catch swallows a missing 'mkiface' and turns a
        // diagnostic into malformed IR.
        IrType expected;
        try
        {
            // Lowered under the substitution OF THE CALLEE rather than the own one: in
            // 'fn count<T>(source: Iterator<T>)' the written parameter type is 'Iterator<T>', and which T
            // is meant is known only to the call site.
            //
            // Without it, lowering the parameter type throws on the unresolved T, the catch below fires,
            // and the argument passes WITHOUT a coercion — a class lands where an interface value has to
            // stand. The verifier reports that as malformed IR, so the compiler crashes instead of
            // diagnosing.
            using (calleeSubstitution is null
                       ? null : _typeTable.PushSubstitution(calleeSubstitution))
                expected = _typeTable.LowerValue(parameter.Type);
        }
        catch (UnsupportedConstructException)
        {
            return LowerExpr(argument);
        }

        return LowerExprAs(argument, expected);
    }

    /// <summary>
    /// The dynamic call. The receiver is an interface value and carries its concrete type along; the
    /// runtime uses that to look up the vtable.
    ///
    /// <para>The slot rather than the name is the same decision as for the field index: Lyric is
    /// statically typed and has no monkey patching, so the position is fixed at compile time. A name
    /// lookup with an inline cache would solve a problem this language does not have.</para>
    /// </summary>
    private TempId? LowerVirtualCall(MemberExpr member, GenericInstance instance, CallExpr expr) =>
        LowerVirtualCall(member, instance.Definition, expr,
            _typeTable.Intern(instance.Definition, instance.Arguments));

    private TempId? LowerVirtualCall(MemberExpr member, TypeSymbol iface, CallExpr expr,
        TypeId? instanceType = null, TempId? receiver = null)
    {
        // For a generic interface the slot table hangs on the INSTANCE; the slot INDEX is the same,
        // because it comes from the declaration and holds for all instances.
        var interfaceId = instanceType ?? _typeTable.InterfaceOf(iface).Type;

        // A GENERIC member is not dispatched: it has no slot and cannot have one, because a slot
        // holds one function and a method with type parameters of its own is one function per
        // instantiation. It is monomorphized and called directly — which is sound precisely
        // because such a member may not be overridden (LYR-SEM0082), so the default IS the
        // implementation for every receiver. Both routes here end up in the same place: a
        // constrained receiver arrives lifted, an interface value arrives as itself.
        //
        // In Lyric 5 that is a GENERIC interface's member alone. A plain interface's generic
        // default never comes here: it is instantiated per conformer (DirectDefault), where the
        // conformer's own may stand in its place (04 D9 whole).
        //
        // A PRIVATE helper (07 V2 S4) takes the same route for the same reason: it has no slot,
        // because no conformer may stand in for it.
        if (iface.Members.LookupLocal(member.Member) is FunctionSymbol generic
            && (generic.Generics.Length > 0 || generic.Declaration is FunctionDecl { IsPrivateHelper: true }))
            return LowerGenericInterfaceMethod(member, expr, iface, generic, interfaceId,
                receiver ?? LowerExprAs(member.Target, new IrInterfaceType(interfaceId)));

        // Read the slot from the entry of THIS instance: going through the symbol would intern 'Src'
        // without type arguments, and that has no entry.
        var slots = _typeTable.MethodSlotsOf(interfaceId);
        var slot = Array.IndexOf(slots, member.Member);
        if (slot < 0)
            throw NotSupported($"interface '{iface.Name}' has no method '{member.Member}'",
                member.Span);

        var args = new TempId[expr.Arguments.Length + 1];

        // The receiver may already be lifted, for instance for a constraint whose default method goes
        // through the interface.
        args[0] = receiver ?? LowerExpr(member.Target);

        // The signature stands on the interface rather than on an implementation: it is the contract.
        var declaration = iface.Members.LookupLocal(member.Member) is FunctionSymbol method
            ? method.Declaration as FunctionDecl
            : null;

        // The parameter types are written against the interface's OWN type parameters, so for an
        // instance they have to be read under its substitution: 'fn(T) -> bool' on an
        // 'Iterator<int>' is 'fn(int) -> bool'. Without it the lowering meets a bare T, which is
        // no type at all — the wall every default of a generic interface ran into.
        var owning = _typeTable.InstanceOf(interfaceId);

        for (var i = 0; i < expr.Arguments.Length; i++)
            args[i + 1] = declaration is not null && i < declaration.Parameters.Length
                ? LowerExprAs(expr.Arguments[i], owning is { } instance
                    ? LowerWithOwner(declaration.Parameters[i].Type, instance, expr.Span)
                    : _typeTable.Lower(declaration.Parameters[i].Type))
                : LowerExpr(expr.Arguments[i]);

        var returnType = TypeOfExpr(expr);
        if (IsVoid(returnType))
        {
            _b.Emit(new CallVirt(null, interfaceId, slot, args, returnType, expr.Span));
            return null;
        }

        var dest = _slots.NewTemp(returnType);
        _b.Emit(new CallVirt(dest, interfaceId, slot, args, returnType, expr.Span));
        return dest;
    }

    /// <summary>A call to a natively backed function. The signature comes from the import table rather
    /// than from a function: an import has no body.</summary>
    /// <param name="receiver">For a method on a host type the receiver, which becomes parameter 0.
    /// <c>null</c> for every free function.</param>
    /// <summary>
    /// <c>[first, ..rest]</c> — the elements the rest covers, copied into an array of their own.
    ///
    /// <para>A copy, not a view: the language has no slice, and a view would alias the array the
    /// pattern matched. Python's <c>case [x, *rest]</c> copies for the same reason; Rust's
    /// <c>rest @ ..</c> does not, because it has slices.</para>
    ///
    /// <para>The length is known only at runtime, so the array comes from
    /// <c>std.core.rawArrayAlloc</c>, whose slots are unwritten until the loop below fills every
    /// one of them. That the native is generic is what makes this work at any element type.</para>
    /// </summary>
    /// <summary><c>..rest</c> binds a view of the elements between the fixed ones — from
    /// <c>before</c> to <c>length - after</c> — sharing them, no copy (03 T13 A2). Lyric 4 copied
    /// them into a new array.</summary>
    private void BindNamedRest(RestPattern rest, TempId value, IrType elementType, TempId length,
        int beforeCount, int afterCount)
    {
        if (_types.RefOf(rest) is not LocalSymbol local)
            throw Bug($"the rest binding '{rest.Name}' was not bound by the type checker");

        var i64 = new IrScalarType(IrScalar.I64);
        var span = rest.Span;
        var low = EmitConst(new IntConst((ulong)beforeCount), i64, span);
        var afterN = EmitConst(new IntConst((ulong)afterCount), i64, span);
        var high = _slots.NewTemp(i64);
        _b.Emit(new BinOp(high, IrBinKind.Sub, i64, length, afterN, span));
        var viewType = new IrSliceType(elementType);
        var view = _slots.NewTemp(viewType);
        _b.Emit(new MakeSlice(view, value, low, high, elementType, span));
        var slot = _slots.DeclareFor(local, viewType);
        _b.Emit(new StoreLocal(slot, view, span));
    }

    /// <summary>
    /// A call to a GENERIC native: the type arguments are substituted into the declared signature
    /// and the resulting row is interned under the same name.
    ///
    /// <para>No instance is built. A native has no body to monomorphize — what varies is only what
    /// the runtime is told the signature is, and the import table has keyed by name AND signature
    /// since coroutines. The binder compares TAGS, so one host implementation answers every
    /// instantiation whose shape it can serve.</para>
    /// </summary>
    private TempId? LowerGenericImportCall(FunctionSymbol symbol, CallExpr expr, string calleeName)
    {
        var (name, decl, _) = _imports.TemplateOf(symbol);

        // Through the sema's inference, with a type argument that is itself a parameter of the
        // CALLING instance resolved through the own substitution — the step every generic call
        // takes.
        var typeArguments = _types.TypeArgumentsOf(expr)
            .Select(t => t is TypeParamType p && _substitution.TryGetValue(p.Param, out var bound)
                ? bound : t)
            .ToArray();

        if (typeArguments.Length != symbol.Generics.Length)
            throw NotSupported(
                $"call to the native '{calleeName}': {typeArguments.Length} type argument(s) for "
                + $"{symbol.Generics.Length} parameter(s)", expr.Span);

        var mapping = new Dictionary<string, LyrType>(StringComparer.Ordinal);
        for (var i = 0; i < symbol.Generics.Length; i++)
            mapping[symbol.Generics[i].Name] = typeArguments[i];

        var parameters = decl.Parameters
            .Select(p => LowerDeclaredUnder(p.Type, mapping, p.Span))
            .ToArray();
        var returnType = decl.ReturnType is null
            ? new IrScalarType(IrScalar.Void)
            : LowerDeclaredUnder(decl.ReturnType, mapping, expr.Span);

        var target = _imports.Intern(new IrImport(name, parameters, returnType));
        return LowerImportCall(target, expr.Arguments, expr.Span);
    }

    /// <summary>A written type of a native's signature, lowered with the call's type arguments put
    /// in for the declaration's own parameter names. Past what a host binds by layout, a generic
    /// native — only the standard library declares one — takes what its non-generic natives take
    /// (ModuleLowerer.NativeType): a type the runtime holds without knowing its layout, an object
    /// such as <c>Atomic&lt;T&gt;</c>, lowered through the table under the same substitution.</summary>
    private IrType LowerDeclaredUnder(TypeNode node, Dictionary<string, LyrType> mapping, Span span)
    {
        if (node is NamedType { Path: [var only], TypeArguments.Length: 0 }
            && mapping.TryGetValue(only, out var bound))
            return LowerType(bound, span);

        if (node is ArrayType array)
            return new IrArrayType(LowerDeclaredUnder(array.Element, mapping, span));

        if (node is NullableType option)
            return new IrOptionalType(LowerDeclaredUnder(option.Inner, mapping, span));

        try
        {
            return DeclaredTypes.Lower(node);
        }
        catch (UnsupportedConstructException)
        {
            using (_typeTable.PushSubstitution(mapping))
                return _typeTable.Lower(node);
        }
    }

    /// <summary>A native's arguments with the defaults its declaration gives for those a call leaves
    /// out (04 D5) — its own expressions, lowered at the call as every argument is: <c>assert(ok)</c>
    /// passes the default message.</summary>
    private static Expr[] WithDefaults(FunctionSymbol native, Expr[] given) =>
        native.Declaration is FunctionDecl { Parameters: var declared } && given.Length < declared.Length
        && declared.Skip(given.Length).All(p => p.Default is not null && !p.IsParams)
            ? [.. given, .. declared.Skip(given.Length).Select(p => p.Default!)]
            : given;

    private TempId? LowerImportCall(ImportId target, Expr[] arguments, Span span,
        TempId? receiver = null)
    {
        var import = _imports.Used[target.Value];
        var offset = receiver is null ? 0 : 1;

        // The shape carries the DECLARED parameters; the import carries the flattened wire
        // signature. Without a shape — runtime helpers, generated host functions — the two
        // coincide.
        var shape = _imports.ShapeOf(target);
        if (arguments.Length + offset != (shape?.Params.Length ?? import.ParamTypes.Length))
            throw NotSupported($"call to '{import.Name}' with default or variadic arguments", span);

        var args = new List<TempId>(import.ParamTypes.Length);
        if (receiver is { } self) args.Add(self);
        for (var i = 0; i < arguments.Length; i++)
        {
            if (shape?.Params[i + offset] is not { Struct: { } structType } flat)
            {
                // A scalar argument is coerced to the wire type it lands in, as an argument to
                // a Lyric function is (03 T1c): 'fromInt(x)' with 'x: int8' widens here, or the
                // call would hand an i8 to a parameter declared i64 — the verifier refused that,
                // and a native callee would have read it as whatever C made of it.
                args.Add(LowerExprAs(arguments[i], import.ParamTypes[args.Count]));
                continue;
            }
            var value = LowerExpr(arguments[i]);

            // A struct crosses as its fields, read at the call: the same snapshot a by-value
            // pass would take, without the copy — and without the object, once the scalarizer
            // has dissolved the operand.
            for (var f = 0; f < flat.Fields.Length; f++)
            {
                var field = _slots.NewTemp(flat.Fields[f]);
                _b.Emit(new LoadField(field, value, structType, new FieldId(f), flat.Fields[f],
                    span));
                args.Add(field);
            }
        }

        // A struct RETURN: the hidden buffer goes in as the trailing argument, the host fills
        // its slots, and the expression's value is a fresh COPY of the buffer — value semantics
        // is what makes the shared buffer safe (any binding copies), and the scalarizer is what
        // makes the copy free when it never escapes.
        if (shape?.Return is { } returned)
        {
            var bufferType = new IrStructType(returned.Struct);
            var buffer = _slots.NewTemp(bufferType);
            _b.Emit(new LoadGlobal(buffer, _imports.ResultBuffer(target, _globals), bufferType,
                span));
            args.Add(buffer);

            _b.Emit(new CallImport(null, target, [.. args], span));

            var copied = _slots.NewTemp(bufferType);
            _b.Emit(new StructCopy(copied, buffer, returned.Struct, span));
            return copied;
        }

        if (IsVoid(import.ReturnType))
        {
            _b.Emit(new CallImport(null, target, [.. args], span));
            return null;
        }

        var dest = _slots.NewTemp(import.ReturnType);
        _b.Emit(new CallImport(dest, target, [.. args], span));
        return dest;
    }

    /// <summary>
    /// A direct call to a runtime helper through its fixed name.
    ///
    /// <para>The lowering references <c>std.string.concat</c> and <c>std.core.panic</c> without anyone
    /// having imported the modules — the same model as Roslyn's reference to <c>String.Concat</c>. Used
    /// by f-strings, by <c>+</c> and <c>*</c> on <c>string</c>, and by <c>panic</c>.</para>
    /// </summary>
    private TempId CallHelper(string name, Span span, params TempId[] args)
    {
        if (!_imports.TryFind(name, out var import))
            throw NotSupported(
                $"the runtime helper '{name}' (is the standard library on the module path?)", span);

        var target = _imports.Intern(import);
        if (IsVoid(import.ReturnType))
        {
            _b.Emit(new CallImport(null, target, args, span));
            return default;
        }

        var dest = _slots.NewTemp(import.ReturnType);
        _b.Emit(new CallImport(dest, target, args, span));
        return dest;
    }

    /// <summary>
    /// An f-string becomes a chain of <c>concat</c> and the <c>fromXxx</c> converters. No arrays and no
    /// varargs — the IR can do neither, and this way it does not need to. Roslyn does the same for
    /// <c>$"…"</c> without a format spec.
    /// </summary>
    private TempId LowerInterpolatedString(InterpolatedStringExpr expr)
    {
        var stringType = new IrScalarType(IrScalar.String);
        var parts = new List<TempId>();
        var pendingText = new System.Text.StringBuilder();

        void FlushText(Span span)
        {
            if (pendingText.Length == 0) return;
            parts.Add(EmitConst(new StringConst(pendingText.ToString()), stringType, span));
            pendingText.Clear();
        }

        foreach (var segment in expr.Segments)
        {
            switch (segment)
            {
                case InterpText text:
                    // The parser stores the text pieces raw (see InterpText); the escapes are resolved
                    // here, and the doubled braces of the f-string form fold to one — '{{' and '}}'
                    // are the grammar's literal-brace escape, and they exist only in THESE chunks.
                    // Adjacent pieces collect into one constant.
                    pendingText.Append(Escapes.Resolve(
                        text.Text.Replace("{{", "{").Replace("}}", "}")));
                    break;

                case InterpHole hole:
                    FlushText(hole.Span);
                    // A hole the sema routed through 'Display' IS the 'show()' call it stored —
                    // the same seam as the operators; the value lowers once, as its receiver.
                    if (_types.OperatorCallOf(hole) is { } shown)
                        parts.Add(LowerCall(shown) ?? throw Bug("'show()' returned no value"));
                    else
                        parts.Add(hole.FormatSpec is { } spec
                            ? FormattedValue(hole.Expr, spec)
                            : ToStringValue(hole.Expr));
                    break;

                default:
                    throw Bug($"unhandled interpolation segment {segment.GetType().Name}");
            }
        }
        FlushText(expr.Span);

        if (parts.Count == 0) return EmitConst(new StringConst(string.Empty), stringType, expr.Span);

        var result = parts[0];
        for (var i = 1; i < parts.Count; i++)
            result = CallHelper("std.string.concat", expr.Span, result, parts[i]);
        return result;
    }

    /// <summary>A hole with a format spec: <c>{avg:N2}</c> becomes <c>std.fmt.formatFloat(avg, "N2")</c>.
    ///
    /// <para>The spec is a LITERAL and is passed as a constant rather than as part of the function name.
    /// Otherwise every spec would need its own import declaration, and <c>{x:N2}</c> and <c>{x:N3}</c>
    /// would be two different functions.</para>
    ///
    /// <para>Without a spec the <c>fromXxx</c> converters remain: a format call that only rebuilds the
    /// default would be a second route to the same result.</para></summary>
    private TempId FormattedValue(Expr expr, string spec)
    {
        var value = LowerExpr(expr);
        var type = TypeOfExpr(expr);
        if (type is not IrScalarType scalar)
            throw NotSupported("formatting a non-scalar value", expr.Span);

        var stringType = new IrScalarType(IrScalar.String);
        var specValue = EmitConst(new StringConst(spec), stringType, expr.Span);

        var helper = scalar.Kind switch
        {
            IrScalar.String => "std.fmt.formatString",
            IrScalar.Bool => "std.fmt.formatBool",
            IrScalar.Char => "std.fmt.formatChar",
            IrScalar.F32 or IrScalar.F64 => "std.fmt.formatFloat",
            _ when IsUnsignedScalar(scalar.Kind) => "std.fmt.formatUint",
            _ when IsIntegerScalar(scalar.Kind) => "std.fmt.formatInt",
            _ => throw NotSupported("formatting a non-scalar value", expr.Span),
        };

        return CallHelper(helper, expr.Span, WidenForHelper(value, scalar.Kind, expr.Span), specValue);
    }

    /// <summary>A hole in an f-string as a string. Strings stay as they are; everything else goes through
    /// the matching converter — the names distinguish by source type, because Lyric has no
    /// Overloading hat.</summary>
    private TempId ToStringValue(Expr expr)
    {
        var value = LowerExpr(expr);
        var type = TypeOfExpr(expr);
        if (type is not IrScalarType scalar)
            throw NotSupported($"interpolating a non-scalar value", expr.Span);

        return scalar.Kind switch
        {
            IrScalar.String => value,
            IrScalar.Bool => CallHelper("std.string.fromBool", expr.Span, value),
            IrScalar.Char => CallHelper("std.string.fromChar", expr.Span, value),
            IrScalar.F32 or IrScalar.F64 => CallHelper("std.string.fromFloat", expr.Span,
                WidenForHelper(value, scalar.Kind, expr.Span)),
            // Unsigned first: 'fromInt' reinterprets a large uint as a negative number. Measured,
            // f"{u}" with u = uint64.MaxValue previously yielded "-1".
            _ when IsUnsignedScalar(scalar.Kind) => CallHelper("std.string.fromUint", expr.Span,
                WidenForHelper(value, scalar.Kind, expr.Span)),
            _ when IsIntegerScalar(scalar.Kind) => CallHelper("std.string.fromInt", expr.Span,
                WidenForHelper(value, scalar.Kind, expr.Span)),
            _ => throw NotSupported($"interpolating a non-scalar value", expr.Span),
        };
    }

    /// <summary>
    /// Widens a scalar to the signature of its converter: integers to <c>i64</c>, <c>f32</c> to
    /// <c>f64</c>.
    ///
    /// <para>The converters in <c>std.string</c> and <c>std.fmt</c> are called <c>fromInt</c> and
    /// <c>fromFloat</c>, singular, because Lyric has no overloading. There is therefore exactly ONE
    /// signature per kind, and it takes the widest type. Whoever passes an <c>int8</c> has to widen it
    /// first.</para>
    ///
    /// <para>Without this step <c>f"{x}"</c> with <c>x: int8</c> crashes the compiler in the IR verifier
    /// ("arg 0 is i8, expected i64") — with a stack trace instead of a diagnostic, and for every type
    /// except <c>int</c> and <c>float</c>.</para>
    ///
    /// <para>KNOWN LIMIT: a <c>uint</c> beyond <c>int64.MaxValue</c> is printed as a negative number, the
    /// bit-pattern cast to <c>i64</c> reinterpreting it. That is not a crash but wrong output, and the fix
    /// would be a <c>fromUint</c> of its own.</para>
    /// </summary>
    private TempId WidenForHelper(TempId value, IrScalar kind, Span span)
    {
        var widened = kind switch
        {
            IrScalar.F32 => IrScalar.F64,
            // A uint8 becomes u64 rather than i64: the intermediate step through a signed type would
            // reinterpret the top bit.
            _ when IsUnsignedScalar(kind) => IrScalar.U64,
            _ when IsIntegerScalar(kind) => IrScalar.I64,
            _ => kind,
        };

        if (widened == kind) return value;

        var from = new IrScalarType(kind);
        var to = new IrScalarType(widened);
        var dest = _slots.NewTemp(to);
        _b.Emit(new Lyric.Ir.Convert(dest, from, to, value, span));
        return dest;
    }

    // ------------------------------------------------------------------ helpers

    private TempId EmitConst(IrConstValue value, IrType type, Span span)
    {
        var dest = _slots.NewTemp(type);
        _b.Emit(new Const(dest, type, value, span));
        return dest;
    }

    /// <summary>
    /// Does this assignment target point at a CAPTURED CELL? It then lies not in a slot of this function
    /// but in a field of its environment, and the write has to go there, or the closure would write into
    /// a copy and the semantics would silently be by-value.
    /// </summary>
    private bool TryCapturedCell(Expr target, out TempId cell, out TypeId cellType,
        out IrType valueType)
    {
        cell = default; cellType = default; valueType = VoidType;

        if (target is not IdentifierExpr identifier) return false;
        if (_types.RefOf(identifier) is not { } symbol) return false;
        if (_slots.TryLookup(symbol, out _)) return false;
        if (!_captureFields.ContainsKey(symbol)) return false;

        var (type, value) = LoadCaptured(symbol, target.Span);
        if (type is not IrRefType reference || !_typeTable.IsCell(reference.Type))
            throw Bug($"assignment to captured '{identifier.Name}', which is not a cell — the " +
                      "sema should have boxed it (ADR-018) or rejected the assignment");

        cell = value;
        cellType = reference.Type;
        valueType = _typeTable.Defs[reference.Type.Value].FieldTypes[0];
        return true;
    }

    private LocalId ResolveLocalTarget(Expr target, string what)
    {
        if (target is not IdentifierExpr identifier)
            throw NotSupported($"{what} target (only parameters and locals)", target.Span);

        var symbol = _types.RefOf(identifier) ?? throw Bug($"identifier '{identifier.Name}' is unbound");
        if (!_slots.TryLookup(symbol, out var slot))
            throw NotSupported($"{what} of '{identifier.Name}'", target.Span);

        return slot;
    }

    private static IrConstValue OneFor(IrType type, Span span) => type switch
    {
        IrScalarType { Kind: IrScalar.F32 or IrScalar.F64 } => new FloatConst(1.0),
        IrScalarType s when IsIntegerScalar(s.Kind) => new IntConst(1),
        _ => throw NotSupported("increment/decrement on a non-numeric type", span)
    };

    /// <summary>Unsigned: decides which converter turns a value into text.</summary>
    /// <remarks>Without this distinction every integer goes through <c>fromInt</c>, and a <c>uint</c>
    /// beyond <c>int64.MaxValue</c> appears as a negative number. Not a crash but wrong output.</remarks>
    private static bool IsUnsignedScalar(IrScalar kind) => kind is
        IrScalar.U8 or IrScalar.U16 or IrScalar.U32 or IrScalar.U64;

    private static bool IsIntegerScalar(IrScalar kind) => kind is
        IrScalar.I8 or IrScalar.I16 or IrScalar.I32 or IrScalar.I64 or
        IrScalar.U8 or IrScalar.U16 or IrScalar.U32 or IrScalar.U64;

    private static bool IsVoid(IrType type) => type is IrScalarType { Kind: IrScalar.Void };

    private IrType TypeOfExpr(Expr expr) =>
        _chainResults.TryGetValue(expr, out var chained)
            ? chained
            : LowerType(_types.TypeOf(expr), expr.Span);

    /// <summary>
    /// The sema type of a call receiver, unwrapped when it comes from a <c>?.</c> chain.
    ///
    /// <para>The case distinction in <see cref="LowerCall"/> asks the STATIC type of the target, and in
    /// the chain that is <c>?Box</c> rather than <c>Box</c>. Without this place it would find neither the
    /// class nor the generic instance nor the interface.</para>
    /// </summary>
    private LyrType ReceiverType(Expr target) =>
        _chainReceivers.ContainsKey(target) && _types.TypeOf(target) is Sema.Optional option
            ? option.Inner
            : _types.TypeOf(target);

    /// <summary>
    /// A sema type to an IR type. The substitution hook of the monomorphization sits here and nothing
    /// else: the mapping itself lives in <see cref="TypeTable.Lower(Sema.LyrType, Core.Span)"/>.
    ///
    /// <para>Not both here, because both were here once. This method used to be a second, complete copy
    /// of the same mapping and, like every second answer to the same question, drifted from the first:
    /// <c>T[]</c> and <c>?T</c> stood here and were missing there, which made a module <c>let</c> with an
    /// array untranslatable while the same expression worked inside a function. The difference is now the
    /// substitution alone.</para>
    /// </summary>
    /// <summary>The type of a place that holds a value of the type — a parameter, a local: the
    /// <see cref="TypeTable.Unit"/> for <c>void</c>, which an instance binds a type parameter to
    /// (03 §9.1).</summary>
    private IrType LowerValueType(LyrType type, Span span) =>
        SubstituteType(type) is PrimitiveType { Kind: PrimitiveKind.Void } ? TypeTable.Unit : LowerType(type, span);

    private IrType LowerType(LyrType type, Span span)
    {
        // Substitute first, then map. Recursively, so 'Box<T>', '?T' and 'T[]' see the instance's
        // arguments too: the TypeTable does not know this function's substitution.
        var concrete = SubstituteType(type);

        // A type parameter the own substitution does not know: that is the boundary of the
        // monomorphization and belongs reported here, where the name is still known.
        if (concrete is TypeParamType parameter)
            throw NotSupported($"type parameter '{parameter.Param.Name}'", span);

        return _typeTable.Lower(concrete, span);
    }

    /// <summary>The return type comes from the syntactic <see cref="TypeNode"/>, because the sema does
    /// not put it into <see cref="TypeResult"/>. The resolution lives in the <see cref="TypeTable"/>, the
    /// same place that resolves field and parameter types, so a factory <c>static fn new(): P</c> yields
    /// the same type as a field of type <c>P</c>.</summary>
    /// <summary>Substitutes the type arguments of this instance into a type, recursively, because an
    /// argument may itself be composite (<c>Box&lt;T[]&gt;</c>).</summary>
    /// <summary>
    /// The id under which a function is callable. Two sources, and the order matters: written functions
    /// have their id from pass 1, an extension method gets it ONLY HERE, at its first call.
    ///
    /// <para>That is the point: an extension that is never called does not reach the bytecode. Without
    /// the distinction every program carries the five Display extensions from <c>std.core</c>, because
    /// that module is always loaded.</para>
    /// </summary>
    /// <summary>
    /// Did the sema bind this member to a visible EXTENSION method?
    ///
    /// <para>§5.4 fixes the order as own member, then extension, then a default of a conformed
    /// interface. The sema follows it; the lowering asked only whether the concrete TYPE carried
    /// the member, so an extension on a type whose interface supplies the same name as a default
    /// was never called directly — the receiver was lifted and dispatched virtually, and the
    /// vtable row found the default. Asking what was BOUND is asking the question once.</para>
    /// </summary>
    private bool BoundToExtension(MemberExpr member) =>
        _types.RefOf(member) is FunctionSymbol bound
        && _typeTable.ExtensionOwnerOf(bound) is not null;

    private bool TryResolveFunction(FunctionSymbol symbol, out FunctionId id)
    {
        if (_functions.TryGetValue(symbol, out id)) return true;
        if (_typeTable.Extensions is not { } table) return false;
        if (table.TryGet(symbol, out id)) return true;

        if (_typeTable.ExtensionOwnerOf(symbol) is not { } owner) return false;
        if (symbol.Declaration is not FunctionDecl decl || decl.Body is null) return false;
        if (decl.Generics.Length > 0) return false;

        id = table.Request(symbol, decl, owner.Module, owner.Target,
            decl.IsStatic ? null : owner.Target,
            decl.IsStatic ? null : owner.TargetNode);
        return true;
    }

    /// <summary>
    /// Substitutes the type arguments of this instance everywhere a type parameter stands.
    ///
    /// <para>BEING COMPLETE IS NOT OPTIONAL HERE. This function was a partial copy three times — first
    /// <c>?T</c> was missing, then <c>T[]</c>, then <c>Box&lt;T&gt;</c>, and every time the error looked
    /// like a new one. Whoever adds a type constructor here adds it here too: an unsubstituted parameter
    /// otherwise arrives as "unsubstituted" in the <see cref="TypeTable"/>, far from its cause.</para>
    /// </summary>
    private LyrType SubstituteType(LyrType type)
    {
        StackGuard.Check("substituting a type");
        return SubstituteTypeOnce(type);
    }

    private LyrType SubstituteTypeOnce(LyrType type) => type switch
    {
        TypeParamType p when _substitution.TryGetValue(p.Param, out var bound) => bound,
        ArrayOf a => new ArrayOf(SubstituteType(a.Element)),
        SliceOf s => new SliceOf(SubstituteType(s.Element)),
        InlineArrayOf ia => new InlineArrayOf(SubstituteType(ia.Element), ia.Length),
        RangeOf r => new RangeOf(SubstituteType(r.Element)),
        Optional o => new Optional(SubstituteType(o.Inner)),
        Sema.TupleOf t => new Sema.TupleOf(t.Elements.Select(SubstituteType).ToArray()) { Labels = t.Labels },
        FnType f => new FnType(
            f.Parameters.Select(SubstituteType).ToArray(), SubstituteType(f.Return)) { Throws = ThrownHere(f.Throws), Places = f.Places },
        CoroutineOf c => c with
        {
            Yield = SubstituteType(c.Yield),
            Result = SubstituteType(c.Result),
            Throws = c.Throws is { } thrown ? SubstituteType(thrown) : null,
        },
        GenericInstance g => TypeChecker.ReduceJoin(new GenericInstance(g.Definition,
            g.Arguments.Select(SubstituteType).ToArray()) { Fixations = g.Fixations }),
        AssocOf a => TypeChecker.ResolveAssociated(SubstituteType(a.Base), a.Member),
        _ => type,
    };

    private static Core.Span SpanOfDecl(FunctionDecl decl) => decl.Span;

    private IrType LowerDeclaredReturnType()
    {
        if (_decl!.ReturnType is null) return VoidType;

        // In a monomorphized instance the written return type may be a type parameter ('fn id<T>(x: T): T')
        // or contain one ('fn next(): ?T'). The parameters go through the LyrType path and meet the
        // substitution there; the return type comes syntactically and has to meet it here, or the type
        // table would look for a class named 'T'.
        return _substitution.Count > 0
            ? LowerSubstituted(_decl.ReturnType)
            : _typeTable.Lower(_decl.ReturnType);
    }

    /// <summary>
    /// Lowers a written type with the substitution of this instance, RECURSIVELY, because a type
    /// parameter can sit deep: <c>?T</c>, <c>T[]</c>.
    ///
    /// <para>Handling only the bare case sufficed for <c>fn get(): T</c> and fell over at
    /// <c>fn next(): ?T</c> — which is the signature of every iterator.</para>
    /// </summary>
    private IrType LowerSubstituted(TypeNode node)
    {
        // The substitution goes to the TYPE TABLE rather than rebuilding a second resolution here. The
        // table can do it completely — it uses the same stack when lowering the members of a generic
        // instance. It only had to learn that a substitution applies here.
        using var scope = _typeTable.PushSubstitution(NamedSubstitution());
        return _typeTable.Lower(node);
    }

    /// <summary>
    /// The type arguments of a call site as a name map for the type table.
    /// </summary>
    /// <remarks>A type argument may itself be a type parameter of the CALLING function
    /// (<c>wrap&lt;T&gt;</c> calls <c>id&lt;T&gt;</c>), so every one goes through the own substitution
    /// before it enters the map.</remarks>
    private Dictionary<string, LyrType> NamedSubstitutionFor(
        FunctionSymbol callee, IReadOnlyList<LyrType> typeArguments)
    {
        var mapping = new Dictionary<string, LyrType>(StringComparer.Ordinal);
        var n = Math.Min(callee.Generics.Length, typeArguments.Count);

        for (var i = 0; i < n; i++)
            mapping[callee.Generics[i].Name] = SubstituteType(typeArguments[i]);

        return mapping;
    }

    /// <summary>The substitution of this instance with names as keys, the form in which the type table
    /// keeps it. It knows no <see cref="GenericParamSymbol"/>, because it resolves written types, where
    /// only names stand.</summary>
    private Dictionary<string, LyrType> NamedSubstitution()
    {
        var mapping = new Dictionary<string, LyrType>(StringComparer.Ordinal);
        foreach (var (parameter, bound) in _substitution) mapping[parameter.Name] = bound;
        return mapping;
    }

    /// <summary>A scope boundary: valid Lyric for which the backend part is still missing. Turned by
    /// <see cref="ModuleLowerer"/> into a <c>LYR-IR0001</c> diagnostic with file, line and column, so no
    /// position is written into the text here; the DiagnosticEngine renders it. The text names the
    /// construct and nothing else — the category is a note the report attaches.</summary>
    private static UnsupportedConstructException NotSupported(string what, Span span,
        string? note = null) => new(what, span, note);

    /// <summary>An internal inconsistency: the compiler is broken, not the source.</summary>
    private InternalCompilationException Bug(string message) =>
        new($"lowering: {message} (in '{_name}')");
}
