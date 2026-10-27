using Lyric.AST;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>
/// The side table of type checking: the type of every expression plus the resolved symbols of
/// expression references (identifier to local, parameter, global, function, …). Like
/// <see cref="BindingResult"/> it leaves the AST immutable.
/// </summary>
public sealed class TypeResult
{
    /// <summary>
    /// Whether the check gave up because a construct nested deeper than it walks (§12.4).
    ///
    /// <para>Read by <c>Semantics.Analyze</c>, which then runs none of the walkers behind the
    /// checker: they recurse over the same tree, so the diagnostic would be reported correctly and
    /// the process would die in the next pass anyway. A flag rather than a search of the
    /// diagnostics for the code, because "did this analysis finish" is a fact about the analysis.
    /// </para>
    /// </summary>
    public bool NestingExceeded { get; internal set; }

    private readonly Dictionary<Expr, LyrType> _types = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Node, Symbol> _refs = new(ReferenceEqualityComparer.Instance);

    /// <summary>The type of each module <c>let</c> and <c>static let</c>. The TypeChecker fills it,
    /// the lowering reads it: a global has no expression its type could hang on.</summary>
    private readonly Dictionary<GlobalSymbol, LyrType> _globals =
        new(ReferenceEqualityComparer.Instance);

    public void BindGlobal(GlobalSymbol symbol, LyrType type) => _globals[symbol] = type;
    private readonly HashSet<Node> _exhaustiveMatches = new(ReferenceEqualityComparer.Instance);

    public void SetType(Expr expr, LyrType type) => _types[expr] = type;

    /// <summary>
    /// Every <c>comptime</c> expression the checker accepted, in the order it met them. The
    /// order is the identity: site <c>i</c> becomes the evaluator's function
    /// <c>&lt;comptime:i&gt;</c> in the first lowering and reads value <c>i</c> in the second,
    /// and both runs check the same sources in the same order.
    /// </summary>
    public SiteList ComptimeSites { get; } = new();

    /// <summary>The program's exports (<c>@Export</c>) in the order the checker met them: each
    /// function with its C symbol and its declaration as the library's header writes it.</summary>
    public List<CExport> Exports { get; } = [];

    /// <summary>The definitions of the <c>@Layout</c> types the exports' declarations name, each
    /// after the types its own fields name.</summary>
    public List<string> CHeaderTypes { get; } = [];

    /// <summary>The quotes of the program, in the order the checker met them, each
    /// with the module it stands in: the lowering numbers them for the interpreter, which builds
    /// the tree, and a free name of the content is that module's.</summary>
    public List<QuoteExpr> Quotes { get; } = [];
    private readonly Dictionary<QuoteExpr, string> _quoteModule = new(ReferenceEqualityComparer.Instance);
    public void NoteQuote(QuoteExpr quote, string module)
    {
        if (_quoteModule.TryAdd(quote, module)) Quotes.Add(quote);
    }
    public string QuoteModule(QuoteExpr quote) => _quoteModule[quote];

    private readonly Dictionary<AttributeNode, ComptimeExpr> _attributeValues = new(ReferenceEqualityComparer.Instance);

    /// <summary>A Lyric 5 attribute's value: the struct it writes, as the
    /// <c>comptime</c> site the checker made of it — one of <see cref="ComptimeSites"/>, whose
    /// evaluated constant holds every field, written or defaulted.</summary>
    public ComptimeExpr? AttributeValueOf(AttributeNode attribute) => _attributeValues.GetValueOrDefault(attribute);

    internal void SetAttributeValue(AttributeNode attribute, ComptimeExpr value) => _attributeValues[attribute] = value;

    /// <summary>Every typed expression with its type. The consumer is the test that checks the
    /// <see cref="ErrorType"/> invariant; without an enumeration it could only be checked where
    /// someone already looks.</summary>
    public IEnumerable<KeyValuePair<Expr, LyrType>> AllTypes => _types;
    public LyrType TypeOf(Expr expr) => _types.TryGetValue(expr, out var t) ? t : LyrType.Error;

    /// <summary>Every type an expression of the program has: what a value of an interface can hold.</summary>
    public IEnumerable<LyrType> ExpressionTypes => _types.Values;

    public void BindRef(Node node, Symbol symbol) => _refs[node] = symbol;

    /// <summary>The parameters <c>@callerExpr</c> sits on, each with the
    /// parameter whose argument's text it gives, and where that text comes from.</summary>
    private readonly Dictionary<Param, string> _callerExpr = new(ReferenceEqualityComparer.Instance);

    public void BindCallerExpr(Param parameter, string target) => _callerExpr[parameter] = target;

    private readonly HashSet<Expr> _cstrArguments = new(ReferenceEqualityComparer.Instance);

    /// <summary>A <c>string</c> argument of a C function's <c>CStr</c> parameter: it crosses as
    /// its bytes, without a copy.</summary>
    public bool IsCStrArgument(Expr argument) => _cstrArguments.Contains(argument);
    internal void MarkCStrArgument(Expr argument) => _cstrArguments.Add(argument);

    private readonly HashSet<Expr> _cCallbacks = new(ReferenceEqualityComparer.Instance);

    /// <summary>A function — named, or a lambda that captures nothing — where a C function's
    /// pointer is expected: C calls it through a trampoline.</summary>
    public bool IsCCallback(Expr source) => _cCallbacks.Contains(source);
    internal void MarkCCallback(Expr source) => _cCallbacks.Add(source);

    private readonly Dictionary<CallExpr, int> _callbackUserdata = new(ReferenceEqualityComparer.Instance);

    /// <summary><c>Callback&lt;C&gt;.new(closure)</c>: which of <c>C</c>'s parameters is the
    /// userdata — its first or its last —, or <c>null</c> for any other call.</summary>
    public int? CallbackUserdataOf(CallExpr call) => _callbackUserdata.TryGetValue(call, out var at) ? at : null;
    internal void SetCallbackUserdata(CallExpr call, int at) => _callbackUserdata[call] = at;

    /// <summary>The parameter whose argument's text <paramref name="parameter"/> takes, or
    /// <c>null</c> where no <c>@callerExpr</c> sits on it.</summary>
    public string? CallerExprOf(Param parameter) => _callerExpr.GetValueOrDefault(parameter);

