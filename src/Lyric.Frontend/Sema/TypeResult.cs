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
    public List<ComptimeExpr> ComptimeSites { get; } = new();

    /// <summary>Every typed expression with its type. The consumer is the test that checks the
    /// <see cref="ErrorType"/> invariant; without an enumeration it could only be checked where
    /// someone already looks.</summary>
    public IEnumerable<KeyValuePair<Expr, LyrType>> AllTypes => _types;
    public LyrType TypeOf(Expr expr) => _types.TryGetValue(expr, out var t) ? t : LyrType.Error;

    public void BindRef(Node node, Symbol symbol) => _refs[node] = symbol;

    /// <summary>The parameters <c>@callerExpr</c> sits on (design/v5/spec/09 A11), each with the
    /// parameter whose argument's text it gives, and where that text comes from.</summary>
    private readonly Dictionary<Param, string> _callerExpr = new(ReferenceEqualityComparer.Instance);

    public void BindCallerExpr(Param parameter, string target) => _callerExpr[parameter] = target;

    /// <summary>The parameter whose argument's text <paramref name="parameter"/> takes, or
    /// <c>null</c> where no <c>@callerExpr</c> sits on it.</summary>
    public string? CallerExprOf(Param parameter) => _callerExpr.GetValueOrDefault(parameter);

    /// <summary>The source text of a span — what a call wrote —, for the lowering.</summary>
    public Func<Lyric.Core.Span, string>? SourceText { get; internal set; }

    /// <summary>The block a call through a constraint reaches on a concrete type where no symbol
    /// of the type holds the conformance (05 §13 rules 6, 8), with the block's method of the
    /// member's name — the sema's answer at the instance the lowering reached.</summary>
    public Func<LyrType, FunctionSymbol, (ExtensionBlock Block, FunctionSymbol? Method)?>? ConformanceBlock { get; internal set; }

    /// <summary>Whether a type conforms to an interface — the sema's answer at an instance the
    /// lowering reached, for a question the checked code could not settle: is the concrete
    /// iterator of a generic loop Closeable (the review's M8a-4).</summary>
    public Func<LyrType, TypeSymbol, bool>? Satisfies { get; internal set; }

    /// <summary>Whether an interface can be the type of a value (04 D9) — only such a one has a
    /// table, and only its defaults are lowered once for it.</summary>
    public Func<TypeSymbol, bool>? ValueInterface { get; internal set; }

    /// <summary>Whether the program is Lyric 5's (04 D9 whole): there a generic default of an
    /// interface is instantiated per conformer, and a conformer's own may stand in its place. The
    /// 4.x path keeps a generic default for the interface's value until it leaves main (M16).</summary>
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

    // The conformances a GENERIC block gave (03 T7 X1), as the sema proved them at a use: the
    // instance, the interface, the block. A row in the lowering's tables is built for exactly
    // these, since a block's constraints may hold for one instance and not for another.
    private readonly List<(LyrType Instance, TypeSymbol Iface, ExtensionBlock Block)> _blockConformances = new();

    public void RecordBlockConformance(LyrType instance, TypeSymbol iface, ExtensionBlock block)
    {
        if (!BlockConformanceRecorded(instance, iface, block)) _blockConformances.Add((instance, iface, block));
    }

    public bool BlockConformanceRecorded(LyrType instance, TypeSymbol iface, ExtensionBlock block) =>
        _blockConformances.Any(e => ReferenceEquals(e.Block, block) && ReferenceEquals(e.Iface, iface) && LyrType.Equal(e.Instance, instance));

    public void RecordConformanceImpl(TypeSymbol implementer, TypeSymbol iface, string member,
        LyrType instance, FunctionSymbol impl)
    {
        var key = (implementer, iface, member);
        if (!_conformanceImpls.TryGetValue(key, out var entries))
            _conformanceImpls[key] = entries = new List<(LyrType, FunctionSymbol)>();
        if (!entries.Any(e => LyrType.Equal(e.Instance, instance))) entries.Add((instance, impl));
    }

    /// <summary><c>:: [Walker by legs]</c> (04 D1): the field a type delegates an interface's
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

    /// <summary>The yields of coroutine bodies (06 A5): where <c>close()</c> finds the coroutine
    /// suspended, such a yield throws <c>Cancelled</c>. Recorded by the checker, which knows whose
    /// body a yield stands in — a lambda's yield in a body is the other, dynamic kind.</summary>
    private readonly HashSet<YieldStmt> _bodyYields = new(ReferenceEqualityComparer.Instance);

    public void MarkBodyYield(YieldStmt yield) => _bodyYields.Add(yield);

    public bool IsBodyYield(YieldStmt yield) => _bodyYields.Contains(yield);

    /// <summary>The generator lambdas (08 Y11 F5): a lambda with a yield of its own, of type
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

    // --- errors (design/v5/spec/05 E2) ---------------------------------------------------------

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
    /// say so (06 M6-4: <c>Cancelled</c>, which it covers itself): from here on its set holds
    /// it, and its call may throw — what the lowering asks for the error slot.</summary>
    public void RecordEntryThrows(FunctionDecl main, LyrType thrown)
    {
        var declared = DeclaredThrows(main);
        if (!declared.Any(t => LyrType.Equal(t, thrown))) _declaredThrows[main] = [.. declared, thrown];
        _throwsAtCall.Add(main);
    }

    private readonly HashSet<FunctionDecl> _throwsAtCall = new(ReferenceEqualityComparer.Instance);

    public void RecordThrowsAtCall(FunctionDecl fn) => _throwsAtCall.Add(fn);

    /// <summary>Whether a CALL of the function may throw (05 E2): its set is non-empty and it is no
    /// coroutine function, whose clause belongs to its pulls. What a back end gives the caller's
    /// error slot.</summary>
    public bool ThrowsAtCall(FunctionDecl fn) => _throwsAtCall.Contains(fn);

    /// <summary>What a call may throw, in its instance's terms (K5) — the checker's settled callee,
    /// overloads and generic substitution done. Recorded for throwing calls only.</summary>
    public void RecordCallThrows(CallExpr call, LyrType[] set) => _callThrows[call] = set;

    public LyrType[] CallThrows(CallExpr call) => _callThrows.GetValueOrDefault(call) ?? [];

    /// <summary>The type a typed <c>catch</c> clause names, resolved.</summary>
    public void RecordCatchType(CatchClause clause, LyrType type) => _catchTypes[clause] = type;

    public LyrType? CatchType(CatchClause clause) => _catchTypes.GetValueOrDefault(clause);

    /// <summary>
    /// The set a catch binding carries (design/v5/spec/05 E2 K7): the types of a set clause, or
    /// what reaches a clause without a type. A rethrow of the binding throws exactly it, a match
    /// over the binding is exhaustive without a default; stored, the binding is an <c>Error</c>.
    /// </summary>
    public void RecordCatchSet(CatchClause clause, LyrType[] set) => _catchSets[clause] = set;

    public LyrType[]? CatchSet(CatchClause clause) => _catchSets.GetValueOrDefault(clause);

    /// <summary>The type a type pattern tests, resolved — named or not.</summary>
    public void RecordTypeTested(TypePattern pattern, LyrType type) => _typesTested[pattern] = type;

    public LyrType? TypeTested(TypePattern pattern) => _typesTested.GetValueOrDefault(pattern);

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

    /// <summary>A <c>for</c> over Lyric 5's protocol (design/v5/spec/10 B6 I2): the two calls the loop
    /// makes and the hidden variable the iterator lives in, checked as written calls are.</summary>
    public sealed record ForInProtocol(CallExpr IterCall, CallExpr NextCall, LocalSymbol Cursor)
    {
        /// <summary>The close of a Closeable iterator (10 B6 I6), a defer of the loop's own.</summary>
        public DeferStmt? Close { get; init; }

        /// <summary>The close stands for an iterator whose type names a type parameter (the
        /// review's M8a-4): whether it is Closeable is the INSTANCE's to say — the lowering keeps
        /// the close where the concrete iterator is one, and drops it otherwise.</summary>
        public bool CloseIfCloseable { get; init; }
    }

    private readonly Dictionary<ForInStmt, ForInProtocol> _forIns = new(ReferenceEqualityComparer.Instance);

    public void RecordForIn(ForInStmt loop, CallExpr iter, CallExpr next, LocalSymbol cursor, DeferStmt? close = null,
        bool closeIfCloseable = false) =>
        _forIns[loop] = new ForInProtocol(iter, next, cursor) { Close = close, CloseIfCloseable = closeIfCloseable };

    public ForInProtocol? ForInOf(ForInStmt loop) => _forIns.TryGetValue(loop, out var p) ? p : null;

    /// <summary>A <c>for</c> over an array, a view or an inline array in Lyric 5: the index loop
    /// (design/v5/spec/10 B6 I10).</summary>
    private readonly HashSet<ForInStmt> _indexed = new(ReferenceEqualityComparer.Instance);

    public void MarkIndexed(ForInStmt loop) => _indexed.Add(loop);

    public bool IsIndexed(ForInStmt loop) => _indexed.Contains(loop);

    public CallExpr? OperatorCallOf(Node op) => _operatorCalls.GetValueOrDefault(op);

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

    /// <summary>The arguments of a call that names some of them (04 D5), in PARAMETER order: the
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
}