    /// <summary>The parameters <c>@callerLine</c> or <c>@callerFile</c> sits on, each with
    /// what of the call's place it takes.</summary>
    private readonly Dictionary<Param, CallerPlace> _callerPlace = new(ReferenceEqualityComparer.Instance);

    public void BindCallerPlace(Param parameter, CallerPlace place) => _callerPlace[parameter] = place;

    /// <summary>What of the call's place <paramref name="parameter"/> takes, or <c>null</c> where
    /// neither <c>@callerLine</c> nor <c>@callerFile</c> sits on it.</summary>
    public CallerPlace? CallerPlaceOf(Param parameter) =>
        _callerPlace.TryGetValue(parameter, out var place) ? place : null;

    /// <summary>The source text of a span — what a call wrote —, for the lowering.</summary>
    public Func<Lyric.Core.Span, string>? SourceText { get; internal set; }

    /// <summary>Where a span begins, as the binary names it: the file
    /// as a backtrace has it, and the line. The compiler sets it before it lowers.</summary>
    public Func<Lyric.Core.Span, (string File, int Line)>? SourcePlace { get; internal set; }

    /// <summary>The block a call through a constraint reaches on a concrete type where no symbol
    /// of the type holds the conformance (05 §13 rules 6, 8), with the block's method of the
    /// member's name — the sema's answer at the instance the lowering reached. Given the instance
    /// the constraint asks for (<c>Into&lt;Centimeters&gt;</c>), it binds the parameters only the
    /// block's conformance names (rule 9): <c>Open</c>, empty where there are none.</summary>
    public Func<LyrType, FunctionSymbol, LyrType?, (ExtensionBlock Block, FunctionSymbol? Method, IReadOnlyDictionary<GenericParamSymbol, LyrType> Open)?>? ConformanceBlock { get; internal set; }

    /// <summary>The parameters of a block only its conformance names (05 §13 rule 9), as a call of
    /// its member bound them — <c>U</c> of <c>m.into()</c> by the type the position expects. The
    /// lowering adds them to what the receiver binds.</summary>
    private readonly Dictionary<Node, IReadOnlyDictionary<GenericParamSymbol, LyrType>> _openBlockArguments = new(ReferenceEqualityComparer.Instance);

    public void SetOpenBlockArguments(Node call, IReadOnlyDictionary<GenericParamSymbol, LyrType> bound) => _openBlockArguments[call] = bound;

    public IReadOnlyDictionary<GenericParamSymbol, LyrType>? OpenBlockArgumentsOf(Node call) => _openBlockArguments.GetValueOrDefault(call);

    /// <summary>The instance of a generic interface a member reached through a type parameter's
    /// constraint belongs to — <c>Into&lt;Centimeters&gt;</c> of <c>t.into()</c> under
    /// <c>T :: [Into&lt;Centimeters&gt;]</c> —, in the generic body's terms: the lowering asks the
    /// block that gives it at the instance for exactly that one (05 §13 rule 9).</summary>
    private readonly Dictionary<MemberExpr, LyrType> _constraintInstances = new(ReferenceEqualityComparer.Instance);

    public void RecordConstraintInstance(MemberExpr member, LyrType instance) => _constraintInstances[member] = instance;

    public LyrType? ConstraintInstanceOf(MemberExpr member) => _constraintInstances.GetValueOrDefault(member);

    /// <summary>Whether a type conforms to an interface — the sema's answer at an instance the
    /// lowering reached, for a question the checked code could not settle: is the concrete
    /// iterator of a generic loop Closeable.</summary>
    public Func<LyrType, TypeSymbol, bool>? Satisfies { get; internal set; }

    /// <summary>Whether an interface can be the type of a value — only such a one has a
    /// table, and only its defaults are lowered once for it.</summary>
    public Func<TypeSymbol, bool>? ValueInterface { get; internal set; }

    /// <summary>Whether the program is Lyric 5's: there a generic default of an
    /// interface is instantiated per conformer, and a conformer's own may stand in its place. The
    /// 4.x path keeps a generic default for the interface's value until it leaves main.</summary>
    public bool Lyric5Modules { get; internal set; }

    /// <summary>
    /// Which function satisfied which conformance: keyed by (implementing type, interface,
    /// member), holding one entry per INSTANCE.
    ///
    /// <para>Recorded because since 3.0 the answer is not readable from the name. A type may
    /// conform to one interface twice — <c>Equatable&lt;Tag&gt;</c> and <c>Equatable&lt;int&gt;</c>
    /// — and satisfy the two with two overloaded <c>equals</c>. The vtable rows are per instance
    /// and each needs ITS one; resolving by name gives both rows the first, which is a silent
    /// wrong call. The conformance check has already compared the signatures, so the lowering
    /// reads its answer instead of comparing again.</para>
    /// </summary>
    private readonly Dictionary<(TypeSymbol, TypeSymbol, string), List<(LyrType Instance, FunctionSymbol Impl)>>
        _conformanceImpls = new();

    // The conformances a GENERIC block gave, as the sema proved them at a use: the
    // instance, the interface, the block. A row in the lowering's tables is built for exactly
    // these, since a block's constraints may hold for one instance and not for another.
    private readonly List<(LyrType Instance, TypeSymbol Iface, ExtensionBlock Block)> _blockConformances = new();

    public void RecordBlockConformance(LyrType instance, TypeSymbol iface, ExtensionBlock block)
    {
        if (!BlockConformanceRecorded(instance, iface, block)) _blockConformances.Add((instance, iface, block));
    }

    public bool BlockConformanceRecorded(LyrType instance, TypeSymbol iface, ExtensionBlock block) =>
        _blockConformances.Any(e => ReferenceEquals(e.Block, block) && ReferenceEquals(e.Iface, iface) && LyrType.Equal(e.Instance, instance));

    /// <summary>The conformances a block on a shape or a blanket block gave a shape (05 §13 rules 6,
    /// 8), as the sema proved them: a value of the interface is made from the shape in a box.</summary>
    public IEnumerable<(LyrType Shape, TypeSymbol Iface, ExtensionBlock Block)> ShapeConformances =>
        _blockConformances.Where(e => (e.Block.IsConstructorTarget || e.Block.IsBlanketTarget)
            && e.Instance is ArrayOf or SliceOf or InlineArrayOf or Optional or TupleOf or FnType
            && !TypeChecker.MentionsTypeParam(e.Instance));

    /// <summary>The blanket block that gives a built-in type the interface, and the type as the sema
    /// recorded it — what a box of the built-in's value is a value of.</summary>
    public (ExtensionBlock Block, LyrType Instance)? BlanketConformanceOfBuiltin(TypeSymbol builtin, TypeSymbol iface) =>
        _blockConformances.Where(e => e.Block.IsBlanketTarget && ReferenceEquals(e.Iface, iface)
                && e.Instance is PrimitiveType or StringViewType && TypeFacts.Display(e.Instance) == builtin.Name)
            .Select(e => ((ExtensionBlock, LyrType)?)(e.Block, e.Instance)).FirstOrDefault();

    /// <summary>The blanket block that gives <paramref name="instance"/> the interface, as the sema
    /// proved it (05 §13 rule 8): a value of the interface is made through it.</summary>
    public ExtensionBlock? BlanketConformanceOf(LyrType instance, TypeSymbol iface) =>
        _blockConformances.FirstOrDefault(e => e.Block.IsBlanketTarget && ReferenceEquals(e.Iface, iface)
            && LyrType.Equal(e.Instance, instance)).Block;

    /// <summary>A generic member as the witness of a plain interface member: its
    /// own type arguments as the conformance to that interface instance bound them —
    /// <c>contains&lt;P&gt;</c> for <c>Contains&lt;char&gt;</c> is <c>contains&lt;char&gt;</c>.</summary>
    private readonly List<(FunctionSymbol Witness, LyrType Instance, LyrType[] Arguments)> _witnessArguments = new();

    public void RecordWitnessArguments(FunctionSymbol witness, LyrType instance, LyrType[] arguments)
    {
        if (!_witnessArguments.Any(w => ReferenceEquals(w.Witness, witness) && LyrType.Equal(w.Instance, instance)))
            _witnessArguments.Add((witness, instance, arguments));
    }

    /// <summary>The witness's type arguments for the interface instance; for the interface alone,
    /// where one conformance names it, that one's.</summary>
    public LyrType[]? WitnessArgumentsOf(FunctionSymbol witness, LyrType? instance)
    {
        foreach (var w in _witnessArguments)
            if (ReferenceEquals(w.Witness, witness) && (instance is null || LyrType.Equal(w.Instance, instance)))
                return w.Arguments;
        return null;
    }

    public void RecordConformanceImpl(TypeSymbol implementer, TypeSymbol iface, string member,
        LyrType instance, FunctionSymbol impl)
    {
        var key = (implementer, iface, member);
        if (!_conformanceImpls.TryGetValue(key, out var entries))
            _conformanceImpls[key] = entries = new List<(LyrType, FunctionSymbol)>();
        if (!entries.Any(e => LyrType.Equal(e.Instance, instance))) entries.Add((instance, impl));
    }

    /// <summary><c>:: [Walker by legs]</c>: the field a type delegates an interface's
    /// members to — the interface and every parent of it. The lowering builds the forwarders
    /// from it; the checker answers member lookups with it.</summary>
    private readonly Dictionary<(TypeSymbol, TypeSymbol), (string Field, FieldSymbol Symbol)> _delegations = new();

    public void RecordDelegation(TypeSymbol implementer, TypeSymbol iface, string field, FieldSymbol symbol) =>
        _delegations[(implementer, iface)] = (field, symbol);

    public string? DelegationOf(TypeSymbol implementer, TypeSymbol iface) =>
        _delegations.TryGetValue((implementer, iface), out var d) ? d.Field : null;

    public IEnumerable<(TypeSymbol Interface, string Field)> DelegationsOf(TypeSymbol implementer) =>
        _delegations.Where(e => ReferenceEquals(e.Key.Item1, implementer)).Select(e => (e.Key.Item2, e.Value.Field));

    /// <summary>The implementation for one conformance, or <c>null</c> when the name was never
    /// ambiguous and the ordinary lookup answers just as well.</summary>
    public FunctionSymbol? ConformanceImpl(TypeSymbol implementer, TypeSymbol iface, string member,
        LyrType? instance)
    {
        if (!_conformanceImpls.TryGetValue((implementer, iface, member), out var entries))
            return null;
        if (entries.Count == 1) return entries[0].Impl;
        if (instance is null) return null;

        foreach (var entry in entries)
            if (LyrType.Equal(entry.Instance, instance))
                return entry.Impl;
        return null;
    }

    /// <summary>The pulls — a <c>resume</c> or a <c>next()</c> — whose coroutine may throw.
    ///
    /// <para>Recorded by the checker because it is the pass that knows the receiver's TYPE, and
    /// read by the exception analysis, which is the pass that knows what handles it. The throw
    /// site of a coroutine is the pull, never the call: a call builds a suspended frame and runs
    /// nothing.</para></summary>
    private readonly Dictionary<Node, LyrType> _throwingPulls = new(ReferenceEqualityComparer.Instance);

    public void MarkThrowingPull(Node pull, LyrType thrown) => _throwingPulls[pull] = thrown;

    /// <summary>The yields of coroutine bodies: where <c>close()</c> finds the coroutine
    /// suspended, such a yield throws <c>Cancelled</c>. Recorded by the checker, which knows whose
    /// body a yield stands in — a lambda's yield in a body is the other, dynamic kind.</summary>
    private readonly HashSet<YieldStmt> _bodyYields = new(ReferenceEqualityComparer.Instance);

    public void MarkBodyYield(YieldStmt yield) => _bodyYields.Add(yield);

    public bool IsBodyYield(YieldStmt yield) => _bodyYields.Contains(yield);

    /// <summary>The generator lambdas: a lambda with a yield of its own, of type
    /// <c>fn(…) -> Coroutine&lt;Y, R&gt;</c>, lowered as a coroutine's factory and body.</summary>
    private readonly HashSet<LambdaExpr> _generatorLambdas = new(ReferenceEqualityComparer.Instance);

    public void MarkGeneratorLambda(LambdaExpr lambda) => _generatorLambdas.Add(lambda);

    /// <summary>The arguments a call spreads over its variadic parameter, <c>f(xs...)</c>
    /// (08 §1.1 rule 1): checked to be the rest, alone, and of the parameter's array type — the
    /// lowering hands such an argument on as the array it is.</summary>
    private readonly HashSet<Expr> _spreads = new(ReferenceEqualityComparer.Instance);

    public void MarkSpread(Expr argument) => _spreads.Add(argument);

    public bool IsSpread(Expr argument) => _spreads.Contains(argument);

    public bool IsGeneratorLambda(LambdaExpr lambda) => _generatorLambdas.Contains(lambda);

    public LyrType? ThrownByPull(Node pull) =>
        _throwingPulls.TryGetValue(pull, out var t) ? t : null;

    /// <summary>The members a clause's binding answers of its record, <c>e.suppressed()</c> and
    /// <c>e.backtrace()</c>: the member, and the binding whose record the
    /// lowering keeps for the clause and reads.</summary>
    private readonly Dictionary<MemberExpr, LocalSymbol> _recordQueries = new(ReferenceEqualityComparer.Instance);

    public void MarkRecordQuery(MemberExpr member, LocalSymbol binding) => _recordQueries[member] = binding;

    public LocalSymbol? RecordQueryOf(MemberExpr member) => _recordQueries.GetValueOrDefault(member);

    // --- errors ---------------------------------------------------------

    private readonly Dictionary<Node, LyrType[]> _declaredThrows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CallExpr, LyrType[]> _callThrows = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CatchClause, LyrType> _catchTypes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CatchClause, LyrType[]> _catchSets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypePattern, LyrType> _typesTested = new(ReferenceEqualityComparer.Instance);

    /// <summary>The thrown set a function declares, resolved — the bare <c>throws</c> as
    /// <c>Error</c>; empty when it throws nothing. What its BODY may throw: for a coroutine
    /// function that is what its pulls throw, not its call.</summary>
    public void RecordDeclaredThrows(FunctionDecl fn, LyrType[] set) => _declaredThrows[fn] = set;

    public LyrType[] DeclaredThrows(FunctionDecl fn) => _declaredThrows.GetValueOrDefault(fn) ?? [];

    /// <summary>The program's <c>main</c> throws <paramref name="thrown"/> although it does not
    /// say so (<c>Cancelled</c>, which it covers itself): from here on its set holds
    /// it, and its call may throw — what the lowering asks for the error slot.</summary>
    public void RecordEntryThrows(FunctionDecl main, LyrType thrown)
    {
        var declared = DeclaredThrows(main);
        if (!declared.Any(t => LyrType.Equal(t, thrown))) _declaredThrows[main] = [.. declared, thrown];
        _throwsAtCall.Add(main);
    }

    private readonly HashSet<FunctionDecl> _throwsAtCall = new(ReferenceEqualityComparer.Instance);

    public void RecordThrowsAtCall(FunctionDecl fn) => _throwsAtCall.Add(fn);

    /// <summary>Whether a CALL of the function may throw: its set is non-empty and it is no
    /// coroutine function, whose clause belongs to its pulls. What a back end gives the caller's
    /// error slot.</summary>
    public bool ThrowsAtCall(FunctionDecl fn) => _throwsAtCall.Contains(fn);

    /// <summary>What a call may throw, in its instance's terms — the checker's settled callee,
    /// overloads and generic substitution done. Recorded for throwing calls only.</summary>
    public void RecordCallThrows(CallExpr call, LyrType[] set) => _callThrows[call] = set;

    public LyrType[] CallThrows(CallExpr call) => _callThrows.GetValueOrDefault(call) ?? [];

    /// <summary>The type a typed <c>catch</c> clause names, resolved.</summary>
    public void RecordCatchType(CatchClause clause, LyrType type) => _catchTypes[clause] = type;

    public LyrType? CatchType(CatchClause clause) => _catchTypes.GetValueOrDefault(clause);

    /// <summary>
    /// The set a catch binding carries: the types of a set clause, or
    /// what reaches a clause without a type. A rethrow of the binding throws exactly it, a match
    /// over the binding is exhaustive without a default; stored, the binding is an <c>Error</c>.
    /// </summary>
    public void RecordCatchSet(CatchClause clause, LyrType[] set) => _catchSets[clause] = set;

    public LyrType[]? CatchSet(CatchClause clause) => _catchSets.GetValueOrDefault(clause);

    /// <summary>The type a type pattern tests, resolved — named or not.</summary>
    public void RecordTypeTested(TypePattern pattern, LyrType type) => _typesTested[pattern] = type;

    public LyrType? TypeTested(TypePattern pattern) => _typesTested.GetValueOrDefault(pattern);

    private readonly Dictionary<TypeSetPattern, LyrType[]> _typeSets = new(ReferenceEqualityComparer.Instance);

    /// <summary>The types a type-set pattern tests, resolved — the set its binding carries.</summary>
    public void RecordTypeSet(TypeSetPattern pattern, LyrType[] set) => _typeSets[pattern] = set;

    public LyrType[]? TypeSet(TypeSetPattern pattern) => _typeSets.GetValueOrDefault(pattern);

    private readonly Dictionary<CallExpr, StructInitExpr> _shorthands = new(ReferenceEqualityComparer.Instance);

    /// <summary><c>Point { v }</c>, read as a call with a trailing block, which the checker found to be
    /// the shorthand initializer: checked as that, and lowered as that.</summary>
    public void DesugarShorthand(CallExpr call, StructInitExpr init) => _shorthands[call] = init;

    public StructInitExpr? ShorthandOf(CallExpr call) => _shorthands.GetValueOrDefault(call);

    public Symbol? RefOf(Node node) => _refs.TryGetValue(node, out var s) ? s : null;

    /// <summary>
    /// Every node bound to a symbol, uses and declarations alike.
    ///
    /// <para>Declarations are in here because the definite-assignment analysis needs them: a
    /// <c>BindingStmt</c>, a <c>Param</c>, a <c>ForInStmt</c> and the pattern bindings are each
    /// bound to the symbol they THEMSELVES declare. A consumer asking for uses separates the two by
    /// <c>ReferenceEquals(symbol.Declaration, node)</c> — no flag is needed, the symbol already
    /// knows where it was declared.</para>
    /// </summary>
    public IEnumerable<KeyValuePair<Node, Symbol>> AllReferences => _refs;

    /// <summary>The type of a module <c>let</c> or <c>static let</c>. Separate from
    /// <see cref="TypeOf"/>, because a global is not an expression: its type hangs on the symbol,
    /// not on a use site.</summary>
    public LyrType TypeOfGlobal(GlobalSymbol symbol) =>
        _globals.TryGetValue(symbol, out var t) ? t : LyrType.Error;

    // Exhaustiveness: matches proven by the TypeChecker. Flow and definite-assignment analysis read
    // this without needing type knowledge of their own.
    public void MarkMatchExhaustive(Node match) => _exhaustiveMatches.Add(match);
    public bool IsMatchExhaustive(Node match) => _exhaustiveMatches.Contains(match);

    // Captures: which outer locals, parameters and 'this' a lambda captures implicitly. The consumer
    // is closure lifting.
    private static readonly IReadOnlyList<Symbol> NoCaptures = [];
    private readonly Dictionary<Node, (IReadOnlyList<Symbol> Symbols, bool This)> _captures = new(ReferenceEqualityComparer.Instance);

    public void SetCaptures(Node lambda, IReadOnlyList<Symbol> symbols, bool capturesThis) =>
        _captures[lambda] = (symbols, capturesThis);
    public (IReadOnlyList<Symbol> Symbols, bool CapturesThis) CapturesOf(Node lambda) =>
        _captures.TryGetValue(lambda, out var c) ? c : (NoCaptures, false);

    /// <summary>
    /// Locals a closure SHARES with its enclosing function: they live in a heap cell rather than in a
    /// frame slot.
    ///
    /// <para>A captured <c>var</c> has to be shared: when the closure writes, the function sees it,
    /// and the other way round. A frame slot cannot do that once the frame ends and the closure
    /// lives on.</para>
    ///
    /// <para>Only <c>var</c>. A <c>let</c> and a parameter never change — assigning to a parameter is
    /// <c>LYR-SEM0019</c> — so for them "copy the value" and "share the variable" are
    /// indistinguishable, and the copy is cheaper.</para>
    /// </summary>
    private readonly HashSet<Symbol> _boxed = new(ReferenceEqualityComparer.Instance);

    public void MarkBoxed(Symbol symbol) => _boxed.Add(symbol);

    /// <summary>
    /// The method call an operator expression stands for.
    ///
    /// <para><c>==</c> on an <c>Equatable</c> type IS <c>a.equals(b)</c>: the checker builds that
    /// call from synthetic nodes, checks it through the ordinary member path, and stores it here.
    /// The lowering emits the stored call instead of a <c>BinOp</c> — deriving the method a second
    /// time there would be a second answer to which function an operator means.</para>
    ///
    /// <para>Only the call is stored; what to make of its result follows from the operator on the
    /// node itself. <c>!=</c> negates <c>equals</c>, and the four orderings compare what
    /// <c>compare</c> answered against zero — a stored flag beside the node's own operator would be
    /// a second copy of it.</para>
    ///
    /// <para>The synthetic nodes hang in no tree, so syntax walks never meet them; they reuse the
    /// REAL operand nodes as receiver and argument, which is what makes the stored call lower the
    /// operands exactly once.</para>
    /// </summary>
    private readonly Dictionary<Node, CallExpr> _operatorCalls =
        new(ReferenceEqualityComparer.Instance);

    public void DesugarOperator(Node op, CallExpr call) => _operatorCalls[op] = call;

    /// <summary>A <c>for</c> over Lyric 5's protocol: the two calls the loop
    /// makes and the hidden variable the iterator lives in, checked as written calls are.</summary>
    public sealed record ForInProtocol(CallExpr IterCall, CallExpr NextCall, LocalSymbol Cursor)
    {
        /// <summary>The close of a Closeable iterator, a defer of the loop's own.</summary>
        public DeferStmt? Close { get; init; }

        /// <summary>The close stands for an iterator whose type names a type parameter:
        /// whether it is Closeable is the INSTANCE's to say — the lowering keeps
        /// the close where the concrete iterator is one, and drops it otherwise.</summary>
        public bool CloseIfCloseable { get; init; }
    }

    private readonly Dictionary<ForInStmt, ForInProtocol> _forIns = new(ReferenceEqualityComparer.Instance);

    /// <summary>An f-string written into one builder: the builder's local, the call
    /// that makes it, the calls that append the pieces in order, the call that takes the text.</summary>
    public sealed record InterpolationPlan(LocalSymbol Builder, CallExpr Start, CallExpr[] Pieces, CallExpr Finish);

    private readonly Dictionary<InterpolatedStringExpr, InterpolationPlan> _interpolations = new(ReferenceEqualityComparer.Instance);

    public void RecordInterpolation(InterpolatedStringExpr text, InterpolationPlan plan) => _interpolations[text] = plan;

    public InterpolationPlan? InterpolationOf(InterpolatedStringExpr text) =>
        _interpolations.TryGetValue(text, out var plan) ? plan : null;

    public void RecordForIn(ForInStmt loop, CallExpr iter, CallExpr next, LocalSymbol cursor, DeferStmt? close = null,
        bool closeIfCloseable = false) =>
        _forIns[loop] = new ForInProtocol(iter, next, cursor) { Close = close, CloseIfCloseable = closeIfCloseable };

    public ForInProtocol? ForInOf(ForInStmt loop) => _forIns.TryGetValue(loop, out var p) ? p : null;

    /// <summary>A <c>for</c> over an array, a view or an inline array in Lyric 5: the index loop.</summary>
    private readonly HashSet<ForInStmt> _indexed = new(ReferenceEqualityComparer.Instance);

    public void MarkIndexed(ForInStmt loop) => _indexed.Add(loop);

    public bool IsIndexed(ForInStmt loop) => _indexed.Contains(loop);

    public CallExpr? OperatorCallOf(Node op) => _operatorCalls.GetValueOrDefault(op);

    /// <summary>A range on a type that gives a view of itself (std.core's <c>AsSlice&lt;T&gt;</c>):
    /// <c>list[a..b]</c> checked as <c>list.asSlice()[a..b]</c>, the index the lowering
    /// takes in its place.</summary>
    private readonly Dictionary<IndexExpr, IndexExpr> _rangesThroughViews = new(ReferenceEqualityComparer.Instance);

    public void DesugarRangeThroughView(IndexExpr range, IndexExpr throughView) => _rangesThroughViews[range] = throughView;

    public IndexExpr? RangeThroughViewOf(IndexExpr range) => _rangesThroughViews.GetValueOrDefault(range);

    /// <summary>An array literal that stands for a <c>FromArrayLiteral&lt;T&gt;</c>,
    /// with the element type its elements were checked against: the lowering builds the array of
    /// them and calls the expected type's <c>fromLiteral</c>.</summary>
    private readonly Dictionary<ArrayLitExpr, LyrType> _literalsInto = new(ReferenceEqualityComparer.Instance);

    public void RecordLiteralInto(ArrayLitExpr literal, LyrType element) => _literalsInto[literal] = element;

    public LyrType? LiteralElementOf(ArrayLitExpr literal) => _literalsInto.GetValueOrDefault(literal);

    /// <summary>
    /// The type arguments of a call site, inferred or written.
    ///
    /// <para>The sema derives them anyway to check the call; without storing them here the lowering
    /// would have to run the inference A SECOND TIME to know which instance of <c>id&lt;T&gt;</c> to
    /// call — two truths about the same question, and the second one would have no diagnostics to
    /// speak up with.</para>
    ///
    /// <para>The order is that of the generics declaration, not that of the arguments: it is what
    /// identifies an instance.</para>
    /// </summary>
    private readonly Dictionary<Node, LyrType[]> _typeArguments =
        new(ReferenceEqualityComparer.Instance);

    public void SetTypeArguments(Node call, LyrType[] arguments) =>
        _typeArguments[call] = arguments;

    /// <summary>The arguments of a call that names some of them, in PARAMETER order: the
    /// entry of a parameter the call leaves to its default is <c>null</c>; what overflows into a
    /// <c>params</c> tail follows the fixed parameters. Absent for a call that names nothing,
    /// whose arguments stand in parameter order already.</summary>
    private readonly Dictionary<Node, Expr?[]> _arranged = new(ReferenceEqualityComparer.Instance);

    public void SetArrangedArguments(Node call, Expr?[] arranged) => _arranged[call] = arranged;

    public Expr?[]? ArrangedArgumentsOf(Node call) => _arranged.TryGetValue(call, out var a) ? a : null;

    /// <summary>The type arguments of a call; empty when the callee is not generic.</summary>
    public LyrType[] TypeArgumentsOf(Node call) =>
        _typeArguments.TryGetValue(call, out var args) ? args : [];

    /// <summary>
    /// The types from <c>std.iter</c> that <c>for-in</c> needs.
    ///
    /// <para>The TypeChecker looks them up anyway to check the loop head; storing them here saves the
    /// lowering a second lookup, and two lookups would be two opportunities to find different
    /// symbols.</para>
    /// </summary>
    public TypeSymbol? IteratorInterface { get; set; }
    public TypeSymbol? ArrayIterator { get; set; }
    public TypeSymbol? RangeIterator { get; set; }
    public TypeSymbol? InclusiveRangeIterator { get; set; }
    public TypeSymbol? UnsignedRangeIterator { get; set; }
    public TypeSymbol? InclusiveUnsignedRangeIterator { get; set; }
    public TypeSymbol? StringIterator { get; set; }

    /// <summary>'Indexable&lt;T&gt;' from std.collections, what '[i]' dispatches to.</summary>
    public TypeSymbol? Indexable { get; set; }

    /// <summary>'Iterable&lt;T&gt;' from std.iter, what 'for-in' asks first.</summary>
    public TypeSymbol? Iterable { get; set; }

    /// <summary>Does this symbol live in a cell rather than in a frame slot? The lowering asks at
    /// EVERY access site, outside the closure too, because both sides have to see the same
    /// cell.</summary>
    public bool IsBoxed(Symbol symbol) => _boxed.Contains(symbol);

    /// <summary>The lambda literals an <c>inline fn</c>'s call sets in place:
    /// no closure, their <c>return</c> the enclosing function's, their loops its loops.</summary>
    private readonly HashSet<LambdaExpr> _inlineLambdas = new(ReferenceEqualityComparer.Instance);

    public void MarkInlineLambda(LambdaExpr lambda) => _inlineLambdas.Add(lambda);

    public bool IsInlineLambda(LambdaExpr lambda) => _inlineLambdas.Contains(lambda);

    // ------------------------------------------------------------------ std.meta

    /// <summary>A field as <c>std.meta.fields</c> answers it: name, type, position in the
    /// declaration, word, whether it is a <c>var</c>.</summary>
    public sealed record MetaField(string Name, LyrType Type, int Index, Visibility Visibility, bool IsVar, FieldSymbol? Symbol)
    {
        /// <summary>The field's declaration where it has no symbol — a variant's payload field.</summary>
        public FieldDecl? Declaration { get; init; }

        /// <summary>The attributes the field carries: what <c>attribute&lt;A&gt;(f)</c> reads.</summary>
        public AttributeNode[] Attributes => (Declaration ?? Symbol?.Declaration as FieldDecl)?.Attributes ?? [];

        /// <summary>Whether the field's declaration writes an initializer: the type's own
        /// default counts for nothing; a payload's field as a struct's.</summary>
        public bool HasDefault => (Declaration ?? Symbol?.Declaration as FieldDecl)?.Default is not null;
    }

    /// <summary>A variant as <c>std.meta.variants</c> answers it, its payload's fields.</summary>
    public sealed record MetaVariant(string Name, int Index, MetaField[] Payload)
    {
        /// <summary>The attributes the variant carries: what <c>variantAttribute&lt;A&gt;(v)</c> reads.</summary>
        public AttributeNode[] Attributes { get; init; } = [];
    }

    /// <summary>One round of a <c>comptime for</c> over the fields of a known type: its own body,
    /// parsed again, its variable — whose value is the field's info —, the field.</summary>
    public sealed record MetaRound(Block Body, LocalSymbol Variable, MetaField Field);

    /// <summary>The fields of a type that a module may name, as the checker reads them;
    /// asked by the lowering, too, for an instance it settles.</summary>
    public Func<LyrType, ModuleSymbol?, MetaField[]>? MetaFields { get; internal set; }

    /// <summary>A function of a type's own body as <c>std.meta.members</c> answers it.</summary>
    public sealed record MetaMember(string Name, Visibility Visibility, bool IsStatic, bool IsMut)
    {
        /// <summary>The member's declaration — a function, a <c>static let</c>, an associated type —,
        /// whose attributes <c>memberAttribute&lt;A&gt;(m)</c> reads.</summary>
        public Decl? Declaration { get; init; }

        /// <summary>What it is, by std.core's <c>MemberKind</c>.</summary>
        public string Kind { get; init; } = "Function";

        /// <summary>A function's parameters, in declaration order; none for another member.</summary>
        public MetaParam[] Parameters { get; init; } = [];

        public AttributeNode[] Attributes => Declaration switch
        {
            FunctionDecl f => f.Attributes,
            StaticBindingDecl s => s.Attributes,
            AssociatedTypeDecl a => a.Attributes,
            _ => [],
        };
    }

    /// <summary>A parameter as <c>m.parameters</c> answers it: its declaration carries its default
    /// and its attributes.</summary>
    public sealed record MetaParam(string Name, LyrType Type, int Index, Param? Declaration)
    {
        public bool HasDefault => Declaration?.Default is not null;

        public AttributeNode[] Attributes => Declaration?.Attributes ?? [];
    }

    /// <summary>The functions of a type's own body that a module may name.</summary>
    public Func<LyrType, ModuleSymbol?, MetaMember[]>? MetaMembers { get; internal set; }

    /// <summary>The variants of an enum (its instance's payload types).</summary>
    public Func<LyrType, MetaVariant[]>? MetaVariants { get; internal set; }

    /// <summary>The interfaces a type conforms to, its parents' with them.</summary>
    public Func<LyrType, TypeSymbol[]>? MetaConformances { get; internal set; }

    private readonly Dictionary<CallExpr, ModuleSymbol> _metaFrom = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ComptimeExpr> _metaSites = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ForInStmt, MetaRound[]> _metaRounds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<FieldByInfoExpr, MemberExpr> _fieldMembers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalSymbol, MetaField> _knownFields = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<(FunctionDecl Default, TypeSymbol Conformer), (FunctionDecl Expansion, LyrType Self)> _expansions = new();

    /// <summary>The module a question to <c>std.meta</c> is asked from: whose fields it may see.</summary>
    public ModuleSymbol? MetaFromOf(CallExpr call) => _metaFrom.GetValueOrDefault(call);
    internal void SetMetaFrom(CallExpr call, ModuleSymbol module) => _metaFrom[call] = module;

    private readonly HashSet<CallExpr> _metaFromConformer = new(ReferenceEqualityComparer.Instance);

    /// <summary>A question about <c>Self</c> in a default of an interface marked
    /// <c>@SeesConformer</c> (11 §4 rule 10; W10), checked once for every conformer: the lowering
    /// asks it with the module of the conformer it answers for, not the interface's.</summary>
    public bool MetaFromConformer(CallExpr call) => _metaFromConformer.Contains(call);
    internal void MarkMetaFromConformer(CallExpr call) => _metaFromConformer.Add(call);

    /// <summary>A <c>comptime</c> expression that is a question to <c>std.meta</c>: answered per
    /// instance by the lowering, never a site of its own.</summary>
    public bool IsMetaSite(ComptimeExpr site) => _metaSites.Contains(site);
    internal void MarkMetaSite(ComptimeExpr site) => _metaSites.Add(site);

    /// <summary>A <c>comptime for</c> over the fields of a known type, one body per field.</summary>
    public MetaRound[]? MetaRoundsOf(ForInStmt loop) => _metaRounds.GetValueOrDefault(loop);
    internal void SetMetaRounds(ForInStmt loop, MetaRound[] rounds) => _metaRounds[loop] = rounds;

    /// <summary><c>e.[f]</c> as the member access it is in its round.</summary>
    public MemberExpr? FieldMemberOf(FieldByInfoExpr access) => _fieldMembers.GetValueOrDefault(access);
    internal void SetFieldMember(FieldByInfoExpr access, MemberExpr member) => _fieldMembers[access] = member;

    /// <summary>The field a round's variable holds the info of: a constant where it is read.</summary>
    public MetaField? KnownFieldOf(LocalSymbol variable) => _knownFields.GetValueOrDefault(variable);
    internal void SetKnownField(LocalSymbol variable, MetaField field) => _knownFields[variable] = field;

    /// <summary>A default whose body walks the fields of <c>Self</c>, set in place for one
    /// conformer: parsed again, checked with <c>Self</c> that conformer.</summary>
    public FunctionDecl? ExpansionOf(FunctionDecl @default, TypeSymbol conformer) =>
        _expansions.TryGetValue((@default, conformer), out var found) ? found.Expansion : null;
    /// <summary>The <c>Self</c> an expansion was checked with: a generic conformer's instance over
    /// its conformance block's parameters, which the lowering binds to the instance's arguments.</summary>
    public LyrType? ExpansionSelfOf(FunctionDecl @default, TypeSymbol conformer) =>
        _expansions.TryGetValue((@default, conformer), out var found) ? found.Self : null;
    internal void SetExpansion(FunctionDecl @default, TypeSymbol conformer, FunctionDecl expansion, LyrType self) =>
        _expansions[(@default, conformer)] = (expansion, self);

    // ------------------------------------------------------------------ variants and initializers over infos
    private readonly Dictionary<LocalSymbol, MetaVariant> _knownVariants = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalSymbol, MetaVariant> _knownPayloads = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalSymbol, MetaMember> _knownMembers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalSymbol, MetaParam> _knownParams = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CallExpr, TypeAliasDecl> _aliasAsked = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TypeTestExpr, MetaVariant> _variantTests = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<PayloadByInfoExpr, (MetaVariant Variant, MetaField Field)> _payloadAccesses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr, Expr> _rewrites = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<VariantInitExpr, (EnumVariantSymbol Variant, LyrType Enum, Expr?[] Values)> _variantInits = new(ReferenceEqualityComparer.Instance);

    /// <summary>The variant a round's variable holds the info of (a loop over <c>variants</c>).</summary>
    public MetaVariant? KnownVariantOf(LocalSymbol variable) => _knownVariants.GetValueOrDefault(variable);
    internal void SetKnownVariant(LocalSymbol variable, MetaVariant variant) => _knownVariants[variable] = variant;

    /// <summary>The variant whose payload a round's variable walks (a loop over <c>v.payload</c>);
    /// the field itself is <see cref="KnownFieldOf"/>.</summary>
    public MetaVariant? KnownPayloadOf(LocalSymbol variable) => _knownPayloads.GetValueOrDefault(variable);
    internal void SetKnownPayload(LocalSymbol variable, MetaVariant variant) => _knownPayloads[variable] = variant;

    /// <summary>The member a round's variable holds the info of (a loop over <c>members</c>).</summary>
    public MetaMember? KnownMemberOf(LocalSymbol variable) => _knownMembers.GetValueOrDefault(variable);
    internal void SetKnownMember(LocalSymbol variable, MetaMember member) => _knownMembers[variable] = member;

    /// <summary>The parameter a round's variable holds the info of (a loop over <c>m.parameters</c>; X4).</summary>
    public MetaParam? KnownParamOf(LocalSymbol variable) => _knownParams.GetValueOrDefault(variable);
    internal void SetKnownParam(LocalSymbol variable, MetaParam parameter) => _knownParams[variable] = parameter;

    /// <summary><c>typeAttribute&lt;T, A&gt;()</c> whose <c>T</c> is written as an alias: the alias, whose
    /// own attributes are the answer — the type argument itself resolves through it.</summary>
    public TypeAliasDecl? AliasAskedOf(CallExpr call) => _aliasAsked.GetValueOrDefault(call);
    internal void SetAliasAsked(CallExpr call, TypeAliasDecl alias) => _aliasAsked[call] = alias;

    /// <summary><c>e is v</c> in a round: the variant tested, by its position.</summary>
    public MetaVariant? VariantTestOf(TypeTestExpr test) => _variantTests.GetValueOrDefault(test);
    internal void SetVariantTest(TypeTestExpr test, MetaVariant variant) => _variantTests[test] = variant;

    /// <summary><c>e.[v, p]</c> in a round: the variant and its payload field.</summary>
    public (MetaVariant Variant, MetaField Field)? PayloadAccessOf(PayloadByInfoExpr access) =>
        _payloadAccesses.TryGetValue(access, out var found) ? found : null;
    internal void SetPayloadAccess(PayloadByInfoExpr access, (MetaVariant Variant, MetaField Field) found) => _payloadAccesses[access] = found;

    private readonly Dictionary<IfStmt, bool> _decidedIfs = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Node, FunctionDecl> _reparsedOwners = new(ReferenceEqualityComparer.Instance);

    /// <summary>The function a round's loop was parsed again from: its 'mut' is the round's.</summary>
    public FunctionDecl? ReparsedOwnerOf(Node root) => _reparsedOwners.GetValueOrDefault(root);
    internal void SetReparsedOwner(Node root, FunctionDecl? owner) { if (owner is not null) _reparsedOwners[root] = owner; }

    /// <summary>A <c>comptime if</c> statement whose condition a round decided: the
    /// branch the checker checked, the one the lowering takes.</summary>
    public bool? DecidedIf(IfStmt stmt) => _decidedIfs.TryGetValue(stmt, out var chosen) ? chosen : null;
    internal void SetDecidedIf(IfStmt stmt, bool chosen) => _decidedIfs[stmt] = chosen;

    /// <summary>What the lowering takes for an expression instead of itself: the chosen arm of a
    /// <c>comptime if</c> a round decided, an initializer over infos as the named one it stands
    /// for, <c>E.[v](…)</c> as the call of the variant.</summary>
    public Expr? RewriteOf(Expr expr) => _rewrites.GetValueOrDefault(expr);
    internal void SetRewrite(Expr from, Expr to) => _rewrites[from] = to;

    /// <summary><c>E.[v] { … }</c>: the variant, the enum, and the payload's values in the
    /// payload's order.</summary>
    public (EnumVariantSymbol Variant, LyrType Enum, Expr?[] Values)? VariantInitOf(VariantInitExpr init) =>
        _variantInits.TryGetValue(init, out var found) ? found : null;
    internal void SetVariantInit(VariantInitExpr init, (EnumVariantSymbol Variant, LyrType Enum, Expr?[] Values) found) => _variantInits[init] = found;

    /// <summary>What was parsed again — a round's body, a default set in place —: nodes of their
    /// own over text the walkers read once, at the declaration. What they say of it, the written
    /// nodes said.</summary>
    public List<Node> ReparsedRoots { get; } = [];
}

/// <summary>
/// The <c>comptime</c> sites in the order the checker met them, each known BY ITS NODE.
/// An AST node is a record, equal to any node of the same text at the same place — and a body
/// parsed again for a round or a conformer is exactly that: its sites are sites of their own, with
/// values of their own. A list's <c>IndexOf</c> found the first of them for every one.
/// </summary>
public sealed class SiteList : IReadOnlyList<ComptimeExpr>
{
    private readonly List<ComptimeExpr> _sites = [];
    private readonly Dictionary<ComptimeExpr, int> _index = new(ReferenceEqualityComparer.Instance);

    public int Count => _sites.Count;

    public ComptimeExpr this[int index] => _sites[index];

    public void Add(ComptimeExpr site)
    {
        if (_index.TryAdd(site, _sites.Count)) _sites.Add(site);
    }

    public bool Contains(ComptimeExpr site) => _index.ContainsKey(site);

    public int IndexOf(ComptimeExpr site) => _index.TryGetValue(site, out var index) ? index : -1;

    public IEnumerator<ComptimeExpr> GetEnumerator() => _sites.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>What of a call's place a parameter takes where the call leaves it out.</summary>
public enum CallerPlace
{
    /// <summary><c>@callerLine</c>: the line the call begins on.</summary>
    Line,

    /// <summary><c>@callerFile</c>: the file the call stands in.</summary>
    File,
}

/// <summary>A function C calls (<c>@Export</c>): its declaration, the symbol C calls it by, and
/// that symbol's declaration in C — <c>int64_t geo_add(int64_t a, int64_t b)</c>, or with
/// <c>LyrStatus</c> where it throws.</summary>
public sealed record CExport(FunctionDecl Function, string Symbol, string Declaration);
