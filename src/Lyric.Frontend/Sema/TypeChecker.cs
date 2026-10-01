using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>
/// Type checking of expressions. Walks function bodies, manages the local scopes (parameters, then
/// block locals), resolves expression names and assigns every expression a <see cref="LyrType"/>
/// in the <see cref="TypeResult"/>.
///
/// Arithmetic is strict: both operands have the same type, and only untyped literals adapt through
/// a range fit. `+` and `*` also serve string and T[] as concatenation and repetition. `as`
/// converts between numeric types only.
/// </summary>
public sealed class TypeChecker
{
    private readonly Compilation _comp;
    private readonly BindingResult _binding;
    private readonly DiagnosticEngine _de;
    private readonly TypeResult _result = new();
    private readonly Dictionary<GlobalSymbol, LyrType> _globals = new(ReferenceEqualityComparer.Instance);

    // The aliases currently being expanded, and the ones already reported as cyclic. An alias names a
    // type rather than being one, so expanding it is a recursion with no base case of its own.
    private readonly HashSet<TypeSymbol> _expanding = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<TypeSymbol> _cyclic = new(ReferenceEqualityComparer.Instance);

    /// <summary>The type nodes whose misplaced <c>throws</c> has already been reported. A signature
    /// is resolved once per declaration and once per call site; the mistake is one.</summary>
    private readonly HashSet<TypeNode> _reportedThrows = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// What an operator desugar has already decided, keyed by the synthetic member node.
    ///
    /// <para>A type may conform to <c>Mul&lt;Vec2, Vec2&gt;</c> and <c>Mul&lt;float, Vec2&gt;</c> at
    /// once — the one place in the language where two instances of one interface stand on a single
    /// type. Both are called <c>mul</c>, so a lookup BY NAME cannot pick between them; the operator
    /// picks by the type of its right operand and leaves the answer here. The member lookup reads it
    /// instead of searching, which is also what keeps the two out of SEM0044: they are not an
    /// ambiguity, they are a choice already made.</para>
    /// </summary>
    private readonly Dictionary<MemberExpr, (LyrType Type, Symbol Symbol)> _operatorTarget =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The synthesized member expressions whose receiver is an IMPLICIT FORM the operator has
    /// typed already: <c>.Red == c</c> desugars to <c>.Red.equals(c)</c>, and in receiver
    /// position <c>.Red</c> has no context of its own (08 Y9) — its type is the one the operand
    /// check gave it. Written source never puts an implicit form there and gets LYR-SEM0113.
    /// </summary>
    private readonly HashSet<MemberExpr> _typedReceiver = new(ReferenceEqualityComparer.Instance);

    /// <summary>The member expressions standing in CALLEE position, marked by
    /// <see cref="CheckTargetOfCall"/>: only there may a generic function's unsubstituted name
    /// appear (§8.1) — everywhere else it is refused as a value (LYR-SEM0052).</summary>
    private readonly HashSet<MemberExpr> _calleePosition = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Is a global initializer running? Then a global that has not been computed yet is an ERROR
    /// rather than merely an unknown type.
    ///
    /// <para>Without this flag the lookup silently yields <see cref="LyrType.Error"/>, the sema
    /// reports nothing, and the lowering trips over an <c>&lt;error&gt;</c> type later.
    /// <c>Error</c> means "already reported", so something has to be reported here.</para>
    /// </summary>
    private bool _inGlobalInitializer;
    /// <summary>The root of everything thrown, caught and declared: <c>std.core</c>'s <c>Error</c>
    /// (design/v5/spec/05 E6 O1). Null without a standard library, and throwability is then not
    /// asked — a missing root must not cascade into every throw.</summary>
    private readonly TypeSymbol? _error;

    /// <summary>What a <c>using let</c> binding closes: <c>std.core</c>'s <c>Closeable</c>
    /// (design/v5/spec/05 E7 R1, 10 K1). Null without a standard library.</summary>
    private readonly TypeSymbol? _closeable;
    private readonly FunctionSymbol? _same;  // the builtin identity test (02 M10)
    private readonly TypeSymbol? _coroutine; // the builtin Coroutine<T>, mapped to CoroutineOf
    private readonly TypeSymbol? _slice;     // the builtin Slice<T>, mapped to SliceOf
    private readonly TypeSymbol? _range, _rangeInclusive; // std.core's Range<T> and RangeInclusive<T> (03 A3)

    /// <summary>The <c>Iterator&lt;T&gt;</c> interface from <c>std.iter</c>, what <c>for-in</c> checks
    /// against. <c>null</c> when the stdlib is not loaded; the loop head then reports the ordinary
    /// "not iterable" diagnostic.</summary>
    private readonly TypeSymbol? _iterator;
    private readonly TypeSymbol? _arrayIterator;
    private readonly TypeSymbol? _rangeIterator;
    private readonly TypeSymbol? _inclusiveRangeIterator;
    private readonly TypeSymbol? _unsignedRangeIterator;
    private readonly TypeSymbol? _inclusiveUnsignedRangeIterator;
    private readonly TypeSymbol? _stringIterator;
    private readonly TypeSymbol? _iterable;
    private readonly TypeSymbol? _indexable;

    private LyrType _currentReturn = LyrType.Void;
    private LyrType? _currentYield; // the yield type when the current function is a coroutine

    /// <summary>Non-null while a block lambda infers its return type: every <c>return</c> of that
    /// lambda lands here instead of being checked against a known type. Saved and restored on
    /// every lambda entry, so a nested lambda's returns never leak into the outer collection.</summary>
    private List<LyrType>? _returnInference;
    private LyrType? _currentThis;
    private ModuleSymbol? _currentModule; // for extension visibility
    private Dictionary<Symbol, LyrType> _narrowed = new(ReferenceEqualityComparer.Instance); // ?T narrowed to T inside a proven non-null region

    public TypeChecker(Compilation comp, BindingResult binding, DiagnosticEngine de)
    {
        _comp = comp;
        _binding = binding;
        _de = de;
        _error = comp.FindModule(["std", "core"])?.Members.LookupLocal("Error") as TypeSymbol is { Kind: TypeSymbolKind.Interface } root ? root : null;
        _closeable = comp.FindModule(["std", "core"])?.Members.LookupLocal("Closeable") as TypeSymbol is { Kind: TypeSymbolKind.Interface } closeable ? closeable : null;
        _same = comp.Builtins.LookupLocal("same") as FunctionSymbol;
        _coroutine = comp.Builtins.LookupLocal("Coroutine") as TypeSymbol;
        _slice = comp.Builtins.LookupLocal("Slice") as TypeSymbol;

        // 'Iterator<T>' lives in the stdlib rather than among the builtins: it is an ordinary
        // interface anyone can implement. The compiler only has to FIND it to check 'for-in'
        // against it.
        var iter = comp.FindModule(["std", "iter"])?.Members;
        _iterator = iter?.LookupLocal("Iterator") as TypeSymbol;
        _arrayIterator = iter?.LookupLocal("ArrayIterator") as TypeSymbol;
        _rangeIterator = iter?.LookupLocal("RangeIterator") as TypeSymbol;
        _inclusiveRangeIterator = iter?.LookupLocal("InclusiveRangeIterator") as TypeSymbol;
        _unsignedRangeIterator = iter?.LookupLocal("UnsignedRangeIterator") as TypeSymbol;
        _inclusiveUnsignedRangeIterator = iter?.LookupLocal("InclusiveUnsignedRangeIterator") as TypeSymbol;
        _stringIterator = iter?.LookupLocal("StringIterator") as TypeSymbol;
        _iterable = iter?.LookupLocal("Iterable") as TypeSymbol;

        // 'Indexable<T>' lives in std.collections and is to '[i]' what 'Iterator<T>' is to 'for-in':
        // the compiler knows ONE built-in form, the array, and binds everything else to an interface
        // from the stdlib.
        _indexable = comp.FindModule(["std", "collections"])?.Members
            .LookupLocal("Indexable") as TypeSymbol;

        // 'Equatable<T>' is what '==' desugars through on a user type, 'Ordered<T>' what the four
        // comparisons desugar through — the same pattern as 'Iterator' for 'for-in': the compiler
        // knows the built-in scalars, and everything else binds to an interface from the stdlib.
        var core = comp.FindModule(["std", "core"])?.Members;
        _equatable = core?.LookupLocal("Equatable") as TypeSymbol;
        _clone = core?.LookupLocal("Clone") as TypeSymbol;
        _range = core?.LookupLocal("Range") as TypeSymbol;
        _rangeInclusive = core?.LookupLocal("RangeInclusive") as TypeSymbol;
        _ordered = core?.LookupLocal("Ordered") as TypeSymbol;
        _add = core?.LookupLocal("Add") as TypeSymbol;
        _sub = core?.LookupLocal("Sub") as TypeSymbol;
        _mul = core?.LookupLocal("Mul") as TypeSymbol;
        _div = core?.LookupLocal("Div") as TypeSymbol;
        _into = core?.LookupLocal("Into") as TypeSymbol;
        _rem = core?.LookupLocal("Rem") as TypeSymbol;
        _neg = core?.LookupLocal("Neg") as TypeSymbol;
        _bitAnd = core?.LookupLocal("BitAnd") as TypeSymbol;
        _bitOr = core?.LookupLocal("BitOr") as TypeSymbol;
        _bitXor = core?.LookupLocal("BitXor") as TypeSymbol;
        _bitNot = core?.LookupLocal("BitNot") as TypeSymbol;
        _shl = core?.LookupLocal("Shl") as TypeSymbol;
        _shr = core?.LookupLocal("Shr") as TypeSymbol;
        _display = core?.LookupLocal("Display") as TypeSymbol;
        _onModule = core?.LookupLocal("OnModule") as TypeSymbol;
        _onType = core?.LookupLocal("OnType") as TypeSymbol;
        _onFunction = core?.LookupLocal("OnFunction") as TypeSymbol;
        _withArg = core?.LookupLocal("WithArg") as TypeSymbol;
    }

    /// <summary>The three attribute markers, under the same rules as <see cref="_equatable"/>:
    /// which of them an attribute struct declares decides where it may sit.</summary>
    private readonly TypeSymbol? _onModule;

    /// <inheritdoc cref="_onModule"/>
    private readonly TypeSymbol? _onType;

    /// <inheritdoc cref="_onModule"/>
    private readonly TypeSymbol? _onFunction;

    /// <summary>The positional-argument conformance (3.9), under the same rules: an attribute
    /// declaring <c>WithArg&lt;T&gt;</c> takes one parenthesized value, filling its first
    /// field.</summary>
    private readonly TypeSymbol? _withArg;

    /// <summary>What a non-numeric <c>as</c> converts through, under the same rules.</summary>
    private readonly TypeSymbol? _into;

    /// <summary>The rest of the operator interfaces (design/v5/spec/04 D6), on the same footing
    /// as <see cref="_add"/>: present where <c>std.core</c> declares them.</summary>
    private readonly TypeSymbol? _rem, _neg, _bitAnd, _bitOr, _bitXor, _bitNot, _shl, _shr;

    /// <summary>What an f-string hole renders a non-scalar value through (§6.6): the hole becomes
    /// <c>value.show()</c> when the type conforms. Null without a standard library, and such a hole
    /// is then the diagnostic it always was.</summary>
    private readonly TypeSymbol? _display;

    /// <summary>The four arithmetic interfaces, under the same rules as <see cref="_equatable"/>.</summary>
    private readonly TypeSymbol? _add;
    private readonly TypeSymbol? _sub;
    private readonly TypeSymbol? _mul;
    private readonly TypeSymbol? _div;

    /// <summary>What <c>==</c> on a user type resolves through. Null without a standard library,
    /// and user-type equality is then a diagnostic rather than a crash.</summary>
    private readonly TypeSymbol? _equatable;

    /// <summary>What <c>&lt;</c> and its three siblings resolve through, under the same rules.</summary>
    private readonly TypeSymbol? _ordered;

    /// <summary>What <c>[x] * n</c> asks of an element that is or holds an object (03 §5.1,
    /// 10 C7), under the same rules.</summary>
    private readonly TypeSymbol? _clone;

    /// <summary>The context type a value block's tail checks against (§3.1, §6.9): the match
    /// expression's context for a block arm, the lambda's return type for a block body. Set by
    /// the owner around <see cref="CheckBlock"/>, and only read by the tail itself.</summary>
    private LyrType? _tailExpected;

    /// <summary>The expression an expression statement stands for while it is checked: where a
    /// value goes nowhere, <c>try?</c> over a <c>void</c> call has nothing to make optional and
    /// only drops the error.</summary>
    private Expr? _statementValue;

    /// <summary>A loop around the statement being checked (05 E11): what a <c>break</c> or
    /// <c>continue</c> finds — and for a <c>loop</c>, the values its breaks give and its plain
    /// breaks.</summary>
    private sealed class LoopContext(bool givesValue, string? label, LyrType? expected)
    {
        public bool GivesValue => givesValue;
        public string? Label => label;
        public LyrType? Expected => expected;
        public List<LyrType> Values { get; } = new();
        public List<Span> Plain { get; } = new();
    }

    /// <summary>The loops around the statement being checked, innermost last. A lambda starts with
    /// none: its body is a function of its own (08 Y11 F5).</summary>
    private List<LoopContext> _loops = new();

    public TypeResult Check()
    {
        try
        {
            return CheckInner();
        }
        catch (NestingTooDeep deep)
        {
            // §12.4, reported once. The walk that hit the bound has unwound past everything that
            // could have said it, and the partial result is still handed back: the caller stops on
            // HasErrors, and a null here would only move the crash.
            _de.Report("LYR-SEM0105", Severity.Error, deep.At,
                $"this nests deeper than the checker walks ({MaxNesting} levels) — the language "
                + "sets no limit, this implementation does, and the alternative was taking the "
                + "process down");
            _result.NestingExceeded = true;
            return _result;
        }
    }

    private TypeResult CheckInner()
    {
        _result.IteratorInterface = _iterator;
        _result.ArrayIterator = _arrayIterator;
        _result.RangeIterator = _rangeIterator;
        _result.InclusiveRangeIterator = _inclusiveRangeIterator;
        _result.UnsignedRangeIterator = _unsignedRangeIterator;
        _result.InclusiveUnsignedRangeIterator = _inclusiveUnsignedRangeIterator;
        _result.StringIterator = _stringIterator;
        _result.Indexable = _indexable;
        _result.Iterable = _iterable;

        // DEPENDENCY order, not discovery order: a module's globals are computed after those of
        // everything it imports, so an initializer may read across an import it declared itself.
        foreach (var module in _comp.InitializationOrder()) ComputeGlobals(module);

        // The associated types (03 T6) first: every conformance's answers are read before any
        // signature that names 'T.Item' is substituted — a body may be checked before the type
        // it instantiates is declared.
        BindAssociatedTypes();
        ProbeImplicitBlocks();

        // The same order for the declarations, and for a reason that took a bug to find: an
        // attribute's field DEFAULT is written in the module that declares the attribute and read
        // in every module that uses it. What it means — a name for a variant, a name for a
        // constant — is settled when its own declaration is checked, so checking a use first
        // asked a question nobody had answered yet, and '@Saved' without arguments failed across
        // a module line while the same code in one file compiled.
        foreach (var module in _comp.InitializationOrder())
        {
            _currentModule = module;
            var ast = _comp.AstOf(module);
            CheckAttributes(ast.Attributes, AttributeTarget.Module, targetIsGeneric: false,
                module.Members, "the module header");
            CheckOverloadSets(module.Members, $"module '{module.FullName}'", inInterface: false);
            foreach (var decl in ast.Declarations)
                CheckDecl(decl, module);
        }
        CheckExtensionBlocks(); // extend bodies, conformance
        CheckMethodSets();      // one name, one function per type (04 D2, D3)
        _currentModule = null;
        new FlowAnalyzer(_comp, _result, _de).Run(); // definite assignment
        return _result;
    }

    // --- declarations ---

    private void ComputeGlobals(ModuleSymbol module)
    {
        // This pass runs before the declaration walk, so the module context is set here as
        // well: an initializer's expression check reads it — extension visibility, and which
        // module an inward opaque cast stands in. Null would mean "bare snippet" for every
        // global of a real module.
        _currentModule = module;
        foreach (var decl in _comp.AstOf(module).Declarations)
        {
            if (decl is not GlobalBindingDecl g) continue;
            var declared = g.Binding.Type is not null ? ResolveType(g.Binding.Type, module.Members) : null;
            LyrType type;
            if (g.Binding.Initializer is not null)
            {
                _inGlobalInitializer = true;
                var quiet = _de.ErrorCount;
                var initT = CheckExpr(g.Binding.Initializer, module.Members);
                quiet = _de.ErrorCount - quiet;
                _inGlobalInitializer = false;
                if (declared is not null) { CheckAssignable(g.Binding.Initializer, initT, declared, g.Span); type = declared; }
                // The same rule as a local binding (§7.1): 'null' and '[]' fix no type.
                else if (quiet == 0 && Unfixed(initT) is { } nothing)
                {
                    _de.Report("LYR-SEM0010", Severity.Error, g.Span,
                        $"'{g.Binding.Name}' needs a type — '{nothing}' fixes none on its own");
                    type = LyrType.Error;
                }
                else type = initT;
            }
            else type = declared ?? LyrType.Error;

            if (module.Members.LookupLocal(g.Binding.Name) is not GlobalSymbol gs) continue;
            _globals[gs] = type;
            _result.BindGlobal(gs, type);   // for the lowering
        }
    }

    /// <summary>
    /// The two rules a set of same-named functions has to keep.
    ///
    /// <para>ONE: they are told apart by HOW MANY arguments they take and by nothing else
    /// (design/v5/spec/04 D4): a call counts its arguments and finds one candidate — no ranking,
    /// no conversion rank, no ambiguity at the call, no interaction with inference. Two whose
    /// argument counts overlap for any number — defaults widen a count to a range — are a
    /// redeclaration (LYR-SEM0085), at the declaration and never at a call.</para>
    ///
    /// <para>TWO: an INTERFACE member may not be overloaded at all. A method table holds one
    /// function per slot and the slot is found by name; two of a name would need two slots, and
    /// every implementing type would owe both. The same structural reason that gives generic
    /// members no slot (LYR-SEM0088).</para>
    /// </summary>
    private void CheckOverloadSets(SymbolTable scope, string what, bool inInterface)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in scope.Symbols)
        {
            if (symbol is not FunctionSymbol fn || !seen.Add(fn.Name)) continue;

            var overloads = scope.OverloadsLocal(fn.Name);
            if (overloads.Count < 2) continue;

            if (inInterface)
            {
                for (var i = 1; i < overloads.Count; i++)
                    _de.Report("LYR-SEM0088", Severity.Error,
                        overloads[i].Declaration?.Span ?? default,
                        $"'{fn.Name}' cannot be overloaded in {what} — a method table holds one "
                        + "function per slot and finds it by name; give the two distinct names",
                        new DiagnosticNote(overloads[0].Declaration?.Span ?? default,
                            $"'{fn.Name}' is already declared here"));
                continue;
            }

            for (var i = 0; i < overloads.Count; i++)
                for (var j = i + 1; j < overloads.Count; j++)
                {
                    var (aMin, aMax) = ArityOf(overloads[i]);
                    var (bMin, bMax) = ArityOf(overloads[j]);
                    if (aMin > bMax || bMin > aMax) continue;
                    var shared = Math.Max(aMin, bMin);
                    _de.Report("LYR-SEM0085", Severity.Error,
                        overloads[j].Declaration?.Span ?? default,
                        $"'{fn.Name}' is declared twice in {what} taking {shared} argument(s) — "
                        + "overloads are told apart by how many arguments they take, never by their "
                        + "types; give the two distinct names",
                        new DiagnosticNote(overloads[i].Declaration?.Span ?? default,
                            $"the other one takes {DisplayArity(overloads[i])}"));
                }
        }
    }

    /// <summary>The interface instance a conformance names, rebuilt from the substitution the
    /// closure walk produced: <c>Equatable&lt;int&gt;</c> rather than bare <c>Equatable</c>. A
    /// non-generic interface is its own instance.</summary>
    private static LyrType InstanceOfConformance(TypeSymbol iface,
        Dictionary<GenericParamSymbol, LyrType> subst)
    {
        if (iface.Generics.Length == 0) return new NamedRef(iface);

        var arguments = new LyrType[iface.Generics.Length];
        for (var i = 0; i < arguments.Length; i++)
            arguments[i] = subst.TryGetValue(iface.Generics[i], out var bound)
                ? bound
                : new TypeParamType(iface.Generics[i]);
        return new GenericInstance(iface, arguments);
    }

    private bool SameParameters(FunctionSymbol a, FunctionSymbol b)
    {
        var left = FnTypeOf(a).Parameters;
        var right = FnTypeOf(b).Parameters;
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
            if (!LyrType.Equal(left[i], right[i]))
                return false;
        return true;
    }

    private void CheckDecl(Decl decl, ModuleSymbol module)
    {
        switch (decl)
        {
            case FunctionDecl fn:
                CheckAttributes(fn.Attributes, AttributeTarget.Function, fn.Generics.Length > 0,
                    module.Members, "a function");
                RequireBody(fn, module);
                CheckFunction(fn, module.Members, thisType: null);
                break;
            case StructDecl s:
                CheckAttributes(s.Attributes, AttributeTarget.Type, s.Generics.Length > 0,
                    module.Members, "a struct");
                CheckValueTypeIsFinite(s, s.Name, "struct", module);
                if (module.Members.LookupLocal(s.Name) is TypeSymbol sType)
                    CheckOverloadSets(sType.Members, $"'{s.Name}'", inInterface: false);
                CheckMethods(s.Name, s.Members, module);
                CheckTypeConformance(s.Name, s.Interfaces, module, s.Delegates);
                break;
            case ClassDecl c:
                CheckAttributes(c.Attributes, AttributeTarget.Type, c.Generics.Length > 0,
                    module.Members, "a class");
                if (module.Members.LookupLocal(c.Name) is TypeSymbol cType)
                    CheckOverloadSets(cType.Members, $"'{c.Name}'", inInterface: false);
                CheckMethods(c.Name, c.Members, module);
                CheckTypeConformance(c.Name, c.Interfaces, module, c.Delegates);
                break;
            case EnumDecl e:
                CheckAttributes(e.Attributes, AttributeTarget.Type, e.Generics.Length > 0,
                    module.Members, "an enum");
                CheckValueTypeIsFinite(e, e.Name, "enum", module);
                if (module.Members.LookupLocal(e.Name) is TypeSymbol eType)
                    CheckOverloadSets(eType.Members, $"'{e.Name}'", inInterface: false);
                CheckEnumMethods(e, module);
                CheckTypeConformance(e.Name, e.Interfaces, module);
                break;
            case InterfaceDecl i:
                CheckInterfaceParents(i, module);
                RequireSealedInModule(i.Interfaces, module, i.Name);
                // An interface's members are public always (design/v5/spec/04 D14): a conformance
                // is visible wherever the type and the interface are, and a member nobody could
                // call through the interface would be no member of it. 'pub' says nothing here,
                // and a word that says nothing is refused rather than ignored.
                foreach (var member in i.Members)
                    if (member.IsPublic)
                        _de.Report("LYR-SEM0118", Severity.Error, member.NameSpan,
                            $"'pub' on '{member.Name}' — an interface's members are public always; drop it");
                if (module.Members.LookupLocal(i.Name) is TypeSymbol iface)
                    CheckOverloadSets(iface.Members, $"interface '{i.Name}'", inInterface: true);
                CheckMethods(i.Name, i.Members, module);
                break;
            // ExtendDecl goes to CheckExtensionBlocks after all types; GlobalBindingDecl to ComputeGlobals.
        }
    }

    /// <summary>
    /// A bodyless function is a NATIVE DECLARATION: the signature is written in Lyric, the
    /// implementation lives in the host and is bound by name at load time. Reserved for the stdlib —
    /// in user code nothing could supply the body.
    /// </summary>
    /// <remarks>Interfaces are exempt: there, bodyless means abstract, and conformance checks that.
    /// <see cref="CheckMethods"/> therefore calls this for struct, class and enum only.</remarks>
    private void RequireBody(FunctionDecl fn, ModuleSymbol module)
    {
        if (fn.Body is not null || _comp.IsNative(module)) return;

        // An 'extern' declaration is the one bodyless form user code may write: the body lives
        // in the host, the runtime binds it by symbol, and the capability the ABI needs is
        // recorded in the module (hostAccess for "dotnet").
        if (fn.Extern is { } spec)
        {
            CheckExtern(fn, spec, module);
            return;
        }

        _de.Report("LYR-SEM0051", Severity.Error, fn.Span,
            $"'{fn.Name}' has no body; only standard-library modules may declare native functions");
    }

    /// <summary>
    /// What may cross an <c>extern</c> boundary, checked where the declaration stands so a
    /// signature nobody can bind is refused at the declaration rather than at load time.
    ///
    /// <para>Stage 1 of the ABI: the <c>"dotnet"</c> ABI, and the marshalling set is the
    /// scalars, <c>bool</c>, <c>char</c> and <c>string</c>, with <c>void</c> as a return. Every
    /// other type is a question the design leaves to a later stage (optionals as
    /// <c>Nullable</c>/null, arrays as <c>Span</c>, structs by field), and until it is answered
    /// the checker says so rather than letting the binder guess.</para>
    /// </summary>
    private void CheckExtern(FunctionDecl fn, ExternSpec spec, ModuleSymbol module)
    {
        if (spec.Abi != "dotnet")
        {
            _de.Report("LYR-SEM0099", Severity.Error, spec.Span,
                $"unknown ABI \"{spec.Abi}\" — this compiler binds \"dotnet\"");
            return;
        }

        // The symbol carries the type; a bare function name could not say where to look.
        if (spec.Symbol is null || !spec.Symbol.Contains("::", StringComparison.Ordinal))
            _de.Report("LYR-SEM0099", Severity.Error, spec.Span,
                $"extern '{fn.Name}' needs a symbol of the form \"Type::Method\" after '=' — "
                + "the \"dotnet\" ABI binds a public static method of a named type");

        if (fn.Generics.Length > 0)
            _de.Report("LYR-SEM0099", Severity.Error, fn.Span,
                $"'{fn.Name}' is extern and cannot have type parameters — a host symbol is one signature");

        if (fn.Throws is not null)
            _de.Report("LYR-SEM0099", Severity.Error, fn.Throws.Span,
                $"'{fn.Name}' is extern and cannot declare 'throws' — a host exception arrives as a panic in stage 1");

        foreach (var p in fn.Parameters)
        {
            var type = ResolveType(p.Type, module.Members);
            if (!CrossesHostBoundary(type, asReturn: false))
                _de.Report("LYR-SEM0099", Severity.Error, p.Type.Span,
                    $"parameter '{p.Name}' of extern '{fn.Name}' has type '{TypeFacts.Display(type)}', which does not "
                    + "cross the \"dotnet\" boundary — scalars, bool, char and string do");
        }

        if (fn.ReturnType is { } ret)
        {
            var type = ResolveType(ret, module.Members);
            if (!CrossesHostBoundary(type, asReturn: true))
                _de.Report("LYR-SEM0099", Severity.Error, ret.Span,
                    $"extern '{fn.Name}' returns '{TypeFacts.Display(type)}', which does not cross the \"dotnet\" "
                    + "boundary — scalars, bool, char, string and void do");
        }
    }

    private static bool CrossesHostBoundary(LyrType type, bool asReturn) =>
        type is PrimitiveType { Kind: var kind } && (kind != PrimitiveKind.Void || asReturn);

    private void CheckMethods(string typeName, Decl[] members, ModuleSymbol module)
    {
        if (module.Members.LookupLocal(typeName) is not TypeSymbol ts) return;
        var thisType = SelfType(ts);
        var isInterface = ts.Kind == TypeSymbolKind.Interface;
        foreach (var m in members)
        {
            // Member attributes (2.1): the list parses on struct/class members; only the
            // row-less '@Deprecated' passes (the Member target above). Interface members carry
            // one since 2.15, under the same restriction and through this same path.
            var memberAttributes = m switch
            {
                FunctionDecl mf => mf.Attributes,
                StaticBindingDecl msb => msb.Attributes,
                FieldDecl mfd => mfd.Attributes,
                _ => [],
            };
            CheckAttributes(memberAttributes, AttributeTarget.Member,
                targetIsGeneric: ts.Generics.Length > 0, ts.Members, "a member");

            if (m is StaticBindingDecl sb)
            {
                CheckStaticBinding(sb, ts);
                continue;
            }

            // A field default is an EXPRESSION and has to be checked like any other. Without this
            // visit its type never reaches the side table, the lowering reads an ErrorType at the
            // construction site (LowerObjectInit) and fails with "ir: type not lowerable: <error>".
            //
            // It only shows when an initializer OMITS the field: 'K { v = 9 }' never evaluates the
            // default, 'K { }' does.
            //
            // The field's type is the context: a literal adapts to it and '.Red' reads the enum
            // from it (08 Y9), as at every other coercion site.
            if (m is FieldDecl { Default: not null } field)
            {
                var fieldType = ResolveType(field.Type, module.Members);
                CheckAssignable(field.Default, CheckExpr(field.Default, module.Members, fieldType),
                    fieldType, field.Default.Span);
                continue;
            }

            if (m is not FunctionDecl fn) continue;
            if (!isInterface) RequireBody(fn, module);
            // A static interface member declares and does not define (03 T5): a body would run
            // for no type — 'T.parse(s)' is the conformer's static, direct under monomorphization.
            if (isInterface && fn.IsStatic && fn.Body is not null)
                _de.Report("LYR-SEM0127", Severity.Error, fn.NameSpan,
                    $"'{fn.Name}' is a static member of an interface and declares only — it is implemented by every conforming type and reached as 'T.{fn.Name}(…)'; drop the body");

            // A GENERIC interface member must have a body (2.17). It gets no vtable slot — a slot
            // holds one function and this is one per instantiation — so it is reached by
            // monomorphization alone, and an abstract one would promise a dispatch nothing can
            // perform. As a DEFAULT it is complete in itself and needs no dispatch at all.
            if (isInterface && fn.Generics.Length > 0 && fn.Body is null)
                _de.Report("LYR-SEM0082", Severity.Error, fn.Span,
                    $"'{typeName}.{fn.Name}' has type parameters of its own and no body — such a "
                    + "member is reached by monomorphization rather than through the method table, "
                    + "so it has to bring its own implementation");

            CheckMemberModifiers(fn);

            // A static member has no receiver, so 'this' is not bound there; CheckExpr reports it as
            // LYR-SEM0008.
            CheckFunction(fn, ts.Members, fn.IsStatic ? null : thisType);
        }
    }

    /// <summary>
    /// <c>static mut fn</c> is an error: <c>mut</c> speaks about the receiver, and a static member
    /// has none.
    ///
    /// <para><c>mut</c> on a class method stays allowed. It enforces nothing there — a reference is
    /// mutable through every binding anyway — but it is a readability convention, and interfaces
    /// declare <c>mut fn</c> that implementing classes have to satisfy.</para>
    /// </summary>
    private void CheckMemberModifiers(FunctionDecl fn)
    {
        if (fn.IsStatic && fn.IsMut)
            _de.Report("LYR-SEM0054", Severity.Error, fn.Span,
                $"'{fn.Name}' is static and cannot be 'mut' — a static member has no receiver");
    }

    /// <summary>A <c>static let</c> constant. Its initializer is checked in the type scope but
    /// without <c>this</c>: there is no instance it could refer to.</summary>
    private void CheckStaticBinding(StaticBindingDecl sb, TypeSymbol ts)
    {
        var outerThis = _currentThis;
        _currentThis = null;

        var declared = sb.Binding.Type is { } t ? ResolveType(t, ts.Members) : null;

        // As with a module 'let': inside an initializer a global that has not been computed yet is an
        // error, not an unknown type.
        _inGlobalInitializer = true;
        var quiet = _de.ErrorCount;
        var init = sb.Binding.Initializer is { } e ? CheckExpr(e, ts.Members, declared) : null;
        quiet = _de.ErrorCount - quiet;
        _inGlobalInitializer = false;

        if (declared is null && init is null)
            _de.Report("LYR-SEM0010", Severity.Error, sb.Span,
                $"'{sb.Binding.Name}' needs a type or an initializer");
        // The same rule as a local binding (§7.1): 'null' and '[]' fix no type on their own.
        else if (declared is null && quiet == 0 && Unfixed(init) is { } nothing)
        {
            _de.Report("LYR-SEM0010", Severity.Error, sb.Span,
                $"'{sb.Binding.Name}' needs a type — '{nothing}' fixes none on its own");
            init = LyrType.Error;
        }
        else if (declared is not null && init is not null)
            CheckAssignable(sb.Binding.Initializer!, init, declared, sb.Span);

        if (ts.Members.LookupLocal(sb.Binding.Name) is GlobalSymbol gs)
        {
            _globals[gs] = declared ?? init ?? LyrType.Error;
            _result.BindGlobal(gs, _globals[gs]);   // for the lowering
        }

        _currentThis = outerThis;
    }

    private void CheckEnumMethods(EnumDecl e, ModuleSymbol module)
    {
        if (module.Members.LookupLocal(e.Name) is not TypeSymbol ts) return;
        var thisType = SelfType(ts);
        foreach (var fn in e.Methods)
        {
            CheckAttributes(fn.Attributes, AttributeTarget.Member,
                targetIsGeneric: ts.Generics.Length > 0, ts.Members, "a member");
            CheckFunction(fn, ts.Members, thisType);
        }
    }

    // `this` inside a method: for a generic type the self-instance Stack<T>, with the type parameters
    // as arguments; otherwise plainly the reference.
    private static LyrType SelfType(TypeSymbol ts) =>
        // A builtin's 'Self' is the primitive ('extend int :: [Equatable]'): its symbol stands
        // in no type, the scalar does.
        ts.Kind == TypeSymbolKind.Builtin && TypeFacts.FromBuiltinName(ts.Name) is { } primitive ? primitive
        : ts.Generics.Length == 0
            ? new NamedRef(ts)
            : new GenericInstance(ts, Array.ConvertAll(ts.Generics, g => (LyrType)new TypeParamType(g)));

    /// <summary>
    /// The implicit synthesis — the <c>Debug</c> every type gets where it can (04 D7 "bei
    /// Bedarf") — is checked MUTED before any body can bind to it, and a block whose body does
    /// not check is withdrawn: the type then has no <c>debug()</c>, as a type with an interface
    /// or a function among its fields has none to give, and nobody is told, because nobody
    /// asked. To a fixpoint, since one type's rendering reads another's.
    /// </summary>
    private void ProbeImplicitBlocks()
    {
        bool withdrew;
        do
        {
            withdrew = false;
            foreach (var block in _comp.Extensions.Blocks.Where(b => b.IsImplicit && b.Target is not null).ToArray())
            {
                _currentModule = block.Module;
                int errors;
                using (var probe = _de.Probe())
                {
                    CheckBlockBodies(block);
                    errors = probe.Errors;
                }
                if (errors == 0) continue;
                _comp.Extensions.Withdraw(block);
                withdrew = true;
            }
        } while (withdrew);
        _currentModule = null;
    }

    /// <summary>The body check of a block's methods: the outer scope is the block's method scope,
    /// which carries cross-calls and the method generics; <c>this</c> is the target type — the
    /// primitive for builtins, the reference otherwise, the instance with the block's own
    /// parameters for <c>List&lt;T&gt;</c> and <c>Slice&lt;T&gt;</c> (03 T7 X1, X2).</summary>
    private void CheckBlockBodies(ExtensionBlock block)
    {
        var thisType = block.Decl.Target is NamedType { TypeArguments.Length: > 0 }
            ? BlockTargetType(block)
            : block.Target!.Kind == TypeSymbolKind.Builtin
                ? TypeFacts.FromBuiltinName(block.Target.Name)
                : new NamedRef(block.Target);
        foreach (var fn in block.Decl.Methods)
        {
            CheckAttributes(fn.Attributes, AttributeTarget.Member,
                targetIsGeneric: false, block.MethodScope, "a member");
            CheckFunction(fn, block.MethodScope, thisType);
        }
    }

    // extend blocks: check the bodies, the orphan rule and interface conformance. Runs after all
    // types, so member lookup goes through the complete registry.
    private void CheckExtensionBlocks()
    {
        foreach (var block in _comp.Extensions.Blocks)
        {
            _currentModule = block.Module;

            if (block.IsConstructorTarget)
            {
                // A built-in constructor as the target (03 T7 X2): 'this' is the shape at the
                // block's parameters. Its conformance — a table row for an array — is not written
                // yet; the members are.
                if (block.Decl.Interfaces.Length > 0)
                    _de.Report("LYR-SEM0047", Severity.Error, block.Decl.Interfaces[0].Span,
                        $"a conformance of '{TypeFacts.Display(BlockTargetType(block))}' is not written yet — a block on a built-in constructor adds members only");
                foreach (var fn in block.Decl.Methods)
                {
                    CheckAttributes(fn.Attributes, AttributeTarget.Member, targetIsGeneric: false, block.MethodScope, "a member");
                    CheckFunction(fn, block.MethodScope, BlockTargetType(block));
                }
                continue;
            }

            if (block.Target is null)
            {
                // An unresolvable target, where RES0002 was already reported, against a resolved but
                // non-extendable one — a function type, an alias. Only the latter reports SEM0047.
                if (block.Decl.Target is not NamedType || _binding.Resolve(block.Decl.Target) is not (null or ErrorSymbol))
                    _de.Report("LYR-SEM0047", Severity.Error, block.Decl.Target.Span,
                        "an extend target is a named type, plain or an instance, or a built-in constructor — an array, an optional, a tuple; a function type is not one");
                continue; // without a target there is no useful body check
            }

            // An interface is extended through the generic form only (04 D15): 'extend Walker
            // { … }' would be a second spelling of 'extend<T :: [Walker]> T { … }', and a
            // conformance of an interface to an interface has no meaning either.
            if (block.Target.Kind == TypeSymbolKind.Interface)
            {
                _de.Report("LYR-SEM0124", Severity.Error, block.Decl.Target.Span,
                    $"'{block.Target.Name}' is an interface, which an extend block does not extend — "
                    + $"write the generic form, 'extend<T :: [{block.Target.Name}]> T {{ … }}'");
                continue;
            }

            // Body check — an implicit block's was probed already, and what survived checks.
            if (!block.IsImplicit) CheckBlockBodies(block);

            // No orphan rule (03 T7 X3): coherence is checked whole-program, and an extend may
            // stand in any module.
            CheckTypeConformance(block.Target, block.Decl.Interfaces, block.Module, block.Target.Name,
                self: block.Decl.Target is NamedType { TypeArguments.Length: > 0 } ? BlockTargetType(block) : null);
        }
        CheckCoherence();
    }

    /// <summary>
    /// Coherence (03 T7 X3, X4): one conformance per type instance and interface instance in the
    /// whole program. A block that could meet another conformance on one instance — the type's
    /// own declaration, another block, a generic block beside a concrete one — is refused where
    /// it stands (<c>LYR-SEM0133</c>); no specialization.
    /// </summary>
    private void CheckCoherence()
    {
        // Per SITE — a type's own list, one block — the interfaces written DIRECTLY: a parent
        // written out beside its child is one conformance (§1.3), and what a chain reaches is
        // not written twice by being reached twice. Across sites the same direct interface on
        // overlapping instances is the duplicate.
        var seen = new List<(int Site, TypeSymbol Target, LyrType Instance, LyrType Iface)>();
        var site = 0;
        void Note(TypeSymbol target, LyrType instance, TypeNode node, LyrType self, SymbolTable scope)
        {
            if (Conformance.InterfaceOf(node, _binding) is not { } iface) return;
            var ifaceInstance = Substitute(ResolveType(node, scope), SelfMap(iface, self));
            foreach (var (priorSite, priorTarget, priorInstance, priorIface) in seen)
                if (priorSite != site && ReferenceEquals(priorTarget, target) && TypeFacts.Overlaps(priorInstance, instance)
                    && TypeFacts.Overlaps(priorIface, ifaceInstance))
                {
                    _de.Report("LYR-SEM0133", Severity.Error, NodeSpan(node),
                        $"'{TypeFacts.Display(instance)}' conforms to '{TypeFacts.Display(ifaceInstance)}' twice — one conformance per type and interface, whole-program; no specialization");
                    break;
                }
            seen.Add((site, target, instance, ifaceInstance));
        }
        foreach (var module in _comp.Modules)
        {
            _currentModule = module;
            foreach (var symbol in module.Members.Symbols)
                if (symbol is TypeSymbol { Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct or TypeSymbolKind.Enum } ts && DeclaredInModule(ts, module))
                {
                    site++;
                    foreach (var node in DeclaredInterfaceNodes(ts))
                        Note(ts, SelfType(ts), node, SelfType(ts), DeclarationScope(ts));
                }
        }
        foreach (var block in _comp.Extensions.Blocks)
        {
            if (block.Target is not { } target || block.Decl.Interfaces.Length == 0) continue;
            _currentModule = block.Module;
            site++;
            var instance = block.Decl.Target is NamedType { TypeArguments.Length: > 0 } ? BlockTargetType(block) : SelfType(target);
            foreach (var node in block.Decl.Interfaces)
                Note(target, instance, node, instance, block.MethodScope);
        }
        _currentModule = null;
    }

    /// <summary>The target of a block as a type, <c>List&lt;T&gt;</c> with the block's own
    /// parameters (03 T7); resolved once.</summary>
    private LyrType BlockTargetType(ExtensionBlock block) =>
        block.TargetType ??= ResolveType(block.Decl.Target, block.MethodScope);

    /// <summary>
    /// What a generic block's parameters are for one receiver: the target matched against it, and
    /// the block's constraints holding for what the match bound — <c>extend&lt;T :: [Display]&gt;
    /// List&lt;T&gt;</c> applies to <c>List&lt;int&gt;</c> and not to <c>List&lt;Foo&gt;</c>.
    /// <c>null</c> where it does not apply.
    /// </summary>
    private Dictionary<GenericParamSymbol, LyrType>? BlockSubstitution(ExtensionBlock block, LyrType receiver)
    {
        var map = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);
        if (!TypeFacts.Match(BlockTargetType(block), receiver, map)) return null;
        foreach (var g in block.Generics)
        {
            if (!map.TryGetValue(g, out var bound)) return null;
            foreach (var c in g.Constraints)
                if (ConstraintInterface(c) is { } ci
                    && !Satisfies(bound, ci, Substitute(ResolveType(c, block.MethodScope), map)))
                    return null;
        }
        return map;
    }

    private static Dictionary<GenericParamSymbol, LyrType> Merge(Dictionary<GenericParamSymbol, LyrType> a, Dictionary<GenericParamSymbol, LyrType> b)
    {
        var merged = new Dictionary<GenericParamSymbol, LyrType>(a, ReferenceEqualityComparer.Instance);
        foreach (var (k, v) in b) merged[k] = v;
        return merged;
    }

    /// <summary>Does the type conform to the interface — by its own list, or through an extend
    /// block anywhere in the program?</summary>
    private bool ConformsTo(TypeSymbol ts, TypeSymbol iface)
    {
        if (Conformance.Implements(ts, iface, _binding)) return true;
        foreach (var block in _comp.Extensions.Blocks)
        {
            if (!ReferenceEquals(block.Target, ts)) continue;
            foreach (var node in block.Decl.Interfaces)
                if (Conformance.InterfaceOf(node, _binding) is { } declared
                    && Conformance.WithParents(declared, _binding).Any(p => ReferenceEquals(p, iface)))
                    return true;
        }
        return false;
    }

    /// <summary>
    /// One method set per type (design/v5/spec/04 D2): own members, inherent extension members,
    /// the implementations of its conformances — own, in a conformance block, a default, or
    /// delegated — hold every name ONCE, and a collision is an error where the second comer is
    /// declared (<c>LYR-SEM0121</c>), never a silent preference. The exceptions are the rules
    /// themselves: an own member IS the implementation of every conformance's member of that
    /// name; a default is overridden by an own member or a conformance block; two conformance
    /// blocks may each implement one name for their interface, scoped to it (D3), and the
    /// unqualified call is then refused where it stands. Two defaults nobody overrides are the
    /// type's to settle (<c>LYR-SEM0043</c>).
    /// </summary>
    private void CheckMethodSets()
    {
        foreach (var module in _comp.Modules)
        {
            _currentModule = module;
            foreach (var symbol in module.Members.Symbols)
                if (symbol is TypeSymbol { Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct or TypeSymbolKind.Enum } ts
                    && DeclaredInModule(ts, module))
                    CheckMethodSet(ts);
        }
    }

    private enum Provenance { Own, Inherent, Block, Default, Delegated }

    private void CheckMethodSet(TypeSymbol ts)
    {
        var entries = new List<(string Name, Provenance Kind, Symbol Symbol, Span Span, string Where)>();
        var blockOf = new Dictionary<Symbol, ExtensionBlock>(ReferenceEqualityComparer.Instance);
        foreach (var s in ts.Members.Symbols)
            if (s is FunctionSymbol own)
                entries.Add((own.Name, Provenance.Own, own, own.Declaration?.Span ?? default, $"'{ts.Name}'"));
        foreach (var ext in _comp.Extensions.MethodsFor(ts))
        {
            entries.Add((ext.Symbol.Name, ext.InConformanceBlock ? Provenance.Block : Provenance.Inherent,
                ext.Symbol, ext.Symbol.Declaration?.Span ?? default,
                ext.InConformanceBlock ? $"the conformance block of '{ts.Name}'" : $"an extension of '{ts.Name}'"));
            blockOf[ext.Symbol] = ext.Block;
        }
        // A delegated interface's DEFAULTS run on the outer type and are not forwarded (D1):
        // they are the ordinary defaults, and only the abstract members go to the field.
        foreach (var (iface, _) in InterfacesOf(ts))
            foreach (var m in iface.Members.Symbols)
                if (m is FunctionSymbol { Declaration: FunctionDecl { Body: not null, Generics.Length: 0 } } def)
                    entries.Add((def.Name, Provenance.Default, def, ts.Declaration?.Span ?? default, $"a default of '{iface.Name}'"));
        foreach (var (iface, field) in _result.DelegationsOf(ts))
            foreach (var m in iface.Members.Symbols)
                if (m is FunctionSymbol { IsStatic: false, Declaration: FunctionDecl { Body: null } } forwarded)
                    entries.Add((forwarded.Name, Provenance.Delegated, forwarded, ts.Declaration?.Span ?? default, $"'{iface.Name}' delegated to '{field}'"));

        foreach (var group in entries.GroupBy(e => e.Name, StringComparer.Ordinal))
        {
            var list = group.ToList();
            if (list.Count < 2) continue;
            var owns = list.Where(e => e.Kind == Provenance.Own).ToList();
            var inherent = list.Where(e => e.Kind == Provenance.Inherent).ToList();
            var blocks = list.Where(e => e.Kind == Provenance.Block).ToList();
            var defaults = list.Where(e => e.Kind == Provenance.Default).Select(e => e.Symbol).Distinct().ToList();
            var delegations = list.Where(e => e.Kind == Provenance.Delegated).ToList();
            var name = group.Key;

            // An own member settles the name for every conformance; what else declares it collides
            // — except a block implementing ANOTHER INSTANCE of an interface the own member
            // implements too ('Mul<float>' beside the own 'mul' of 'Mul<int>', 04 D6): the
            // operand picks between them, and the block's stays reachable qualified.
            if (owns.Count > 0)
            {
                foreach (var other in inherent.Concat(blocks))
                {
                    if (other.Kind == Provenance.Block && blockOf.TryGetValue(other.Symbol, out var block)
                        && block.Decl.Interfaces.Any(n => ResolveType(n, DeclarationScope(ts)) is GenericInstance { Definition: var heterogeneous }
                            && heterogeneous.Members.LookupLocal(name) is FunctionSymbol))
                        continue;
                    _de.Report("LYR-SEM0121", Severity.Error, other.Span,
                        $"'{name}' is a member of '{ts.Name}' already, and a type holds one function of a name — "
                        + (other.Kind == Provenance.Block
                            ? $"'{ts.Name}.{name}' implements it for every conformance; drop this one"
                            : "rename the extension, or make it the member"),
                        new DiagnosticNote(owns[0].Span, $"'{name}' is declared here"));
                }
                continue;
            }

            // Inherent extensions: one, and not beside a default or a conformance's implementation.
            for (var i = 1; i < inherent.Count; i++)
                _de.Report("LYR-SEM0121", Severity.Error, inherent[i].Span,
                    $"'{name}' is added to '{ts.Name}' twice — a type holds one function of a name",
                    new DiagnosticNote(inherent[0].Span, "the other one is here"));
            if (inherent.Count > 0)
            {
                foreach (var block in blocks)
                    _de.Report("LYR-SEM0121", Severity.Error, block.Span,
                        $"'{name}' is both an extension of '{ts.Name}' and the implementation of a conformance — "
                        + "one function of a name: make the extension the implementation, or rename it",
                        new DiagnosticNote(inherent[0].Span, "the extension is here"));
                foreach (var def in defaults)
                    _de.Report("LYR-SEM0121", Severity.Error, inherent[0].Span,
                        $"'{name}' is a default of an interface '{ts.Name}' conforms to, and an extension of the "
                        + "same name is a second function of it — make the extension the conformance's "
                        + "implementation ('extend' with the interface), or rename it",
                        new DiagnosticNote(def.Declaration?.Span ?? default, "the default is here"));
                foreach (var d in delegations)
                    _de.Report("LYR-SEM0121", Severity.Error, inherent[0].Span,
                        $"'{name}' is forwarded by {d.Where}, and an extension of the same name is a second function of it");
                continue;
            }

            // Delegations: one field answers a name, and no default of another interface beside it.
            if (delegations.Count > 0)
            {
                var fields = delegations.Select(d => d.Where).Distinct().ToList();
                if (fields.Count > 1)
                    _de.Report("LYR-SEM0121", Severity.Error, delegations[0].Span,
                        $"'{name}' would be delegated twice on '{ts.Name}': {string.Join(" and ", fields)} — one function of a name; write '{name}' on '{ts.Name}'");
                else if (blocks.Count > 0 || defaults.Count > 0)
                    _de.Report("LYR-SEM0121", Severity.Error, delegations[0].Span,
                        $"'{name}' is forwarded by {delegations[0].Where} and also "
                        + (blocks.Count > 0 ? "implemented in a conformance block" : "a default of another interface")
                        + $" — one function of a name; write '{name}' on '{ts.Name}' or drop one");
                continue;
            }

            // Two defaults, nobody's implementation: the type decides (D3).
            if (blocks.Count == 0 && defaults.Count > 1)
                _de.Report("LYR-SEM0043", Severity.Error, ts.Declaration?.Span ?? default,
                    $"'{ts.Name}' conforms to interfaces that both default '{name}' — implement '{name}' on "
                    + $"'{ts.Name}', one function for both",
                    defaults.Select(d => new DiagnosticNote(d.Declaration?.Span ?? default, "a default is here")).ToArray());
            // Conformance blocks each implementing the name for their interface are the one case
            // of two functions, scoped (D3): the unqualified call is refused where it stands.
        }
    }


    private bool DeclaredInModule(TypeSymbol ts, ModuleSymbol module) =>
        ReferenceEquals(module.Members.LookupLocal(ts.Name), ts);

    private static bool IsSealed(TypeSymbol iface) => iface.Declaration is InterfaceDecl { IsSealed: true };

    /// <summary>
    /// A sealed interface's conformers stand in the module that declares it (04 D8) — a type, a
    /// conformance block, a child interface alike; the set is closed there, and a match of type
    /// patterns over it may rely on that.
    /// </summary>
    private void RequireSealedInModule(TypeNode[] interfaces, ModuleSymbol module, string name)
    {
        foreach (var node in interfaces)
            foreach (var (iface, _) in ClosureOfNode(node))
                if (IsSealed(iface) && !DeclaredInModule(iface, module))
                    _de.Report("LYR-SEM0132", Severity.Error, NodeSpan(node),
                        $"'{iface.Name}' is sealed: its conformers stand in the module that declares it, and '{name}' does not");
    }

    /// <summary>The conformers of a sealed interface: the structs, classes and enums of its module
    /// that reach it, directly or through a parent, in their own list or in a conformance block
    /// of that module.</summary>
    private IEnumerable<TypeSymbol> SealedConformers(TypeSymbol iface)
    {
        var module = _comp.Modules.FirstOrDefault(m => DeclaredInModule(iface, m));
        if (module is null) yield break;
        bool Reaches(TypeNode node) => ClosureOfNode(node).Any(p => ReferenceEquals(p.iface, iface));
        var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
        foreach (var symbol in module.Members.Symbols)
            if (symbol is TypeSymbol { Kind: TypeSymbolKind.Struct or TypeSymbolKind.Class or TypeSymbolKind.Enum } ts
                && DeclaredInModule(ts, module) && DeclaredInterfaceNodes(ts).Any(Reaches) && seen.Add(ts))
                yield return ts;
        foreach (var block in _comp.Extensions.Blocks)
            if (ReferenceEquals(block.Module, module)
                && block.Target is { Kind: TypeSymbolKind.Struct or TypeSymbolKind.Class or TypeSymbolKind.Enum } target
                && block.Decl.Interfaces.Any(Reaches) && seen.Add(target))
                yield return target;
    }

    /// <summary>What the type patterns leave uncovered of a sealed interface's conformers, as the
    /// patterns to add.</summary>
    private List<string> MissingConformers(TypeSymbol iface, List<Pattern> pats)
    {
        var covered = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
        foreach (var p in pats)
            if (p is TypePattern { Type: NamedType nt } && _binding.Resolve(nt) is { } bound
                && (bound is ImportBindingSymbol ib ? ib.Target : bound) is TypeSymbol tested)
                covered.Add(tested);
        return SealedConformers(iface).Where(c => !covered.Contains(c)).Select(c => $"_: {c.Name}").ToList();
    }

    // --- interface conformance with a signature match ---

    private void CheckTypeConformance(string typeName, TypeNode[] interfaces, ModuleSymbol module, string?[]? delegates = null)
    {
        if (module.Members.LookupLocal(typeName) is TypeSymbol ts)
            CheckTypeConformance(ts, interfaces, module, typeName, delegates);
    }

    // One matching implementation per abstract interface method, either an own member or a visible
    // extension; default methods may be missing. The signature has to match exactly.
    /// <param name="delegates">The field an entry delegates to (<c>Walker by legs</c>, 04 D1), by
    /// index: its members the type does not write itself are forwarded, and conformance is
    /// satisfied by that.</param>
    /// <param name="self">What <c>Self</c> is for the conformer: the block's target instance for a
    /// generic block (<c>List&lt;T&gt;</c> with the block's own T, 03 T7), else the type itself.</param>
    private void CheckTypeConformance(TypeSymbol implementer, TypeNode[] interfaces, ModuleSymbol module, string name,
        string?[]? delegates = null, LyrType? self = null)
    {
        if (interfaces.Length == 0) return;
        RequireSealedInModule(interfaces, module, name);
        var candidates = CandidateMethods(implementer, module);
        var delegated = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < interfaces.Length; i++)
        {
            if (delegates is null || i >= delegates.Length || delegates[i] is not { } field) continue;
            if (Conformance.InterfaceOf(interfaces[i], _binding) is not { } target) continue;
            foreach (var reached in Conformance.WithParents(target, _binding)) delegated.Add(reached);
            if (implementer.Members.LookupLocal(field) is not FieldSymbol fs)
            {
                _de.Report("LYR-SEM0123", Severity.Error, NodeSpan(interfaces[i]),
                    $"'{name}' delegates '{target.Name}' to '{field}', which is no field of it");
                continue;
            }
            // The field conforms to the interface, or is a value of it (or of a child of it).
            var fieldType = FieldType(fs);
            var ok = fieldType is NamedRef { Symbol: { Kind: TypeSymbolKind.Interface } held }
                ? Conformance.WithParents(held, _binding).Any(p => ReferenceEquals(p, target))
                : TypeFacts.SymbolOf(fieldType) is { } sym && ConformsTo(sym, target);
            if (!ok)
            {
                _de.Report("LYR-SEM0123", Severity.Error, NodeSpan(interfaces[i]),
                    $"'{name}' delegates '{target.Name}' to '{field}', and '{field}: "
                    + $"{TypeFacts.Display(fieldType)}' does not conform to it");
                continue;
            }
            foreach (var reached in Conformance.WithParents(target, _binding))
                _result.RecordDelegation(implementer, reached, field, fs);
        }
        // One instance list across the walk: a parent written out beside its child is checked
        // once, while two instances of one interface are still two conformances to check.
        var seen = new List<LyrType>();

        // A GENERIC member of an interface may not be overridden (2.17). It has no slot, so a call
        // picks its target by the receiver's STATIC type: through the interface it would find the
        // default, through the concrete type the override — one name, two functions, chosen by
        // where the caller happens to stand. That is the failure SEM0079 refuses inside a chain,
        // and it is the same one here.
        foreach (var node in interfaces)
        {
            if (Conformance.InterfaceOf(node, _binding) is not { } iface) continue;
            foreach (var contributed in Conformance.WithParents(iface, _binding))
                foreach (var symbol in contributed.Members.Symbols)
                    if (symbol is FunctionSymbol { Generics.Length: > 0 } generic
                        && candidates.TryGetValue(generic.Name, out var owns))
                        foreach (var own in owns)
                        {
                            if (own.Declaration is not { } ownDeclaration) continue;
                            _de.Report("LYR-SEM0082", Severity.Error, ownDeclaration.Span,
                                $"'{name}.{generic.Name}' overrides a generic member of "
                                + $"'{contributed.Name}', which cannot be overridden — it has no "
                                + "slot to dispatch through, so the two would be chosen by the "
                                + "static type of the receiver rather than by the value",
                                new DiagnosticNote(generic.Declaration?.Span ?? default,
                                    $"'{generic.Name}' is declared here"));
                        }
        }

        // The ENTRIES of this one list, resolved: an entry repeating an earlier one at the same
        // arguments is refused (3.6.0). Entries, not closures — a parent written out beside its
        // child is documenting style, and a repetition ACROSS declarations may live in another
        // module, where refusing it would let an upstream adoption break a downstream build.
        var entrySeen = new List<(LyrType Type, Span Span)>();

        foreach (var node in interfaces)
        {
            if (Conformance.InterfaceOf(node, _binding) is not { } direct)
            {
                // An unknown name was the resolver's error already; a known one that is not an
                // interface is this one. Until 2.15 it was NOTHING: the entry was skipped, so
                // 'struct S :: [Vec2]' declared a conformance nobody ever checked and nobody
                // reported — the quietest way for a mistake to survive a compiler.
                var written = node is NamedType nt ? _binding.Resolve(nt) : null;
                if (written is ImportBindingSymbol imported) written = imported.Target;
                if (written is not null and not ErrorSymbol)
                    _de.Report("LYR-SEM0078", Severity.Error, NodeSpan(node),
                        $"only an interface can stand in the conformance list of '{name}' — "
                        + $"'{written.Name}' is not one");
                continue;
            }

            var entry = ResolveType(node, _currentModule?.Members ?? _comp.Builtins);
            if (entrySeen.FirstOrDefault(e => LyrType.Equal(e.Type, entry)) is { Type: not null } first)
            {
                _de.Report("LYR-SEM0078", Severity.Error, NodeSpan(node),
                    $"'{TypeFacts.Display(entry)}' repeats an earlier entry of this list — one "
                    + "conformance, declared once",
                    new DiagnosticNote(first.Span, "the first entry is here"));
                continue;
            }
            entrySeen.Add((entry, NodeSpan(node)));

            // The WithArg promise (3.9, §4.7): the parenthesized value fills the FIRST field,
            // and the type argument names its type. Checked at the entry that REACHES the
            // conformance — the type's own list, an extend block, or an entry whose interface
            // parent carries it (3.9.1) — so a mismatch lands with whoever declared it, not at
            // a use. Only a struct can be an attribute, so only a struct carries the promise.
            if (_withArg is not null && implementer.Declaration is StructDecl attrDecl)
            {
                foreach (var reached in InstancesOfNode(node, implementer, _withArg, EmptySubst))
                {
                    if (reached is not GenericInstance { Arguments: [var promised] }) continue;
                    var firstField = attrDecl.Members.OfType<FieldDecl>().FirstOrDefault();
                    var actual = firstField is not null
                        && implementer.Members.LookupLocal(firstField.Name) is FieldSymbol ffs
                            ? FieldType(ffs)
                            : null;
                    if (actual is null)
                        _de.Report("LYR-SEM0095", Severity.Error, NodeSpan(node),
                            $"'{name}' declares 'WithArg<{TypeFacts.Display(promised)}>' and "
                            + "has no field — the parenthesized value fills the first field, "
                            + "so there must be one");
                    else if (!LyrType.Equal(actual, promised))
                        _de.Report("LYR-SEM0095", Severity.Error, NodeSpan(node),
                            $"'{name}' declares 'WithArg<{TypeFacts.Display(promised)}>' but "
                            + $"its first field '{firstField!.Name}' is "
                            + $"'{TypeFacts.Display(actual)}' — the conformance names what "
                            + $"'@{name}(value)' fills");
                }
            }

            foreach (var (iface, subst) in ClosureOfNode(node, seen))
            {
                if (iface.Declaration is not InterfaceDecl idecl) continue;
                var implied = ReferenceEquals(iface, direct) ? "" : $" (implied by '{direct.Name}')";

                foreach (var im in idecl.Members)
                {
                    // A GENERIC member is not part of the contract: it always has a body, it
                    // cannot be overridden, and it is reached by monomorphization rather than
                    // through the table. Comparing signatures here would compare two different
                    // U's that print identically, which is a confusing way to say what
                    // LYR-SEM0082 says plainly.
                    if (im.Generics.Length > 0) continue;

                    var found = candidates.TryGetValue(im.Name, out var c) ? c : null;
                    if (found is null)
                    {
                        if (delegated.Contains(iface)) continue; // forwarded to the field (D1)
                        if (im.Body is null) // abstract and not implemented
                            _de.Report("LYR-SEM0020", Severity.Error, NodeSpan(node),
                                $"'{name}' does not implement abstract method '{im.Name}' of interface '{iface.Name}'{implied}",
                                new DiagnosticNote(im.Span, $"'{im.Name}' is declared here"));
                        continue; // a default method is inherited
                    }
                    var at = self ?? SelfType(implementer);
                    var want = (FnType)Substitute(FnTypeOf(FnSym(iface, im.Name)!), WithSelf(subst, iface, at));
                    // An associated type the conformer did not answer was reported where it
                    // stands (SEM0128); the signature that reads it has nothing to compare.
                    if (ContainsError(want)) continue;

                    // With several candidates the CONFORMANCE decides which one is meant, so a
                    // match anywhere in the list satisfies it. Only when none fits is there
                    // something to report, and then the single-candidate case reports as before:
                    // one name, one implementation, one reason it does not match.
                    if (found.FirstOrDefault(candidate => SignatureMismatch(want, im, candidate, at) is null)
                        is { } satisfying)
                    {
                        // The lowering builds one vtable row per instance and cannot tell two
                        // overloads apart by name; this is the comparison it would otherwise have
                        // to repeat.
                        _result.RecordConformanceImpl(implementer, iface, im.Name,
                            InstanceOfConformance(iface, subst), satisfying);
                        continue;
                    }

                    var impl = found[0];
                    if (found.Count > 1)
                        _de.Report("LYR-SEM0042", Severity.Error, NodeSpan(node),
                            $"'{name}' has {found.Count} '{im.Name}', and none of them matches "
                            + $"interface '{iface.Name}'{implied}: expected "
                            + $"'{TypeFacts.Display(want)}'",
                            found.Select(candidate => new DiagnosticNote(
                                candidate.Declaration?.Span ?? default,
                                $"this one is '{TypeFacts.Display(FnTypeOf(candidate))}'")).ToArray());
                    else if (SignatureMismatch(want, im, impl, at) is { } reason)
                        _de.Report("LYR-SEM0042", Severity.Error, impl.Declaration?.Span ?? NodeSpan(node),
                            $"'{name}.{im.Name}' does not match interface '{iface.Name}'{implied}: {reason}");
                }
            }
        }
    }

    // The parent list of an interface: every entry an interface, no chain back to the declaring
    // one, and no member redeclared. An inherited member keeps its declaring interface — a name
    // occurring twice along one chain would make the same call dispatch differently through the
    // child and through the parent, so redeclaration is refused rather than given override
    // semantics the method tables do not have.
    private void CheckInterfaceParents(InterfaceDecl decl, ModuleSymbol module)
    {
        if (decl.Interfaces.Length == 0) return;
        if (module.Members.LookupLocal(decl.Name) is not TypeSymbol self) return;

        // SEVERAL parents since 2.16. The rule before it said one, on the grounds that a parent's
        // default method needs its own slot indexes to stay valid behind a child-typed receiver.
        // Measured: they do. The dispatch table is keyed by (concrete type, interface) and the
        // lowering emits a row per interface in the closure, so every parent keeps its own slot
        // numbering and nothing is remapped. What a second parent really costs is the rule below.

        // The same repetition rule the conformance list has (3.6.0): the ENTRIES of one parent
        // list, compared as resolved types, so '[G<int>, G<string>]' stays two entries and
        // '[P, P]' is one written twice.
        var entrySeen = new List<(LyrType Type, Span Span)>();

        foreach (var node in decl.Interfaces)
        {
            if (Conformance.InterfaceOf(node, _binding) is not { } parent)
            {
                // An unknown name was the resolver's error already; a known one that is not an
                // interface is this one.
                var s = node is NamedType nt ? _binding.Resolve(nt) : null;
                if (s is ImportBindingSymbol ib) s = ib.Target;
                if (s is not null and not ErrorSymbol)
                    _de.Report("LYR-SEM0078", Severity.Error, NodeSpan(node),
                        $"only an interface can stand in the parent list of '{decl.Name}'");
                continue;
            }
            if (Conformance.WithParents(parent, _binding).Any(p => ReferenceEquals(p, self)))
            {
                _de.Report("LYR-SEM0078", Severity.Error, NodeSpan(node),
                    ReferenceEquals(parent, self)
                        ? $"interface '{decl.Name}' cannot inherit itself"
                        : $"interface '{decl.Name}' inherits itself through '{parent.Name}' — the parent chain cannot be circular");
                continue;
            }

            var entry = ResolveType(node, DeclarationScope(self));
            if (entrySeen.FirstOrDefault(e => LyrType.Equal(e.Type, entry)) is { Type: not null } first)
                _de.Report("LYR-SEM0078", Severity.Error, NodeSpan(node),
                    $"'{TypeFacts.Display(entry)}' repeats an earlier entry of this list — one "
                    + "parent, named once",
                    new DiagnosticNote(first.Span, "the first entry is here"));
            else entrySeen.Add((entry, NodeSpan(node)));
        }

        // Seeding with 'self' keeps a cyclic chain from presenting the declaring interface's own
        // members as inherited ones. The set is shared across the parents, which is what makes a
        // DIAMOND cost nothing: a shared ancestor is walked once, so its members arrive once and
        // the two paths to it are indistinguishable afterwards — as they should be, since they
        // lead to the same declaration.
        var seen = new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance) { self };
        var inherited = new Dictionary<string, (TypeSymbol Iface, FunctionSymbol Fn)>(StringComparer.Ordinal);

        // Per NODE rather than per symbol, so the clash can be reported at the entry that brought
        // the second one rather than at the whole declaration.
        foreach (var node in decl.Interfaces)
        {
            if (Conformance.InterfaceOf(node, _binding) is not { } parent) continue;

            foreach (var iface in Conformance.WithParents(parent, _binding, seen))
                foreach (var sym in iface.Members.Symbols)
                {
                    if (sym is not FunctionSymbol fn) continue;
                    if (inherited.TryAdd(fn.Name, (iface, fn))) continue;

                    // Two parents, one name, two declarations — the one thing several parents
                    // genuinely cost. A slot holds one method, and a call through the child would
                    // have to pick; there is no rule that picks correctly, so this is refused
                    // rather than resolved. (The same name reached twice through a diamond does
                    // not land here: the set above walked that ancestor once.)
                    var first = inherited[fn.Name];
                    _de.Report("LYR-SEM0079", Severity.Error, NodeSpan(node),
                        $"'{decl.Name}' inherits '{fn.Name}' from both '{first.Iface.Name}' and "
                        + $"'{iface.Name}' — one slot cannot hold two methods; rename one of them",
                        new DiagnosticNote(first.Fn.Declaration?.Span ?? default,
                            $"'{fn.Name}' is declared here"),
                        new DiagnosticNote(fn.Declaration?.Span ?? default,
                            $"and here"));
                }
        }

        foreach (var m in decl.Members)
            if (inherited.TryGetValue(m.Name, out var have))
                _de.Report("LYR-SEM0079", Severity.Error, m.Span,
                    $"'{m.Name}' redeclares a member of parent interface '{have.Iface.Name}' — "
                    + "an inherited member keeps its declaring interface; rename this one",
                    new DiagnosticNote(have.Fn.Declaration?.Span ?? default,
                        $"'{m.Name}' is declared here"));
    }

    /// <summary>Own methods plus visible extension methods, by name; the own one first.
    ///
    /// <para>ALL of them per name, not one: a type conforming to <c>Mul&lt;Vec2, Vec2&gt;</c> and
    /// <c>Mul&lt;float, Vec2&gt;</c> has two <c>mul</c>, and each conformance is served by the one
    /// whose signature it demands. Keeping a single candidate per name would report the other
    /// conformance as unimplemented against an implementation standing right there.</para></summary>
    private Dictionary<string, List<FunctionSymbol>> CandidateMethods(TypeSymbol ts, ModuleSymbol module)
    {
        var map = new Dictionary<string, List<FunctionSymbol>>();
        void Add(FunctionSymbol fn)
        {
            if (!map.TryGetValue(fn.Name, out var list)) map[fn.Name] = list = new List<FunctionSymbol>();
            if (!list.Any(f => ReferenceEquals(f, fn))) list.Add(fn);
        }

        foreach (var s in ts.Members.Symbols)
            if (s is FunctionSymbol fn) Add(fn);
        foreach (var ext in _comp.Extensions.MethodsFor(ts))
            if (_comp.Sees(module, ext.Module)) Add(ext.Symbol);
        return map;
    }

    private static FunctionSymbol? FnSym(TypeSymbol iface, string name) =>
        iface.Members.LookupLocal(name) as FunctionSymbol;

    // Signature comparison, invariant: arity, parameter types, return type, mut, and throws ⊆.
    private string? SignatureMismatch(FnType want, FunctionDecl ifaceMethod, FunctionSymbol impl, LyrType? at = null)
    {
        var decl = (FunctionDecl)impl.Declaration!;
        var have = FnTypeOf(impl);
        // A candidate from a GENERIC block speaks in that block's own parameters: 'Pair<T>' of
        // 'extend<T :: [Equatable]> Pair<T>' is not the 'Pair<T>' of the site checking an
        // implied parent from another block. Read at this site's target, where the block's
        // parameters bind to ours (03 T7 X1).
        if (at is not null && _comp.Extensions.BlockOf(impl) is { Generics.Length: > 0 } block
            && BlockSubstitution(block, at) is { } map)
            have = (FnType)Substitute(have, map);
        if (ifaceMethod.IsStatic != decl.IsStatic)
            return ifaceMethod.IsStatic ? "expected a static member" : "expected an instance member, found a static one";
        if (want.Parameters.Length != have.Parameters.Length)
            return $"expected {want.Parameters.Length} parameter(s), found {have.Parameters.Length}";
        for (var i = 0; i < want.Parameters.Length; i++)
            if (!LyrType.Equal(want.Parameters[i], have.Parameters[i]))
                return $"parameter {i + 1} is '{TypeFacts.Display(have.Parameters[i])}', expected '{TypeFacts.Display(want.Parameters[i])}'";
        if (!LyrType.Equal(want.Return, have.Return))
            return $"returns '{TypeFacts.Display(have.Return)}', expected '{TypeFacts.Display(want.Return)}'";
        if (ifaceMethod.IsMut != decl.IsMut)
            return decl.IsMut ? "must not be 'mut'" : "must be declared 'mut'";
        // An implementation throws at most what the member allows (05 E2 K6): every type of its
        // set covered by the member's, both read at the conformance instance.
        if (have.Throws.FirstOrDefault(t => !want.Throws.Any(w => ThrownCoveredBy(t, w, _currentModule))) is { } extra)
            return want.Throws.Length == 0
                ? $"throws '{TypeFacts.Display(extra)}', and the interface member throws nothing"
                : $"throws '{TypeFacts.Display(extra)}', which the interface member's 'throws' does not cover";
        return null;
    }

    private static Span NodeSpan(TypeNode n) => n.Span;

    /// <summary>
    /// A second parameter of one name (<c>LYR-RES0001</c>, §7.1a).
    ///
    /// <para>It compiled. <c>fn f(x: int, x: int)</c> declared both, the first won every lookup,
    /// and the argument written for the second went nowhere — measured: <c>f(1, 2)</c> returning
    /// <c>x</c> answered 1. A second binding of a name can only replace the first or be
    /// unreachable, and both readings silently discard an argument the caller wrote out.</para>
    ///
    /// <para>The same rule as <c>LYR-RES0001</c> for a module or type body and
    /// <c>LYR-SEM0097</c> for a pattern; the parameter list was the one scope nobody had asked
    /// about. The CODE names the rule rather than the phase — the resolver never declares
    /// parameter symbols, so there is no RES-phase place to put it.</para>
    /// </summary>
    private void ReportDuplicateParameter(string name, Span at, Span? previous)
    {
        // '_' IS THE DELIBERATE NON-NAME (§7.1) and may repeat, here as in a pattern: 'fn g(_: int,
        // _: int)' says twice that it ignores an argument, which is not a collision but the way to
        // write one. A first version of this check refused it, and its own control test caught
        // that -- which is what a control is for.
        if (name == "_") return;

        _de.Report("LYR-RES0001", Severity.Error, at,
            $"'{name}' is already declared in this parameter list",
            previous is { } where
                ? new DiagnosticNote(where, "previous declaration")
                : new DiagnosticNote("previous declaration"));
    }

    private void CheckFunction(FunctionDecl fn, SymbolTable outerScope, LyrType? thisType)
    {
        var savedReturn = _currentReturn;
        var savedYield = _currentYield;
        var savedThis = _currentThis;
        var savedNarrowed = _narrowed;
        _currentThis = thisType;
        _narrowed = new(ReferenceEqualityComparer.Instance);

        var scope = new SymbolTable(outerScope);
        // The function's own type parameters (fn map<U>) go into the body scope. Signature types are
        // already bound by the resolver, but body types (let x: U) resolve through this scope only.
        if (outerScope.FunctionFor(fn.Name, fn) is { } fsym)
            foreach (var g in fsym.Generics) scope.TryDeclare(g);
        foreach (var p in fn.Parameters)
        {
            var pt = ResolveType(p.Type, scope);
            var ps = new ParameterSymbol(p.Name, pt, p);
            if (!scope.TryDeclare(ps))
                ReportDuplicateParameter(p.Name, p.Span,
                    Array.Find(fn.Parameters, q => q.Name == p.Name && !ReferenceEquals(q, p))?.Span);
            _result.BindRef(p, ps); // for definite-assignment analysis
            if (p.Default is not null)
            {
                CheckAssignable(p.Default, CheckExpr(p.Default, scope, pt), pt, p.Span);
                // A default runs at the call, before the receiver is anyone's (04 D5 F2): it may
                // read the parameters before it and nothing of 'this'. The sema sees the method's
                // scope here, so without this walk the lowering met 'this' outside a method.
                if (Descendants(p.Default).OfType<ThisExpr>().FirstOrDefault() is { } receiver)
                    _de.Report("LYR-SEM0120", Severity.Error, receiver.Span,
                        $"a default of '{p.Name}' cannot read 'this' — it is evaluated at the call, "
                        + "before the receiver; it may read the parameters before it");
            }
        }
        _currentReturn = fn.ReturnType is not null ? ResolveType(fn.ReturnType, scope) : LyrType.Void;
        // Coroutine: the body never produces the coroutine value, which the runtime builds at the
        // call. Return coverage does not apply; the yield context does instead.
        _currentYield = _currentReturn is CoroutineOf co ? co.Yield : null;
        CheckThrowsClause(fn, scope);

        if (fn.Body is not null)
        {
            CheckBlock(fn.Body, scope);
            if (!TypeFacts.IsVoid(_currentReturn) && _currentYield is null && !Flow.AlwaysReturns(fn.Body, _result))
                _de.Report("LYR-SEM0017", Severity.Error, fn.Span, _currentReturn is NeverType
                    ? $"'{fn.Name}' returns 'never', and a path of it ends — end every path in a throw, a panic or a call that does not return"
                    : $"not all code paths of '{fn.Name}' return a value");
        }

        _currentReturn = savedReturn;
        _currentYield = savedYield;
        _currentThis = savedThis;
        _narrowed = savedNarrowed;
    }

    // --- statements ---

    private void CheckBlock(Block block, SymbolTable parent)
    {
        var scope = new SymbolTable(parent);
        var savedNarrowed = new Dictionary<Symbol, LyrType>(_narrowed, ReferenceEqualityComparer.Instance);
        foreach (var stmt in block.Statements) CheckStmt(stmt, scope);
        EndScope(savedNarrowed); // narrowings established inside the block by an early exit end here
    }

    /// <summary>
    /// Leaves a region: the narrowings it ESTABLISHED end with it, the ones it ENDED stay ended.
    ///
    /// <para>The distinction is the whole point. An assignment ends a narrowing "from that point
    /// on" (§7.4), and restoring the state before the region wholesale undid exactly that: after
    /// <c>if (o != null) { if (c) { o = null; } … }</c> the sema believed <c>o</c> was still an
    /// <c>int</c>, typed <c>o + 1</c>, and the program panicked with <c>LYR-VM0007</c> — a sound
    /// analysis reporting nothing about a program that cannot run.</para>
    /// </summary>
    private void EndScope(Dictionary<Symbol, LyrType> before)
    {
        foreach (var symbol in before.Keys.ToList())
            if (!_narrowed.ContainsKey(symbol)) before.Remove(symbol);
        _narrowed = before;
    }

    private void CheckStmt(Stmt stmt, SymbolTable scope)
    {
        switch (stmt)
        {
            case Block b: CheckBlock(b, scope); break;
            // The tail of a value block: checked against the context the block stands in, so a
            // literal tail adapts like an expression arm would (§3.1). Its type is read back by
            // whoever owns the block — a match arm, a lambda — through the side table.
            case TailExprStmt tail: CheckExpr(tail.Expr, scope, _tailExpected); break;
            case BindingStmt bnd: CheckBinding(bnd, scope); break;
            case DestructuringStmt d: CheckDestructuring(d, scope); break;
            case LetPatternStmt lp: CheckLetPattern(lp, scope); break;
            case IfStmt f: CheckIf(f, scope); break;
            case WhileStmt w: InLoop(new LoopContext(false, w.Label, null), () => CheckWhile(w, scope)); break;
            case DoWhileStmt d:
                InLoop(new LoopContext(false, d.Label, null), () => { CheckBlock(d.Body, scope); CheckCondition(d.Condition, scope); });
                break;
            case ForInStmt fo: InLoop(new LoopContext(false, fo.Label, null), () => CheckForIn(fo, scope)); break;
            case ReturnStmt r:
                if (_currentYield is not null) // in a coroutine only a bare return is allowed, as an early end
                {
                    if (r.Value is not null)
                    {
                        CheckExpr(r.Value, scope);
                        _de.Report("LYR-SEM0039", Severity.Error, r.Span,
                            "a coroutine ends with a bare 'return;' — it cannot return a value");
                    }
                }
                // A block lambda inferring its return type: the returns are COLLECTED here and
                // unified afterwards — there is nothing to check assignability against yet.
                else if (_returnInference is { } inferred)
                    inferred.Add(r.Value is not null ? CheckExpr(r.Value, scope) : LyrType.Void);
                // A function returning 'never' does not return (05 E12): a 'return' would — unless
                // what it returns does not return either.
                else if (_currentReturn is NeverType)
                {
                    if (r.Value is null || CheckExpr(r.Value, scope) is not NeverType)
                        _de.Report("LYR-SEM0146", Severity.Error, r.Span,
                            "a function returning 'never' does not return — end the path in a throw, a panic or a call that does not return");
                }
                else if (r.Value is not null) CheckAssignable(r.Value, CheckExpr(r.Value, scope, _currentReturn), _currentReturn, r.Span);
                else if (!TypeFacts.IsVoid(_currentReturn) && !_currentReturn.IsError)
                    _de.Report("LYR-SEM0001", Severity.Error, r.Span, "return without a value in a non-void function");
                break;
            case ExprStmt es:
            {
                var savedStatement = _statementValue;
                _statementValue = es.Expr;
                CheckExpr(es.Expr, scope);
                _statementValue = savedStatement;
                break;
            }
            case ThrowStmt t:
                CheckThrown(CheckExpr(t.Value, scope), t.Span);
                break;
            case YieldStmt y:
                // Since 4.0 'yield' is legal in EVERY function (§10a): which chain it suspends
                // is a runtime fact, so outside a coroutine body the value is typed as what it
                // is — there is no context to adapt against — and the meeting with the chain is
                // checked where it happens, by panic. Inside a body the chain IS known, so the
                // static check stays: the better error, kept, and the half of LYR-SEM0038 that
                // survives the 4.0 narrowing.
                var yv = y.Value is not null ? CheckExpr(y.Value, scope, _currentYield) : null;
                if (_currentYield is null)
                    break;
                if (yv is null)
                {
                    if (!TypeFacts.IsVoid(_currentYield) && !_currentYield.IsError)
                        _de.Report("LYR-SEM0038", Severity.Error, y.Span,
                            $"'yield' without a value requires 'Coroutine<void>', this coroutine yields '{TypeFacts.Display(_currentYield)}'");
                }
                else CheckAssignable(y.Value!, yv, _currentYield, y.Span);
                break;
            case DeferStmt de: CheckStmt(de.Body, scope); break;
            case TryStmt tr:
                CheckBlock(tr.Body, scope);
                CheckClauses(tr.Body, tr.Catches, scope, (clause, catchScope) => CheckBlock(clause.Body, catchScope));
                break;
            case MatchStmt m: CheckMatch(m, m.Scrutinee, m.Arms, scope, asExpression: false); break;
            case BreakStmt br: CheckBreak(br, scope); break;
            case ContinueStmt co:
                if (co.Label is null && _loops.Count == 0)
                    _de.Report("LYR-SEM0147", Severity.Error, co.Span,
                        "'continue' stands outside a loop — a lambda's body is no part of the loop around the lambda");
                break;
            // an error statement needs no check.
        }
    }

    private void CheckBinding(BindingStmt bnd, SymbolTable scope)
    {
        var declared = bnd.Type is not null ? ResolveType(bnd.Type, scope) : null;
        var quiet = _de.ErrorCount;
        var initT = bnd.Initializer is not null ? CheckExpr(bnd.Initializer, scope, declared) : null;
        quiet = _de.ErrorCount - quiet;

        LyrType type;
        if (declared is not null && initT is not null) { CheckAssignable(bnd.Initializer!, initT, declared, bnd.Span); type = declared; }
        else if (declared is not null) type = declared;
        // 'null' and '[]' fix no type of their own (§7.1 of the specification). Without this
        // report the null type or the empty array's error element flowed SILENTLY into the
        // lowering, which died on it as an internal exception — a crash for a two-line program.
        else if (quiet == 0 && Unfixed(initT) is { } nothing)
        {
            _de.Report("LYR-SEM0010", Severity.Error, bnd.Span,
                $"binding '{bnd.Name}' needs a type — '{nothing}' fixes none on its own");
            type = LyrType.Error;
        }
        else if (initT is not null) type = initT;
        else { _de.Report("LYR-SEM0010", Severity.Error, bnd.Span, $"binding '{bnd.Name}' needs a type or an initializer"); type = LyrType.Error; }

        var local = new LocalSymbol(bnd.Name, type, bnd.IsMutable, bnd);
        scope.TryDeclare(local);
        _result.BindRef(bnd, local); // for definite-assignment analysis

        // 'using let' (05 E7 R1–R6): what it binds is Closeable (R6), and the close it owes its
        // scope is checked as the defer it is — a site of this scope, the keyword its mark.
        if (bnd.Cleanup is { } cleanup && !type.IsError)
        {
            if (_closeable is not null && ThrownCoveredBy(type, new NamedRef(_closeable), _currentModule))
                CheckStmt(cleanup, scope);
            else
                _de.Report("LYR-SEM0143", Severity.Error, cleanup.Span,
                    $"'using' closes what it binds — '{TypeFacts.Display(type)}' is no 'Closeable'");
        }
    }

    /// <summary>
    /// The written form inside an inferred type that fixes NO type — <c>"null"</c> or <c>"[]"</c> —
    /// or <c>null</c> when the type is fixed and the binding needs no annotation.
    ///
    /// <para>ASKED OF THE TYPE, NOT OF THE WRITTEN FORM. The test used to be syntactic —
    /// "is the initializer an empty array literal" — and therefore saw only the outermost one.
    /// Six shapes got past it and died in the lowering as an internal exception:
    /// <c>[[]]</c>, <c>[[[]]]</c>, <c>(1, [])</c>, <c>[null]</c>, <c>([], [])</c> and the same at
    /// module level. The unfixed element sits at an arbitrary depth, so the depth is where it is
    /// looked for.</para>
    ///
    /// <para>A BARE <see cref="ErrorType"/> answers <c>null</c> deliberately: reached through an
    /// array it is the empty literal's marker and <c>"[]"</c> names it, but standing alone it is
    /// some other failure and this diagnostic would guess at its cause. The caller asks only when
    /// the initializer reported nothing, so an error that named itself is never spoken over.</para>
    /// </summary>
    private static string? Unfixed(LyrType? type, bool inArray = false) => type switch
    {
        NullType => "null",
        ErrorType => inArray ? "[]" : null,
        ArrayOf a => Unfixed(a.Element, true),
        Optional o => Unfixed(o.Inner, inArray),
        TupleOf t => t.Elements.Select(e => Unfixed(e, inArray)).FirstOrDefault(n => n is not null),
        _ => null,
    };

    /// <summary>The one <c>RangeExpr</c> that may stand as a value: the iterable of the
    /// <c>for-in</c> currently being checked. See <see cref="CheckRange"/>.</summary>
    private Expr? _rangeInPosition;

    private void CheckForIn(ForInStmt fo, SymbolTable scope)
    {
        var outerRange = _rangeInPosition;
        _rangeInPosition = fo.Iterable;
        var iterType = CheckExpr(fo.Iterable, scope);
        _rangeInPosition = outerRange;
        var elem = iterType switch
        {
            // The three built-in forms. They have no declaration a conformance could hang on, so the
            // compiler builds an adapter from std.iter for them. Semantically the same protocol; only
            // the way it is obtained differs.
            ArrayOf a => a.Element,
            RangeOf r => r.Element,
            PrimitiveType { Kind: PrimitiveKind.String } => LyrType.Char,

            ErrorType => LyrType.Error,

            // Everything else has to satisfy 'Iterator<T>'. What T is stands in the type's
            // conformance. 'Iterable<T>' comes first — a container SAYS how to walk it — then
            // 'Iterator<T>', for the case where the expression is already a cursor
            // ('for (x in myIterator)').
            //
            // The order matters: reversed, a type satisfying both would be used as its own cursor,
            // and two loops over it would advance each other.
            _ => TypeArgumentOfConformance(iterType, _iterable)
                 ?? YieldTypeOfIterator(iterType, fo.Iterable.Span)
                 ?? Report(fo.Iterable.Span, "LYR-SEM0007",
                     $"'{TypeFacts.Display(iterType)}' is not iterable — it must implement "
                     + "'Iterable<T>' or 'Iterator<T>' from std.iter")
        };
        // An OPTIONAL element cannot be walked, and the reason is the protocol rather than the
        // container: 'next()' answers '?T' and uses null to mean "the end", so an element that is
        // itself optional would need '??T' to be told apart from it — and '?' does not nest.
        // Refused here, where the source position is, rather than in the lowering: it produced
        // '??i64' in the IR, which crashed the verifier in debug and, in release, wrote a module
        // the loader refuses. 'check' answered 'ok' either way.
        if (elem is Optional)
            _de.Report("LYR-SEM0091", Severity.Error, fo.Iterable.Span,
                $"iterating this yields '{TypeFacts.Display(elem)}', and an iterator already "
                + "answers null to mean the end — an optional element cannot be told apart from it",
                new DiagnosticNote(
                    "walk the indices instead: 'for (i in 0..xs.length) { let x = xs[i]; ... }'"));

        var loopScope = new SymbolTable(scope);
        var loopVar = new LocalSymbol(fo.Variable, elem is Optional ? LyrType.Error : elem,
            false, fo);
        loopScope.TryDeclare(loopVar);
        _result.BindRef(fo, loopVar); // for definite-assignment analysis

        // 'for ((k, v) in …)': the element still has its (hidden) slot; the pattern binds the
        // names from it. It has to hold for every element — a test has no failure path here.
        if (fo.Pattern is { } pattern) BindIrrefutable(pattern, loopVar.Type, loopScope, "a for-loop head");

        CheckBlock(fo.Body, loopScope);
    }

    /// <summary>Binds a pattern that is not allowed to fail — a for-loop head or a lambda
    /// parameter. A refutable pattern is LYR-SEM0098 with the way out named.</summary>
    private void BindIrrefutable(Pattern pattern, LyrType type, SymbolTable scope, string where)
    {
        if (!type.IsError && !IsIrrefutable(pattern, type))
            _de.Report("LYR-SEM0098", Severity.Error, pattern.Span,
                $"this pattern can fail on a '{TypeFacts.Display(type)}', and {where} has no path for a miss — bind a name and use 'let … else' in the body");
        BindPattern(pattern, type, scope);
    }

    /// <summary>
    /// What does this type yield when iterated? The yield type stands in its
    /// <c>Iterator&lt;T&gt;</c> conformance.
    ///
    /// <para><c>null</c> when the type is not an iterator; the caller reports then. A return value
    /// rather than a diagnostic here, so the message sits at ONE place and can name the expression it
    /// is about.</para>
    /// </summary>
    private LyrType? YieldTypeOfIterator(LyrType type, Span span) =>
        TypeArgumentOfConformance(type, _iterator);

    /// <summary>
    /// The type argument with which <paramref name="type"/> satisfies the interface
    /// <paramref name="iface"/>: <c>class Ones :: [Iterator&lt;int&gt;]</c> yields <c>int</c>.
    ///
    /// <para>One function for both existing cases: <c>Iterator&lt;T&gt;</c> behind <c>for-in</c> and
    /// <c>Indexable&lt;T&gt;</c> behind <c>[i]</c>.</para>
    ///
    /// <para><c>null</c> when the type does not satisfy the interface; the caller reports then. A
    /// return value rather than a diagnostic here, so the message sits at ONE place and can name the
    /// expression it is about.</para>
    /// </summary>
    private LyrType? TypeArgumentOfConformance(LyrType type, TypeSymbol? iface)
    {
        if (iface is null) return null;

        if (TypeFacts.SymbolOf(type) is not { } symbol) return null;

        // The type IS the interface ('Iterator<int>' as a parameter type) or implements it.
        if (ReferenceEquals(symbol, iface))
            return type is GenericInstance { Arguments.Length: 1 } direct ? direct.Arguments[0] : null;

        if (!Conformance.Implements(symbol, iface, _binding)) return null;

        // The type argument stands in the declaration's conformance list.
        var declared = symbol.Declaration switch
        {
            ClassDecl c => c.Interfaces,
            StructDecl v => v.Interfaces,
            _ => null,
        };

        if (declared is null) return null;

        foreach (var node in declared)
        {
            if (node is not NamedType { TypeArguments.Length: 1 } named) continue;

            // Bound through an import ('import std.iter { Iterator }'), the binding points at the
            // import symbol rather than at the type. Without this step the comparison never finds
            // anything, and 'for-in' rejects every user-written iterator.
            var bound = _binding.Resolve(named);
            if (bound is ImportBindingSymbol import) bound = import.Target;

            if (bound is TypeSymbol target && ReferenceEquals(target, iface))
            {
                var argument = ResolveType(named.TypeArguments[0], _comp.Builtins);

                // The conformance list holds the declaration's type PARAMETER:
                // 'class List<T> :: [Indexable<T>]' names T there. For an instance it has to be
                // substituted, or 'List<int>' yields 'T' as its element type instead of 'int'.
                //
                // For a non-generic conformance ('class Ones :: [Iterator<int>]') the substitution is
                // empty and the result unchanged, so there is no special case here.
                return type is GenericInstance instance
                    ? Substitute(argument, SubstMap(instance))
                    : argument;
            }
        }

        // Reached when the conformance is inherited: 'class Ones :: [Counting]' with
        // 'interface Counting :: [Iterator<int>]'. The direct list has no Iterator node, but the
        // closure carries the parent instance with the child's arguments substituted through.
        foreach (var node in declared)
        {
            if (Conformance.InterfaceOf(node, _binding) is not { } direct) continue;
            foreach (var (p, inst) in InterfaceClosure(direct, ResolveType(node, DeclarationScope(symbol))))
                if (ReferenceEquals(p, iface) && inst is GenericInstance { Arguments.Length: 1 } via)
                    return type is GenericInstance outer
                        ? Substitute(via.Arguments[0], SubstMap(outer))
                        : via.Arguments[0];
        }

        return null;
    }

    /// <summary>
    /// A <c>throws</c> clause (design/v5/spec/05 E2): a SET of types that conform to <c>Error</c>
    /// (E3, <c>LYR-SEM0030</c>), each named once (K1, <c>LYR-SEM0137</c>); the bare form is
    /// <c>Error</c> (K2). The resolved set is recorded for the exception analysis — not for a
    /// coroutine function, whose clause describes the pulls of the coroutine it returns.
    /// </summary>
    private void CheckThrowsClause(FunctionDecl fn, SymbolTable scope)
    {
        if (fn.Throws is not { } clause) return;
        var seen = new List<(LyrType Type, Span Span)>();
        foreach (var node in clause.Types)
        {
            var t = ResolveType(node, scope);
            if (t.IsError) continue;
            if (!IsThrowable(t))
                _de.Report("LYR-SEM0030", Severity.Error, node.Span,
                    $"'{TypeFacts.Display(t)}' in 'throws' does not conform to 'Error' — what is thrown is an 'Error'");
            if (seen.FirstOrDefault(s => LyrType.Equal(s.Type, t)) is { Type: not null } first)
                _de.Report("LYR-SEM0137", Severity.Error, node.Span,
                    $"'{TypeFacts.Display(t)}' is named twice in 'throws' — a set names each type once",
                    new DiagnosticNote(first.Span, "named first here"));
            else seen.Add((t, node.Span));
            if (TypeFacts.SymbolOf(t) is { } thrown) _result.BindRef(node, thrown);
        }
        _result.RecordDeclaredThrows(fn, ClauseSetOf(fn));
        if (DeclaredThrowsOf(fn).Length > 0) _result.RecordThrowsAtCall(fn);
    }

    /// <summary>
    /// The set a function type or a lambda writes (03 T17, 08 Y11 F7), under a declaration's rules:
    /// every type throwable (<c>LYR-SEM0030</c>), each named once (<c>LYR-SEM0137</c>), the bare form
    /// <c>Error</c> (K2). Reported once per clause — a parameter's type is resolved again through its
    /// function's type at every call.
    /// </summary>
    private LyrType[] ResolveThrownSet(ThrowsClause clause, SymbolTable scope)
    {
        if (clause.Types.Length == 0) return _error is { } root ? [new NamedRef(root)] : [];
        var report = _checkedSets.Add(clause);
        var set = new List<(LyrType Type, Span Span)>();
        foreach (var node in clause.Types)
        {
            var t = ResolveType(node, scope);
            if (t.IsError) continue;
            if (report && !IsThrowable(t))
                _de.Report("LYR-SEM0030", Severity.Error, node.Span,
                    $"'{TypeFacts.Display(t)}' in 'throws' does not conform to 'Error' — what is thrown is an 'Error'");
            if (set.FindIndex(s => LyrType.Equal(s.Type, t)) is var first && first >= 0)
            {
                if (report)
                    _de.Report("LYR-SEM0137", Severity.Error, node.Span,
                        $"'{TypeFacts.Display(t)}' is named twice in 'throws' — a set names each type once",
                        new DiagnosticNote(set[first].Span, "named first here"));
                continue;
            }
            set.Add((t, node.Span));
            if (report && TypeFacts.SymbolOf(t) is { } named) _result.BindRef(node, named);
        }
        return set.Select(s => s.Type).ToArray();
    }

    /// <summary>The written sets whose diagnostics are out (<see cref="ResolveThrownSet"/>).</summary>
    private readonly HashSet<ThrowsClause> _checkedSets = new(ReferenceEqualityComparer.Instance);

    /// <summary>A set after a substitution: <c>never</c> names nothing — <c>throws E</c> at
    /// <c>E = never</c> is no clause (05 E2 K4) — and two entries bound to one type are one.</summary>
    internal static LyrType[] ThrownAfter(IEnumerable<LyrType> substituted)
    {
        var set = new List<LyrType>();
        foreach (var t in substituted)
            if (t is not NeverType && !set.Any(s => LyrType.Equal(s, t))) set.Add(t);
        return set.ToArray();
    }

    /// <summary>What a function's clause names, resolved — the bare form as <c>Error</c> (K2),
    /// nothing without a clause. What its BODY may throw, a coroutine's included.</summary>
    private LyrType[] ClauseSetOf(FunctionDecl fn)
    {
        if (fn.Throws is not { } clause) return [];
        if (clause.Types.Length == 0) return _error is { } root ? [new NamedRef(root)] : [];
        return clause.Types.Select(t => ResolveType(t, _comp.Builtins)).Where(t => !t.IsError).ToArray();
    }

    /// <summary>What a CALL of this function may throw: the clause's set — except for a coroutine
    /// function, whose clause belongs to the coroutine it returns (<see cref="CoroutineThrowsOf"/>):
    /// its call builds a frame and runs nothing.</summary>
    private LyrType[] DeclaredThrowsOf(FunctionDecl fn) =>
        fn.ReturnType is { } returned && ResolveType(returned, _comp.Builtins) is CoroutineOf ? [] : ClauseSetOf(fn);

    /// <summary>What a <c>throw</c> throws conforms to <c>Error</c> (design/v5/spec/05 E3): a
    /// struct, a class or an enum that conforms, an interface value of it or of a child of it, a
    /// type parameter constrained to it.</summary>
    private void CheckThrown(LyrType thrown, Span span)
    {
        if (!IsThrowable(thrown))
            _de.Report("LYR-SEM0030", Severity.Error, span,
                $"cannot throw '{TypeFacts.Display(thrown)}' — what is thrown conforms to 'Error'");
    }

    /// <summary>The root as a type, for the exception analysis.</summary>
    internal LyrType? ErrorRoot => _error is null ? null : new NamedRef(_error);

    private bool IsThrowable(LyrType t) =>
        _error is null || ThrownCoveredBy(t, new NamedRef(_error), _currentModule);

    /// <summary>The expression a <c>try</c> marks, the marks taken off: <c>try f();</c> is the
    /// call statement it marks, as the statement rules see it.</summary>
    internal static Expr Unmarked(Expr expr)
    {
        while (expr is TryExpr tried) expr = tried.Value;
        return expr;
    }

    /// <summary>
    /// Whether an element of a thrown set — a <c>throws</c> entry, a <c>catch</c> type — covers a
    /// thrown type (design/v5/spec/05 E2 K5, K8): it IS that type, on the instance —
    /// <c>Box&lt;int&gt;</c> is not <c>Box&lt;string&gt;</c> — or an interface the type conforms
    /// to, <c>Error</c> covering everything thrown. Asked at <paramref name="at"/>, the module the
    /// site stands in: a conformance from an extend block counts where it is seen. A shape — an
    /// array, an optional, a function — conforms to nothing.
    /// </summary>
    internal bool ThrownCoveredBy(LyrType thrown, LyrType element, ModuleSymbol? at)
    {
        if (thrown.IsError || element.IsError || thrown is NeverType) return true;
        if (LyrType.Equal(thrown, element)) return true;
        if (thrown is not (NamedRef or GenericInstance or TypeParamType or PrimitiveType)) return false;
        if (TypeFacts.SymbolOf(element) is not { Kind: TypeSymbolKind.Interface } iface) return false;
        var saved = _currentModule;
        _currentModule = at ?? saved;
        try { return Satisfies(thrown, iface, element); }
        finally { _currentModule = saved; }
    }

    /// <summary>
    /// The clauses of a try, in order (05 E9): each bound in a scope of its own and held against the
    /// clauses above it (C1, C2) before <paramref name="body"/> checks it. A clause without a type
    /// gets the set that reaches it first (K7) — what <paramref name="tried"/> throws past every try
    /// inside it, less what the clauses above take whole — so a match in its body can be exhaustive
    /// over it.
    /// </summary>
    private void CheckClauses(Node tried, CatchClause[] clauses, SymbolTable scope, Action<CatchClause, SymbolTable> body)
    {
        for (var i = 0; i < clauses.Length; i++)
        {
            var above = clauses[..i];
            if (clauses[i].TakesAll) _result.RecordCatchSet(clauses[i], Reaching(tried, above));
            var catchScope = BindCatch(clauses[i], scope);
            CheckAgainstAbove(clauses[i], above);
            body(clauses[i], catchScope);
        }
    }

    /// <summary>What reaches a clause without a type (05 E2 K7): what the sites under the try throw
    /// past every try inside it — the exception analysis's own walk, run muted, so there is one
    /// notion of a site — less what the clauses above take whole.</summary>
    private LyrType[] Reaching(Node tried, CatchClause[] above)
    {
        LyrType[] escaping;
        using (_de.Mute())
            escaping = new ExceptionAnalyzer(_comp, _result, _de, ThrownCoveredBy, ErrorRoot).Escaping(tried, _currentModule);
        return escaping.Where(t => !above.Any(c => TakesWhole(c, t))).ToArray();
    }

    /// <summary>The types a clause names: its type, or its set's; none for the clause without a type.</summary>
    private LyrType[] CaughtTypes(CatchClause clause) =>
        clause.BindingTypes.Length > 0 ? _result.CatchSet(clause) ?? []
        : _result.CatchType(clause) is { } caught ? [caught] : [];

    /// <summary>Does a clause take every value of a type — the type itself, or an interface it
    /// conforms to, among what the clause names; everything, for the clause without a type?</summary>
    private bool TakesWhole(CatchClause clause, LyrType thrown) =>
        clause.TakesAll || CaughtTypes(clause).Any(c => ThrownCoveredBy(thrown, c, _currentModule));

    /// <summary>
    /// A clause against the clauses above it (05 E9): a type caught a second time is refused (C1,
    /// <c>LYR-SEM0141</c>), and so is one a type caught before it takes whole — an interface it
    /// conforms to, <c>Error</c> — since no value of it can reach the clause (C2,
    /// <c>LYR-SEM0142</c>). Within a set the same holds from left to right. A clause below the one
    /// without a type is SEM0035's, and not asked again.
    /// </summary>
    private void CheckAgainstAbove(CatchClause clause, CatchClause[] above)
    {
        if (above.Any(c => c.TakesAll)) return;
        var nodes = clause.BindingTypes.Length > 0 ? clause.BindingTypes : clause.BindingType is { } one ? [one] : [];
        var mine = CaughtTypes(clause);
        if (mine.Length != nodes.Length) return;
        var before = above.SelectMany(CaughtTypes).ToList();
        for (var i = 0; i < mine.Length; i++)
        {
            var t = mine[i];
            if (t.IsError) continue;
            if (before.FirstOrDefault(b => LyrType.Equal(b, t)) is not null)
                _de.Report("LYR-SEM0141", Severity.Error, NodeSpan(nodes[i]),
                    $"'{TypeFacts.Display(t)}' is caught twice — one clause per type");
            else if (before.FirstOrDefault(b => !b.IsError && ThrownCoveredBy(t, b, _currentModule)) is { } taker)
                _de.Report("LYR-SEM0142", Severity.Error, NodeSpan(nodes[i]),
                    $"no '{TypeFacts.Display(t)}' reaches this — '{TypeFacts.Display(taker)}' is caught before it, "
                    + $"and every '{TypeFacts.Display(t)}' is one");
            before.Add(t);
        }
    }

    /// <summary>
    /// The <c>try</c> family as an expression (design/v5/spec/05 E4). A mark is worth its operand,
    /// and so is <c>try!</c> — an error panics. <c>try?</c> is worth <c>?T</c>, never flattened: "the
    /// call failed" stays apart from "the call gave null". The expression form is worth its operand
    /// or the value of the clause that took the error, unified like the arms of a <c>match</c>.
    /// </summary>
    private LyrType CheckTry(TryExpr tried, SymbolTable scope, LyrType? expected)
    {
        if (tried.Kind != TryKind.Propagate)
        {
            var signed = CheckSignedTry(tried, scope, expected);
            // Clauses after 'try?' or 'try!' were refused by the parser (PAR0051); they are checked
            // all the same, so nothing downstream meets a body without types.
            CheckClauses(tried.Value, tried.Catches, scope, (clause, catchScope) => ClauseValue(clause, catchScope, null, valueless: true));
            return signed;
        }

        var operand = CheckExpr(tried.Value, scope, expected);
        if (tried.Catches.Length == 0) return operand;

        // With a context every part checks against it and the expression HAS it, as an 'if' or a
        // 'match' expression does (§6.9); without one the parts unify.
        var context = expected is not null && !expected.IsError;
        if (context) CheckAssignable(tried.Value, operand, expected!, tried.Value.Span);
        var values = new List<LyrType> { operand };
        var valueless = TypeFacts.IsVoid(operand);
        CheckClauses(tried.Value, tried.Catches, scope, (clause, catchScope) =>
        {
            if (ClauseValue(clause, catchScope, context ? expected : null, valueless) is { } value) values.Add(value);
        });
        return context ? expected! : UnifyArms(values, tried.Span, "a 'try' expression's value and its clauses");
    }

    /// <summary><c>try?</c> and <c>try!</c>: worth <c>?T</c> unflattened, and the value.</summary>
    private LyrType CheckSignedTry(TryExpr tried, SymbolTable scope, LyrType? expected)
    {
        if (tried.Kind == TryKind.Force) return CheckExpr(tried.Value, scope, expected);
        var value = CheckExpr(tried.Value, scope, expected is Optional wanted ? wanted.Inner : null);
        if (value.IsError) return value;
        // Over an operand that never gives a value the form would be worth '?never' — always null,
        // and 'never' stands only as a return type (05 E12).
        if (value is NeverType)
            return Report(tried.KeywordSpan, "LYR-SEM0145",
                "'try?' over an expression that never gives a value would be worth '?never' — and 'never' stands only as a return type");
        if (!TypeFacts.IsVoid(value)) return new Optional(value);
        // Nothing to make optional: as a statement it drops the error, and that is all it does.
        return ReferenceEquals(_statementValue, tried) ? value : Report(tried.KeywordSpan, "LYR-SEM0140",
            "'try?' over an expression without a value has nothing to make optional — it stands "
            + "as a statement, where it drops the error");
    }

    /// <summary>A clause of the expression form, bound: the body a value block whose tail is the
    /// clause's value. A body without a tail leaves — by return, throw, break or continue — unless
    /// the expression has no value at all; leaving, it contributes nothing (<c>null</c>).</summary>
    private LyrType? ClauseValue(CatchClause clause, SymbolTable catchScope, LyrType? expected, bool valueless)
    {
        var savedTail = _tailExpected;
        _tailExpected = valueless ? null : expected;
        CheckBlock(clause.Body, catchScope);
        _tailExpected = savedTail;

        if (clause.Body.Tail is { } tail)
        {
            var value = _result.TypeOf(tail.Expr);
            if (expected is not null && !valueless) CheckAssignable(tail.Expr, value, expected, tail.Span);
            return value;
        }
        if (valueless || Flow.AlwaysExits(clause.Body, _result)) return null;
        _de.Report("LYR-SEM0033", Severity.Error, clause.Span,
            "a clause of a 'try' expression delivers its value — end it in a tail expression without ';', "
            + "or leave on every path");
        return null;
    }

    /// <summary>A clause's binding in a scope of its own: the type it names, checked to be an error;
    /// the root for a set, which the binding carries besides (K7), and for a clause without a type.</summary>
    private SymbolTable BindCatch(CatchClause clause, SymbolTable scope)
    {
        var catchScope = new SymbolTable(scope);
        LyrType bt;
        if (clause.BindingTypes.Length > 0)
        {
            // The set form (05 E9 C5): every type an error; the binding an 'Error' that carries the
            // set (K7) — what it may hold, asked by a rethrow and a match, not a second type.
            var set = new List<LyrType>();
            foreach (var node in clause.BindingTypes)
            {
                var caught = ResolveType(node, scope);
                if (!caught.IsError && !IsThrowable(caught))
                    _de.Report("LYR-SEM0030", Severity.Error, node.Span,
                        $"cannot catch '{TypeFacts.Display(caught)}' — what is caught conforms to 'Error'");
                if (TypeFacts.SymbolOf(caught) is { } named) _result.BindRef(node, named); // for the editor
                set.Add(caught);
            }
            _result.RecordCatchSet(clause, set.ToArray());
            bt = _error is not null ? new NamedRef(_error) : LyrType.Error;
        }
        else if (clause.BindingType is not null)
        {
            bt = ResolveType(clause.BindingType, scope);
            if (!bt.IsError && !IsThrowable(bt))
                _de.Report("LYR-SEM0030", Severity.Error, clause.BindingType.Span,
                    $"cannot catch '{TypeFacts.Display(bt)}' — what is caught conforms to 'Error'");
            if (TypeFacts.SymbolOf(bt) is { } caught)
                _result.BindRef(clause.BindingType, caught); // for the lowering and the editor
            _result.RecordCatchType(clause, bt);
        }
        else bt = _error is not null ? new NamedRef(_error) : LyrType.Error; // a catch-all binds the root

        // A clause with a NAME scopes it; a clause with a TYPE needs the symbol either way,
        // because the lowering reads the resolved catch type off it — 'catch (_: Boom)' used to
        // leave the clause unbound and took the lowering down with an internal error (#115),
        // which was the exact form the SEM0071 note recommends. The '_' symbol is declared
        // nowhere, so the body cannot reach it, and the warning analyzer skips the name.
        if (clause.BindingName is not null || clause.BindingType is not null)
        {
            var local = new LocalSymbol(clause.BindingName ?? "_", bt, false, clause);
            if (clause.BindingName is not null) catchScope.TryDeclare(local);
            _result.BindRef(clause, local); // for definite-assignment analysis: the catch assigns the binding
        }
        return catchScope;
    }

    private void CheckCondition(Expr cond, SymbolTable scope)
    {
        var t = CheckExpr(cond, scope);
        if (!TypeFacts.IsBool(t) && !t.IsError)
            _de.Report("LYR-SEM0004", Severity.Error, cond.Span, $"condition must be 'bool', got '{TypeFacts.Display(t)}'");
    }

    // if with nullable narrowing: 'if (x != null)' narrows x to T in the then branch, and
    // 'if (x == null) { return; }' narrows x afterwards through the early exit.
    private void CheckIf(IfStmt f, SymbolTable scope)
    {
        if (f.Condition is LetCondExpr letCond) { CheckIfLet(f, letCond, scope); return; }

        CheckCondition(f.Condition, scope);
        var (thenFacts, elseFacts) = NarrowingFacts(f.Condition);

        var before = new Dictionary<Symbol, LyrType>(_narrowed, ReferenceEqualityComparer.Instance);

        Apply(thenFacts);
        CheckBlock(f.Then, scope);
        var afterThen = _narrowed;

        // The else branch starts from the state BEFORE the if, not from what the then branch left:
        // the two are exclusive, and inheriting the then branch's endings would report an error in
        // the else branch about a narrowing only the OTHER branch destroyed.
        var afterElse = before;
        if (f.Else is not null)
        {
            _narrowed = new Dictionary<Symbol, LyrType>(before, ReferenceEqualityComparer.Instance);
            Apply(elseFacts);
            CheckStmt(f.Else, scope);
            afterElse = _narrowed;
        }

        // 'AlwaysExits' rather than 'AlwaysReturns': for narrowing what counts is whether the code
        // after the 'if' is reached at all, and 'continue' leaves the block just as 'return' does.
        var thenExits = Flow.AlwaysExits(f.Then, _result);
        var elseExits = f.Else is not null && Flow.AlwaysExits(f.Else, _result);

        // A narrowing survives the if when every branch that can REACH the code after it still
        // holds the narrowing there. A branch that always exits never reaches it and has no say.
        _narrowed = new Dictionary<Symbol, LyrType>(ReferenceEqualityComparer.Instance);
        foreach (var (symbol, type) in before)
            if ((thenExits || afterThen.ContainsKey(symbol))
                && (elseExits || afterElse.ContainsKey(symbol)))
                _narrowed[symbol] = type;

        if (thenExits) Apply(elseFacts);
        else if (elseExits) Apply(thenFacts);
    }

    /// <summary>
    /// <c>while (x != null) { … }</c> — inside the body x is no longer a <c>?T</c>.
    ///
    /// <para>Sound although a loop may change its variable: the condition is re-checked before EVERY
    /// iteration and therefore holds at the start of the body, and an assignment in the body drops
    /// the narrowing from that point on (see <c>CheckAssign</c>).</para>
    ///
    /// <para><c>do-while</c> deliberately does NOT get this: there the body runs before the first
    /// check, so the condition says nothing at the start of the body.</para>
    /// </summary>
    private void CheckWhile(WhileStmt w, SymbolTable scope)
    {
        if (w.Condition is LetCondExpr letCond)
        {
            // 'while (let x = it.next())': the pattern is tried before EVERY iteration, and the
            // body sees its names — the loop form of the iterator protocol (§7.2).
            CheckBlock(w.Body, BindLetCondition(letCond, scope, "'while let'"));
            return;
        }

        CheckCondition(w.Condition, scope);

        var (thenFacts, _) = NarrowingFacts(w.Condition);
        var snapshot = new Dictionary<Symbol, LyrType>(_narrowed, ReferenceEqualityComparer.Instance);

        Apply(thenFacts);
        CheckBlock(w.Body, scope);
        EndScope(snapshot);
    }

    private void InLoop(LoopContext loop, Action check)
    {
        _loops.Add(loop);
        try { check(); }
        finally { _loops.RemoveAt(_loops.Count - 1); }
    }

    /// <summary>The loop a jump leaves: the one its label names, or the innermost.</summary>
    private LoopContext? TargetOf(string? label)
    {
        for (var i = _loops.Count - 1; i >= 0; i--)
            if (label is null || _loops[i].Label == label) return _loops[i];
        return null;
    }

    /// <summary>
    /// <c>loop { … }</c> (05 E11): its type is what its breaks give — their values unified, or the
    /// type its position expects, each checked against it; <c>void</c> when they give none;
    /// <c>never</c> when no break leaves it. Some giving a value and some none is
    /// <c>LYR-SEM0149</c>.
    /// </summary>
    private LyrType CheckLoop(LoopExpr loop, SymbolTable scope, LyrType? expected)
    {
        var wanted = expected is null || expected.IsError || expected is NeverType || TypeFacts.IsVoid(expected) ? null : expected;
        var context = new LoopContext(givesValue: true, loop.Label, wanted);
        InLoop(context, () => CheckBlock(loop.Body, scope));
        if (context.Values.Count == 0) return context.Plain.Count == 0 ? LyrType.Never : LyrType.Void;
        foreach (var plain in context.Plain)
            _de.Report("LYR-SEM0149", Severity.Error, plain,
                "this 'break' gives no value, and the loop's other breaks give one — a 'loop' gives a value through every 'break' or through none");
        return wanted ?? UnifyArms(context.Values, loop.Span, "the values of this loop's breaks");
    }

    /// <summary>
    /// <c>break</c> (05 E11, 08 S4): it leaves the loop its label names, or the innermost — of its
    /// own function, a lambda's body being one (08 Y11 F5, <c>LYR-SEM0147</c>). A value leaves only a
    /// <c>loop</c> (<c>LYR-SEM0148</c>), checked against what the loop's position expects.
    /// </summary>
    private void CheckBreak(BreakStmt br, SymbolTable scope)
    {
        // 'break outr;' where no loop is labeled so and nothing is named so: the parser took the name
        // for a value, and the mistake is the label's.
        if (br.Value is IdentifierExpr { Name: var name } lost && scope.Lookup(name) is null && CoreMember(name) is null)
        {
            _de.Report("LYR-SEM0101", Severity.Error, lost.Span,
                $"no enclosing loop is labeled '{name}', and no value is named so");
            return;
        }
        var target = TargetOf(br.Label);
        if (target is null)
        {
            // A label no loop around carries is SemaRules' to report (LYR-SEM0101).
            if (br.Label is null)
                _de.Report("LYR-SEM0147", Severity.Error, br.Span,
                    "'break' stands outside a loop — a lambda's body is no part of the loop around the lambda");
            if (br.Value is not null) CheckExpr(br.Value, scope);
            return;
        }
        if (br.Value is null) { target.Plain.Add(br.Span); return; }
        if (!target.GivesValue)
        {
            CheckExpr(br.Value, scope);
            _de.Report("LYR-SEM0148", Severity.Error, br.Value.Span,
                "'break' gives a value only to a 'loop' — a 'while' or a 'for' may end without one");
            return;
        }
        var value = CheckExpr(br.Value, scope, target.Expected);
        if (target.Expected is { } wantedType) CheckAssignable(br.Value, value, wantedType, br.Value.Span);
        target.Values.Add(value);
    }

    private void Apply(Dictionary<Symbol, LyrType> facts)
    {
        foreach (var (sym, type) in facts) _narrowed[sym] = type;
    }

    // --- binding conditions and pattern bindings: if-let, while-let, let-else (§7.1, §7.4) ---

    /// <summary>
    /// <c>if (let Pattern = e) { … } else { … }</c>. The pattern's names live in the then
    /// branch only; the else branch sees the scope as it was. Nothing narrows: the binding IS
    /// the proof, and a fresh local of the narrowed type is what the branch gets (§7.4).
    /// </summary>
    private void CheckIfLet(IfStmt f, LetCondExpr letCond, SymbolTable scope)
    {
        var thenScope = BindLetCondition(letCond, scope, "'if let'");
        var snapshot = new Dictionary<Symbol, LyrType>(_narrowed, ReferenceEqualityComparer.Instance);
        CheckBlock(f.Then, thenScope);
        _narrowed = snapshot;
        if (f.Else is not null) CheckStmt(f.Else, scope);
    }

    /// <summary>Checks the initializer, binds the pattern into a scope of its own — the one the
    /// branch or body is checked in — and warns when the pattern cannot fail, because then the
    /// form promises a test it never performs.</summary>
    private SymbolTable BindLetCondition(LetCondExpr letCond, SymbolTable scope, string form)
    {
        var type = CheckExpr(letCond.Initializer, scope);
        _result.SetType(letCond, LyrType.Bool);
        var inner = new SymbolTable(scope);
        BindPattern(letCond.Pattern, type, inner);
        if (!type.IsError && IsIrrefutable(letCond.Pattern, type))
            _de.Report("LYR-SEM0104", Severity.Warning, letCond.Pattern.Span,
                $"this pattern matches every '{TypeFacts.Display(type)}' — the {form} never fails");
        return inner;
    }

    /// <summary>
    /// <c>let Pattern = e;</c> and <c>let Pattern = e else { … };</c>. The names are bound into
    /// the surrounding block; the else block is checked BEFORE them, because it runs when they
    /// do not exist, and it has to leave — the same rule the early exit of §7.4 follows, and
    /// what lets §7.7 count every name as assigned afterwards.
    /// </summary>
    private void CheckLetPattern(LetPatternStmt stmt, SymbolTable scope)
    {
        var declared = stmt.Type is null ? null : ResolveType(stmt.Type, scope);
        var actual = CheckExpr(stmt.Initializer, scope, declared);
        if (declared is not null) CheckAssignable(stmt.Initializer, actual, declared, stmt.Span);
        var source = declared ?? actual;

        if (stmt.Else is { } els)
        {
            CheckBlock(els, scope);
            if (!Flow.AlwaysExits(els, _result))
                _de.Report("LYR-SEM0098", Severity.Error, els.Span,
                    "the 'else' of a 'let … else' must leave on every path — return, throw, break, continue or panic — because the names are not bound when it runs");
        }

        if (source.IsError) { BindPoison(stmt.Pattern, scope); return; }

        var refutable = !IsIrrefutable(stmt.Pattern, source);
        if (refutable && stmt.Else is null)
            _de.Report("LYR-SEM0098", Severity.Error, stmt.Pattern.Span,
                $"this pattern can fail on a '{TypeFacts.Display(source)}' — add 'else {{ … }}' that leaves, or use 'match'");
        else if (!refutable && stmt.Else is not null)
            _de.Report("LYR-SEM0104", Severity.Warning, stmt.Else.Span,
                $"this pattern matches every '{TypeFacts.Display(source)}' — the 'else' never runs");

        BindPattern(stmt.Pattern, source, scope, stmt.IsMutable);
    }

    /// <summary>
    /// What a condition proves about nullable variables, separately for the true and the false
    /// outcome.
    ///
    /// <para>Composite conditions count: for <c>a &amp;&amp; b</c> the then branch gets what BOTH
    /// sides prove, because the branch runs only when both are true. For the else branch nothing
    /// holds: it is reached as soon as ONE side is false, and which one is unknown. For <c>||</c> it
    /// is exactly the other way round.</para>
    ///
    /// <para>That asymmetry is why the two directions are collected separately rather than as one
    /// fact with a sign.</para>
    /// </summary>
    private (Dictionary<Symbol, LyrType> then, Dictionary<Symbol, LyrType> els) NarrowingFacts(Expr cond)
    {
        var then = new Dictionary<Symbol, LyrType>(ReferenceEqualityComparer.Instance);
        var els = new Dictionary<Symbol, LyrType>(ReferenceEqualityComparer.Instance);

        if (cond is BinaryExpr { Operator: BinaryOp.LogicalAnd or BinaryOp.LogicalOr } logical)
        {
            var (leftThen, leftElse) = NarrowingFacts(logical.Left);
            var (rightThen, rightElse) = NarrowingFacts(logical.Right);

            if (logical.Operator == BinaryOp.LogicalAnd)
            {
                foreach (var (sym, type) in leftThen) then[sym] = type;
                foreach (var (sym, type) in rightThen) then[sym] = type;
            }
            else
            {
                foreach (var (sym, type) in leftElse) els[sym] = type;
                foreach (var (sym, type) in rightElse) els[sym] = type;
            }

            return (then, els);
        }

        // A test narrows by ONE level, from the type the name has where the test stands
        // (design/v5/spec/03 T4 O3): a '??int' proven present is a '?int', and a second test on
        // it reaches the 'int'. Reading the declared type instead narrowed every test to the same
        // level, which was the whole story while optionals did not nest.
        if (cond is BinaryExpr { Operator: BinaryOp.Ne or BinaryOp.Eq } b
            && NullCompared(b) is { } id
            && _result.RefOf(id) is { } sym2
            && (_result.TypeOf(id) is Optional tested ? tested : DeclaredType(sym2) as Optional) is { } opt)
        {
            (b.Operator == BinaryOp.Ne ? then : els)[sym2] = opt.Inner;
        }

        // 'x is Circle' (03 T11): in the branch the test guards, 'x' is a 'Circle' — the smart
        // cast, the same mechanism as the null test. Nothing is known in the other branch.
        if (cond is TypeTestExpr { Operand: IdentifierExpr tid } test
            && _result.RefOf(tid) is { } tsym && _typeTests.TryGetValue(test, out var tested2))
            then[tsym] = tested2;

        return (then, els);
    }

    private static IdentifierExpr? NullCompared(BinaryExpr b) => b switch
    {
        { Left: IdentifierExpr l, Right: NullLiteralExpr } => l,
        { Left: NullLiteralExpr, Right: IdentifierExpr r } => r,
        _ => null
    };

    // --- expressions ---

    // 'expected' is the context type of the position: the binding type, the return type, a field type.
    // Only the forms that need it use it — an empty array literal, a contextual enum variant
    // construction; everything else ignores it.
    /// <summary>
    /// An expression in VALUE POSITION. If it names a type or a module instead, that is an error here
    /// — exactly once, at the place where the value is needed. From there on <see cref="ErrorType"/>
    /// applies, and with it the ordinary "already reported" rule.
    /// </summary>
    /// <summary>
    /// How deeply a type or an expression may nest before the CHECK refuses (§12.4).
    ///
    /// <para>A separate bound from the parser's, and it has to be: the parse can accept what this
    /// walk cannot carry. A chain of a hundred thousand <c>+</c> is read by a LOOP and builds a
    /// left-leaning tree a hundred thousand deep — shallow to read, deep to walk — and it took the
    /// process down in <c>Compute</c>. A wall of <c>[]</c> did the same in <c>ResolveType</c> while
    /// the parser shrugged. That is also why §12.1 gives the two stages their own codes.</para>
    ///
    /// <para>The same number as the parser's, so a program is either accepted by both or refused
    /// by the earlier one, which reports at the better position.</para>
    /// </summary>
    private const int MaxNesting = Parsing.Parser.MaxNesting;

    private int _depth;

    /// <summary>Thrown when the bound is reached, caught once by <c>Check</c>. The same shape as
    /// the parser's, for the same reason: the recursion has to stop, and no frame between here and
    /// the top has anything sensible to return.</summary>
    private sealed class NestingTooDeep(Span at) : Exception
    {
        public Span At { get; } = at;
    }

    private LyrType Deeper(Span at)
    {
        _depth--;
        throw new NestingTooDeep(at);
    }

    private LyrType CheckExpr(Expr expr, SymbolTable scope, LyrType? expected = null)
    {
        if (++_depth > MaxNesting) return Deeper(expr.Span);
        try { return CheckExprInner(expr, scope, expected); }
        finally { _depth--; }
    }

    private LyrType CheckExprInner(Expr expr, SymbolTable scope, LyrType? expected)
    {
        var type = CheckTarget(expr, scope, expected);
        if (type is not NonValueType nv) return type;

        _de.Report("LYR-SEM0052", Severity.Error, expr.Span,
            $"'{nv.Symbol.Name}' is a {nv.Kind}, not a value{ValueHintFor(nv.Symbol)}");

        _result.SetType(expr, LyrType.Error);
        return LyrType.Error;
    }

    /// <summary>
    /// What to suggest for a TYPE standing where a value belongs.
    ///
    /// <para>The suggestion used to be <c>'X { … }'</c> for every type symbol there is, and only a
    /// struct or a class can take that advice. An enum is built through a variant, an interface is
    /// not built at all, and <c>let i = int;</c> was answered with "did you mean 'int { … }'?" —
    /// a form of the language that does not exist. A suggestion costs the reader the time it takes
    /// to try it, so a wrong one is worse than none.</para>
    ///
    /// <para>An ALIAS gets none either, deliberately: <c>type A = MyStruct</c> could take the
    /// brace form and <c>type A = int</c> could not, and telling them apart here means resolving
    /// the alias in a scope this method does not have. Silence is the answer that is never
    /// wrong.</para>
    /// </summary>
    private static string ValueHintFor(Symbol symbol) => symbol switch
    {
        TypeSymbol { Kind: TypeSymbolKind.Struct or TypeSymbolKind.Class } t
            => $" — did you mean '{t.Name} {{ … }}'?",
        TypeSymbol { Kind: TypeSymbolKind.Enum, Declaration: EnumDecl { Variants: [var first, ..] } } e
            => $" — did you mean a variant, such as '{e.Name}.{first.Name}'?",
        _ => "",
    };

    /// <summary>
    /// Like <see cref="CheckExpr"/>, but leaves a <see cref="NonValueType"/> standing. Only for the
    /// TARGET OF A MEMBER ACCESS, where a type or module name is legitimate (<c>Point.new(…)</c>,
    /// <c>console.println(…)</c>) and <c>CheckMember</c> continues through the symbol anyway, not
    /// through the type.
    /// </summary>
    private LyrType CheckTarget(Expr expr, SymbolTable scope, LyrType? expected = null)
    {
        var type = Compute(expr, scope, expected);
        _result.SetType(expr, type);
        return type;
    }

    private LyrType Compute(Expr expr, SymbolTable scope, LyrType? expected)
    {
        switch (expr)
        {
            case IntLiteralExpr il: return il.Suffix is { } isx ? IntSuffixType(isx) : LyrType.Int;
            case FloatLiteralExpr fl: return fl.Suffix is { } fsx ? FloatSuffixType(fsx) : LyrType.Float;
            case StringLiteralExpr: return LyrType.String;
            case CharLiteralExpr: return LyrType.Char;
            case BoolLiteralExpr: return LyrType.Bool;
            case NullLiteralExpr: return LyrType.Null;
            case IdentifierExpr id: return CheckIdentifier(id, scope, expected);
            case ImplicitMemberExpr im: return CheckImplicitMember(im, expected);
            case ThisExpr t:
                if (_currentThis is not null) return _currentThis;
                return Report(t.Span, "LYR-SEM0008", "'this' is only valid inside a method");
            case UnaryExpr u: return CheckUnary(u, scope);
            case PostfixExpr p: return CheckPostfix(p, scope);
            // '[x] * 64' where a 'T[64]' is expected builds the inline array at compile time (10 C7):
            // the count is a literal equal to the type's length.
            case BinaryExpr { Operator: BinaryOp.Mul, Left: ArrayLitExpr { Elements.Length: 1 } one, Right: IntLiteralExpr count } repeat
                when expected is InlineArrayOf inline:
            {
                var element = CheckExpr(one.Elements[0], scope, inline.Element);
                CheckAssignable(one.Elements[0], element, inline.Element, one.Elements[0].Span);
                _result.SetType(one, inline);
                _result.SetType(count, LyrType.Int);
                if (count.Value != (ulong)inline.Length)
                    return Report(repeat.Span, "LYR-SEM0001",
                        $"'{TypeFacts.Display(inline)}' holds {inline.Length} elements; the repetition makes {count.Value}");
                return inline;
            }
            case BinaryExpr b: return CheckBinary(b, scope);
            case AssignExpr a: return CheckAssign(a, scope);
            case RangeExpr r: return CheckRange(r, scope);
            case CastExpr c: return CheckCast(c, scope);
            case TypeTestExpr tt: return CheckTypeTest(tt, scope);
            case IndexExpr ix: return CheckIndex(ix, scope);
            case ArrayLitExpr arr: return CheckArrayLit(arr, scope, expected);
            case WithExpr w: return CheckWith(w, scope);
            case TupleLitExpr tu: return new TupleOf(tu.Elements.Select(e => CheckExpr(e, scope)).ToArray());
            case InterpolatedStringExpr fs:
                foreach (var seg in fs.Segments)
                    if (seg is InterpHole h)
                    {
                        var hole = CheckExpr(h.Expr, scope);
                        // An opaque value does not render: the converter would print the
                        // underlying and leak exactly what the wall hides. The way through is
                        // explicit, like every other crossing.
                        if (hole is OpaqueRef opaque)
                            _de.Report("LYR-SEM0006", Severity.Error, h.Expr.Span,
                                $"'{TypeFacts.Display(hole)}' is opaque and does not render in "
                                + "an f-string — convert explicitly: "
                                + $"'{{value as {TypeFacts.Display(opaque.Underlying)}}}'");
                        else if (hole is not PrimitiveType && !hole.IsError)
                            CheckDisplayHole(h, hole, scope);
                    }
                return LyrType.String;
            case ErrorExpr: return LyrType.Error;

            case CallExpr call: return CheckCall(call, scope, expected);
            case MemberExpr mem: return CheckMember(mem, scope, expected);
            case StructInitExpr si: return CheckStructInit(si, scope, expected);
            case TypePathExpr tp: return CheckTypePath(tp, scope, expected);
            case IfExpr iff: return CheckIfExpr(iff, scope, expected);
            case LoopExpr loop: return CheckLoop(loop, scope, expected);
            case LetCondExpr lc:
                // Reached only OUTSIDE an if/while head, where CheckIf/CheckWhile take it first.
                _de.Report("LYR-SEM0098", Severity.Error, lc.Span,
                    "a 'let' condition belongs directly in 'if (…)' or 'while (…)' — it cannot be combined or nested");
                CheckExpr(lc.Initializer, scope);
                BindPoison(lc.Pattern, new SymbolTable(scope));
                return LyrType.Bool;
            case MatchExpr ma:
            {
                var armTypes = CheckMatch(ma, ma.Scrutinee, ma.Arms, scope, asExpression: true, expected);
                // No arm delivers a value — every one is a block that leaves: the match never
                // produces anything, and 'never' says so; the lowering seals it as diverging.
                if (armTypes.Count == 0 && ma.Arms.Length > 0) return LyrType.Never;
                // With a context the arms were checked against it inside; the match HAS it.
                if (expected is not null && !expected.IsError) return expected;
                return UnifyArms(armTypes, ma.Span);
            }
            case LambdaExpr lam: return CheckLambda(lam, scope, expected);
            case ResumeExpr re: return CheckResume(re, scope);
            case ComptimeExpr ct: return CheckComptime(ct, scope, expected);
            case ThrowExpr te:
            {
                // The same rule as the statement (SEM0030); the difference is the type. 'never'
                // fits anywhere (IsAssignable) and drops out of every unification, so
                // 'x ?? throw e' is 'T' and a throwing arm leaves the other arms' type alone.
                CheckThrown(CheckExpr(te.Value, scope), te.Span);
                return LyrType.Never;
            }
            // The 'try' family (design/v5/spec/05 E4): the types here; what each form covers —
            // which sites, where they propagate — is the exception analysis's question.
            case TryExpr tried: return CheckTry(tried, scope, expected);
            // An attribute is not an expression: it describes the declaration it precedes and has
            // no value. Reporting that rather than silently yielding Error is the difference
            // between "does not work" and "does not work unnoticed".
            case AtIdentifierExpr at:
                return Report(at.Span, "LYR-SEM0053",
                    $"'{at.Name}' is not an expression — an attribute stands before a declaration");

            default: return LyrType.Error;
        }
    }

    /// <summary>
    /// The type of a global being read, and the place where "used before it is initialized" shows.
    ///
    /// <para>Globals are filled module by module in IMPORT order — every module after the ones it
    /// imports — and in declaration order within a module; reading a later one would see a null
    /// value nobody wrote. Go sorts the same way and refuses cycles, and so does this language —
    /// an import cycle is <c>LYR-RES0005</c> before the question ever arises.</para>
    ///
    /// <para>Only INSIDE an initializer is this an error. From a function body every global is
    /// readable wherever it stands, because the init phase is long over by then.</para>
    /// </summary>
    private LyrType TypeOfGlobalReference(GlobalSymbol symbol, Span span)
    {
        if (_globals.TryGetValue(symbol, out var type)) return type;
        if (!_inGlobalInitializer) return LyrType.Error;

        return Report(span, "LYR-SEM0057",
            $"'{symbol.Name}' is used before it is initialized; globals are initialized module by "
            + "module in import order and in declaration order within a module, so an initializer "
            + "may read what its own module declared earlier and anything from a module it "
            + "imports");
    }

    private LyrType CheckIdentifier(IdentifierExpr id, SymbolTable scope, LyrType? expected = null)
    {
        // A name that means SEVERAL functions, standing where a value is wanted rather than in
        // callee position. Nothing about the expression chooses, so the expected type has to: a
        // parameter of type 'fn(int) -> string' picks the overload with that shape, and without
        // one there is nothing to pick by.
        if (scope.Overloads(id.Name) is { Count: > 1 } overloads)
        {
            var wanted = expected as FnType;
            var matching = wanted is null
                ? []
                : overloads.Where(f => FitsFunctionType(FnTypeOf(f), wanted)).ToArray();

            if (matching.Length != 1)
            {
                _de.Report("LYR-SEM0089", Severity.Error, id.Span,
                    $"'{id.Name}' names {overloads.Count} functions, and "
                    + (wanted is null
                        ? "nothing here says which — a call chooses by its arguments, a value by "
                          + "the type it is wanted as"
                        : $"none of them is a '{TypeFacts.Display(wanted)}'"),
                    overloads.Select(f => new DiagnosticNote(f.Declaration?.Span ?? default,
                        $"this one is a '{TypeFacts.Display(FnTypeOf(f))}'")).ToArray());
                return LyrType.Error;
            }

            _result.BindRef(id, matching[0]);
            return FnTypeOf(matching[0]);
        }

        var sym = scope.Lookup(id.Name) ?? CoreMember(id.Name); // 'Ordering.Less', 'debugArray(xs)': std.core, unasked (10 U-series)
        if (sym is null)
            return Report(id.Span, "LYR-SEM0002", $"unknown identifier '{id.Name}'",
                NameSuggestion.Note(id.Name, NamesIn(scope, typesOnly: false)));
        _result.BindRef(id, sym);
        if (_narrowed.TryGetValue(sym, out var narrowed)) return narrowed; // ?T is T inside the narrowed region
        // A selective import (import std.io.console { println }) binds to a shell; the target is what
        // gets typed. Without this every stdlib call falls into the error branch and is skipped
        // silently, leaving the signature from the .lyr file without effect.
        if (sym is ImportBindingSymbol binding) sym = binding.Target;
        return sym switch
        {
            ParameterSymbol p => p.Type,
            LocalSymbol l => l.Type,
            GlobalSymbol g => TypeOfGlobalReference(g, id.Span),

            // A GENERIC function is not a function value (§8.1): fn values are monomorphic, and
            // an unsubstituted 'fn(T) -> T' fits no function type — it used to flow anyway and
            // explode at the use with a cascade about a 'T' nobody wrote. Callee position never
            // reaches this arm: CheckTargetOfCall short-circuits it, the way it already does for
            // an overload set.
            FunctionSymbol { Generics.Length: > 0 } gf => Report(id.Span, "LYR-SEM0052",
                $"generic function '{gf.Name}' is not a value — a function value is monomorphic; "
                + "call it, or write a lambda that calls it"),
            FunctionSymbol f => FnTypeOf(f),

            // A type or module name is not a value. As a member TARGET it is legitimate all the same,
            // so no immediate error here; CheckExpr reports it where a value is needed.
            TypeSymbol ts => new NonValueType(ts, "type"),
            GenericParamSymbol gp => new NonValueType(gp, "type parameter"),
            ModuleSymbol ms => new NonValueType(ms, "module"),

            // ExternalSymbol: the module could not be found and was reported as LYR-RES0003, so Error
            // is the correct poison here.
            _ => LyrType.Error
        };
    }

    private FnType FnTypeOf(FunctionSymbol f)
    {
        var fn = (FunctionDecl)f.Declaration!;
        var ps = fn.Parameters.Select(p => ResolveType(p.Type, _comp.Builtins)).ToArray();
        // 'never' is a return type like any other (05 E12): 'panic', 'unreachable' and 'todo' say it
        // in their declarations, and so may any function.
        var ret = fn.ReturnType is not null ? ResolveType(fn.ReturnType, _comp.Builtins) : LyrType.Void;
        return new FnType(ps, CoroutineThrowsOf(fn, ret, _comp.Builtins)) { Throws = DeclaredThrowsOf(fn) };
    }

    /// <summary>
    /// Where a coroutine function's <c>throws</c> clause belongs: on the COROUTINE it returns.
    ///
    /// <para><c>fn gen(): Coroutine&lt;int&gt; throws Exception</c> has one clause and it has always
    /// described one thing — that pulling this coroutine may throw. The call cannot: it runs no
    /// body, it builds a suspended frame. Until 3.0 the clause was checked at the call anyway,
    /// which is why the demand appeared to follow the local variable and vanished at the first
    /// field or optional (#73).</para>
    ///
    /// <para>Only when the return type IS a coroutine. A function returning <c>?Coroutine&lt;T&gt;</c>
    /// is not a coroutine function — it cannot yield — so its clause stays its own.</para>
    /// </summary>
    private LyrType CoroutineThrowsOf(FunctionDecl fn, LyrType declared, SymbolTable scope) =>
        fn.Throws is { } clause && declared is CoroutineOf co && co.Throws is null
            // A coroutine's type carries one thrown type until area 6 gives it a set (M6): one
            // named stays itself, several join to the root, as composed sets do (05 K7 Nachtrag).
            ? co with { Throws = ThrownTypeOf(clause.Types is [var one] ? one : null, scope) }
            : declared;

    /// <summary>What a <c>throws</c> names: the written type, or <c>std.core</c>'s <c>Error</c>
    /// when it names none — the "anything" the typeless clause means (05 E2 K2).</summary>
    private LyrType? ThrownTypeOf(TypeNode? written, SymbolTable scope) =>
        written is null
            ? _error is { } any ? new NamedRef(any) : null
            : ResolveType(written, scope);

    /// <summary>
    /// Every function a call's callee could mean. One entry — the ordinary case — is not an
    /// overload set and the caller does nothing with it.
    ///
    /// <para>Read off the tables the lookup itself used, and AFTER the callee was checked, so the
    /// receiver of a method call is already typed and nothing is checked twice.</para>
    /// </summary>
    private IReadOnlyList<OverloadCandidate> OverloadCandidates(Expr callee, SymbolTable scope)
    {
        switch (callee)
        {
            case IdentifierExpr id:
            {
                var set = scope.Overloads(id.Name);
                if (set.Count == 0) set = CoreOverloads(id.Name);
                return set.Select(f => new OverloadCandidate(f, FromExtension: false)).ToArray();
            }

            // A STATIC method or an enum's, where the receiver NAMES the type rather than being
            // a value of it: 'Id.of(7)'. The members are the same table; only the way in differs,
            // and reading the receiver's type as a value type finds nothing here.
            case MemberExpr mem when ReceiverTypeOf(mem) is NonValueType { Symbol: TypeSymbol named }:
                return named.Members.OverloadsLocal(mem.Member)
                    .Select(f => new OverloadCandidate(f, FromExtension: false)).ToArray();

            // A MODULE-qualified name ('net.localPort', alias or dotted path): the module's
            // members hold the whole overload set, and the qualified route must see the same
            // set a selective import brings — §4.3a would otherwise depend on the import
            // style. Only public members take part from the outside, as for the single lookup.
            case MemberExpr mem when ModuleReceiverOf(mem.Target, scope) is { } mod:
                return mod.Members.OverloadsLocal(mem.Member)
                    .Where(f => f.Visibility == Visibility.Public
                        || ReferenceEquals(mod, _currentModule))
                    .Select(f => new OverloadCandidate(f, FromExtension: false)).ToArray();

            // A method or an extension. Both scopes contribute, because both are searched when a
            // member is looked up: own members first, then the visible extensions.
            case MemberExpr mem when TypeFacts.SymbolOf(ReceiverTypeOf(mem)) is { } ts:
            {
                var found = ts.Members.OverloadsLocal(mem.Member)
                    .Select(f => new OverloadCandidate(f, FromExtension: false)).ToList();
                // Members of several CONFORMANCE blocks are scoped to their interfaces (04 D3),
                // not a set to choose from: the lookup refused the unqualified call already.
                var blocks = _comp.Extensions.MethodsFor(ts)
                    .Count(ext => ext.Symbol.Name == mem.Member && ext.InConformanceBlock);
                foreach (var ext in _comp.Extensions.MethodsFor(ts))
                    if (ext.Symbol.Name == mem.Member
                        && (_currentModule is null || _comp.Sees(_currentModule, ext.Module))
                        && !(blocks > 1 && ext.InConformanceBlock)
                        && !found.Any(c => ReferenceEquals(c.Fn, ext.Symbol)))
                        found.Add(new OverloadCandidate(ext.Symbol, FromExtension: true));
                return found;
            }

            default:
                return [];
        }
    }

    /// <summary>One function a call could mean, and whether it came from an <c>extend</c> block
    /// rather than from the type itself. The second half decides only where the first cannot: an
    /// own member and an extension that fit a call EQUALLY well are not an ambiguity, because the
    /// member has won that since extensions existed.</summary>
    private readonly record struct OverloadCandidate(FunctionSymbol Fn, bool FromExtension);

    /// <summary>The module a qualified callee's receiver names — an alias (<c>net</c>) or a
    /// dotted path (<c>std.io.net</c>) — and <c>null</c> for every receiver that is a value.
    /// </summary>
    private ModuleSymbol? ModuleReceiverOf(Expr target, SymbolTable scope)
    {
        switch (target)
        {
            case IdentifierExpr id:
            {
                var found = scope.Lookup(id.Name);
                if (found is ImportBindingSymbol ib) found = ib.Target;
                return found as ModuleSymbol;
            }
            case MemberExpr mem when ModuleReceiverOf(mem.Target, scope) is { } outer:
            {
                var inner = outer.Members.LookupLocal(mem.Member);
                if (inner is ImportBindingSymbol ib) inner = ib.Target;
                return inner as ModuleSymbol;
            }
            default:
                return null;
        }
    }

    /// <summary>The receiver's type as the checker recorded it a moment ago, with an optional
    /// receiver unwrapped the way the member lookup unwraps it.</summary>
    private LyrType ReceiverTypeOf(MemberExpr mem)
    {
        var target = _result.TypeOf(mem.Target);
        return mem.IsOptional && target is Optional opt ? opt.Inner : target ?? LyrType.Error;
    }

    /// <summary>The chosen overload's type, seen from the call site: through the receiver's
    /// instance for a method of a generic type, plain otherwise.</summary>
    private LyrType OverloadTypeOf(FunctionSymbol chosen, Expr callee) =>
        callee is MemberExpr mem && ReceiverTypeOf(mem) is GenericInstance gi
            ? Substitute(FnTypeOf(chosen), SubstMap(gi))
            : FnTypeOf(chosen);

    /// <summary>
    /// Which overload a call means.
    ///
    /// <para><b>The arguments decide, and only the arguments.</b> Not the return type — a call
    /// site does not always have one to offer, and a rule that sometimes reads the context and
    /// sometimes does not is a rule nobody can hold in their head.</para>
    ///
    /// <para>A LAMBDA argument takes no part: it has no type until a parameter gives it one, so
    /// letting it choose would be circular. Candidates are separated by the other arguments, and
    /// when they are not, the call is ambiguous and says so.</para>
    /// </summary>
    /// <returns><c>null</c> when nothing fits or too much does; the diagnostic is already
    /// reported.</returns>
    private FunctionSymbol? SelectOverload(CallExpr call, IReadOnlyList<OverloadCandidate> candidates,
        SymbolTable scope)
    {
        // ONE SYMBOL IS ONE CANDIDATE, however many times it reached this list. Importing a name
        // twice -- 'import std.io.console { println };' written on two lines -- put the same
        // FunctionSymbol into the set twice, and two copies of one function fit each other
        // exactly by construction: the call was then reported AMBIGUOUS between a function and
        // itself, with two identical notes pointing at one declaration.
        //
        // Deduplicated here rather than at the import, because a name may arrive by more than one
        // route -- a namespace import beside a selective one, a re-export -- and none of those
        // routes is the wrong one. What is wrong is counting a function twice because it was
        // named twice.
        if (candidates.Count > 1) candidates = candidates.Distinct().ToArray();

        // The count decides (04 D4), and the declaration rule made the count decisive: two
        // candidates of one scope never take the same number. Names decide nothing (D5 F7).
        var count = call.Arguments.Length;
        var fitting = candidates.Where(c => Takes(c.Fn, count)).ToList();

        if (fitting.Count == 0)
        {
            _de.Report("LYR-SEM0087", Severity.Error, call.Span,
                $"no '{candidates[0].Fn.Name}' takes {count} argument(s)",
                candidates.Select(c => new DiagnosticNote(c.Fn.Declaration?.Span ?? default,
                    $"this one takes {DisplayArity(c.Fn)}")).ToArray());
            return null;
        }

        // An own member and an extension of one count: the member, as since extensions exist.
        // 04 D2 makes the pair a declaration error (M4 S3); until then the member wins.
        if (fitting.Count > 1 && fitting.Any(f => !f.FromExtension))
            fitting = fitting.Where(f => !f.FromExtension).ToList();

        if (fitting.Count > 1)
        {
            _de.Report("LYR-SEM0086", Severity.Error, call.Span,
                $"'{candidates[0].Fn.Name}' is ambiguous for {count} argument(s): "
                + $"{fitting.Count} declarations take that many",
                fitting.Select(f => new DiagnosticNote(f.Fn.Declaration?.Span ?? default,
                    $"this one takes {DisplayArity(f.Fn)}")).ToArray());
            return null;
        }

        return fitting[0].Fn;
    }

    /// <summary>
    /// <c>same(a, b)</c>: whether two references are one object (design/v5/spec/02 M10).
    /// Identity belongs to references — a class, an array, a coroutine — and both sides have
    /// one type. On a value it is an ERROR, not <c>false</c>: a struct has no identity to ask
    /// about, not even a stable address, and an answer would invent one.
    /// </summary>
    private LyrType CheckSame(CallExpr call, IdentifierExpr callee, SymbolTable scope)
    {
        _result.BindRef(callee, _same!);
        var types = call.Arguments.Select(a => CheckExpr(a, scope)).ToArray();
        if (types.Length != 2)
            return Report(call.Span, "LYR-SEM0014", $"'same' takes two references, got {types.Length} argument(s)");
        if (types[0].IsError || types[1].IsError) return LyrType.Bool;

        foreach (var type in types)
        {
            var reference = TypeFacts.KindOf(type) == TypeSymbolKind.Class || type is ArrayOf or CoroutineOf;
            if (reference) continue;
            Report(call.Span, "LYR-SEM0003",
                $"'same' asks whether two references are one object, and '{TypeFacts.Display(type)}' is "
                + (type is Optional ? "an optional; narrow it first"
                    : "a value, which has no identity; compare values with '=='"));
            return LyrType.Bool;
        }

        if (!LyrType.Equal(types[0], types[1]))
            Report(call.Span, "LYR-SEM0003",
                $"'same' compares two references of one type, got '{TypeFacts.Display(types[0])}' and '{TypeFacts.Display(types[1])}'");
        return LyrType.Bool;
    }

    /// <summary>The interface an expression names — bare, or through a module — and <c>null</c>
    /// for everything else, a local of that name included.</summary>
    private static TypeSymbol? InterfaceNamed(Expr target, SymbolTable scope)
    {
        static Symbol? Unwrap(Symbol? symbol) => symbol is ImportBindingSymbol binding ? binding.Target : symbol;
        var named = target switch
        {
            IdentifierExpr id => Unwrap(scope.Lookup(id.Name)),
            MemberExpr { IsOptional: false, Target: IdentifierExpr holder } member
                when Unwrap(scope.Lookup(holder.Name)) is ModuleSymbol module
                => Unwrap(module.Members.LookupLocal(member.Member)),
            _ => null,
        };
        return named is TypeSymbol { Kind: TypeSymbolKind.Interface } iface ? iface : null;
    }

    /// <summary>The class or struct a callee NAMES, when the callee is a type name in call
    /// position: <c>Point</c>, or <c>geometry.Point</c> through a module. A local of that name
    /// shadows the type as it does everywhere. Generic types wait for the generics slice.</summary>
    private static TypeSymbol? ConstructedType(Expr callee, SymbolTable scope)
    {
        static Symbol? Unwrap(Symbol? symbol) => symbol is ImportBindingSymbol binding ? binding.Target : symbol;
        var named = callee switch
        {
            IdentifierExpr id => Unwrap(scope.Lookup(id.Name)),
            MemberExpr { IsOptional: false, Target: IdentifierExpr holder } member
                when Unwrap(scope.Lookup(holder.Name)) is ModuleSymbol module
                => Unwrap(module.Members.LookupLocal(member.Member)),
            _ => null,
        };
        return named is TypeSymbol { Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct, Generics.Length: 0 } type
            ? type : null;
    }

    /// <summary>
    /// <c>Point(1, 2)</c> is <c>Point.new(1, 2)</c> (design/v5/spec/08 Y9, 04 D11): a type name
    /// in call position means the type's factory, and <c>new</c> is an ordinary static
    /// function — there are no constructors, so there is no half-built <c>this</c>, and a
    /// factory may validate, answer an optional, throw, or hand out an object that exists. The
    /// call is checked as the member call it stands for and stored for the lowering, the seam
    /// the operators use.
    /// </summary>
    private LyrType CheckConstruction(CallExpr call, TypeSymbol type, SymbolTable scope, LyrType? expected)
    {
        if (type.Members.LookupLocal("new") is not FunctionSymbol { Declaration: FunctionDecl { IsStatic: true } })
        {
            foreach (var argument in call.Arguments) CheckExpr(argument, scope);
            return Report(call.Span, "LYR-SEM0013",
                $"'{type.Name}(…)' calls '{type.Name}.new(…)', and '{type.Name}' declares no 'static fn new' — "
                + $"build the value with its initializer, '{type.Name} {{ … }}', or declare the factory");
        }

        // MemberSpan stays invalid, as on the operator desugar: the text writes no 'new'.
        var factory = new MemberExpr(call.Callee, "new", IsOptional: false, call.Callee.Span) { MemberSpan = default };
        var meant = new CallExpr(factory, call.Arguments, call.Span) { ArgumentNames = call.ArgumentNames };
        var result = CheckExpr(meant, scope, expected);
        _result.DesugarOperator(call, meant);
        return result;
    }

    private string DisplayParameters(FunctionSymbol candidate) =>
        string.Join(", ", FnTypeOf(candidate).Parameters.Select(TypeFacts.Display));

    /// <summary>
    /// The callee of a call, checked with the expected RESULT type behind it.
    ///
    /// <para>Needed at exactly one place: an enum variant without written type arguments.
    /// <c>Opt.Some(7)</c> names the instance nowhere, <c>let o: Opt&lt;int&gt; = …</c> does — and the
    /// struct form <c>Ev.Hit { … }</c> has always read the context.</para>
    ///
    /// <para>The expected type belongs to the call, not to the callee; it is therefore used ONLY for
    /// resolving the instance and is not passed on as an expectation about the function type.</para>
    /// </summary>
    private LyrType CheckTargetOfCall(Expr callee, SymbolTable scope, LyrType? expected)
    {
        // A name meaning SEVERAL functions is not ambiguous here, and must not be reported as
        // though it were: this is the one position where something else chooses — the arguments,
        // a moment later in CheckCall. The first candidate is bound so the ordinary path has a
        // shape to work with, and the selection rebinds it.
        if (callee is IdentifierExpr id && scope.Overloads(id.Name) is { Count: > 1 } set)
        {
            _result.BindRef(id, set[0]);
            return FnTypeOf(set[0]);
        }

        // A GENERIC function is likewise legitimate here and nowhere else as a bare name: as a
        // VALUE it is refused (fn values are monomorphic, §8.1), as a callee the inference is
        // about to substitute it. The import shell stays the bound symbol, as CheckIdentifier
        // binds it, so unused-import accounting sees the same reference either way.
        if (callee is IdentifierExpr gid && (scope.Lookup(gid.Name) ?? CoreMember(gid.Name)) is { } found // 'debugArray(xs)': std.core, unasked
            && (found is ImportBindingSymbol ib ? ib.Target : found)
                is FunctionSymbol { Generics.Length: > 0 } generic)
        {
            _result.BindRef(gid, found);
            var declared = FnTypeOf(generic);
            _result.SetType(gid, declared); // hover reads the callee's type off the table
            return declared;
        }

        // The member-shaped callee ('m.ident(…)') resolves through CheckMember, which cannot
        // otherwise tell a call from a value use — the same distinction the identifier gets from
        // the short circuit above, carried as a per-node mark.
        if (callee is MemberExpr marked) _calleePosition.Add(marked);

        return callee is MemberExpr or ImplicitMemberExpr ? CheckExpr(callee, scope, expected) : CheckExpr(callee, scope);
    }

    /// <summary>
    /// <c>.Red</c>: a member of the type this position expects, the type unnamed (design/v5/spec/03
    /// T9, 08 Y9) — a variant as it stands, the callee of <c>.Num(3)</c>, a static member. The
    /// expectation comes from the position: an initializer with a written type, an argument, a
    /// return, an assignment, the other side of <c>==</c>. Where none is, the member has no type
    /// to belong to, and the diagnostic says to name it.
    /// </summary>
    private LyrType CheckImplicitMember(ImplicitMemberExpr im, LyrType? expected)
    {
        if (EnumFromExpected(expected) is not { } target)
        {
            var hint = expected is null || expected.IsError
                ? "this position expects no particular type; name the enum"
                : $"this position expects '{TypeFacts.Display(expected)}', which is not an enum";
            return Report(im.Span, "LYR-SEM0113", $"'.{im.Name}' names a member of the type this position expects — {hint}");
        }

        var (type, symbol) = MemberOfType(target.def, im.Name, im.Span, target.instance);
        if (symbol is not null) _result.BindRef(im, symbol);
        return type;
    }

    /// <summary>The forms that read the type they belong to from their position (Y9).</summary>
    private static bool IsImplicitForm(Expr e) =>
        e is ImplicitMemberExpr or CallExpr { Callee: ImplicitMemberExpr } or StructInitExpr { IsImplicit: true };

    private LyrType CheckUnary(UnaryExpr u, SymbolTable scope)
    {
        var t = CheckExpr(u.Operand, scope);
        if (t.IsError) return LyrType.Error;
        // An operand that gives no value leaves none to operate on (05 E12): neither does the expression.
        if (t is NeverType) return LyrType.Never;
        switch (u.Operator)
        {
            case UnaryOp.Not:
                if (!TypeFacts.IsBool(t)) BadOp(u.Span, "!", t);
                return LyrType.Bool;
            case UnaryOp.Neg:
                if (TypeFacts.IsNumeric(t)) return t;
                return DesugarUnary(u, t, scope, _neg, "neg", "-") ?? BadOpType(u.Span, "-", t);
            case UnaryOp.BitNot:
                if (TypeFacts.IsInteger(t)) return t;
                return DesugarUnary(u, t, scope, _bitNot, "bitNot", "~") ?? BadOpType(u.Span, "~", t);
            case UnaryOp.FromEnd: // as an index it never reaches here: CheckIndexValue takes it
                return Report(u.Span, "LYR-SEM0114",
                    "'^n' counts from the end inside '[…]' only — as the index, or as a bound of its range");
            default: // PreInc and PreDec
                if (!TypeFacts.IsNumeric(t)) BadOp(u.Span, "++/--", t);
                return t;
        }
    }

    /// <summary><c>-x</c> as <c>x.neg()</c>, <c>~x</c> as <c>x.bitNot()</c> (04 D6): the one
    /// conformance, the call checked through the ordinary member path and recorded as what the
    /// operator means. <c>null</c> where no interface takes part; the caller reports.</summary>
    private LyrType? DesugarUnary(UnaryExpr u, LyrType t, SymbolTable scope, TypeSymbol? iface, string method, string opText)
    {
        if (!CanConform(t) || iface is null) return null;
        if (!Satisfies(t, iface, new NamedRef(iface)))
        {
            _de.Report("LYR-SEM0003", Severity.Error, u.Span,
                $"'{opText}' is not defined for '{TypeFacts.Display(t)}' — it comes from '{iface.Name}': "
                + $"declare the type with ':: [{iface.Name}]' and a 'fn {method}(): {TypeFacts.Display(t)}'");
            return LyrType.Error;
        }
        var member = new MemberExpr(u.Operand, method, IsOptional: false, u.Span) { MemberSpan = default };
        var call = new CallExpr(member, [], u.Span);
        var type = CheckExpr(call, scope);
        if (type.IsError) return LyrType.Error;
        _result.DesugarOperator(u, call);
        return type;
    }

    private LyrType BadOpType(Span span, string op, LyrType t)
    {
        BadOp(span, op, t);
        return t;
    }

    private LyrType CheckPostfix(PostfixExpr p, SymbolTable scope)
    {
        var t = CheckExpr(p.Operand, scope);
        if (t.IsError) return LyrType.Error;
        switch (p.Operator)
        {
            case PostfixOp.ForceUnwrap:
                if (t is Optional o) return o.Inner;
                _de.Report("LYR-SEM0005", Severity.Error, p.Span, $"cannot force-unwrap non-nullable '{TypeFacts.Display(t)}'");
                return t;
            default: // Inc and Dec
                if (!TypeFacts.IsNumeric(t)) BadOp(p.Span, "++/--", t);
                return t;
        }
    }

    private LyrType CheckBinary(BinaryExpr b, SymbolTable scope)
    {
        // 'x == .Red': the implicit member takes the other side's type (08 Y9). When the
        // implicit side is the left one, the right side goes first and lends its type.
        if (b.Operator is BinaryOp.Eq or BinaryOp.Ne && IsImplicitForm(b.Left) && !IsImplicitForm(b.Right))
        {
            var rightFirst = CheckExpr(b.Right, scope);
            var leftAfter = CheckExpr(b.Left, scope, rightFirst.IsError ? null : rightFirst);
            if (leftAfter.IsError || rightFirst.IsError) return LyrType.Error;
            return CheckBinaryTyped(b, leftAfter, rightFirst, scope);
        }

        var l = CheckExpr(b.Left, scope);

        // Short circuit: the right side runs only when the left is true, and for '||' only when it is
        // false. So while checking the right side, what the left proved holds: 'x != null && x > 0'
        // is well formed, because x is no longer a '?int' in the second part.
        LyrType r;
        if (b.Operator is BinaryOp.LogicalAnd or BinaryOp.LogicalOr)
        {
            var (thenFacts, elseFacts) = NarrowingFacts(b.Left);
            var snapshot = new Dictionary<Symbol, LyrType>(_narrowed, ReferenceEqualityComparer.Instance);
            Apply(b.Operator == BinaryOp.LogicalAnd ? thenFacts : elseFacts);
            r = CheckExpr(b.Right, scope);
            EndScope(snapshot);
        }
        else
        {
            r = CheckExpr(b.Right, scope, b.Operator is BinaryOp.Eq or BinaryOp.Ne && IsImplicitForm(b.Right) && !l.IsError ? l : null);
        }

        if (l.IsError || r.IsError) return LyrType.Error;
        // An operand that gives no value fits every type (05 E12): it takes the other one's, so
        // 'ok || fail("…")' is a 'bool' and '1 + unreachable()' an 'int'. '??' has its own rule for
        // a right side that gives none.
        if (b.Operator is not BinaryOp.Coalesce)
        {
            if (l is NeverType && r is NeverType) return LyrType.Never;
            if (l is NeverType) l = r;
            else if (r is NeverType) r = l;
        }
        return CheckBinaryTyped(b, l, r, scope);
    }

    private LyrType CheckBinaryTyped(BinaryExpr b, LyrType l, LyrType r, SymbolTable scope)
    {
        switch (b.Operator)
        {
            case BinaryOp.Add: return CheckAdd(b, l, r, scope);
            case BinaryOp.Mul: return CheckMul(b, l, r, scope);
            case BinaryOp.Sub:
                return UnifyNumeric(b.Left, l, b.Right, r)
                       ?? DesugarArithmetic(b, l, r, scope, _sub, "sub", "-")
                       ?? BadBinary(b, l, r);
            case BinaryOp.Div:
                return UnifyNumeric(b.Left, l, b.Right, r)
                       ?? DesugarArithmetic(b, l, r, scope, _div, "div", "/")
                       ?? BadBinary(b, l, r);
            case BinaryOp.Rem:
                return UnifyNumeric(b.Left, l, b.Right, r)
                       ?? DesugarArithmetic(b, l, r, scope, _rem, "rem", "%")
                       ?? BadBinary(b, l, r);
            // The wrap operators are for integers only (08 Y4): a float knows no wrap (IEEE has
            // its infinities) and a 'char' is not a number (03 T1e).
            case BinaryOp.AddWrap or BinaryOp.SubWrap or BinaryOp.MulWrap:
                if (TypeFacts.IsInteger(l) && TypeFacts.IsInteger(r))
                    return UnifyNumeric(b.Left, l, b.Right, r) ?? BadBinary(b, l, r);
                return BadBinary(b, l, r);
            // The bit operators and the shifts: integers natively, a conforming type through its
            // interface (04 D6) — 'BitAnd<Rhs = Self>', 'Shl<Rhs = int>'.
            case BinaryOp.BitAnd or BinaryOp.BitXor or BinaryOp.BitOr or BinaryOp.Shl or BinaryOp.Shr:
            {
                if (TypeFacts.IsInteger(l) && TypeFacts.IsInteger(r))
                    return UnifyNumeric(b.Left, l, b.Right, r) ?? BadBinary(b, l, r);
                var (bitIface, bitMethod, bitText) = b.Operator switch
                {
                    BinaryOp.BitAnd => (_bitAnd, "bitAnd", "&"),
                    BinaryOp.BitOr => (_bitOr, "bitOr", "|"),
                    BinaryOp.BitXor => (_bitXor, "bitXor", "^"),
                    BinaryOp.Shl => (_shl, "shl", "<<"),
                    _ => (_shr, "shr", ">>"),
                };
                return DesugarArithmetic(b, l, r, scope, bitIface, bitMethod, bitText) ?? BadBinary(b, l, r);
            }
            case BinaryOp.Lt or BinaryOp.Le or BinaryOp.Gt or BinaryOp.Ge:
                // Numerics keep their opcodes. Two chars order by scalar value (Rust, Swift) —
                // that is not a comparison with a number, which T1e refuses. Everything else
                // orders through 'Ordered' — 'string < string' included, because the stdlib
                // conforms string to Ordered<string>.
                if (UnifyNumeric(b.Left, l, b.Right, r) is not null)
                    return LyrType.Bool;
                if (TypeFacts.IsChar(l) && TypeFacts.IsChar(r))
                    return LyrType.Bool;
                CheckOrdered(b, l, r, scope);
                return LyrType.Bool;
            case BinaryOp.Eq or BinaryOp.Ne:
                // '?T == ?T', and 'x == 5' with 'x: ?int' through the coercion (03 O6).
                if (OptionalEquality(l, r) is { } inner)
                {
                    CheckOptionalEquality(b, inner, scope);
                    return LyrType.Bool;
                }
                if (!LyrType.Equal(l, r) && UnifyNumeric(b.Left, l, b.Right, r) is null
                    && l is not NullType && r is not NullType)
                    BadBinary(b, l, r);
                else CheckEquatable(b, l, r, scope);
                return LyrType.Bool;
            case BinaryOp.LogicalAnd or BinaryOp.LogicalOr:
                if (!TypeFacts.IsBool(l) || !TypeFacts.IsBool(r)) BadBinary(b, l, r);
                return LyrType.Bool;
            default: // Coalesce
                return CheckCoalesce(b, l, r);
        }
    }

    /// <summary>
    /// What <c>==</c> and <c>!=</c> may compare: scalars, <c>null</c>, and every type that conforms
    /// to <c>Equatable</c>.
    /// </summary>
    /// <remarks>
    /// <para>On a conforming type the operator IS the interface method. The checker builds the call
    /// <c>a.equals(b)</c> from synthetic nodes, checks it through the ordinary member path — which
    /// settles extensions, generic instances and constraint dispatch exactly as a written call
    /// would — and records it for the lowering. No second dispatch mechanism: written as an
    /// operator, resolved as the method.</para>
    ///
    /// <para>CONFORMANCE is required, not the method alone. A type with an <c>equals</c> nobody
    /// declared as <c>Equatable</c> stays rejected — otherwise any method of that name would
    /// silently become an operator, and the diagnostic could not name the contract.</para>
    ///
    /// <para>For everything else the rule lives here as well as in the IR verifier. Without it the
    /// sema lets <c>a == b</c> through and the verifier rejects it afterwards, as a compiler crash
    /// rather than a diagnostic.</para>
    /// </remarks>
    /// <summary>The instance a conformance question about <paramref name="self"/> asks for: the
    /// bare interface where <c>Self</c> names the conformer (<c>Equatable</c>, 04 D6), the
    /// instance at the type where the library parametrizes it (<c>Equatable&lt;T&gt;</c> of 4.x).</summary>
    private static LyrType SelfInstance(TypeSymbol iface, LyrType self) =>
        iface.Generics.Length == 0 ? new NamedRef(iface) : new GenericInstance(iface, [self]);

    private void CheckEquatable(BinaryExpr b, LyrType l, LyrType r, SymbolTable scope)
    {
        if (l is NullType || r is NullType) { CheckNullTest(b, l, r); return; }
        if (l is PrimitiveType or ErrorType) return;

        // Two values of the SAME opaque alias compare like their underlying — a handle must be
        // able to find itself. Everything else about the alias stays walled off: mixed-type
        // equality already failed the Equal test before this method, arithmetic and ordering
        // never reach an opcode, and the lowering sees the underlying scalar here.
        if (l is OpaqueRef lo && r is OpaqueRef && LyrType.Equal(l, r)
            && lo.Underlying is PrimitiveType)
            return;

        var op = b.Operator is BinaryOp.Eq ? "==" : "!=";

        // NO unwrapping of '?T': comparing two optionals is not implemented in the backend, where
        // the verifier reports "equality comparison on type ?i64". The common case '?T == null' is
        // already handled above, and flow narrowing covers it too.
        if (l is Optional)
        {
            _de.Report("LYR-SEM0059", Severity.Error, b.Span,
                $"'{op}' is not defined for '{TypeFacts.Display(l)}' — an optional compares "
                + "against 'null'; narrow it first, then compare the value");
            return;
        }

        if (CanConform(l))
        {
            if (_equatable is { } equatable && Satisfies(l, equatable, SelfInstance(equatable, l)))
            {
                // A failed check inside the call has reported already; no second message.
                DesugarToMethodCall(b, "equals", scope);
                return;
            }

            _de.Report("LYR-SEM0059", Severity.Error, b.Span,
                $"'{op}' is not defined for '{TypeFacts.Display(l)}' — equality comes from "
                + $"'Equatable': declare the type with ':: [Equatable]' "
                + $"and a 'fn equals(o: {TypeFacts.Display(l)}): bool'");
            return;
        }

        _de.Report("LYR-SEM0059", Severity.Error, b.Span,
            $"'{op}' is not defined for '{TypeFacts.Display(l)}'");
    }

    /// <summary>
    /// <c>x == null</c> where <c>x</c> can never be null (<c>LYR-SEM0059</c>, §6.2).
    ///
    /// <para>The lowering refused it as <c>LYR-IR0001</c>, which says "this compiler version
    /// cannot lower it yet" about a program no version will ever run — §12.1 keeps that code for
    /// VALID Lyric. The pattern twin has been a sema error all along (<c>LYR-SEM0029</c>, a
    /// <c>null</c> pattern against a non-optional scrutinee); this is the expression side of the
    /// same sentence, and it now reaches <c>lyrc check</c> too.</para>
    ///
    /// <para>A BARE TYPE PARAMETER IS OPAQUE (design/v5/spec/03 T4 O2): no null operation on an
    /// expression of type <c>T</c> — not <c>== null</c>, not <c>??</c>, not <c>!</c>, not a
    /// <c>null</c> pattern — only on <c>?…</c>. A generic body is checked at its declaration, and
    /// there <c>T</c> is not an optional; that an instantiation may bind it to one changes
    /// nothing, because a generic <c>?T</c> at <c>T = ?int</c> is <c>??int</c> (O1), and the body
    /// asks about ITS level. Lyric 4 exempted the type parameter here and in <c>??</c> while
    /// <c>!</c> and the pattern refused it — four ways of asking with two answers.</para>
    /// </summary>
    private void CheckNullTest(BinaryExpr b, LyrType l, LyrType r)
    {
        // Which side is the value: 'null == x' is the same test written round the other way.
        var value = l is NullType ? r : l;

        if (value is NullType or ErrorType or Optional) return;

        var op = b.Operator is BinaryOp.Eq ? "==" : "!=";
        if (value is TypeParamType parameter)
        {
            _de.Report("LYR-SEM0059", Severity.Error, b.Span,
                $"'{op} null' on '{parameter.Param.Name}' — a bare type parameter is opaque: whether it is "
                + $"absent is asked of a '?{parameter.Param.Name}'",
                new DiagnosticNote($"declare the value '?{parameter.Param.Name}' if it may be absent"));
            return;
        }
        _de.Report("LYR-SEM0059", Severity.Error, b.Span,
            $"'{op} null' on '{TypeFacts.Display(value)}' — a value of this type is never null",
            new DiagnosticNote($"declare it '?{TypeFacts.Display(value)}' if it may be absent"));
    }

    /// <summary>Can this type conform to an interface at all? Only what can carry a conformance
    /// list — directly or through an <c>extend</c> block.</summary>
    private static bool CanConform(LyrType t) => t switch
    {
        NamedRef { Symbol.Kind: TypeSymbolKind.Struct or TypeSymbolKind.Class or TypeSymbolKind.Enum }
            => true,
        GenericInstance { Definition.Kind: TypeSymbolKind.Struct or TypeSymbolKind.Class or TypeSymbolKind.Enum }
            => true,
        TypeParamType => true,
        _ => false,
    };

    /// <summary>
    /// Builds <c>left.method(right)</c>, checks it, and records it as what the operator means.
    ///
    /// <para>The synthetic nodes carry the operator expression's span, so anything reported or
    /// mapped from them lands on what the user wrote. They reuse the REAL operand nodes, which is
    /// what makes the lowering evaluate each operand exactly once. The operands were clean — poison
    /// returned before the operator was examined — so their second pass through the checker
    /// reproduces the same table entries.</para>
    /// </summary>
    private LyrType DesugarToMethodCall(BinaryExpr b, string method, SymbolTable scope,
        (LyrType Type, Symbol Symbol)? target = null)
    {
        // MemberSpan stays invalid: this node is synthesized, the operator text carries no member
        // name an editor could point at or edit.
        var member = new MemberExpr(b.Left, method, IsOptional: false, b.Span)
            { MemberSpan = default };
        var call = new CallExpr(member, [b.Right], b.Span);
        if (target is { } t) _operatorTarget[member] = t;
        if (IsImplicitForm(b.Left)) _typedReceiver.Add(member);

        var type = CheckExpr(call, scope);
        if (type.IsError) return LyrType.Error;

        _result.DesugarOperator(b, call);
        return type;
    }

    /// <summary>
    /// An operator that IS a call of a <c>std.core</c> function: <c>equalOptionals(a, b)</c> for
    /// <c>?T == ?T</c>, <c>repeatArray(xs, n)</c> for <c>[x] * n</c> over objects. The name
    /// resolves as any callee does — the scope first, <c>std.core</c> unasked — and the call is
    /// checked like one written, so inference binds the function's <c>T</c> from the operands.
    /// </summary>
    private LyrType DesugarToFreeCall(BinaryExpr b, string function, SymbolTable scope, params Expr[] args)
    {
        var call = new CallExpr(new IdentifierExpr(function, b.Span), args, b.Span);
        var type = CheckExpr(call, scope);
        if (type.IsError) return LyrType.Error;
        _result.DesugarOperator(b, call);
        return type;
    }

    /// <summary>The value type of an equality between optionals (design/v5/spec/03 O6): two
    /// optionals of one type, or an optional and a value of its type, which coerces up at the
    /// argument. <c>null</c> where this is no such equality.</summary>
    private static LyrType? OptionalEquality(LyrType l, LyrType r) =>
        l is Optional lo && (LyrType.Equal(l, r) || LyrType.Equal(lo.Inner, r)) ? lo.Inner
        : r is Optional ro && LyrType.Equal(ro.Inner, l) ? ro.Inner
        : null;

    /// <summary>
    /// <c>?T == ?T</c>: both absent, or both present and equal through the value's own equality
    /// (03 O6) — <c>std.core</c>'s <c>equalOptionals</c> under <c>T :: [Equatable]</c>, which the
    /// scalars satisfy through their conformances in <c>std.core</c>. No ordering on an optional.
    /// </summary>
    private void CheckOptionalEquality(BinaryExpr b, LyrType inner, SymbolTable scope)
    {
        if (inner is ErrorType) return;
        var equatable = inner is PrimitiveType
            || (CanConform(inner) && _equatable is { } eq && Satisfies(inner, eq, SelfInstance(eq, inner)));
        if (!equatable)
        {
            _de.Report("LYR-SEM0059", Severity.Error, b.Span,
                $"'{(b.Operator is BinaryOp.Eq ? "==" : "!=")}' is not defined for '?{TypeFacts.Display(inner)}' — two optionals "
                + $"compare through their values, and '{TypeFacts.Display(inner)}' carries no 'Equatable'");
            return;
        }
        DesugarToFreeCall(b, "equalOptionals", scope, b.Left, b.Right);
    }

    /// <summary>Does a value of this type hold an object — a class, an array, a view, a closure,
    /// an interface value — directly or inside a struct, an enum payload, a tuple, an optional
    /// or an inline array? A string does not count: shared, nobody can tell. What <c>[x] * n</c>
    /// asks before it clones.</summary>
    private bool HoldsReference(LyrType type) => type switch
    {
        ArrayOf or SliceOf or FnType or CoroutineOf => true,
        Optional o => HoldsReference(o.Inner),
        InlineArrayOf ia => HoldsReference(ia.Element),
        TupleOf t => t.Elements.Any(HoldsReference),
        _ when TypeFacts.Is(type, TypeSymbolKind.Class) || TypeFacts.Is(type, TypeSymbolKind.Interface) => true,
        _ when TypeFacts.SymbolOf(type) is { Kind: TypeSymbolKind.Struct or TypeSymbolKind.Enum } ts =>
            PayloadTypes(ts, type).Any(HoldsReference),
        _ => false,
    };

    /// <summary>The field types of a struct, or every payload type of an enum, in the instance's
    /// terms.</summary>
    private IEnumerable<LyrType> PayloadTypes(TypeSymbol ts, LyrType instance)
    {
        var subst = instance is GenericInstance gi ? SubstMap(gi) : EmptySubst;
        LyrType Of(LyrType t) => instance is GenericInstance ? Substitute(t, subst) : t;
        switch (ts.Declaration)
        {
            case StructDecl or ClassDecl:
                foreach (var fs in ts.Members.Symbols.OfType<FieldSymbol>()) yield return Of(FieldType(fs));
                break;
            case EnumDecl e:
                foreach (var v in e.Variants)
                {
                    foreach (var t in v.TupleFields ?? []) yield return Of(ResolveType(t, ts.Members));
                    foreach (var f in v.StructFields ?? []) yield return Of(ResolveType(f.Type, ts.Members));
                }
                break;
        }
    }

    /// <summary>
    /// What <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> and <c>&gt;=</c> may compare beyond numerics:
    /// every type that conforms to <c>Ordered</c>.
    /// </summary>
    /// <remarks>
    /// <para>The desugar is <c>a.compare(b)</c>; which of the four operators stood there decides
    /// how the lowering reads the sign of the answer. One method, four operators — the reason
    /// <c>Ordered</c> has a single <c>compare</c> rather than four members.</para>
    ///
    /// <para><c>string</c> arrives here as a primitive and conforms through the stdlib's own
    /// <c>extend string :: [Ordered&lt;string&gt;]</c> — the same route any type takes, which is
    /// what finally admits <c>string &lt; string</c> without a rule of its own.</para>
    /// </remarks>
    private void CheckOrdered(BinaryExpr b, LyrType l, LyrType r, SymbolTable scope)
    {
        if (!LyrType.Equal(l, r))
        {
            BadBinary(b, l, r);
            return;
        }

        if ((CanConform(l) || l is PrimitiveType)
            && _ordered is { } ordered
            && Satisfies(l, ordered, SelfInstance(ordered, l)))
        {
            DesugarToMethodCall(b, "compare", scope);
            return;
        }

        if (CanConform(l))
        {
            var op = b.Operator switch
            {
                BinaryOp.Lt => "<",
                BinaryOp.Le => "<=",
                BinaryOp.Gt => ">",
                _ => ">=",
            };
            _de.Report("LYR-SEM0003", Severity.Error, b.Span,
                $"'{op}' is not defined for '{TypeFacts.Display(l)}' — ordering comes from "
                + $"'Ordered': declare the type with ':: [Ordered]' and a "
                + $"'fn compare(o: {TypeFacts.Display(l)}): ?Ordering'");
            return;
        }

        BadBinary(b, l, r);
    }

    private LyrType CheckAdd(BinaryExpr b, LyrType l, LyrType r, SymbolTable scope)
    {
        if (UnifyNumeric(b.Left, l, b.Right, r) is { } n) return n;
        if (TypeFacts.IsString(l) && TypeFacts.IsString(r)) return LyrType.String;      // "a" + "b"
        if (l is ArrayOf la && r is ArrayOf ra && LyrType.Equal(la.Element, ra.Element)) // [..] + [..]
            return new ArrayOf(la.Element);
        return DesugarArithmetic(b, l, r, scope, _add, "add", "+") ?? BadBinary(b, l, r);
    }

    private LyrType CheckMul(BinaryExpr b, LyrType l, LyrType r, SymbolTable scope)
    {
        if (UnifyNumeric(b.Left, l, b.Right, r) is { } n) return n;
        if (TypeFacts.IsString(l) && TypeFacts.IsInteger(r)) return LyrType.String;   // "x" * 3
        if (TypeFacts.IsString(r) && TypeFacts.IsInteger(l)) return LyrType.String;   // 3 * "x"
        // '[x] * n' repeats (10 C7); 'n * [x]' does not — one order, as for a string.
        if (l is ArrayOf la && TypeFacts.IsInteger(r))
        {
            // An element that is or holds an object is CLONED into every slot (03 §5.1), never
            // shared: the array is built by std.core's 'repeatArray' under 'T :: [Clone]'. A
            // value element copies natively.
            if (!HoldsReference(la.Element)) return new ArrayOf(la.Element); // [0] * 5
            // A shape — 'int[]', '?C', a tuple, a function — has no conformance of its own yet
            // (05 §13.6), and 'Satisfies' passes it through opaquely; asked here, not there.
            var cloneable = la.Element is NamedRef or GenericInstance or TypeParamType
                && _clone is not null && Satisfies(la.Element, _clone, SelfInstance(_clone, la.Element));
            if (!cloneable)
                return Report(b.Span, "LYR-SEM0136",
                    $"'[x] * n' with an element of type '{TypeFacts.Display(la.Element)}', which is or holds "
                    + "an object, clones every slot — declare the element type with ':: [Clone]', or build "
                    + "the array with 'arrayOf(n, (i) => …)'");
            return DesugarToFreeCall(b, "repeatArray", scope, b.Left, b.Right);
        }
        return DesugarArithmetic(b, l, r, scope, _mul, "mul", "*") ?? BadBinary(b, l, r);
    }

    /// <summary>
    /// Arithmetic on a conforming type: <c>a + b</c> IS <c>a.add(b)</c>, under the rules of
    /// equality and ordering — conformance required, homogeneous operands, the call checked through
    /// the ordinary member path.
    /// </summary>
    /// <returns><c>null</c> when this is not a case for an interface at all, so the caller falls to
    /// its own rejection; a reported <see cref="ErrorType"/> when it is one and the conformance is
    /// missing.</returns>
    private LyrType? DesugarArithmetic(BinaryExpr b, LyrType l, LyrType r, SymbolTable scope,
        TypeSymbol? iface, string method, string opText)
    {
        if (!CanConform(l) || iface is null) return null;

        // The conformance is picked by the RIGHT operand: 'Mul<float, Vec2>' is what 'v * 2.0'
        // asks for, and its SECOND argument is the result type. Homogeneous arithmetic is the
        // case where both arguments are the receiver's own type, and takes no separate rule.
        if (ArithmeticConformance(b, l, iface, r, method) is { } chosen)
            return DesugarToMethodCall(b, method, scope, chosen);

        var lt = TypeFacts.Display(l);
        var rt = TypeFacts.Display(r);
        var conformance = LyrType.Equal(l, r) ? iface.Name : $"{iface.Name}<{rt}>";
        _de.Report("LYR-SEM0003", Severity.Error, b.Span,
            $"'{opText}' is not defined for '{lt}' and '{rt}' — it comes from '{iface.Name}': "
            + $"declare the type with ':: [{conformance}]' and a "
            + $"'fn {method}(rhs: {rt}): {lt}'"
            + (LyrType.Equal(l, r) ? "" : $", or write the operand as a '{lt}'"));
        return LyrType.Error;
    }

    /// <summary>
    /// The implementation behind <c>a op b</c>: the conformance of <c>a</c>'s type to
    /// <paramref name="iface"/> whose first argument is <c>b</c>'s type, and the method the site
    /// that declared it provides.
    ///
    /// <para>Resolved HERE rather than by the member lookup because the name does not decide: two
    /// conformances contribute two <c>mul</c>, and only the operand type tells them apart. What
    /// comes back is bound to the synthetic member node, so the call — and the lowering after it
    /// — sees one target and no ambiguity.</para>
    /// </summary>
    /// <returns><c>null</c> when no conformance matches; the caller reports.</returns>
    private (LyrType Type, Symbol Symbol)? ArithmeticConformance(
        BinaryExpr b, LyrType receiver, TypeSymbol iface, LyrType argument, string method)
    {
        var candidates = ArithmeticCandidates(receiver, iface, method).ToList();

        // The written type first, an ADAPTED literal second. Two passes rather than one rule,
        // because an exact conformance has to win: a type carrying both 'Mul<int, T>' and
        // 'Mul<float, T>' multiplies by '2' through the int one, and the float one would be just
        // as reachable if a single pass took whatever matched first.
        if (Pick(candidates.Where(c => LyrType.Equal(c.Operand, argument))) is { } exact)
            return exact;

        var adapting = candidates
            .Where(c => c.Operand is PrimitiveType prim && LiteralAdaptsTo(b.Right, prim))
            .ToList();
        if (Pick(adapting) is not { } fitted) return null;

        AdaptLiteralType(b.Right, (PrimitiveType)adapting[0].Operand);
        return fitted;

        (LyrType Type, Symbol Symbol)? Pick(IEnumerable<ArithmeticCandidate> matching)
        {
            (LyrType Type, Symbol Symbol)? found = null;
            var ambiguous = false;
            foreach (var c in matching)
            {
                if (found is null) found = (c.Type, c.Implementation);
                else if (!ReferenceEquals(found.Value.Symbol, c.Implementation)) ambiguous = true;
            }

            // Two conformances agreeing on the operand and differing in what they call: '*' would
            // have two meanings on one pair of types, and nothing in the expression says which.
            // Refused rather than resolved by declaration order, which is not a rule anybody
            // could rely on.
            if (ambiguous)
                _de.Report("LYR-SEM0083", Severity.Error, b.Span,
                    $"'{TypeFacts.Display(receiver)}' conforms to '{iface.Name}' with "
                    + $"'{TypeFacts.Display(argument)}' more than once, and the two do not agree "
                    + "on what to call — one of them has to go");
            return found;
        }
    }

    /// <summary>One way to satisfy <c>a op b</c>: what the conformance takes on the right, and the
    /// function that serves it with its type in the receiver's terms.</summary>
    private readonly record struct ArithmeticCandidate(
        LyrType Operand, LyrType Type, FunctionSymbol Implementation);

    /// <summary>Everything the receiver's type could multiply (add, …) with, one entry per
    /// conformance to <paramref name="iface"/>.</summary>
    private IEnumerable<ArithmeticCandidate> ArithmeticCandidates(
        LyrType receiver, TypeSymbol iface, string method)
    {
        // A TYPE PARAMETER carries its conformance in a constraint, and there is no implementation
        // to point at: the call goes to the interface member and monomorphization fills it in per
        // instantiation. Which constraint, though, is the same question — 'T :: [Mul<float, T>]'
        // and 'T :: [Mul<T, T>]' both provide 'mul', and the operand tells them apart.
        if (receiver is TypeParamType tp)
        {
            foreach (var constraint in tp.Param.Constraints)
            {
                if (constraint is not NamedType nt) continue;
                foreach (var (it, subst) in ClosureOfNode(nt))
                {
                    if (!ReferenceEquals(it, iface) || it.Generics.Length is not (1 or 2)) continue;
                    if (!subst.TryGetValue(it.Generics[0], out var operand)) continue;
                    if (it.Members.LookupLocal(method) is not FunctionSymbol member) continue;
                    // 'Self' in the constraint is the parameter (03 T5); the 5 shape's 'Out'
                    // stays 'T.Out' until the constraint fixes it ('Add<Out = T>').
                    var withSelf = WithSelf(subst, it, receiver);
                    var signature = Substitute(FnTypeOf(member), withSelf);
                    if (ResolveType(nt, _currentModule?.Members ?? _comp.Builtins) is GenericInstance { Fixations: { Length: > 0 } fixations })
                        signature = ApplyFixations(signature, tp.Param, fixations);
                    yield return new ArithmeticCandidate(Substitute(operand, withSelf), signature, member);
                }
            }
            yield break;
        }

        if (TypeSymbolOf(receiver) is not { } ts) yield break;
        var ofInstance = receiver is GenericInstance gi ? SubstMap(gi) : EmptySubst;

        foreach (var (instance, site) in ConformancesTo(ts, iface, ofInstance))
        {
            // Two shapes: 'Add<T, R>' of the 4.x library, and 'Add<Rhs = Self>' with its 'Out'
            // answer (04 D6) — the one argument is the operand either way. Another shape is a
            // stdlib mismatch, not a program error; it matches nothing.
            if (instance is not GenericInstance { Arguments.Length: 1 or 2 } inst) continue;
            var operandType = Substitute(inst.Arguments[0], SelfMap(iface, receiver));
            if (ImplementationOf(ts, site, method, operandType, ofInstance) is not { } impl)
                continue;

            yield return new ArithmeticCandidate(
                operandType, Substitute(FnTypeOf(impl), ofInstance), impl);
        }
    }

    /// <summary>Every conformance of a type to ONE interface, with the site that declared it:
    /// <c>null</c> for the type's own <c>::</c> list, otherwise the <c>extend</c> block.
    ///
    /// <para>Unlike <see cref="InterfacesOf"/> this does NOT deduplicate by instance — the whole
    /// point is that a type may name the same interface twice with different arguments.</para>
    /// </summary>
    private IEnumerable<(LyrType Instance, ExtensionBlock? Site)> ConformancesTo(
        TypeSymbol ts, TypeSymbol iface, Dictionary<GenericParamSymbol, LyrType> ofInstance)
    {
        foreach (var node in DeclaredInterfaceNodes(ts))
            foreach (var instance in InstancesOfNode(node, ts, iface, ofInstance))
                yield return (instance, null);

        foreach (var block in _comp.Extensions.Blocks)
        {
            if (!ReferenceEquals(block.Target, ts)) continue;
            if (_currentModule is not null && !_comp.Sees(_currentModule, block.Module)) continue;
            var map = ofInstance;
            if (block.Generics.Length > 0)
            {
                if (BlockSubstitution(block, Substitute(SelfType(ts), ofInstance)) is not { } blockMap) continue;
                map = Merge(ofInstance, blockMap);
            }
            foreach (var node in block.Decl.Interfaces)
                foreach (var instance in InstancesOfNode(node, ts, iface, map))
                    yield return (instance, block);
        }
    }

    // What one conformance node contributes for 'iface' — itself when it names it, and every
    // parent that reaches it — in the conforming type's terms.
    private IEnumerable<LyrType> InstancesOfNode(TypeNode node, TypeSymbol ts, TypeSymbol iface,
        Dictionary<GenericParamSymbol, LyrType> ofInstance)
    {
        if (Conformance.InterfaceOf(node, _binding) is not { } direct) yield break;
        foreach (var (p, instance) in InterfaceClosure(direct, ResolveType(node, DeclarationScope(ts))))
            if (ReferenceEquals(p, iface))
                yield return Substitute(instance, ofInstance);
    }

    /// <summary>The method one conformance site provides: the block's own, or — for a conformance
    /// in the type's own list — its member, else a visible extension method that fits the
    /// operand.</summary>
    private FunctionSymbol? ImplementationOf(TypeSymbol ts, ExtensionBlock? site, string method,
        LyrType parameter, Dictionary<GenericParamSymbol, LyrType> ofInstance)
    {
        if (site is not null) return site.MethodScope.LookupLocal(method) as FunctionSymbol;
        if (ts.Members.LookupLocal(method) is FunctionSymbol own) return own;

        // The conformance stands on the type, the implementation in an extension — allowed since
        // extensions exist. With several of them the parameter type decides, which is the same
        // question the conformance check answers with SEM0042.
        foreach (var ext in _comp.Extensions.MethodsFor(ts))
        {
            if (ext.Symbol.Name != method) continue;
            if (_currentModule is not null && !_comp.Sees(_currentModule, ext.Module)) continue;
            if (Substitute(FnTypeOf(ext.Symbol), ofInstance) is FnType { Parameters.Length: 1 } fn
                && LyrType.Equal(fn.Parameters[0], parameter))
                return ext.Symbol;
        }
        return null;
    }

    /// <summary>The declaring symbol behind a conforming type, for the two forms that carry a
    /// conformance list of their own.</summary>
    private static TypeSymbol? TypeSymbolOf(LyrType t) => t switch
    {
        NamedRef nr => nr.Symbol,
        GenericInstance gi => gi.Definition,
        _ => null,
    };

    /// <summary>
    /// Gives an EMPTY array literal its element type from the context.
    /// </summary>
    /// <remarks>
    /// <para><c>[]</c> has no element type of its own; it comes from outside. Where the expected type
    /// is already at hand while checking (<c>let xs: int[] = []</c>), the ordinary context passing
    /// does it; where it is not, it has to be supplied afterwards.</para>
    /// <para>There are two such places: arguments, where the two-phase inference types non-lambda
    /// arguments without context, and <c>??</c>, where only <see cref="IsAssignable"/> is asked
    /// rather than <see cref="CheckAssignable"/>.</para>
    /// <para>The call has to come BEFORE any poison check: the type of the empty literal contains an
    /// <c>ErrorType</c>, but it is not a reported error — it is an open slot the context is about to
    /// close.</para>
    /// </remarks>
    private bool AdaptEmptyArray(Expr expr, LyrType to)
    {
        while (to is Optional optional) to = optional.Inner;

        if (expr is not ArrayLitExpr { Elements.Length: 0 } || to is not ArrayOf) return false;

        _result.SetType(expr, to);
        return true;
    }

    private LyrType CheckCoalesce(BinaryExpr b, LyrType l, LyrType r)
    {
        if (l is Optional o)
        {
            // Adapt first, then ask: 'source() ?? []' would otherwise fail on an '<error>[]' on the
            // right, although the target type stands next to it.
            if (AdaptEmptyArray(b.Right, o.Inner)) return o.Inner;

            if (IsAssignable(b.Right, r, o.Inner)) return o.Inner; // ?T ?? T yields T
            if (IsAssignable(b.Right, r, l)) return l;              // ?T ?? ?T yields ?T
            BadBinary(b, l, r);
            return o.Inner;
        }
        if (l is NullType) return r; // null ?? b yields the type of b

        // AN OPTIONAL-ONLY OPERATOR ON SOMETHING THAT IS NEVER NULL. This used to read "no effect,
        // but no type error" and leave the refusal to the lowering, as LYR-IR0001 — a code §12.1
        // keeps for VALID Lyric, carrying the note "this compiler version cannot lower it yet"
        // about a program no version will ever run. The force-unwrap beside it has been
        // LYR-SEM0005 all along; this is the same rule and now the same code, in the phase that
        // knows the types. 'lyrc check' sees it too, which it did not before.
        //
        // A bare type parameter is opaque, as CheckNullTest says (03 T4 O2): '??' is asked of a
        // '?T', never of a 'T' that an instantiation might happen to make optional.
        if (l is not ErrorType)
            _de.Report("LYR-SEM0005", Severity.Error, b.Span,
                l is TypeParamType
                    ? $"'??' on '{TypeFacts.Display(l)}' — a bare type parameter is opaque; '??' is asked of a '?{TypeFacts.Display(l)}'"
                    : $"'??' on '{TypeFacts.Display(l)}' — a value of this type is never null, so the "
                      + "right side can never be reached");

        return l;
    }

    private LyrType CheckAssign(AssignExpr a, SymbolTable scope)
    {
        CheckExpr(a.Target, scope); // binds RefOf
        var targetSym = a.Target is IdentifierExpr ? _result.RefOf(a.Target) : null;
        // For identifier targets take the DECLARED type rather than the narrowed one, or 'x = null' on
        // a narrowed ?T would wrongly be an error.
        var targetType = targetSym is not null ? DeclaredType(targetSym) ?? _result.TypeOf(a.Target) : _result.TypeOf(a.Target);

        // A compound assignment is the operator it carries: whatever 'a = a + b' says, 'a += b'
        // says too. Checked by synthesizing the binary and running it through CheckBinary — the
        // SAME rules as the written form, not a second copy of them.
        //
        // Until this check, the only rule on a compound was that the right operand be assignable to
        // the left, which holds for any value of the target's own type. 'p += p' on a struct passed
        // the checker and the lowering emitted an integer add over two references — the 's += "x"'
        // bug of v1.1.0, one type over, invisible in a release build where the verifier does not
        // run.
        //
        // The short-circuit and coalesce forms keep the old path: their meaning is not "apply the
        // operator to both sides" — '??=' assigns only when the target is null — and running the
        // left side through CheckBinary would apply narrowing rules meant for expressions.
        LyrType value;
        if (a.Operator is { } op
            and not (BinaryOp.LogicalAnd or BinaryOp.LogicalOr or BinaryOp.Coalesce))
        {
            var binary = new BinaryExpr(a.Target, op, a.Value, a.Span);
            value = CheckBinary(binary, scope);

            // The binary desugars through an operator interface: store the call ON THE ASSIGN,
            // where the lowering finds it and lowers it whole. For an identifier target that
            // evaluates the receiver exactly once — a local load has no side effects. A field or
            // element target would evaluate its object or index a second time through the call,
            // and whether that is allowed is a language question deliberately not answered by an
            // implementation detail — those stay written out.
            if (_result.OperatorCallOf(binary) is { } operatorCall)
            {
                if (a.Target is IdentifierExpr)
                    _result.DesugarOperator(a, operatorCall);
                else
                    _de.Report("LYR-SEM0003", Severity.Error, a.Span,
                        $"compound assignment through an operator interface needs a simple "
                        + $"variable target — write it out: 'a = a {OperatorText(op)} b'");
            }
        }
        else
        {
            value = CheckExpr(a.Value, scope, targetType);

            // '??=' carries the rule of the '??' it is named after: a target that can never be
            // null can never take the right side. Same code, same reason — see CheckCoalesce.
            if (a.Operator is BinaryOp.Coalesce
                && targetType is not (ErrorType or Optional or NullType))
                _de.Report("LYR-SEM0005", Severity.Error, a.Span,
                    targetType is TypeParamType
                        ? $"'??=' on '{TypeFacts.Display(targetType)}' — a bare type parameter is opaque; "
                          + $"'??=' is asked of a '?{TypeFacts.Display(targetType)}'"
                        : $"'??=' on '{TypeFacts.Display(targetType)}' — a value of this type is never "
                          + "null, so the assignment can never happen");
        }

        // The lvalue and mutability check happens in SemaRules; only type compatibility here.
        CheckAssignable(a.Value, value, targetType, a.Span);
        if (targetSym is not null) _narrowed.Remove(targetSym); // a reassignment drops the narrowing
        return targetType;
    }

    private static string OperatorText(BinaryOp op) => op switch
    {
        BinaryOp.Add => "+",
        BinaryOp.Sub => "-",
        BinaryOp.Mul => "*",
        BinaryOp.Div => "/",
        BinaryOp.Rem => "%",
        BinaryOp.BitAnd => "&",
        BinaryOp.BitOr => "|",
        BinaryOp.BitXor => "^",
        BinaryOp.Shl => "<<",
        BinaryOp.Shr => ">>",
        BinaryOp.AddWrap => "+%",
        BinaryOp.SubWrap => "-%",
        BinaryOp.MulWrap => "*%",
        BinaryOp.Lt => "<",
        BinaryOp.Le => "<=",
        BinaryOp.Gt => ">",
        BinaryOp.Ge => ">=",
        BinaryOp.Eq => "==",
        BinaryOp.Ne => "!=",
        BinaryOp.LogicalAnd => "&&",
        BinaryOp.LogicalOr => "||",
        BinaryOp.Coalesce => "??",
        _ => op.ToString(),
    };

    private static LyrType? DeclaredType(Symbol s) => s switch
    {
        LocalSymbol l => l.Type,
        ParameterSymbol p => p.Type,
        _ => null
    };

    /// <summary>
    /// <c>a..b</c>, which is a loop head and not a value.
    ///
    /// <para>The grammar has no range expression — <c>RangePattern</c> in a <c>match</c> and the
    /// iterable of a <c>for-in</c> are the two places <c>..</c> occurs — and
    /// <see cref="RangeOf"/> says of itself that it is the internal type of <c>0..9</c> and not a
    /// spec type. Nothing lowers it, so a range that escapes into a value position reached the
    /// backend and crashed there: <c>let r = 1..5;</c> and <c>[1..3]</c> both did, because an
    /// INFERRED type is the one case nothing checks it against. Where a type is written down the
    /// assignment already refused it.</para>
    ///
    /// <para>Refused here rather than turned into a lowering limit, because it is a rule and not a
    /// gap: a range has no representation to give it, and giving it one would be a language
    /// decision.</para>
    /// </summary>
    private LyrType CheckRange(RangeExpr r, SymbolTable scope)
    {
        var lo = CheckExpr(r.Low, scope);
        var hi = CheckExpr(r.High, scope);
        var elem = UnifyNumeric(r.Low, lo, r.High, hi);
        if (elem is null && !lo.IsError && !hi.IsError)
            _de.Report("LYR-SEM0003", Severity.Error, r.Span, $"range bounds must be matching numerics, got '{TypeFacts.Display(lo)}' and '{TypeFacts.Display(hi)}'");

        // In a 'for' head the range is the counted loop and no value (03 T13 A3); everywhere
        // else 'a..b' is a 'Range<T>' and 'a..=b' a 'RangeInclusive<T>' of std.core, holding its
        // bounds. Lyric 4 refused the value.
        if (ReferenceEquals(r, _rangeInPosition)) return new RangeOf(elem ?? LyrType.Error);
        if (elem is null || elem.IsError) return LyrType.Error;
        var symbol = r.IsInclusive ? _rangeInclusive : _range;
        if (symbol is null)
            return Report(r.Span, "LYR-SEM0090", "a range value needs 'Range' of std.core, which is not loaded");
        return new GenericInstance(symbol, [elem]);
    }

    /// <summary>
    /// <c>x as T</c>: numeric to numeric through an opcode, everything else through <c>Into</c>.
    /// </summary>
    /// <remarks>
    /// <para>The numeric branch stands FIRST and is not overridable: <c>1 as float</c> never
    /// desugars, whatever conformances exist. Beyond it, the cast is a conversion the operand's type
    /// declared — <c>x as T</c> is <c>x.into()</c> where the type conforms to <c>Into&lt;T&gt;</c>,
    /// checked and stored exactly as the operators are.</para>
    ///
    /// <para>Explicit only, by decision: an implicit conversion is a second, invisible mechanism
    /// beside the visible one. And ONE target per type — <c>into</c> is a member name, and a type
    /// has one member of a name; the second conversion is an ordinary named method.</para>
    /// </remarks>
    /// <summary>The types a test resolved, for the narrowing: the test stands in a condition,
    /// and the facts are read off the condition without a scope.</summary>
    private readonly Dictionary<TypeTestExpr, LyrType> _typeTests = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// <c>x is T</c> (03 T11): the operand is an interface value — on anything else the static
    /// type answers already (<c>LYR-SEM0131</c>) — and <c>T</c> a struct, class, enum or an
    /// interface a value can have. A type parameter is no test (O2): dynamic typing takes
    /// <c>Any</c>.
    /// </summary>
    private LyrType CheckTypeTest(TypeTestExpr t, SymbolTable scope)
    {
        var op = CheckExpr(t.Operand, scope);
        var target = ResolveType(t.Type, scope);
        if (op.IsError || target.IsError) return LyrType.Bool;
        if (TypeFacts.SymbolOf(op) is not { Kind: TypeSymbolKind.Interface })
        {
            _de.Report("LYR-SEM0131", Severity.Error, t.Span,
                $"'is' asks an interface value what it holds; a '{TypeFacts.Display(op)}' is known already"
                + (op is Optional ? " — test for null first, then the value" : ""));
            return LyrType.Bool;
        }
        if (CheckTestTarget(target, t.Type.Span)) _typeTests[t] = target;
        return LyrType.Bool;
    }

    /// <summary>What a value behind an interface can be: a struct, a class, an enum, or an
    /// interface that is a value type itself (04 D9).</summary>
    private bool CheckTestTarget(LyrType target, Span span)
    {
        switch (target)
        {
            case TypeParamType tp:
                _de.Report("LYR-SEM0131", Severity.Error, span,
                    $"no type test on the type parameter '{tp.Param.Name}' (03 T11): a value of 'Any' is what a dynamic test takes");
                return false;
            case NamedRef { Symbol.Kind: TypeSymbolKind.Interface } or GenericInstance { Definition.Kind: TypeSymbolKind.Interface }:
                if (!ValueUsable(TypeFacts.SymbolOf(target)!, out var reason))
                {
                    _de.Report("LYR-SEM0126", Severity.Error, span,
                        $"'{TypeFacts.Display(target)}' is a constraint, not a type for a value: {reason}");
                    return false;
                }
                return true;
            case NamedRef { Symbol.Kind: TypeSymbolKind.Struct or TypeSymbolKind.Class or TypeSymbolKind.Enum }
                or GenericInstance { Definition.Kind: TypeSymbolKind.Struct or TypeSymbolKind.Class or TypeSymbolKind.Enum }:
                return true;
            default:
                _de.Report("LYR-SEM0131", Severity.Error, span,
                    $"'{TypeFacts.Display(target)}' is never behind an interface value — only a struct, a class, an enum or an interface is");
                return false;
        }
    }

    private LyrType CheckCast(CastExpr c, SymbolTable scope)
    {
        var op = CheckExpr(c.Operand, scope);
        var target = ResolveType(c.Type, scope);
        if (op.IsError || target.IsError) return target;

        // 'as' between numbers always succeeds and is bit-near (03 T1d): a narrowing wraps, a
        // float to an integer saturates and NaN gives 0, an integer to a float rounds. A 'char'
        // meets only 'uint32' — outward freely, inward with a check of the scalar value at
        // runtime — and 'bool' meets no number at all: neither is a number (T1e), and a cast
        // that made one would be the arithmetic the type exists to refuse.
        if (TypeFacts.IsNumeric(op) && TypeFacts.IsNumeric(target)) return target;
        if (TypeFacts.IsChar(op) != TypeFacts.IsChar(target)
            && (op is PrimitiveType { Kind: PrimitiveKind.Uint32 } || target is PrimitiveType { Kind: PrimitiveKind.Uint32 }))
            return target;
        if (TypeFacts.IsChar(op) && TypeFacts.IsNumeric(target) || TypeFacts.IsNumeric(op) && TypeFacts.IsChar(target))
        {
            _de.Report("LYR-SEM0006", Severity.Error, c.Span,
                $"cannot cast '{TypeFacts.Display(op)}' to '{TypeFacts.Display(target)}' — a 'char' "
                + "converts to and from 'uint32' only, its scalar value; go through 'as uint32'");
            return target;
        }
        if (TypeFacts.IsBool(op) && (TypeFacts.IsNumeric(target) || TypeFacts.IsChar(target))
            || TypeFacts.IsBool(target) && (TypeFacts.IsNumeric(op) || TypeFacts.IsChar(op)))
        {
            _de.Report("LYR-SEM0006", Severity.Error, c.Span,
                $"cannot cast '{TypeFacts.Display(op)}' to '{TypeFacts.Display(target)}' — 'bool' is "
                + "not a number; write the test or the choice out ('x != 0', 'if (b) 1 else 0')");
            return target;
        }

        // An opaque alias converts to EXACTLY its underlying and back — the one door in its
        // wall, and it is explicit by construction: this cast is the only way through. At
        // runtime the value is untouched; the lowering emits nothing for it.
        if (op is OpaqueRef from && LyrType.Equal(from.Underlying, target)) return target;
        if (target is OpaqueRef to && LyrType.Equal(op, to.Underlying))
        {
            // Making one is the declaring module's privilege (§3.5): elsewhere the inward cast
            // forges a handle the alias exists to protect. An ERROR since 4.0 — 3.8 announced
            // it as a warning, the LYR-SEM0074 path, and this is the clock coming due. The
            // outward cast above stays free everywhere: reading the number breaks no promise.
            // A null module is a bare snippet, which has no module line to cross.
            if (_currentModule is { } here && !DeclaredInModule(to.Symbol, here))
            {
                var owner = _comp.Modules.FirstOrDefault(
                    m => ReferenceEquals(m.Members.LookupLocal(to.Symbol.Name), to.Symbol));
                var ownerName = owner?.FullName ?? "its declaring module";
                _de.Report("LYR-SEM0093", Severity.Error, c.Span,
                    $"making a '{to.Symbol.Name}' is '{ownerName}''s privilege — a handle is "
                    + $"issued, not assembled; let '{ownerName}' offer a constructor for values "
                    + "rebuilt from a stored number");
            }
            return target;
        }

        if (_into is { } into && (CanConform(op) || op is PrimitiveType)
            && Satisfies(op, into, new GenericInstance(into, [target])))
        {
            // MemberSpan stays invalid, as on the operator desugar: 'as T' writes no 'into'.
            var member = new MemberExpr(c.Operand, "into", IsOptional: false, c.Span)
                { MemberSpan = default };
            var call = new CallExpr(member, [], c.Span);

            if (!CheckExpr(call, scope).IsError) _result.DesugarOperator(c, call);
            return target;
        }

        _de.Report("LYR-SEM0006", Severity.Error, c.Span,
            $"cannot cast '{TypeFacts.Display(op)}' to '{TypeFacts.Display(target)}' — a "
            + $"conversion comes from 'Into': give '{TypeFacts.Display(op)}' the conformance "
            + $":: [Into<{TypeFacts.Display(target)}>]' with a 'fn into(): {TypeFacts.Display(target)}'");
        return target;
    }

    /// <summary>
    /// A hole whose value is not a scalar renders through <c>Display</c> (§6.6): <c>{p}</c> means
    /// <c>{p.show()}</c> when the type conforms, and the call is recorded as what the hole means —
    /// the same seam the operators and <c>as</c> use, so the lowering sees an ordinary method call.
    ///
    /// <para>A format specifier on such a hole is refused: <c>Display</c> has no spec language, and
    /// silently ignoring one would be the classic "it printed, but not what I asked for". What does
    /// not conform stays the error it was, with the conformance named instead of the converter.</para>
    /// </summary>
    private void CheckDisplayHole(InterpHole hole, LyrType type, SymbolTable scope)
    {
        // An interface VALUE renders through its vtable when the interface reaches Display —
        // 'd.show()' on a 'd: Display' is a dispatch, not a conformance question.
        var conforms = _display is { } display
                       && (type is NamedRef { Symbol.Kind: TypeSymbolKind.Interface } iface
                           ? Conformance.WithParents(iface.Symbol, _binding).Any(i => ReferenceEquals(i, display))
                           : CanConform(type) && Satisfies(type, display, new NamedRef(display)));
        if (!conforms)
        {
            _de.Report("LYR-SEM0006", Severity.Error, hole.Expr.Span,
                $"'{TypeFacts.Display(type)}' does not render in an f-string — a value renders "
                + "through 'Display': give the type the conformance ':: [Display]' with a "
                + "'fn show(): string', or narrow and convert it explicitly");
            return;
        }
        if (hole.FormatSpec is { } spec)
        {
            _de.Report("LYR-SEM0006", Severity.Error, hole.Span,
                $"a format specifier ':{spec}' does not apply to '{TypeFacts.Display(type)}' — "
                + "'Display' renders one way; format the text 'show()' answers instead");
            return;
        }
        // MemberSpan stays invalid, as on the operator desugar: '{p}' writes no 'show'.
        var member = new MemberExpr(hole.Expr, "show", IsOptional: false, hole.Expr.Span)
            { MemberSpan = default };
        var call = new CallExpr(member, [], hole.Expr.Span);
        if (!CheckExpr(call, scope).IsError) _result.DesugarOperator(hole, call);
    }

    private LyrType CheckIndex(IndexExpr ix, SymbolTable scope)
    {
        var target = CheckExpr(ix.Target, scope);

        // 'xs[a..b]' takes a view (03 T13 A2): a Slice<T> of an array or of a view, sharing the
        // elements. The bounds are indices, '^n' among them, either left open.
        if (ix.Index is SliceRangeExpr range)
        {
            if (range.Low is not null) CheckIndexValue(range.Low, target, scope);
            if (range.High is not null) CheckIndexValue(range.High, target, scope);
            return target switch
            {
                ArrayOf a => new SliceOf(a.Element),
                SliceOf s => s,
                InlineArrayOf ia => ViewOfInline(ix.Target, ia, ix.Span),
                ErrorType => LyrType.Error,
                _ => Report(ix.Span, "LYR-SEM0007",
                    $"'{TypeFacts.Display(target)}' has no view to take — a range indexes an array or a 'Slice<T>'"),
            };
        }

        CheckIndexValue(ix.Index, target, scope);
        return target switch
        {
            ArrayOf a => a.Element,
            SliceOf s => s.Element,
            InlineArrayOf ia => ia.Element,

            // A string has NO index operator. A code point position costs O(n) — a 'char' is a code
            // point and the length counts the same units — so the obvious indexing loop would be
            // quadratic without looking like it.
            //
            // The message names both ways out: 'charAt' is what the user is looking for right now,
            // 'for-in' is what they usually want.
            PrimitiveType { Kind: PrimitiveKind.String } =>
                Report(ix.Span, "LYR-SEM0007",
                    "a string cannot be indexed — a codepoint position costs O(n), so an index "
                    + "loop would be quadratic. Use 's.charAt(i)' if you really need "
                    + "one position, or 'for (c in s)' to walk all of them"),

            ErrorType => LyrType.Error,

            // Everything else has to satisfy 'Indexable<T>' from std.collections — the same rule and
            // mechanism 'for-in' uses for 'Iterator<T>'. The compiler knows exactly ONE built-in
            // indexable form, the array; every other container goes through the interface.
            _ => TypeArgumentOfConformance(target, _indexable)
                 ?? Report(ix.Span, "LYR-SEM0007",
                     $"'{TypeFacts.Display(target)}' is not indexable — it must implement "
                     + "'Indexable<T>' from std.collections")
        };
    }

    /// <summary>
    /// The index is an <c>int</c> (03 T14 N4): a narrower integer widens to it as at any
    /// coercion site, a <c>uint</c> or a <c>uint64</c> is converted with <c>as</c>, nothing else
    /// stands there. <c>^n</c> counts from the end (N6): sugar for <c>length() - n</c> of the
    /// indexed value, so it stands inside <c>[…]</c> only and on a value that has a length.
    /// </summary>
    private void CheckIndexValue(Expr index, LyrType target, SymbolTable scope)
    {
        if (index is UnaryExpr { Operator: UnaryOp.FromEnd } fromEnd)
        {
            var n = CheckExpr(fromEnd.Operand, scope, LyrType.Int);
            if (!n.IsError) CheckAssignable(fromEnd.Operand, n, LyrType.Int, fromEnd.Operand.Span);
            _result.SetType(fromEnd, LyrType.Int);
            if (target is not (ArrayOf or SliceOf or InlineArrayOf or ErrorType))
                _de.Report("LYR-SEM0114", Severity.Error, fromEnd.Span,
                    $"'^' counts from the end of a value that has a length; '{TypeFacts.Display(target)}' has none");
            return;
        }

        var t = CheckExpr(index, scope, LyrType.Int);
        if (t.IsError) return;
        if (TypeFacts.IsInteger(t) && !LyrType.Equal(t, LyrType.Int) && !TypeFacts.Widens(t, LyrType.Int))
            _de.Report("LYR-SEM0007", Severity.Error, index.Span,
                $"an index is an 'int'; a '{TypeFacts.Display(t)}' does not widen to it — convert with 'as int'");
        else if (!TypeFacts.IsInteger(t))
            _de.Report("LYR-SEM0007", Severity.Error, index.Span, $"an index is an 'int', got '{TypeFacts.Display(t)}'");
    }

    /// <summary>
    /// A view of an inline array is taken where the array is HEAP-RESIDENT (03 T13 A4): a field
    /// of an object, an element of an array or of a view, or inside a struct that is — reached
    /// through a reference somewhere on the way. A view of a local, a parameter or a field of a
    /// struct local would point into a frame that may end before the view does; that array is
    /// copied or passed whole instead.
    /// </summary>
    private LyrType ViewOfInline(Expr array, InlineArrayOf type, Span span)
    {
        if (IsHeapResident(array)) return new SliceOf(type.Element);
        return Report(span, "LYR-SEM0115",
            $"a view of an inline array is taken only where the array lies in the heap — a field of an object or an element of an array; "
            + "this one lies in a frame. Copy it into an object, or pass the array itself");
    }

    private bool IsHeapResident(Expr expr)
    {
        switch (expr)
        {
            case IndexExpr ix:
                return _result.TypeOf(ix.Target) is ArrayOf or SliceOf || IsHeapResident(ix.Target);
            case MemberExpr m:
                return TypeFacts.KindOf(_result.TypeOf(m.Target)) == TypeSymbolKind.Class || IsHeapResident(m.Target);
            case ThisExpr t:
                return TypeFacts.KindOf(_result.TypeOf(t)) == TypeSymbolKind.Class;
            default:
                return false;
        }
    }

    private LyrType CheckArrayLit(ArrayLitExpr arr, SymbolTable scope, LyrType? expected)
    {
        // '[a, b, c]' where a 'T[3]' is expected builds the inline array (03 T13 A4; 10 C7): the
        // literal has exactly the length the type says, or it is refused.
        if (expected is InlineArrayOf inline)
        {
            foreach (var element in arr.Elements)
                CheckAssignable(element, CheckExpr(element, scope, inline.Element), inline.Element, element.Span);
            if (arr.Elements.Length != inline.Length)
                return Report(arr.Span, "LYR-SEM0001",
                    $"'{TypeFacts.Display(inline)}' holds {inline.Length} elements; the literal has {arr.Elements.Length}");
            return inline;
        }

        var elemExpected = expected is ArrayOf ea ? ea.Element : null;
        if (arr.Elements.Length == 0)
            return new ArrayOf(elemExpected ?? LyrType.Error); // empty: the element type comes from the context alone
        // With a context the elements check AGAINST it (§3.1 since 2.1): an unsuffixed literal
        // adapts, a misfit is the ordinary assignment error per element, and the array has the
        // context's element type. Without one the elements unify among themselves, as always.
        if (elemExpected is not null && !elemExpected.IsError)
        {
            foreach (var element in arr.Elements)
                CheckAssignable(element, CheckExpr(element, scope, elemExpected), elemExpected,
                    element.Span);
            return new ArrayOf(elemExpected);
        }

        var first = CheckExpr(arr.Elements[0], scope, elemExpected);
        for (var i = 1; i < arr.Elements.Length; i++)
        {
            var t = CheckExpr(arr.Elements[i], scope, elemExpected);
            if (!LyrType.Equal(t, first) && !t.IsError && !first.IsError)
                _de.Report("LYR-SEM0009", Severity.Error, arr.Elements[i].Span,
                    $"array elements must share a type: '{TypeFacts.Display(first)}' vs '{TypeFacts.Display(t)}'");
        }
        return new ArrayOf(first);
    }

    // --- calls, members, struct initializers, composites ---

    // Two phases: type the non-lambda arguments eagerly, infer the type arguments from them, then
    // check the lambdas with the substituted parameter type as context — their actual type binds the
    // remaining type arguments, such as U from the lambda return.
    /// <param name="expected">The expected type of the CALL. Needed for an enum variant alone:
    /// 'let o: Opt&lt;int&gt; = Opt.Some(7);' names the instance only on the left.</param>
    private LyrType CheckCall(CallExpr call, SymbolTable scope, LyrType? expected = null)
    {
        // The two calls that are not calls of what their callee names: identity, which the
        // compiler answers itself, and a type name, which means its factory.
        if (call.Callee is IdentifierExpr identity && _same is not null
            && ReferenceEquals(scope.Lookup(identity.Name), _same))
            return CheckSame(call, identity, scope);
        if (ConstructedType(call.Callee, scope) is { } constructed)
            return CheckConstruction(call, constructed, scope, expected);

        // 'Walker.describe(x)' (04 D2 R5): the interface's member, called with its receiver as
        // the first argument — the qualified form that reaches an implementation the unqualified
        // call cannot name (D3). Checked as the method call it stands for, on a receiver that
        // conforms, with the member settled here rather than looked up on the receiver's type.
        if (call.Callee is MemberExpr { IsOptional: false } qualified
            && InterfaceNamed(qualified.Target, scope) is { } named
            && Conformance.WithParents(named, _binding)
                .Select(i => i.Members.LookupLocal(qualified.Member)).OfType<FunctionSymbol>()
                .FirstOrDefault() is { IsStatic: false, Generics.Length: 0 } promised
            && named.Generics.Length == 0)
        {
            if (call.Arguments.Length == 0)
                return Report(call.Span, "LYR-SEM0014",
                    $"'{named.Name}.{qualified.Member}' takes the receiver as its first argument");
            var receiverExpr = call.Arguments[0];
            var receiverType = CheckExpr(receiverExpr, scope);
            var conforms = receiverType.IsError
                || (receiverType is NamedRef { Symbol: { Kind: TypeSymbolKind.Interface } held }
                    ? Conformance.WithParents(held, _binding).Any(p => ReferenceEquals(p, named))
                    : TypeFacts.SymbolOf(receiverType) is { } rs && ConformsTo(rs, named));
            if (!conforms)
                return Report(receiverExpr.Span, "LYR-SEM0125",
                    $"'{TypeFacts.Display(receiverType)}' does not conform to '{named.Name}', so "
                    + $"'{named.Name}.{qualified.Member}' is not one of its members");

            var member = new MemberExpr(receiverExpr, qualified.Member, IsOptional: false, qualified.Span) { MemberSpan = qualified.MemberSpan };
            var names = call.ArgumentNames is { } all ? all.Skip(1).ToArray() : null;
            var meant = new CallExpr(member, call.Arguments[1..], call.Span) { ArgumentNames = names is { } n && n.Any(x => x is not null) ? n : null };
            _typedReceiver.Add(member);
            _operatorTarget[member] = (FnTypeOf(promised), promised);
            var result = CheckExpr(meant, scope, expected);
            _result.DesugarOperator(call, meant);
            return result;
        }

        var calleeType = CheckTargetOfCall(call.Callee, scope, expected);

        // OVERLOADING: the lookup above answered with the first function of the name, which is the
        // right answer whenever there is only one. With several, the arguments decide, and the
        // decision is recorded by rebinding the callee — from here on everything downstream, the
        // lowering included, reads one target and knows nothing of the set.
        if (!(call.Callee is MemberExpr settled && _operatorTarget.ContainsKey(settled))
            && OverloadCandidates(call.Callee, scope) is { Count: > 1 } candidates)
        {
            if (SelectOverload(call, candidates, scope) is not { } chosen) return LyrType.Error;
            _result.BindRef(call.Callee, chosen);
            calleeType = OverloadTypeOf(chosen, call.Callee);
        }

        // 'b?.get()' — optional chaining with a CALL. 'b?.get' is a '?fn() -> int', and without this
        // case the sema reports '"?fn() -> int" is not callable': a statement about an intermediate
        // type nobody wrote down.
        //
        // The call is checked against the unwrapped signature and the RESULT becomes optional: when
        // the receiver is empty no call happens and there is nothing to return.
        //
        // For a method ONLY. If the member holds a function VALUE ('f: fn() -> int') there are two
        // questions and one '?': whether the receiver is there, and whether the field is set.
        // Unwrapping here would answer the second one with yes, and for 'f: ?fn() -> int' that would
        // be a call on null. The language has no '?()', so the form does not exist.
        var optionalCall = false;
        if (call.Callee is MemberExpr { IsOptional: true } chainCallee && calleeType is Optional o)
        {
            if (_result.RefOf(chainCallee) is FunctionSymbol && o.Inner is FnType method)
            {
                optionalCall = true;
                calleeType = method;
            }
            else if (o.Inner is FnType)
            {
                foreach (var a in call.Arguments) CheckExpr(a, scope);
                return Report(call.Span, "LYR-SEM0062",
                    $"'?.' with a call works on a method; '{chainCallee.Member}' holds a function "
                    + "value — read it into a variable first, then call that");
            }
        }

        if (calleeType is not FnType fn)
        {
            foreach (var a in call.Arguments) CheckExpr(a, scope); // type them anyway, to avoid follow-up errors
            if (calleeType.IsError) return LyrType.Error;
            return Report(call.Span, "LYR-SEM0013", $"'{TypeFacts.Display(calleeType)}' is not callable");
        }

        var fsym = TargetSymbol(call.Callee) as FunctionSymbol;
        var decl = fsym?.Declaration as FunctionDecl;
        // In parameter order, a named argument where its name says (04 D5); a hole is a parameter
        // the call leaves to its default.
        var args = ArrangeArguments(call, decl);
        var argTypes = new LyrType?[args.Length];

        // Phase A: non-lambdas, eagerly — with the declared parameter type as the context, the same
        // one phase C gives a lambda. It is what lets 'f(Opt.Some(5))' name its instance: without
        // it an argument position was the one value position with no expected type, while a
        // binding, a return and a field all had one.
        //
        // Only when the parameter is CONCRETE. In 'fn f<T>(o: Opt<T>)' the parameter still holds the
        // type parameter, and offering 'Opt<T>' as the expected type would fix the instance to
        // something the inference is supposed to determine from this very argument.
        for (var i = 0; i < args.Length; i++)
            if (args[i] is { } given && given is not LambdaExpr)
                argTypes[i] = CheckExpr(given, scope, ConcreteExpectation(fn, decl, i, given));

        // Phase B: type arguments from the eagerly typed arguments.
        Dictionary<GenericParamSymbol, LyrType>? map = null;
        var substituted = fn;
        if (fsym is { Generics.Length: > 0 })
        {
            map = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);

            // Explicitly written type arguments ('f<int>()') bind FIRST. The inference then runs
            // unchanged and only fills what is still open: 'UnifyInfer' uses 'TryAdd' and therefore
            // overwrites nothing. What is written always wins, and 'id<int>("x")' becomes a type
            // error instead of silently turning into 'id<string>'.
            //
            // They are needed where the arguments give nothing: a factory 'empty<T>(): List<T>' has
            // none and would not be callable without this route.
            if (call.TypeArguments is { Length: > 0 } written)
            {
                if (written.Length != fsym.Generics.Length)
                    _de.Report("LYR-SEM0026", Severity.Error, call.Span,
                        $"generic function '{fsym.Name}' expects {fsym.Generics.Length} type "
                        + $"argument(s), got {written.Length}");

                // A placeholder ('collect<_, int>(xs)', 03 T8) is not bound here: it is the one
                // position the inference below is asked to fill, the way it fills every argument
                // of a call written without any. The constraints of a list with a placeholder are
                // checked once the list is complete, with the inferred ones.
                var explicitArgs = new LyrType[Math.Min(written.Length, fsym.Generics.Length)];
                var placeholders = false;
                for (var i = 0; i < explicitArgs.Length; i++)
                {
                    if (IsPlaceholder(written[i])) { placeholders = true; continue; }
                    explicitArgs[i] = ResolveType(written[i], scope);
                    map[fsym.Generics[i]] = explicitArgs[i];
                }

                // Constraints apply to written arguments too, or the explicit form would be a way
                // around them.
                if (!placeholders) CheckConstraints(fsym.Generics, explicitArgs, call.Span);
            }

            var n = Math.Min(fn.Parameters.Length, args.Length);
            for (var i = 0; i < n; i++)
                if (args[i] is { } given && given is not LambdaExpr) UnifyInfer(fn.Parameters[i], argTypes[i]!, map, given.Span);
            substituted = (FnType)Substitute(fn, map);
        }

        // Phase C: lambdas with context; their actual type binds the type arguments still open.
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is not LambdaExpr lambda) continue;
            argTypes[i] = CheckExpr(lambda, scope, ExpectedParamAt(substituted, decl, i, lambda));
            if (map is not null && i < fn.Parameters.Length)
                UnifyInfer(Substitute(fn.Parameters[i], map), argTypes[i]!, map, lambda.Span);
        }
        if (map is not null)
        {
            // The position's expected type binds what the arguments left open (03 T8: an
            // assignment, an argument, a return, a field): 'let xs: int[] = empty();'. After the
            // arguments, never over them — 'UnifyInfer' adds and does not overwrite — so a
            // mismatch stays what it is, the assignment's, and is reported there.
            if (expected is not null && !expected.IsError && fsym!.Generics.Any(g => !map.ContainsKey(g)))
                UnifyInfer(fn.Return, expected, map, call.Span);

            CheckInferredConstraints(fsym!.Generics, map, call.Span);
            substituted = (FnType)Substitute(fn, map);

            // A type parameter the inference could not bind is reported HERE. Otherwise it silently
            // becomes 'LyrType.Error' and only the lowering trips over it, with a compiler-internal
            // message at a place where the user merely omitted a type argument.
            //
            // Not reported when an argument was already faulty: the cause is reported then, and a
            // second line about a type argument would be follow-up noise.
            if (!argTypes.Any(t => t is not null && ContainsError(t)))
                foreach (var generic in fsym.Generics)
                    if (!map.ContainsKey(generic))
                        _de.Report("LYR-SEM0060", Severity.Error, call.Span,
                            $"cannot infer type argument '{generic.Name}' for '{fsym.Name}' — " +
                            "no argument determines it; write it explicitly");

            // For monomorphization: which instance is meant is settled here and nowhere else. In
            // declaration order, because that is the identity of the instance.
            _result.SetTypeArguments(call, fsym.Generics
                .Select(g => map.TryGetValue(g, out var bound) ? bound : LyrType.Error)
                .ToArray());
        }

        CheckCallArgs(call, substituted, args, argTypes, decl);

        // What this call may throw, in its instance's terms (05 E2 K5): the exception analysis
        // asks whether it is marked and covered.
        if (substituted.Throws.Length > 0) _result.RecordCallThrows(call, substituted.Throws);

        // If the receiver was optional the result is too, collapsed, because optionals do not nest.
        return optionalCall ? Optionalized(substituted.Return) : substituted.Return;
    }

    /// <summary>
    /// The declared type of parameter <paramref name="i"/>, but only when it names no type parameter
    /// that is still open.
    ///
    /// <para>An expectation is a statement about which instance is meant. A type still holding a
    /// <c>T</c> makes no such statement — it is the question the inference answers from the argument
    /// — and passing it down would answer that question with itself.</para>
    /// </summary>
    private static LyrType? ConcreteExpectation(FnType fn, FunctionDecl? decl, int i,
        Expr argument) =>
        ExpectedParamAt(fn, decl, i, argument) is { } t && !MentionsTypeParam(t) ? t : null;

    /// <summary>Does this type still carry a type parameter anywhere inside it?</summary>
    private static bool MentionsTypeParam(LyrType type) => type switch
    {
        TypeParamType => true,
        AssocOf a => MentionsTypeParam(a.Base),
        Optional o => MentionsTypeParam(o.Inner),
        ArrayOf a => MentionsTypeParam(a.Element),
        SliceOf s => MentionsTypeParam(s.Element),
        InlineArrayOf ia => MentionsTypeParam(ia.Element),
        CoroutineOf c => MentionsTypeParam(c.Yield),
        TupleOf t => t.Elements.Any(MentionsTypeParam),
        GenericInstance g => g.Arguments.Any(MentionsTypeParam),
        FnType f => f.Parameters.Any(MentionsTypeParam) || MentionsTypeParam(f.Return) || f.Throws.Any(MentionsTypeParam),
        _ => false,
    };

    private static LyrType? ExpectedParamAt(FnType fn, FunctionDecl? decl, int i, Expr argument)
    {
        var ps = decl?.Parameters;
        var variadic = ps is { Length: > 0 } && ps[^1].IsParams;
        var fixedCount = variadic ? ps!.Length - 1 : fn.Parameters.Length;
        if (i < fixedCount && i < fn.Parameters.Length) return fn.Parameters[i];
        // The variadic position expects the ELEMENT — that is what names an enum variant's
        // instance in 'f(Opt.Some(1))'. EXCEPT for an array-literal argument: it may be one
        // element or the whole array, the literal's own shape decides (PassesWholeArray), and
        // since 2.1 an expectation PROPAGATES into the literal — offering the element type
        // would force the element reading and take the whole-array form with it.
        if (variadic && argument is not ArrayLitExpr && fn.Parameters[^1] is ArrayOf elem)
            return elem.Element;
        return null;
    }

    /// <summary>
    /// Is the <c>params</c> array passed as a whole rather than piece by piece?
    ///
    /// <para>Exactly when ONE argument is left and its type is that of the array — <c>sum(xs)</c>
    /// with <c>xs: int[]</c>. That is unambiguous in Lyric for two reasons C# lacks: there is no
    /// implicit conversion between <c>T</c> and <c>T[]</c>, and there is no overloading. With
    /// <c>params xs: int[][]</c> an element is <c>int[]</c> and the array <c>int[][]</c> — different
    /// types, no conflict.</para>
    ///
    /// <para>Allowed at all because without this route no variadic function could delegate to
    /// another: <c>fn logged(params xs: int[]) { return sum(xs); }</c> would be impossible. To pass
    /// an array deliberately as ONE element, write <c>f([a])</c>.</para>
    /// </summary>
    private static bool PassesArrayDirectly(LyrType[] argTypes, int fixedCount, LyrType arrayType) =>
        argTypes.Length == fixedCount + 1 && LyrType.Equal(argTypes[fixedCount], arrayType);

    /// <param name="args">In parameter order (<see cref="ArrangeArguments"/>); a <c>null</c> is a
    /// parameter the call leaves to its default.</param>
    private void CheckCallArgs(CallExpr call, FnType fn, Expr?[] args, LyrType?[] argTypes, FunctionDecl? decl)
    {
        var ps = decl?.Parameters;
        var variadic = ps is { Length: > 0 } && ps[^1].IsParams;
        var fixedCount = variadic ? ps!.Length - 1 : ps?.Length ?? fn.Parameters.Length;
        var minRequired = ps is null ? fn.Parameters.Length : ps.Take(fixedCount).Count(p => p.Default is null);
        var given = args.Count(a => a is not null);

        // What the call leaves out: by name when it names any argument — then a hole is a
        // parameter, and the parameter is what to say — by count otherwise, as a positional
        // call sees it.
        if (call.ArgumentNames is not null && ps is not null)
        {
            for (var i = 0; i < fixedCount && i < args.Length; i++)
                if (args[i] is null && ps[i].Default is null)
                    _de.Report("LYR-SEM0014", Severity.Error, call.Span,
                        $"call gives no argument for '{ps[i].Name}', which has no default");
        }
        else if (given < minRequired || (!variadic && given > fn.Parameters.Length))
            _de.Report("LYR-SEM0014", Severity.Error, call.Span,
                $"call expects {(variadic ? $"at least {minRequired}" : minRequired == fn.Parameters.Length ? minRequired.ToString() : $"{minRequired}–{fn.Parameters.Length}")} argument(s), got {given}");

        for (var i = 0; i < args.Length && i < fixedCount && i < fn.Parameters.Length; i++)
            if (args[i] is { } a) CheckAssignable(a, argTypes[i]!, fn.Parameters[i], a.Span);

        if (variadic && fn.Parameters[^1] is ArrayOf elem)
        {
            // A ready-made array passes through as a whole, or one variadic function could not
            // delegate to another. See PassesArrayDirectly.
            var typed = argTypes.Select(t => t ?? LyrType.Error).ToArray();
            if (PassesArrayDirectly(typed, fixedCount, fn.Parameters[^1])) return;

            for (var i = fixedCount; i < args.Length; i++)
                if (args[i] is { } a) CheckAssignable(a, argTypes[i]!, elem.Element, a.Span);
        }
    }

    /// <summary>
    /// The arguments of a call in PARAMETER order (design/v5/spec/04 D5): the positional ones
    /// first, in their order, then each named one where its name says. A name decides nothing
    /// about WHICH function is called — the count does (D4) — and names every parameter but a
    /// <c>params</c> tail; a positional argument after a named one, a name given twice or for a
    /// parameter already set, and a name no parameter has are refused (<c>LYR-SEM0119</c>). The
    /// arrangement is recorded for the lowering, which materializes a hole from its default.
    /// </summary>
    private Expr?[] ArrangeArguments(CallExpr call, FunctionDecl? decl)
    {
        if (call.ArgumentNames is not { } names) return call.Arguments;
        if (decl is null)
        {
            _de.Report("LYR-SEM0119", Severity.Error, call.Span,
                "a named argument needs a declared function to name a parameter of — a function value has none");
            return call.Arguments;
        }

        var parameters = decl.Parameters;
        var variadic = parameters.Length > 0 && parameters[^1].IsParams;
        var fixedCount = variadic ? parameters.Length - 1 : parameters.Length;
        var arranged = new List<Expr?>(new Expr?[fixedCount]);
        var positional = 0;
        var namedSeen = false;
        for (var i = 0; i < call.Arguments.Length; i++)
        {
            var argument = call.Arguments[i];
            if (i >= names.Length || names[i] is not { } name)
            {
                if (namedSeen)
                    _de.Report("LYR-SEM0119", Severity.Error, argument.Span,
                        "a positional argument after a named one — the named ones come last");
                if (positional < fixedCount) arranged[positional] = argument;
                else arranged.Add(argument);
                positional++;
                continue;
            }

            namedSeen = true;
            var index = Array.FindIndex(parameters, p => p.Name == name);
            if (index < 0)
            {
                _de.Report("LYR-SEM0119", Severity.Error, argument.Span,
                    $"'{decl.Name}' has no parameter '{name}' — its parameters are "
                    + string.Join(", ", parameters.Select(p => $"'{p.Name}'")));
                continue;
            }
            if (variadic && index == parameters.Length - 1)
            {
                _de.Report("LYR-SEM0119", Severity.Error, argument.Span,
                    $"'{name}' is the 'params' parameter, which takes the rest and is not named");
                continue;
            }
            if (arranged[index] is not null)
            {
                _de.Report("LYR-SEM0119", Severity.Error, argument.Span,
                    index < positional
                        ? $"'{name}' is already set by the argument at position {index + 1}"
                        : $"'{name}' is given twice");
                continue;
            }
            arranged[index] = argument;
        }

        var result = arranged.ToArray();
        _result.SetArrangedArguments(call, result);
        return result;
    }

    /// <summary>The node and everything under it, in tree order.</summary>
    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (var child in AstChildren.Of(node))
            foreach (var below in Descendants(child)) yield return below;
    }

    private static (int Min, int Max) ArityOf(FunctionSymbol fn)
    {
        if (fn.Declaration is not FunctionDecl decl) return (0, int.MaxValue);
        var variadic = decl.Parameters.Length > 0 && decl.Parameters[^1].IsParams;
        var fixedCount = variadic ? decl.Parameters.Length - 1 : decl.Parameters.Length;
        var min = decl.Parameters.Take(fixedCount).Count(p => p.Default is null);
        return (min, variadic ? int.MaxValue : fixedCount);
    }

    private static bool Takes(FunctionSymbol fn, int count)
    {
        var (min, max) = ArityOf(fn);
        return count >= min && count <= max;
    }

    private static string DisplayArity(FunctionSymbol fn)
    {
        var (min, max) = ArityOf(fn);
        return max == int.MaxValue ? $"{min} or more" : min == max ? $"{min}" : $"{min}–{max}";
    }

    /// <param name="expected">Needed for an enum variant without written type arguments only;
    /// without effect otherwise.</param>
    private LyrType CheckMember(MemberExpr mem, SymbolTable scope, LyrType? expected = null)
    {
        // CheckTarget rather than CheckExpr: a type or module name is allowed here.
        var targetType = _typedReceiver.Contains(mem) ? _result.TypeOf(mem.Target) : CheckTarget(mem.Target, scope);

        // An operator that already resolved its target says so here; see _operatorTarget. The
        // receiver is still checked above, because it is the operand and has to be typed.
        if (_operatorTarget.TryGetValue(mem, out var chosen))
            return targetType.IsError ? LyrType.Error : BindMember(mem, chosen);

        // Whether the receiver NAMES something or produces a value is what CheckTarget has just
        // decided: a NonValueType is a name, everything else is a value. Asking the reference table
        // instead is a second answer to that question and the weaker one — the table knows that
        // 'Point { … }' mentions Point, not that it BUILDS one, and would dispatch the member as if
        // the type itself stood there.
        //
        // An unresolvable import gives an error type and falls through: InstanceMemberOf has no case
        // for it and the IsError check below returns without a second diagnostic.
        switch (targetType)
        {
            case NonValueType { Symbol: TypeSymbol ts } named:
                return BindMember(mem, MemberOfType(ts, mem.Member, mem.Span,
                    named.Instance ?? ExpectedInstance(expected, ts)));

            case NonValueType { Symbol: ModuleSymbol mod }:
                var moduleMember = MemberOfModule(mod, mem.Member, mem.Span);
                // The qualified twin of the identifier rule (§8.1): a generic function is not a
                // value, and only callee position — marked by CheckTargetOfCall — may hold its
                // unsubstituted name.
                if (moduleMember is (_, FunctionSymbol { Generics.Length: > 0 } qualifiedGeneric)
                    && !_calleePosition.Contains(mem))
                    moduleMember = (Report(mem.Span, "LYR-SEM0052",
                        $"generic function '{qualifiedGeneric.Name}' is not a value — a function "
                        + "value is monomorphic; call it, or write a lambda that calls it"),
                        qualifiedGeneric);
                return BindMember(mem, moduleMember);

            // 'T.parse(s)': a static member through the constraint (03 T5).
            case NonValueType { Symbol: GenericParamSymbol gp }:
                return BindMember(mem, StaticMemberOfTypeParam(gp, mem.Member, mem.Span));
        }

        var baseType = mem.IsOptional && targetType is Optional opt ? opt.Inner : targetType;

        // 'length()' on T[] is built in rather than a library method (03 T13 A1: the length and
        // the index are the primitives), and it is a CALL, with parentheses like every length
        // (10 N8). Without them the name is refused rather than read as a field: the two forms
        // would otherwise mean one thing, and 'xs.length' read like a field of the array.
        // An element of a tuple, by position or by the label its type gives it (03 T16).
        if (baseType is TupleOf tuple)
        {
            var at = tuple.Labels is { } labels ? Array.IndexOf(labels, mem.Member) : -1;
            if (at < 0 && int.TryParse(mem.Member, out var position) && mem.Member.All(char.IsAsciiDigit)) at = position;
            if (at < 0 || at >= tuple.Elements.Length)
            {
                // A member a block on the tuple shape adds (03 T7 X2) comes before the refusal.
                if (at < 0 && ConstructorMember(baseType, mem, mem.Span) is { } added)
                    return mem.IsOptional ? Optionalized(added) : added;
                return Report(mem.MemberSpan, "LYR-SEM0012",
                    $"'{TypeFacts.Display(baseType)}' has no element '{mem.Member}' — the elements are '.0' to '.{tuple.Elements.Length - 1}'"
                    + (tuple.Labels is not null ? " and their labels" : ""));
            }
            return mem.IsOptional ? Optionalized(tuple.Elements[at]) : tuple.Elements[at];
        }

        if (baseType is ArrayOf or SliceOf or InlineArrayOf && mem.Member == "length")
        {
            if (_calleePosition.Contains(mem)) return new FnType([], LyrType.Int);
            return Report(mem.MemberSpan, "LYR-SEM0012", "'length' is called: write 'length()'");
        }

        // 'next' on a coroutine is built in the same way: the safe pull beside the panicking
        // 'resume', same word and shape as Iterator<T>.next. '?T' answers value-or-done; a
        // coroutine yielding void answers bool (advanced?), and one yielding an optional is
        // refused — a null result would mean two different things there.
        if (baseType is CoroutineOf co && mem.Member == "next")
        {
            // A PULL, so a throw site. 'next()' is lenient about EXHAUSTION — it answers null for
            // a finished coroutine — and says nothing about an exception from the body, which
            // passes straight through it.
            if (co.Throws is { } pulled) _result.MarkThrowingPull(mem, pulled);

            if (co.Yield is Optional)
                return Report(mem.Span, "LYR-SEM0080",
                    $"'next()' on '{TypeFacts.Display(baseType)}' cannot tell an exhausted "
                    + "coroutine from a yielded null; drive it with 'resume' and an explicit "
                    + "protocol instead");
            return new FnType([],
                TypeFacts.IsVoid(co.Yield) ? LyrType.Bool : new Optional(co.Yield));
        }

        if (InstanceMemberOf(baseType, mem, mem.Span) is { } mt)
            return mem.IsOptional ? Optionalized(mt) : mt;
        if (targetType.IsError) return LyrType.Error;
        return Report(mem.Span, "LYR-SEM0012", $"'{TypeFacts.Display(targetType)}' has no member '{mem.Member}'");
    }

    /// <summary>
    /// <c>T</c> becomes <c>?T</c>, while <c>?T</c> stays <c>?T</c>.
    ///
    /// <para>Optionals do not nest; the lowering reports <c>??T</c> as <c>LYR-IR0001</c>. Without the
    /// collapse, <c>b?.v</c> on a field of type <c>?int</c> would be a <c>??int</c>, and the error
    /// would arrive as "cannot assign '?int' to 'int'" one level too late.</para>
    /// </summary>
    private static LyrType Optionalized(LyrType t) => t is Optional ? t : new Optional(t);

    // Instance members over the three "object" types: a concrete one (NamedRef), a generic instance,
    // where the member type has T substituted by the argument, or a type parameter, whose members
    // come from its constraints.
    private LyrType? InstanceMemberOf(LyrType baseType, MemberExpr mem, Span span)
    {
        _memberReceiver = baseType;
        switch (baseType)
        {
            case NamedRef nr:
                return BindMember(mem, InstanceMember(nr.Symbol, mem.Member, span));
            case GenericInstance gi:
                var (t, s) = InstanceMember(gi.Definition, mem.Member, span);
                if (s is not null) _result.BindRef(mem, s);
                return Substitute(t, SubstMap(gi));
            case TypeParamType tp:
                return BindMember(mem, MemberOfTypeParam(tp.Param, mem.Member, span));
            case PrimitiveType p when BuiltinSymbol(p) is { } bs: // extensions on builtins, such as string.shout()
                return BindMember(mem, InstanceMember(bs, mem.Member, span));
            case ArrayOf or SliceOf or InlineArrayOf or Optional or TupleOf:
                return ConstructorMember(baseType, mem, span);
            default:
                return null;
        }
    }

    /// <summary>A member a block on a built-in constructor adds (03 T7 X2): the block whose
    /// target matches the receiver's shape and whose constraints hold — 'extend&lt;T&gt; T[]',
    /// 'extend&lt;T :: [Display]&gt; ?T', 'extend&lt;T&gt; Slice&lt;T&gt;'.</summary>
    private LyrType? ConstructorMember(LyrType receiver, MemberExpr mem, Span span)
    {
        ExtensionBlock? shapeOnly = null; // the shape matched, the constraints did not
        foreach (var block in _comp.Extensions.Blocks)
        {
            if (!(block.IsConstructorTarget || (block.Target is { } t && ReferenceEquals(t, _slice)))) continue;
            if (_currentModule is not null && !_comp.Sees(_currentModule, block.Module)) continue;
            if (block.MethodScope.LookupLocal(mem.Member) is not FunctionSymbol found) continue;
            if (BlockSubstitution(block, receiver) is not { } map)
            {
                if (TypeFacts.Match(BlockTargetType(block), receiver, new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance)))
                    shapeOnly ??= block;
                continue;
            }
            _result.BindRef(mem, found);
            if (found.IsStatic)
                return Report(span, "LYR-SEM0074", $"'{mem.Member}' is a static extension and belongs to the type — the instance form is an error");
            return Substitute(FnTypeOf(found), map);
        }
        if (shapeOnly is { } failed)
            return Report(span, "LYR-SEM0134",
                $"'{mem.Member}' is added to '{TypeFacts.Display(BlockTargetType(failed))}' under the block's constraints, "
                + $"which '{TypeFacts.Display(receiver)}' does not satisfy");
        return null;
    }

    // The builtin TypeSymbol for a primitive type, used for extension lookup on string, int and so on.
    private TypeSymbol? BuiltinSymbol(PrimitiveType p) =>
        _comp.Builtins.LookupLocal(TypeFacts.Display(p)) as TypeSymbol;

    // Members on a type parameter T: what its constraint interfaces provide — the closure, since
    // a constraint on the child interface implies the parents' members too.
    private (LyrType, Symbol?) MemberOfTypeParam(GenericParamSymbol gp, string member, Span span)
    {
        foreach (var c in gp.Constraints)
        {
            if (c is not NamedType nt) continue;
            foreach (var (it, subst) in ClosureOfNode(nt))
            {
                if (it.Members.LookupLocal(member) is not FunctionSymbol fn) continue;
                if (fn.IsStatic)
                    return (Report(span, "LYR-SEM0055",
                        $"'{member}' is a static member of '{it.Name}' — call it on the type parameter: '{gp.Name}.{member}(…)'"), fn);

                // Substitute the type arguments OF THE CONSTRAINT. 'T :: [Eq<T>]' means the 'T' in
                // 'Eq<T>.eq(other: T)' is the 'T' of the calling function — two different symbols
                // with the same name. 'Self' is the type parameter itself (03 T5), and an
                // associated type the constraint fixes is that type (T6).
                //
                // Without the substitution the raw interface type comes back and 'a.eq(b)' fails
                // with "cannot assign 'T' to 'T'".
                var signature = Substitute(FnTypeOf(fn), WithSelf(subst, it, new TypeParamType(gp)));
                if (ResolveType(nt, _currentModule?.Members ?? _comp.Builtins) is GenericInstance { Fixations: { Length: > 0 } fixations })
                    signature = ApplyFixations(signature, gp, fixations);
                return (signature, fn);
            }
        }
        return (Report(span, "LYR-SEM0027",
            $"type parameter '{gp.Name}' has no member '{member}' (no constraint provides it)"), null);
    }

    /// <summary><c>T.parse(s)</c> (03 T5): a STATIC member of an interface the type parameter is
    /// constrained by, reachable through the parameter alone — monomorphization makes it the
    /// concrete type's static, a direct call.</summary>
    private (LyrType, Symbol?) StaticMemberOfTypeParam(GenericParamSymbol gp, string member, Span span)
    {
        foreach (var c in gp.Constraints)
        {
            if (c is not NamedType nt) continue;
            foreach (var (it, subst) in ClosureOfNode(nt))
            {
                if (it.Members.LookupLocal(member) is not FunctionSymbol fn) continue;
                if (!fn.IsStatic)
                    return (Report(span, "LYR-SEM0055",
                        $"'{member}' is an instance member of '{it.Name}' and needs a receiver — call it on a value of '{gp.Name}'"), fn);
                return (Substitute(FnTypeOf(fn), WithSelf(subst, it, new TypeParamType(gp))), fn);
            }
        }
        return (Report(span, "LYR-SEM0027",
            $"type parameter '{gp.Name}' has no static member '{member}' (no constraint provides it)"), null);
    }

    /// <summary>The substitution with the interface's <c>Self</c> bound (03 T5): to the conforming
    /// type at a conformance, to the type parameter at a constraint, to the type at a lookup.</summary>
    private static Dictionary<GenericParamSymbol, LyrType> WithSelf(
        Dictionary<GenericParamSymbol, LyrType> subst, TypeSymbol iface, LyrType self)
    {
        if (iface.SelfParam is not { } param) return subst;
        // The instance's own arguments may name 'Self' too — 'Add' is 'Add<Self>' (T18) — and the
        // substitution is one pass, so they are resolved here as well.
        var selfOnly = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance) { [param] = self };
        var map = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance) { [param] = self };
        foreach (var (key, value) in subst) map[key] = Substitute(value, selfOnly);
        return map;
    }

    /// <summary>The substitution of an interface's <c>Self</c> alone.</summary>
    private static Dictionary<GenericParamSymbol, LyrType> SelfMap(TypeSymbol iface, LyrType self) =>
        WithSelf(EmptySubst, iface, self);

    private static Dictionary<GenericParamSymbol, LyrType> SubstMap(GenericInstance gi)
    {
        var map = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);
        var n = Math.Min(gi.Definition.Generics.Length, gi.Arguments.Length);
        for (var i = 0; i < n; i++) map[gi.Definition.Generics[i]] = gi.Arguments[i];
        return map;
    }

    // T to its argument across a type: Stack<T>.items of type T[] becomes int[] for Stack<int>.
    private static LyrType Substitute(LyrType type, Dictionary<GenericParamSymbol, LyrType> map)
    {
        if (map.Count == 0) return type;
        return type switch
        {
            TypeParamType tp => map.TryGetValue(tp.Param, out var m) ? m : tp,
            Optional o => new Optional(Substitute(o.Inner, map)),
            ArrayOf a => new ArrayOf(Substitute(a.Element, map)),
            SliceOf s => new SliceOf(Substitute(s.Element, map)),
            InlineArrayOf ia => new InlineArrayOf(Substitute(ia.Element, map), ia.Length),
            TupleOf t => new TupleOf(t.Elements.Select(e => Substitute(e, map)).ToArray()) { Labels = t.Labels },
            FnType f => new FnType(f.Parameters.Select(p => Substitute(p, map)).ToArray(), Substitute(f.Return, map))
                { Throws = ThrownAfter(f.Throws.Select(t => Substitute(t, map))) },
            GenericInstance gi => new GenericInstance(gi.Definition, gi.Arguments.Select(a => Substitute(a, map)).ToArray())
                { Fixations = gi.Fixations?.Select(f => (f.Member, Substitute(f.Type, map))).ToArray() },
            RangeOf r => new RangeOf(Substitute(r.Element, map)),
            CoroutineOf co => co with { Yield = Substitute(co.Yield, map) },
            AssocOf a => ResolveAssociated(Substitute(a.Base, map), a.Member, InstanceFromMap(a.Member, map)),
            _ => type // primitive, NamedRef, error, null
        };
    }

    /// <summary>
    /// <c>T.Item</c> once <c>T</c> is known (03 T6): the conformer's binding of the associated
    /// type, read off the interface's declaration symbol, through the conformer's own type
    /// arguments; still a type parameter, the question stays open as it is.
    /// </summary>
    internal static LyrType ResolveAssociated(LyrType @base, AssociatedTypeSymbol member, LyrType? instance = null)
    {
        switch (@base)
        {
            case TypeParamType or AssocOf: return new AssocOf(@base, member);
            case NamedRef nr when member.Answer(nr.Symbol, instance) is { } bound: return bound;
            case GenericInstance gi when member.Answer(gi.Definition, instance) is { } bound:
                return Substitute(bound, SubstMap(gi));
            case PrimitiveType p when member.BuiltinAnswer(TypeFacts.Display(p), instance) is { } bound: return bound;
            case ErrorType: return LyrType.Error;
            default: return new AssocOf(@base, member);
        }
    }

    /// <summary>The conformance instance a substitution names, where it binds the interface's
    /// own parameters — the conformance check does; a call site binding 'T' alone does not,
    /// and the question then takes the conformer's first answer.</summary>
    private static LyrType? InstanceFromMap(AssociatedTypeSymbol member, Dictionary<GenericParamSymbol, LyrType> map)
    {
        if (member.Owner is not { Generics.Length: > 0 } owner) return null;
        var args = new LyrType[owner.Generics.Length];
        for (var i = 0; i < args.Length; i++)
            if (!map.TryGetValue(owner.Generics[i], out args[i]!)) return null;
        return new GenericInstance(owner, args);
    }

    /// <summary>The associated types of a type parameter's constraint, fixed at the constraint
    /// (<c>T :: [Iterator&lt;Item = int&gt;]</c>): <c>T.Item</c> is <c>int</c> wherever it stands.</summary>
    private static LyrType ApplyFixations(LyrType type, GenericParamSymbol gp,
        (AssociatedTypeSymbol Member, LyrType Type)[] fixations)
    {
        LyrType Fix(LyrType t) => t switch
        {
            AssocOf { Base: TypeParamType tp } a when ReferenceEquals(tp.Param, gp)
                && Array.FindIndex(fixations, f => ReferenceEquals(f.Member, a.Member)) is var i && i >= 0 => fixations[i].Type,
            Optional o => new Optional(Fix(o.Inner)),
            ArrayOf ar => new ArrayOf(Fix(ar.Element)),
            SliceOf s => new SliceOf(Fix(s.Element)),
            InlineArrayOf ia => new InlineArrayOf(Fix(ia.Element), ia.Length),
            TupleOf tu => new TupleOf(tu.Elements.Select(Fix).ToArray()) { Labels = tu.Labels },
            FnType f => new FnType(f.Parameters.Select(Fix).ToArray(), Fix(f.Return)) { Throws = f.Throws.Select(Fix).ToArray() },
            GenericInstance g => new GenericInstance(g.Definition, g.Arguments.Select(Fix).ToArray()) { Fixations = g.Fixations },
            RangeOf r => new RangeOf(Fix(r.Element)),
            CoroutineOf c => c with { Yield = Fix(c.Yield) },
            _ => t,
        };
        return Fix(type);
    }

    /// <summary>
    /// Every conformance's answers to the associated types of its interfaces (03 T6): a
    /// binding in the type's body or in the conformance block, else the interface's default
    /// with <c>Self</c> as the conformer, else refused (<c>LYR-SEM0128</c>). A binding no
    /// interface of the type asks for is refused too (<c>LYR-SEM0129</c>).
    /// </summary>
    private void BindAssociatedTypes()
    {
        foreach (var module in _comp.Modules)
        {
            _currentModule = module;
            foreach (var symbol in module.Members.Symbols)
                if (symbol is TypeSymbol { Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct or TypeSymbolKind.Enum } ts
                    && DeclaredInModule(ts, module))
                    BindAssociatedTypes(ts, DeclaredInterfaceNodes(ts), ts.Members, ts.Declaration?.Span ?? default);
        }
        foreach (var block in _comp.Extensions.Blocks)
        {
            // A built-in conforms through its block alone ('extend int :: [Add]'): its answers
            // hang on the builtin's symbol, found by the type's name.
            if (block.Target is not { Kind: TypeSymbolKind.Class or TypeSymbolKind.Struct or TypeSymbolKind.Enum or TypeSymbolKind.Builtin } target) continue;
            _currentModule = block.Module;
            BindAssociatedTypes(target, block.Decl.Interfaces, block.MethodScope, block.Decl.Span);
        }
        _currentModule = null;
    }

    private void BindAssociatedTypes(TypeSymbol ts, TypeNode[] entries, SymbolTable answers, Span at)
    {
        var asked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in entries)
        {
            foreach (var (iface, subst) in ClosureOfNode(node))
            {
                // The answer belongs to the conformance INSTANCE (04 D6): 'Mul<int>' and
                // 'Mul<float>' of one type answer 'Out' each for themselves.
                var instance = Substitute(InstanceOfConformance(iface, subst), SelfMap(iface, SelfType(ts)));
                foreach (var member in iface.Members.Symbols.OfType<AssociatedTypeSymbol>())
                {
                    asked.Add(member.Name);
                    var answer = answers.LookupLocal(member.Name) is AssociatedTypeSymbol own
                        && own.Declaration is AssociatedTypeDecl { Type: { } bound } ? (own, bound) : default;
                    // Answered already: this is a second block of the same conformance, which
                    // coherence refuses as a whole (03 T7 X3, LYR-SEM0133) — its answers are not
                    // asked again, so the one mistake gets one error.
                    if (member.Answer(ts, instance, exact: true) is not null) continue;
                    if (answer.bound is { } written)
                    {
                        member.Bind(ts, instance, ResolveType(written, answers));
                        continue;
                    }
                    if (member.Declaration is AssociatedTypeDecl { Type: { } fallback })
                    {
                        member.Bind(ts, instance, Substitute(ResolveType(fallback, DeclarationScope(iface)),
                            WithSelf(subst, iface, SelfType(ts))));
                        continue;
                    }
                    _de.Report("LYR-SEM0128", Severity.Error, NodeSpan(node),
                        $"'{ts.Name}' does not say what '{iface.Name}'s associated type '{member.Name}' is — write 'type {member.Name} = …;'");
                    member.Bind(ts, instance, LyrType.Error);
                }
            }
        }
        foreach (var own in answers.Symbols.OfType<AssociatedTypeSymbol>())
            if (!asked.Contains(own.Name))
                _de.Report("LYR-SEM0129", Severity.Error, own.Declaration?.Span ?? at,
                    $"'type {own.Name}' answers no interface — none that '{ts.Name}' conforms to here declares an associated type '{own.Name}'");
    }

    /// <summary>An interface's associated type of that name, its parents' included.</summary>
    private AssociatedTypeSymbol? FindAssociated(TypeSymbol iface, string name)
    {
        foreach (var part in Conformance.WithParents(iface, _binding))
            if (part.Members.LookupLocal(name) is AssociatedTypeSymbol member) return member;
        return null;
    }

    /// <summary>
    /// <c>T.Item</c>, <c>Self.Item</c>, <c>P.Item</c> (03 T6): the associated type named off a
    /// type parameter (through its constraints, or the interface it is the <c>Self</c> of), or
    /// off a concrete type (its answer).
    /// </summary>
    private LyrType ResolveAssociated(Symbol head, string name, Span span)
    {
        if (head is ImportBindingSymbol ib) head = ib.Target;
        switch (head)
        {
            case GenericParamSymbol gp:
            {
                IEnumerable<TypeSymbol> interfaces = gp.SelfOf is { } self
                    ? Conformance.WithParents(self, _binding)
                    : gp.Constraints.OfType<NamedType>().SelectMany(c => ClosureOfNode(c).Select(p => p.iface));
                foreach (var iface in interfaces)
                    if (iface.Members.LookupLocal(name) is AssociatedTypeSymbol member)
                        return new AssocOf(new TypeParamType(gp), member);
                return Report(span, "LYR-SEM0128",
                    gp.SelfOf is { } owner
                        ? $"'{owner.Name}' declares no associated type '{name}'"
                        : $"no constraint of '{gp.Name}' declares an associated type '{name}' — constrain it by an interface that does");
            }
            case TypeSymbol { Kind: TypeSymbolKind.Interface } iface:
                return Report(span, "LYR-SEM0128",
                    $"'{iface.Name}.{name}' names the associated type of no type in particular — name the type parameter, 'T.{name}', or the conforming type");
            case TypeSymbol ts:
            {
                foreach (var (iface, _) in InterfacesOf(ts))
                    if (iface.Members.LookupLocal(name) is AssociatedTypeSymbol member)
                        return ResolveAssociated(SelfType(ts), member);
                return Report(span, "LYR-SEM0128", $"'{ts.Name}' has no associated type '{name}'");
            }
            default:
                return Report(span, "LYR-SEM0011", $"unresolved type '{head.Name}.{name}'");
        }
    }

    // --- generic call inference and constraint satisfaction ---

    // Resolves type parameters: the parameter carries T, the argument is concrete. The first binding
    // wins; contradictions such as pair(1, "x") surface later in CheckCallArgs as type errors.
    /// <summary>
    /// Binds type parameters by running the parameter type and the argument type against each other.
    /// </summary>
    /// <remarks>
    /// <para>The unification is STRUCTURAL: it compares shapes. That covers everything that nests
    /// like a tree — <c>T[]</c> against <c>int[]</c>, <c>fn(T) -&gt; bool</c> against
    /// <c>fn(int) -&gt; bool</c>, <c>Box&lt;T&gt;</c> against <c>Box&lt;int&gt;</c>.</para>
    /// <para>CONFORMANCE is not structural: between <c>RangeIterator</c> and
    /// <c>Iterator&lt;int&gt;</c> there is no similarity of shape but a declaration
    /// (<c>class RangeIterator :: [Iterator&lt;int&gt;]</c>). The last case below looks it up.</para>
    /// </remarks>
    private void UnifyInfer(LyrType param, LyrType arg, Dictionary<GenericParamSymbol, LyrType> map,
        Span argSpan)
    {
        switch (param)
        {
            case TypeParamType tp:
                if (!arg.IsError) map.TryAdd(tp.Param, arg);
                break;
            case ArrayOf pa when arg is ArrayOf aa: UnifyInfer(pa.Element, aa.Element, map, argSpan); break;
            case InlineArrayOf pi when arg is InlineArrayOf ai && pi.Length == ai.Length:
                UnifyInfer(pi.Element, ai.Element, map, argSpan); break;
            case SliceOf ps when arg is SliceOf or ArrayOf:
                UnifyInfer(ps.Element, arg is SliceOf sa ? sa.Element : ((ArrayOf)arg).Element, map, argSpan); break;
            case Optional po when arg is Optional ao: UnifyInfer(po.Inner, ao.Inner, map, argSpan); break;
            case TupleOf pt when arg is TupleOf at && pt.Elements.Length == at.Elements.Length:
                for (var i = 0; i < pt.Elements.Length; i++) UnifyInfer(pt.Elements[i], at.Elements[i], map, argSpan);
                break;
            case GenericInstance pg when arg is GenericInstance ag
                && ReferenceEquals(pg.Definition, ag.Definition) && pg.Arguments.Length == ag.Arguments.Length:
                for (var i = 0; i < pg.Arguments.Length; i++) UnifyInfer(pg.Arguments[i], ag.Arguments[i], map, argSpan);
                break;
            case FnType pf when arg is FnType af && pf.Parameters.Length == af.Parameters.Length:
                for (var i = 0; i < pf.Parameters.Length; i++) UnifyInfer(pf.Parameters[i], af.Parameters[i], map, argSpan);
                UnifyInfer(pf.Return, af.Return, map, argSpan);
                // A set written as one type parameter, 'throws E', takes the argument's (05 E2 K4):
                // nothing thrown binds 'never', one type binds it, several their join — 'Error', as
                // composed sets join (K7). The first binding wins, as for every parameter.
                if (pf.Throws is [TypeParamType thrownBy] && !map.ContainsKey(thrownBy.Param) && !af.Throws.Any(t => t.IsError))
                    map[thrownBy.Param] = af.Throws switch
                    {
                        [] => LyrType.Never,
                        [var one] => one,
                        _ => ErrorRoot ?? LyrType.Error,
                    };
                break;
            case CoroutineOf pc when arg is CoroutineOf ac: UnifyInfer(pc.Yield, ac.Yield, map, argSpan); break;

            // 'Iterator<T>' against 'RangeIterator': the parameter is an instance of an INTERFACE and
            // the argument a type satisfying it. Structurally the two have nothing in common; the
            // connection stands in the argument's declaration.
            //
            // Without this case 'T' stays unbound, and silently: the sema reports nothing and only
            // the lowering finds '<error>' as a type argument. Affected is every generic function
            // whose type parameter occurs in the interface parameter ONLY.
            case GenericInstance { Definition.Kind: TypeSymbolKind.Interface } pi:
                UnifyThroughConformance(pi, arg, map, argSpan);
                break;
        }
    }

    /// <summary>Looks up the conformance to <paramref name="wanted"/> in the argument type and
    /// unifies its type arguments against the written ones.
    ///
    /// <para>SEVERAL conformances to the wanted interface do not choose (LYR-SEM0092, §8.3 rule 4):
    /// the order of a <c>::</c> list must never decide a call, and it did — the same call compiled
    /// or failed depending on which conformance stood first. The open type parameters are bound to
    /// <see cref="ErrorType"/> so the report stays the one sentence: no SEM0060 about the parameter
    /// behind it, no assignment error about an argument the error already explains.</para></summary>
    private void UnifyThroughConformance(GenericInstance wanted, LyrType arg,
        Dictionary<GenericParamSymbol, LyrType> map, Span argSpan)
    {
        // Nothing open, nothing to choose: with 'pick<int>(t, …)' the written argument has
        // already bound T (rule 1), and several conformances stop mattering — the argument is
        // checked against the substituted parameter like any other.
        if (!HasOpenParam(wanted, map)) return;
        if (TypeFacts.SymbolOf(arg) is not { } symbol) return;

        // If the argument is itself an instance ('ArrayIterator<string>'), its substitution has to go
        // on top: the DEFINITION's conformance list says 'Iterator<T>' with the class's type
        // parameter, not 'Iterator<string>'. Without this step a type parameter comes out instead of
        // a concrete type.
        var ofInstance = arg is GenericInstance gi ? SubstMap(gi) : EmptySubst;

        // All conformances to the wanted interface, not the first: InterfacesOf deduplicates by
        // INSTANCE across the type's list and its visible extend blocks, so two hits are genuinely
        // two different conformances.
        var hits = new List<Dictionary<GenericParamSymbol, LyrType>>();
        foreach (var (iface, subst) in InterfacesOf(symbol))
            if (ReferenceEquals(iface, wanted.Definition))
                hits.Add(subst);

        if (hits.Count > 1)
        {
            var instances = hits.Select(subst => TypeFacts.Display(
                new GenericInstance(wanted.Definition, wanted.Definition.Generics
                    .Select(g => subst.TryGetValue(g, out var b) ? Substitute(b, ofInstance) : LyrType.Error)
                    .ToArray())));
            var count = hits.Count == 2 ? "twice" : $"{hits.Count} times";
            _de.Report("LYR-SEM0092", Severity.Error, argSpan,
                $"'{TypeFacts.Display(arg)}' conforms to '{wanted.Definition.Name}' {count} — as "
                + string.Join(" and as ", instances.Select(i => $"'{i}'"))
                + " — and inference through a conformance does not choose; write the type argument "
                + "explicitly");
            BindOpenParamsToError(wanted, map);
            return;
        }

        if (hits.Count == 1)
        {
            // 'subst' maps the INTERFACE's generics onto what stood in the '::'. Walking both in the
            // same order, 'Iterator<T>' against '{T_iface -> int}' binds the calling function's T to
            // int.
            var subst = hits[0];
            var n = Math.Min(wanted.Definition.Generics.Length, wanted.Arguments.Length);
            for (var i = 0; i < n; i++)
                if (subst.TryGetValue(wanted.Definition.Generics[i], out var bound))
                    UnifyInfer(wanted.Arguments[i], Substitute(bound, ofInstance), map, argSpan);
        }
    }

    /// <summary>Does this type mention a type parameter the mapping has not bound yet?</summary>
    private static bool HasOpenParam(LyrType t, Dictionary<GenericParamSymbol, LyrType> map) => t switch
    {
        TypeParamType tp => !map.ContainsKey(tp.Param),
        AssocOf a => HasOpenParam(a.Base, map),
        Optional o => HasOpenParam(o.Inner, map),
        ArrayOf a => HasOpenParam(a.Element, map),
        SliceOf s => HasOpenParam(s.Element, map),
        InlineArrayOf ia => HasOpenParam(ia.Element, map),
        TupleOf tu => tu.Elements.Any(e => HasOpenParam(e, map)),
        FnType f => f.Parameters.Any(p => HasOpenParam(p, map)) || HasOpenParam(f.Return, map)
                    || f.Throws.Any(t => HasOpenParam(t, map)),
        GenericInstance g => g.Arguments.Any(a => HasOpenParam(a, map)),
        RangeOf r => HasOpenParam(r.Element, map),
        CoroutineOf c => HasOpenParam(c.Yield, map),
        _ => false,
    };

    /// <summary>Binds every type parameter still open inside <paramref name="wanted"/> to
    /// <see cref="ErrorType"/> — the "already reported here" marker that keeps the downstream
    /// checks quiet.</summary>
    private static void BindOpenParamsToError(LyrType wanted, Dictionary<GenericParamSymbol, LyrType> map)
    {
        switch (wanted)
        {
            case TypeParamType tp: map.TryAdd(tp.Param, LyrType.Error); break;
            case Optional o: BindOpenParamsToError(o.Inner, map); break;
            case ArrayOf a: BindOpenParamsToError(a.Element, map); break;
            case SliceOf s: BindOpenParamsToError(s.Element, map); break;
            case InlineArrayOf ia: BindOpenParamsToError(ia.Element, map); break;
            case TupleOf t: foreach (var e in t.Elements) BindOpenParamsToError(e, map); break;
            case FnType f:
                foreach (var p in f.Parameters) BindOpenParamsToError(p, map);
                BindOpenParamsToError(f.Return, map);
                foreach (var t in f.Throws) BindOpenParamsToError(t, map);
                break;
            case GenericInstance g: foreach (var a in g.Arguments) BindOpenParamsToError(a, map); break;
            case RangeOf r: BindOpenParamsToError(r.Element, map); break;
            case CoroutineOf c: BindOpenParamsToError(c.Yield, map); break;
        }
    }

    /// <summary>
    /// Is there an <see cref="ErrorType"/> anywhere inside this type?
    /// </summary>
    /// <remarks>
    /// <para><c>IsError</c> alone is not enough: a block lambda whose returns disagree has the type
    /// <c>fn(int) -&gt; &lt;error&gt;</c> — intact outside, broken inside. The cause is already
    /// reported, and a second line about a type argument that cannot be inferred would bury it.</para>
    /// </remarks>
    private static bool ContainsError(LyrType type) => type switch
    {
        AssocOf a => ContainsError(a.Base),
        ErrorType => true,
        OpaqueRef oq => ContainsError(oq.Underlying),
        Optional o => ContainsError(o.Inner),
        ArrayOf a => ContainsError(a.Element),
        SliceOf s => ContainsError(s.Element),
        InlineArrayOf ia => ContainsError(ia.Element),
        TupleOf t => t.Elements.Any(ContainsError),
        FnType f => f.Parameters.Any(ContainsError) || ContainsError(f.Return) || f.Throws.Any(ContainsError),
        GenericInstance g => g.Arguments.Any(ContainsError),
        RangeOf r => ContainsError(r.Element),
        CoroutineOf c => ContainsError(c.Yield),
        _ => false,
    };

    private void CheckConstraints(GenericParamSymbol[] generics, LyrType[] args, Span span)
    {
        var n = Math.Min(generics.Length, args.Length);

        // The FULL mapping rather than one parameter at a time: a constraint may name the other type
        // parameters (`<K, V :: [Map<K, V>]>`), and without the other bindings `Map<K, V>` could not
        // resolve to `Map<string, int>`.
        var map = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < n; i++) map[generics[i]] = args[i];

        for (var i = 0; i < n; i++) CheckSatisfies(generics[i], args[i], map, span);
    }

    private void CheckInferredConstraints(GenericParamSymbol[] generics, Dictionary<GenericParamSymbol, LyrType> map, Span span)
    {
        foreach (var g in generics)
            if (map.TryGetValue(g, out var arg)) CheckSatisfies(g, arg, map, span);
    }

    /// <summary>
    /// Does <paramref name="arg"/> satisfy the constraints of <paramref name="param"/>?
    /// </summary>
    /// <param name="substitution">The bindings of all type parameters at this call site. A constraint
    /// carries its own type argument (`&lt;T :: [Eq&lt;T&gt;]&gt;`), and `Eq&lt;T&gt;` only becomes the
    /// question actually asked once `T := int`.</param>
    private void CheckSatisfies(GenericParamSymbol param, LyrType arg,
        Dictionary<GenericParamSymbol, LyrType> substitution, Span span)
    {
        foreach (var c in param.Constraints)
        {
            if (ConstraintInterface(c) is not { } iface) continue;

            // The constraint as a TYPE rather than only as a symbol: 'Src<string>' and 'Src<int>' are
            // the same symbol and different requirements.
            var wanted = Substitute(
                ResolveType(c, _currentModule?.Members ?? _comp.Builtins), substitution);

            if (Satisfies(arg, iface, wanted)) continue;

            _de.Report("LYR-SEM0028", Severity.Error, span,
                $"type '{TypeFacts.Display(arg)}' does not satisfy constraint "
                + $"'{TypeFacts.Display(wanted)}' on '{param.Name}'");
        }
    }

    private TypeSymbol? ConstraintInterface(TypeNode c) => Conformance.InterfaceOf(c, _binding);

    // Does arg satisfy the constraint iface? User types through their declared interface list, a type
    // parameter through its own constraints, and a BUILTIN through a visible 'extend int :: [I]' —
    // the same question as for every other type, answered by the same function.
    /// <param name="wanted">The interface WITH ITS TYPE ARGUMENTS, such as <c>Src&lt;string&gt;</c>.
    /// <c>iface</c> alone would be the symbol <c>Src</c>, under which <c>Ones :: [Src&lt;int&gt;]</c>
    /// would satisfy a <c>Src&lt;string&gt;</c> too.</param>
    private bool Satisfies(LyrType arg, TypeSymbol iface, LyrType wanted) => arg switch
    {
        NamedRef nr => IsTheConstraint(nr, nr.Symbol, iface, wanted)
                       || ImplementsWithExtensions(nr.Symbol, iface, wanted, EmptySubst),

        // For an instance its own arguments count: 'class Box<T> :: [Src<T>]' satisfies exactly
        // 'Src<int>' for 'Box<int>'.
        GenericInstance gi => IsTheConstraint(gi, gi.Definition, iface, wanted)
                              || ImplementsWithExtensions(gi.Definition, iface, wanted, SubstMap(gi)),

        TypeParamType tp => tp.Param.Constraints.Any(c =>
            NodeReaches(c, _currentModule?.Members ?? _comp.Builtins, iface, wanted, EmptySubst, tp)),

        PrimitiveType prim when BuiltinSymbol(prim) is { } builtin =>
            ImplementsWithExtensions(builtin, iface, wanted, EmptySubst),

        // 'never' has no value and so every conformance vacuously: 'E = never', what a set of
        // nothing binds (05 E2 K4), satisfies 'E :: [Error]'.
        NeverType => true,

        // An OPAQUE alias satisfies nothing: it has no conformance list, and falling into the
        // permissive default below would let 'Map<Entity, V>' compile against members the type
        // walled off. Convert to the underlying where a constraint is needed.
        OpaqueRef => false,
        _ => true // external or error: pass through opaquely
    };

    /// <summary>
    /// The argument IS the interface the constraint names.
    ///
    /// <para>Asked first because the walk below cannot answer it: it reads the interfaces a type
    /// DECLARES, which for an interface are its parents — so <c>Named</c> found everything above
    /// <c>Named</c> and never <c>Named</c> itself. A value of an interface type could therefore not
    /// be passed to <c>fn show&lt;T :: [Named]&gt;</c>, and said so in the one sentence that cannot
    /// be true: "type 'Named' does not satisfy constraint 'Named' on 'T'".</para>
    ///
    /// <para>It is sound for the same reason the constraint exists: the constraint promises the
    /// members, and an interface value carries a table of exactly those. The call inside the
    /// generic becomes a virtual dispatch rather than a direct one, which is what an interface
    /// value is for.</para>
    ///
    /// <para>The type arguments still have to line up — <c>Src&lt;int&gt;</c> is not
    /// <c>Src&lt;string&gt;</c> — so the same <c>Matches</c> decides it as everywhere else.</para>
    /// </summary>
    private static bool IsTheConstraint(LyrType arg, TypeSymbol ts, TypeSymbol iface, LyrType wanted) =>
        ReferenceEquals(ts, iface) && Matches(arg, EmptySubst, wanted);

    // Conformance through the declared interfaces OR a visible `extend T :: [I]` block — each
    // reaching through its parents: declaring the child implies the whole chain.
    private bool ImplementsWithExtensions(TypeSymbol ts, TypeSymbol iface, LyrType wanted,
        Dictionary<GenericParamSymbol, LyrType> ofInstance)
    {
        var self = Substitute(SelfType(ts), ofInstance);
        foreach (var node in DeclaredInterfaceNodes(ts))
            if (NodeReaches(node, DeclarationScope(ts), iface, wanted, ofInstance, self))
                return true;

        foreach (var block in _comp.Extensions.Blocks)
        {
            if (!ReferenceEquals(block.Target, ts)) continue;
            if (_currentModule is not null && !_comp.Sees(_currentModule, block.Module)) continue;
            if (block.Decl.Target is not NamedType { TypeArguments.Length: > 0 })
            {
                foreach (var node in block.Decl.Interfaces)
                    if (NodeReaches(node, DeclarationScope(ts), iface, wanted, ofInstance, self))
                        return true;
                continue;
            }
            // A block on an INSTANCE (03 T7 X1) — 'List<int>', or 'List<T>' with the block's own
            // parameters — reaches where its target matches the instance and its constraints hold
            // for what the match bound; the lowering builds a row for exactly the instances
            // proved here.
            if (BlockSubstitution(block, self) is not { } blockMap) continue;
            foreach (var node in block.Decl.Interfaces)
                if (NodeReaches(node, block.MethodScope, iface, wanted, blockMap, self))
                {
                    _result.RecordBlockConformance(self, iface, block);
                    return true;
                }
        }
        return false;
    }

    // Does this conformance node reach 'iface' — directly or through a parent — with matching
    // type arguments?
    private bool NodeReaches(TypeNode node, SymbolTable scope, TypeSymbol iface, LyrType wanted,
        Dictionary<GenericParamSymbol, LyrType> ofInstance, LyrType? self = null)
    {
        if (Conformance.InterfaceOf(node, _binding) is not { } direct) return false;
        foreach (var (p, inst) in InterfaceClosure(direct, ResolveType(node, scope)))
        {
            if (!ReferenceEquals(p, iface)) continue;
            // 'Self' on both sides is the conforming type (03 T5): 'Vec2 :: [Add]' is 'Add<Vec2>'
            // written short, and so is the constraint 'T :: [Add]' at 'T = Vec2'.
            var selfMap = self is null ? EmptySubst : SelfMap(p, self);
            // The fixations are asked below, the arguments here: 'Iterator<Item = int>' names the
            // same conformance as 'Iterator'.
            var bare = wanted is GenericInstance { Fixations: not null } fixing
                ? fixing.Definition.Generics.Length == 0 ? new NamedRef(fixing.Definition) : fixing with { Fixations = null }
                : wanted;
            if (!Matches(Substitute(inst, selfMap), ofInstance, Substitute(bare, selfMap))) continue;
            // A fixation at the constraint, 'T :: [Iterator<Item = int>]' (03 T6), asks the
            // conformer's answer to be that type.
            if (wanted is GenericInstance { Fixations: { Length: > 0 } fixations } && self is not null && self is not TypeParamType)
            {
                var all = true;
                foreach (var (member, fixedTo) in fixations)
                    if (!LyrType.Equal(ResolveAssociated(self, member, Substitute(inst, selfMap)), Substitute(fixedTo, ofInstance))) { all = false; break; }
                if (!all) continue;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Does a declared conformance match what the constraint demands?
    ///
    /// <para>An interface WITHOUT type arguments compares through the symbol — there is nothing to
    /// distinguish there, and <c>ResolveType</c> yields a <c>NamedRef</c>. Only for a generic instance
    /// do the arguments count.</para>
    /// </summary>
    private static bool Matches(LyrType declared, Dictionary<GenericParamSymbol, LyrType> subst,
        LyrType wanted)
    {
        var resolved = Substitute(declared, subst);
        if (resolved is not GenericInstance && wanted is not GenericInstance) return true;
        return LyrType.Equal(resolved, wanted);
    }

    /// <summary>The scope in which a type's conformance list stands; its own type parameters are
    /// visible there (<c>class Box&lt;T&gt; :: [Src&lt;T&gt;]</c>).</summary>
    private SymbolTable DeclarationScope(TypeSymbol ts) => ts.Members;

    private LyrType BindMember(MemberExpr mem, (LyrType type, Symbol? sym) r)
    {
        if (r.sym is not null) _result.BindRef(mem, r.sym);
        return r.type;
    }

    /// <summary>
    /// The symbol a callee expression was bound to, through an import binding to what it imports.
    ///
    /// <para>A question about the table and nothing else. It does NOT answer what kind of thing the
    /// expression is — <c>CheckMember</c> reads that off the type, because the table cannot tell a
    /// type NAME from an expression that builds a value of that type.</para>
    /// </summary>
    private Symbol? TargetSymbol(Expr target)
    {
        var sym = _result.RefOf(target);
        return sym is ImportBindingSymbol ib ? ib.Target : sym;
    }

    // Member resolution: own members, then visible extensions, then interface default methods.
    private (LyrType, Symbol?) InstanceMember(TypeSymbol ts, string member, Span span)
    {
        if (ts.Members.LookupLocal(member) is { } own)
            return own switch
            {
                FieldSymbol fs => (FieldType(fs), fs),

                // A static member belongs to the type, not to the instance.
                FunctionSymbol { IsStatic: true } fn => (Report(span, "LYR-SEM0055",
                    $"'{fn.Name}' is static — call it on the type: '{ts.Name}.{member}(…)'"), fn),

                FunctionSymbol fn => (FnTypeOf(fn), fn),

                GlobalSymbol g => (Report(span, "LYR-SEM0055",
                    $"'{member}' is a static constant — read it from the type: '{ts.Name}.{member}'"), g),

                _ => (Report(span, "LYR-SEM0012", $"'{ts.Name}' has no member '{member}'"), null)
            };
        // An interface value reaches its parent's members: the chain-prefix slot layout puts them
        // in the child's own method table, so the dispatch needs no re-wrapping toward the parent.
        // This also serves 'this' inside a default body, which has the interface's own type.
        if (ts.Kind == TypeSymbolKind.Interface)
            foreach (var (parent, inst) in InterfaceClosure(ts, new NamedRef(ts)))
            {
                if (ReferenceEquals(parent, ts)) continue;
                if (parent.Members.LookupLocal(member) is not FunctionSymbol pfn) continue;
                var subst = inst is GenericInstance g ? SubstMap(g) : EmptySubst;
                return (Substitute(FnTypeOf(pfn), subst), pfn);
            }
        if (ExtensionMember(ts, member, span) is { } ext)
        {
            // The instance path used to fall through to a STATIC extension without checking — the
            // asymmetry the type path never had. Recorded as an accident, a warning through 1.x,
            // an error since 2.0: the clock its message announced has run out. The member still
            // returns so the rest of the expression types.
            if (ext.IsStatic)
                _de.Report("LYR-SEM0074", Severity.Error, span,
                    $"'{member}' is a static extension and belongs to the type — "
                    + $"call '{ts.Name}.{member}(…)'; the instance form is an error since 2.0");
            return (ExtensionSignature(ext, span), ext);
        }
        if (DefaultMember(ts, member, span) is { } def) return def;
        if (DelegatedMember(ts, member) is { } forwarded) return forwarded;
        return (Report(span, "LYR-SEM0012", $"'{ts.Name}' has no member '{member}'",
            NameSuggestion.Note(member, MemberFacts
                .OfInstance(_comp, _binding, ts, _currentModule)
                .Select(candidate => candidate.Symbol.Name))), null);
    }

    /// <summary>A visible extension method of this name, meaning the declaring module is the
    /// current one or is imported. The first one wins.
    ///
    /// <para>Several of a name are an OVERLOAD SET since 3.0, not an ambiguity: the call site
    /// chooses among them by its arguments, and reports for itself when they do not separate.
    /// What stays ambiguous here is two extensions offering the same member with the SAME
    /// parameters — nothing could ever tell those apart (LYR-SEM0044).</para></summary>
    /// <summary>The receiver a member is looked up on, for a generic block's signature.</summary>
    private LyrType? _memberReceiver;

    /// <summary>A block member's signature for the receiver at hand: a generic block's parameters
    /// bound by the receiver (03 T7 X1), its constraints checked — the member is not there where
    /// they fail (<c>LYR-SEM0134</c>).</summary>
    private LyrType ExtensionSignature(FunctionSymbol ext, Span span, LyrType? receiver = null)
    {
        var signature = FnTypeOf(ext);
        receiver ??= _memberReceiver; // a static member names its receiver: 'Box<int>.default()'
        if (_comp.Extensions.BlockOf(ext) is not { Decl.Target: NamedType { TypeArguments.Length: > 0 } } block
            || receiver is null) return signature;
        if (BlockSubstitution(block, receiver) is { } map) return Substitute(signature, map);
        // A block on one instance adds to that instance alone: elsewhere the member is simply
        // not there. A generic block's constraints failing is worth the sentence.
        if (block.Generics.Length == 0)
            return Report(span, "LYR-SEM0012", $"'{TypeFacts.Display(receiver)}' has no member '{ext.Name}'");
        return Report(span, "LYR-SEM0134",
            $"'{ext.Name}' is added to '{TypeFacts.Display(BlockTargetType(block))}' under the block's constraints, "
            + $"which '{TypeFacts.Display(receiver)}' does not satisfy");
    }

    private FunctionSymbol? ExtensionMember(TypeSymbol ts, string member, Span span)
    {
        List<ExtensionMethod> visible = new();
        foreach (var ext in _comp.Extensions.MethodsFor(ts))
        {
            if (ext.Symbol.Name != member) continue;
            if (_currentModule is not null && !_comp.Sees(_currentModule, ext.Module)) continue;
            if (!visible.Any(f => ReferenceEquals(f.Symbol, ext.Symbol))) visible.Add(ext);
        }

        // Two conformance blocks each implementing the name for their interface (04 D3): the
        // name belongs to the block, and the unqualified call says nothing about which. Every
        // other duplicate was refused at its declaration (LYR-SEM0121).
        if (visible.Count > 1 && visible.All(v => v.InConformanceBlock))
        {
            var interfaces = visible.Select(v => Conformance.InterfaceOf(v.Block.Decl.Interfaces[0], _binding)?.Name ?? "?").ToList();
            _de.Report("LYR-SEM0122", Severity.Error, span,
                $"'{member}' is implemented on '{ts.Name}' for {string.Join(" and ", interfaces.Select(i => $"'{i}'"))} "
                + $"separately — qualify the call: '{interfaces[0]}.{member}(…)'");
            return visible[0].Symbol;
        }

        return visible.Count > 0 ? visible[0].Symbol : null;
    }

    /// <summary>A member a type forwards to a field (<c>:: [Walker by legs]</c>, 04 D1): the
    /// interface's member, typed as the interface declares it.</summary>
    private (LyrType, Symbol?)? DelegatedMember(TypeSymbol ts, string member)
    {
        foreach (var (iface, subst) in InterfacesOf(ts))
        {
            if (_result.DelegationOf(ts, iface) is null) continue;
            if (iface.Members.LookupLocal(member) is not FunctionSymbol { IsStatic: false } fn) continue;
            return (Substitute(FnTypeOf(fn), WithSelf(subst, iface, SelfType(ts))), fn);
        }
        return null;
    }

    // An interface default method, one with a body, through the type's interfaces, declared or via
    // extend. Two defaults from different interfaces give SEM0043, which asks for an explicit
    // override.
    private (LyrType, Symbol?)? DefaultMember(TypeSymbol ts, string member, Span span)
    {
        (LyrType type, Symbol sym)? found = null;
        var ambiguous = false;
        foreach (var (iface, subst) in InterfacesOf(ts))
        {
            if (iface.Members.LookupLocal(member) is not FunctionSymbol fn) continue;
            if (fn.Declaration is not FunctionDecl { Body: not null }) continue; // defaults only, not abstract ones
            var t = Substitute(FnTypeOf(fn), WithSelf(subst, iface, SelfType(ts)));
            if (found is null) found = (t, fn);
            else if (!ReferenceEquals(found.Value.sym, fn)) ambiguous = true;
        }
        // Two defaults nobody overrides were refused at the type's declaration (CheckMethodSet,
        // LYR-SEM0043); here the first answers, so the call types and the error stays one.
        _ = ambiguous;
        return found is { } f ? (f.type, f.sym) : null;
    }

    // All interfaces of a type with their substitution, mapping the interface generics to the type
    // arguments from the '::'. Declared interfaces plus those from visible `extend T :: [I]` blocks.
    private IEnumerable<(TypeSymbol iface, Dictionary<GenericParamSymbol, LyrType> subst)> InterfacesOf(TypeSymbol ts)
    {
        // One instance list across the whole walk: the same instance reached twice — a parent
        // written out beside its child — is one conformance, not two.
        var seen = new List<LyrType>();
        foreach (var node in DeclaredInterfaceNodes(ts))
            foreach (var r in ClosureOfNode(node, seen)) yield return r;
        foreach (var block in _comp.Extensions.Blocks)
        {
            if (!ReferenceEquals(block.Target, ts)) continue;
            if (_currentModule is not null && !_comp.Sees(_currentModule, block.Module)) continue;
            foreach (var node in block.Decl.Interfaces)
                foreach (var r in ClosureOfNode(node, seen)) yield return r;
        }
    }

    private static TypeNode[] DeclaredInterfaceNodes(TypeSymbol ts) => ts.Declaration switch
    {
        StructDecl s => s.Interfaces,
        ClassDecl c => c.Interfaces,
        EnumDecl e => e.Interfaces,
        InterfaceDecl i => i.Interfaces, // the parents, for the child-to-parent transition (04 D10)
        _ => []
    };

    /// <summary>
    /// One resolved interface instance plus its transitive parents, each with the child's type
    /// arguments substituted through: <c>Hashable&lt;S&gt;</c> yields itself and
    /// <c>Equatable&lt;S&gt;</c> when <c>Hashable&lt;T&gt;</c> declares <c>:: [Equatable&lt;T&gt;]</c>.
    ///
    /// <para>Deliberately NOT reached from a value of interface type: a fat pointer carries the
    /// method table of its own interface and nothing above it, so the closure applies where a
    /// conformance is declared, not where an interface value flows.</para>
    /// </summary>
    private IEnumerable<(TypeSymbol iface, LyrType instance)> InterfaceClosure(
        TypeSymbol iface, LyrType instance, HashSet<TypeSymbol>? seen = null)
    {
        seen ??= new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance);
        if (!seen.Add(iface)) yield break;
        yield return (iface, instance);
        if (iface.Declaration is not InterfaceDecl decl) yield break;
        var subst = instance is GenericInstance gi ? SubstMap(gi) : EmptySubst;
        foreach (var node in decl.Interfaces)
        {
            if (Conformance.InterfaceOf(node, _binding) is not { } parent) continue;
            // The parent node stands in the child's scope ('Equatable<T>' with Hashable's T);
            // substituting through the child instance turns it into the conforming type's terms.
            var resolved = Substitute(ResolveType(node, DeclarationScope(iface)), subst);
            foreach (var r in InterfaceClosure(parent, resolved, seen)) yield return r;
        }
    }

    /// <summary>The closure behind one conformance node: the named interface and its transitive
    /// parents, as (definition, substitution) pairs.
    ///
    /// <para><paramref name="seenInstances"/> deduplicates ACROSS a conformance list by the
    /// resolved instance, not by the symbol: a parent written out beside its child is one
    /// conformance, but <c>Mul&lt;Vec2&gt;</c> beside <c>Mul&lt;float&gt;</c> is two — the second
    /// must still reach the signature check that refuses it.</para></summary>
    private IEnumerable<(TypeSymbol iface, Dictionary<GenericParamSymbol, LyrType> subst)>
        ClosureOfNode(TypeNode node, List<LyrType>? seenInstances = null)
    {
        if (Conformance.InterfaceOf(node, _binding) is not { } iface) yield break;
        var resolved = ResolveType(node, _currentModule?.Members ?? _comp.Builtins);
        foreach (var (i, inst) in InterfaceClosure(iface, resolved))
        {
            if (seenInstances is not null)
            {
                if (seenInstances.Any(t => LyrType.Equal(t, inst))) continue;
                seenInstances.Add(inst);
            }
            yield return (i, inst is GenericInstance gi ? SubstMap(gi) : EmptySubst);
        }
    }

    /// <param name="instance">The instance from a type path with arguments
    /// (<c>Pair&lt;int&gt;.of(3)</c>). When present, the member's type is substituted through it;
    /// otherwise <c>T</c> stands in the result and the error arrives as "cannot assign 'int' to 'T'"
    /// one level too late.</param>
    private (LyrType, Symbol?) MemberOfType(TypeSymbol ts, string member, Span span,
        GenericInstance? instance = null)
    {
        // A generic type WITHOUT arguments in value position: a type path requires them explicitly,
        // as there is no field inference. Saying so here rather than letting the substitution
        // silently not happen is the difference between a message about the cause and one about its
        // consequence.
        if (instance is null && ts.Generics.Length > 0
            && ts.Members.LookupLocal(member) is FunctionSymbol or EnumVariantSymbol or GlobalSymbol)
            return (Report(span, "LYR-SEM0063",
                $"'{ts.Name}' is generic — write its type arguments: "
                + $"'{ts.Name}<{string.Join(", ", ts.Generics.Select(g => g.Name))}>.{member}'"),
                null);

        var subst = instance is null ? EmptySubst : SubstMap(instance);
        LyrType Of(LyrType t) => instance is null ? t : Substitute(t, subst);

        return ts.Members.LookupLocal(member) switch
        {
            // Without 'static' the method needs a receiver; otherwise the lowering would produce a
            // field access without an object.
            FunctionSymbol { IsStatic: false } fn => (Report(span, "LYR-SEM0055",
                $"'{fn.Name}' is an instance method and needs a receiver — " +
                $"call it on a value, or declare it 'static fn {member}(…)'"), fn),

            FunctionSymbol fn => (Of(FnTypeOf(fn)), fn),             // a static fn, such as the factory Point.new
            GlobalSymbol g => (Of(TypeOfGlobalReference(g, span)), g), // static let
            EnumVariantSymbol ev => (Of(VariantConstructorType(ev, ts, span, instance)), ev),

            FieldSymbol => (Report(span, "LYR-SEM0055",
                $"'{member}' is a field of '{ts.Name}' and belongs to an instance, not to the type"), null),

            // The same fallback the instance path has: an extension block may add a static member,
            // and the lowering already emits one without a receiver.
            _ => ExtensionMember(ts, member, span) switch
            {
                // A generic block's own parameters are bound by the named instance (03 T7 X1):
                // 'Box<int>.default()' from 'extend<T :: [Default]> Box<T>' answers 'Box<int>'.
                { IsStatic: true } ext => (Of(ExtensionSignature(ext, span, instance)), ext),

                { } ext => (Report(span, "LYR-SEM0055",
                    $"'{ext.Name}' is an instance method and needs a receiver — " +
                    $"call it on a value, or declare it 'static fn {member}(…)'"), ext),

                null => (Report(span, "LYR-SEM0012",
                    $"'{ts.Name}' has no static member '{member}'",
                    NameSuggestion.Note(member, ts.Members.Symbols.Select(s => s.Name))), null),
            }
        };
    }

    private (LyrType, Symbol?) MemberOfModule(ModuleSymbol mod, string member, Span span) =>
        mod.Members.LookupLocal(member) switch
        {
            FunctionSymbol fn => (FnTypeOf(fn), fn),
            GlobalSymbol g => (TypeOfGlobalReference(g, span), g),
            ExternalSymbol ex => (LyrType.Error, ex),

            // The qualified name stands for the type itself, as the bare name does: as a member
            // TARGET it carries on ('random.Random.seeded(…)' dispatches like 'Random.seeded(…)'),
            // and in value position CheckExpr reports LYR-SEM0052. An Error here instead poisoned
            // the whole chain silently — no diagnostic, and the lowering threw on the <error> type.
            TypeSymbol tsym => (new NonValueType(tsym, "type"), tsym),
            _ => (Report(span, "LYR-SEM0012", $"module '{mod.FullName}' has no member '{member}'"), null)
        };

    /// <param name="instance">The instance in <c>Opt&lt;int&gt;.Some(5)</c>. It is the RESULT TYPE of
    /// the construction, which the substitution cannot supply, because what stands here is the bare
    /// enum symbol rather than a type parameter.</param>
    private LyrType VariantConstructorType(EnumVariantSymbol ev, TypeSymbol enumTs, Span span,
        GenericInstance? instance = null)
    {
        var v = (EnumVariant)ev.Declaration!;
        LyrType result = instance is not null ? instance : new NamedRef(enumTs);

        if (v.TupleFields is not null)
            return new FnType(v.TupleFields.Select(t => ResolveType(t, enumTs.Members)).ToArray(), result);
        if (v.StructFields is not null)
            return Report(span, "LYR-SEM0031", $"struct variant '{ev.Name}' must be constructed with '{ev.Name} {{ … }}'");
        return result; // a unit variant as a value
    }

    private LyrType FieldType(FieldSymbol fs) => ResolveType(((FieldDecl)fs.Declaration!).Type, _comp.Builtins);

    // --- attributes ---

    private enum AttributeTarget { Module, Type, Function, Member }

    private static string MarkerName(AttributeTarget target) => target switch
    {
        AttributeTarget.Module => "OnModule",
        AttributeTarget.Type => "OnType",
        _ => "OnFunction",
    };

    /// <summary>
    /// The attributes of one declaration or of the module header.
    ///
    /// <para>An attribute IS a struct: the name resolves like any type name, the arguments are
    /// checked as its initializer, and where it may sit is the marker interface it declares —
    /// conformance, not the name, the same nominal rule the operators follow. What ends up in the
    /// bytecode has to be a value at compile time, hence the literal rule; and because the
    /// emitted row carries EVERY field, a field the use does not write needs a literal default.
    /// </para>
    /// </summary>
    private void CheckAttributes(AttributeNode[] attributes, AttributeTarget target,
        bool targetIsGeneric, SymbolTable scope, string targetDescription)
    {
        if (attributes.Length == 0) return;

        // Duplicates by resolved symbol, not by written path: two spellings of one type are still
        // one metadata row too many.
        var seen = new List<TypeSymbol>();
        foreach (var attribute in attributes)
        {
            var ts = CheckAttribute(attribute, target, targetIsGeneric, scope, targetDescription);
            if (ts is null) continue;
            if (seen.Contains(ts))
                _de.Report("LYR-SEM0068", Severity.Error, attribute.PathSpan,
                    $"'@{ts.Name}' sits on this declaration twice");
            else
                seen.Add(ts);
        }
    }

    /// <returns>The resolved attribute type, or <c>null</c> when the name resolved to nothing an
    /// attribute could be. Diagnosed findings beyond that still return the symbol, so the
    /// duplicate check above keeps working on a faulty attribute.</returns>
    private TypeSymbol? CheckAttribute(AttributeNode attribute, AttributeTarget target,
        bool targetIsGeneric, SymbolTable scope, string targetDescription)
    {
        var (sym, _) = ResolveInitPath(attribute.Path, scope);
        var written = string.Join('.', attribute.Path);
        if (sym is null)
        {
            Report(attribute.PathSpan, "LYR-SEM0011", $"unknown type '{written}'",
                attribute.Path is [var single]
                    ? NameSuggestion.Note(single, NamesIn(scope, typesOnly: true))
                    : null);
            foreach (var f in attribute.Fields) CheckExpr(f.Value, scope);
            return null;
        }

        if (sym is not TypeSymbol { Kind: TypeSymbolKind.Struct } ts)
        {
            _de.Report("LYR-SEM0065", Severity.Error, attribute.PathSpan,
                $"'@{written}' is not an attribute — an attribute is a struct declaring "
                + $"'{MarkerName(target)}'");
            foreach (var f in attribute.Fields) CheckExpr(f.Value, scope);
            return null;
        }

        _result.BindRef(attribute, ts); // '@Component' is a reference to its type, as an initializer is

        if (ts.Generics.Length > 0)
        {
            _de.Report("LYR-SEM0065", Severity.Error, attribute.PathSpan,
                $"a generic type cannot be an attribute — one metadata row cannot stand for every "
                + $"instance of '{ts.Name}'");
            return ts;
        }

        // A MEMBER carries only the row-less '@Deprecated' (§4.7): the module format has no
        // member targets, so any attribute that would need a row has no slot to land in. The
        // marker test is bypassed — 'Deprecated' declares no OnMember, and inventing one for
        // a single permitted attribute would be a marker without a second customer.
        if (target == AttributeTarget.Member)
        {
            if (!IsCanonicalDeprecated(ts))
            {
                _de.Report("LYR-SEM0065", Severity.Error, attribute.PathSpan,
                    $"'@{ts.Name}' cannot sit on {targetDescription} — only '@Deprecated' may: "
                    + "the module format has no member rows for anything else");
                return ts;
            }
        }
        else
        {
            var marker = target switch
            {
                AttributeTarget.Module => _onModule,
                AttributeTarget.Type => _onType,
                _ => _onFunction,
            };
            if (marker is null || !Satisfies(new NamedRef(ts), marker, new NamedRef(marker)))
            {
                _de.Report("LYR-SEM0065", Severity.Error, attribute.PathSpan,
                    $"'@{ts.Name}' cannot sit on {targetDescription} — declare '{ts.Name}' with "
                    + $"':: [{MarkerName(target)}]' to allow it here");
                return ts;
            }
        }

        // One exception: the compiler-read @Deprecated. Its consumer is the sema, not a
        // metadata row, so the one-row-many-instances conflict does not arise — the lowering
        // emits NO row for an attribute on a generic declaration.
        if (targetIsGeneric && !IsCanonicalDeprecated(ts))
            _de.Report("LYR-SEM0067", Severity.Error, attribute.PathSpan,
                "an attribute cannot sit on a generic declaration — there is one metadata row "
                + "and as many instances as the program creates");

        // A row holds a number, a string, a char, a bool or a variant tag (3.9.1, §4.7): a
        // field of any other type has no encoding, however writable its literal looks after
        // adaptation — the writer's total-function throw is what named this place. At the USE,
        // because the struct alone is an ordinary struct.
        foreach (var field in (ts.Declaration as StructDecl)?.Members.OfType<FieldDecl>() ?? [])
        {
            if (ts.Members.LookupLocal(field.Name) is not FieldSymbol rfs) continue;
            var rft = FieldType(rfs);
            if (rft.IsError || RowableType(rft)) continue;
            _de.Report("LYR-SEM0096", Severity.Error, attribute.PathSpan,
                $"'@{ts.Name}' cannot form a row: field '{field.Name}' is "
                + $"'{TypeFacts.Display(rft)}', and a row holds a number, a string, a char, a "
                + "bool or a unit variant");
        }

        var writtenFields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in attribute.Fields)
        {
            if (!writtenFields.Add(field.Name))
                _de.Report("LYR-SEM0068", Severity.Error, field.Span,
                    $"'@{ts.Name}' sets '{field.Name}' twice");

            if (ts.Members.LookupLocal(field.Name) is FieldSymbol fs)
            {
                var ft = FieldType(fs);
                CheckAssignable(field.Value, CheckExpr(field.Value, scope, ft), ft, field.Span);
                // AFTER the check, which is what binds the name this may be resolving through.
                if (AttributeValues.LiteralOf(field.Value, _result) is null)
                    ReportAttributeValueNotLiteral(field.Value);
            }
            else
            {
                _de.Report("LYR-SEM0015", Severity.Error, field.Span,
                    $"'{ts.Name}' has no field '{field.Name}'");
                CheckExpr(field.Value, scope);
            }
        }

        if (attribute.Positional is { } positional
            && !CheckPositionalValue(attribute, ts, positional, scope, writtenFields))
            return ts; // the form itself was refused; completeness would only echo it

        // The emitted row is COMPLETE: absent fields are filled from their defaults, so the host
        // never resolves one. A field that is neither written nor literal-defaulted has no value
        // to fill in.
        foreach (var field in (ts.Declaration as StructDecl)?.Members.OfType<FieldDecl>() ?? [])
        {
            if (writtenFields.Contains(field.Name)) continue;
            if (field.Default is not null && AttributeValues.LiteralOf(field.Default, _result) is not null)
                continue;
            _de.Report("LYR-SEM0069", Severity.Error, attribute.Span,
                field.Default is null
                    ? $"'@{ts.Name}' leaves '{field.Name}' without a value — write it, or give "
                      + "the field a literal default"
                    : $"'@{ts.Name}' leaves '{field.Name}' to a default that is not a value at "
                      + "compile time — a literal, or a 'let' whose initializer is one");
        }
        return ts;
    }

    /// <summary>The parenthesized value (3.9, §4.7): admitted by the WithArg conformance and
    /// filling the FIRST field under the same value rules as a written one. False when the
    /// attribute does not take the form at all.</summary>
    private bool CheckPositionalValue(AttributeNode attribute, TypeSymbol ts, Expr value,
        SymbolTable scope, HashSet<string> writtenFields)
    {
        var admitted = _withArg is not null && ConformancesTo(ts, _withArg, EmptySubst).Any();
        if (!admitted)
        {
            _de.Report("LYR-SEM0094", Severity.Error, attribute.Span,
                $"'@{ts.Name}' does not take a parenthesized value — the form belongs to an "
                + $"attribute declaring ':: [WithArg<T>]'; write the field by name instead");
            CheckExpr(value, scope);
            return false;
        }

        // The value fills the first field; that its type is the declared T is the DECLARATION's
        // promise (LYR-SEM0095), so here the field itself is the measure — as for a named write.
        var first = (ts.Declaration as StructDecl)?.Members.OfType<FieldDecl>().FirstOrDefault();
        if (first is null || ts.Members.LookupLocal(first.Name) is not FieldSymbol fs)
        {
            CheckExpr(value, scope);
            return true; // no field at all: SEM0095 already stands at the declaration
        }
        writtenFields.Add(first.Name);
        var ft = FieldType(fs);
        CheckAssignable(value, CheckExpr(value, scope, ft), ft, value.Span);
        if (AttributeValues.LiteralOf(value, _result) is null)
            ReportAttributeValueNotLiteral(value);
        return true;
    }

    /// <summary>Can a metadata row hold a value of this type? Scalars, string, char, bool and
    /// enums — the set §11's ConstValue encodes. Everything else has no encoding.</summary>
    private static bool RowableType(LyrType t) => t switch
    {
        PrimitiveType p => p.Kind != PrimitiveKind.Void,
        _ => TypeSymbolOf(t) is { Kind: TypeSymbolKind.Enum },
    };

    private void ReportAttributeValueNotLiteral(Expr value) =>
        _de.Report("LYR-SEM0066", Severity.Error, value.Span,
            PayloadVariant(value) is { } carried
                ? $"'{carried}' carries a payload; a row holds one value per field, "
                  + "so only a variant without one stands in an attribute"
                : "an attribute argument must be a value at compile time — a number, "
                  + "a string, a char, a bool, a unit enum variant, or a 'let' bound "
                  + "to one");

    /// <summary>The variant name when the expression CONSTRUCTS one with a payload — the near
    /// miss of an enum attribute argument, and worth its own sentence: "must be a literal" says
    /// nothing to someone who wrote a variant and only got the payload wrong.</summary>
    private string? PayloadVariant(Expr expr) =>
        expr is CallExpr { Callee: MemberExpr member }
        && _result.RefOf(member) is EnumVariantSymbol
            ? $"{string.Join('.', Flatten(member))}"
            : null;

    private static IEnumerable<string> Flatten(MemberExpr member) =>
        member.Target is MemberExpr inner
            ? Flatten(inner).Append(member.Member)
            : member.Target is IdentifierExpr id
                ? [id.Name, member.Member]
                : [member.Member];

    /// <summary>
    /// <c>p with { x = 3, pos.y = 4 }</c> (design/v5/spec/02 M6): a copy of <c>p</c> with the
    /// named fields replaced, of <c>p</c>'s type — on a struct only (W2): a class would be a
    /// clone with a new identity, a tuple has no field names. A path reaches into a struct the
    /// value holds by value (W6); a field named twice is refused, a field the type does not
    /// have too (W4); no field needs <c>var</c>, since nothing is written in place. The values
    /// see the old <c>p</c>: they are expressions over it, and the copy is what changes.
    /// </summary>
    private LyrType CheckWith(WithExpr w, SymbolTable scope)
    {
        var target = CheckExpr(w.Target, scope);
        if (target.IsError) { foreach (var f in w.Fields) CheckExpr(f.Value, scope); return LyrType.Error; }
        if (TypeFacts.KindOf(target) != TypeSymbolKind.Struct)
        {
            foreach (var f in w.Fields) CheckExpr(f.Value, scope);
            return Report(w.Span, "LYR-SEM0116",
                $"'with' copies a struct with fields replaced; '{TypeFacts.Display(target)}' is "
                + (TypeFacts.KindOf(target) == TypeSymbolKind.Class ? "a class — an object is changed in place, or cloned"
                    : target is TupleOf ? "a tuple, which has no fields to name" : "no struct"));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in w.Fields)
        {
            var key = string.Join(".", field.Path);
            if (!seen.Add(key))
                _de.Report("LYR-SEM0070", Severity.Error, field.Span, $"duplicate field '{key}' in 'with'");

            // Walk the path: every segment but the last names a struct held by value.
            var holder = target;
            LyrType? fieldType = null;
            for (var i = 0; i < field.Path.Length; i++)
            {
                if (TypeFacts.KindOf(holder) != TypeSymbolKind.Struct)
                {
                    _de.Report("LYR-SEM0116", Severity.Error, field.Span,
                        $"'{string.Join(".", field.Path.Take(i))}' is a '{TypeFacts.Display(holder)}', not a struct — a path in 'with' reaches through structs only");
                    fieldType = null;
                    break;
                }
                var owner = TypeFacts.SymbolOf(holder)!;
                if (owner.Members.LookupLocal(field.Path[i]) is not FieldSymbol fs)
                {
                    _de.Report("LYR-SEM0015", Severity.Error, field.Span, $"'{owner.Name}' has no field '{field.Path[i]}'");
                    fieldType = null;
                    break;
                }
                if (i == field.Path.Length - 1) _result.BindRef(field, fs);
                var subst = holder is GenericInstance gi ? SubstMap(gi) : EmptySubst;
                fieldType = Substitute(FieldType(fs), subst);
                holder = fieldType;
            }

            if (fieldType is null) { CheckExpr(field.Value, scope); continue; }
            CheckAssignable(field.Value, CheckExpr(field.Value, scope, fieldType), fieldType, field.Span);
        }
        return target;
    }

    private LyrType CheckStructInit(StructInitExpr si, SymbolTable scope, LyrType? expected)
    {
        // '.Rect { w = 1 }': the variant of the enum this position expects (08 Y9).
        if (si.IsImplicit)
        {
            if (EnumFromExpected(expected) is { } target
                && target.def.Members.LookupLocal(si.Path[0]) is EnumVariantSymbol implied)
                return CheckVariantInit(si, implied, target.def, target.instance, scope);
            foreach (var f in si.Fields) CheckExpr(f.Value, scope);
            return Report(si.Span, "LYR-SEM0113", EnumFromExpected(expected) is { } known
                ? $"'{known.def.Name}' has no variant '{si.Path[0]}'"
                : $"'.{si.Path[0]} {{ … }}' names a variant of the type this position expects — "
                  + (expected is null || expected.IsError ? "this position expects no particular type; name the enum"
                      : $"this position expects '{TypeFacts.Display(expected)}', which is not an enum"));
        }

        var (sym, owner) = ResolveInitPath(si.Path, scope);

        // An enum struct variant: qualified (Shape.Triangle { … }) or contextual (Triangle { … } in a
        // position with an expected enum type).
        if (sym is EnumVariantSymbol ev && owner is not null)
            return CheckVariantInit(si, ev, owner, ExpectedInstance(expected, owner), scope);
        if (sym is null && si.Path.Length == 1 && EnumFromExpected(expected) is { } ex
            && ex.def.Members.LookupLocal(si.Path[0]) is EnumVariantSymbol cev)
            return CheckVariantInit(si, cev, ex.def, ex.instance, scope);

        if (sym is not TypeSymbol ts)
        {
            if (sym is null)
                Report(si.Span, "LYR-SEM0011", $"unknown type '{string.Join('.', si.Path)}'",
                    si.Path is [var single]
                        ? NameSuggestion.Note(single, NamesIn(scope, typesOnly: true))
                        : null);
            foreach (var f in si.Fields) CheckExpr(f.Value, scope);
            return LyrType.Error;
        }

        // The initializer names its type, and until now that name was resolved and dropped — the
        // enum variant case records its symbol, this one did not, so nothing knew that
        // 'Point { … }' refers to Point. Safe to record only now that no consumer reads this table
        // to decide what KIND of receiver an expression is.
        _result.BindRef(si, ts);

        // A HOST type cannot be constructed. It has no layout this module knows: the host creates it
        // and the script passes it on. Without this diagnostic it is a compiler crash — the lowering
        // allocates an object after the empty class declaration while the variable carries the host
        // type, and the verifier reports 'cannot compare IrHostType with IrRefType'.
        if (Ir.Lowering.HostTypes.NameOf(ts, _comp) is { } hostName)
        {
            _de.Report("LYR-SEM0061", Severity.Error, si.Span,
                $"'{hostName}' is a host type — only the host can create one; a script receives "
                + "it and passes it on");
            foreach (var f in si.Fields) CheckExpr(f.Value, scope);
            return LyrType.Error;
        }

        // A generic type takes its type arguments from what is WRITTEN, else from the context. There
        // is still no inference from the field values: 'P { v = 1 }' with no context anywhere is an
        // error, not a guess from the '1'.
        LyrType result;
        Dictionary<GenericParamSymbol, LyrType> subst;
        Dictionary<StructInitField, LyrType>? prechecked = null;
        if (ts.Generics.Length > 0 || si.TypeArguments.Length > 0)
        {
            // Written arguments beat the context, as they do for an enum variant: 'Box<int> { … }'
            // says itself which instance is meant, and that holds where there is no context at all.
            LyrType[] args;
            if (si.TypeArguments.Any(IsPlaceholder))
                args = FillPlaceholders(si, ts, expected, scope, out prechecked);
            else if (si.TypeArguments.Length > 0)
                args = FillDefaults(ts, si.TypeArguments.Select(a => ResolveType(a, scope)).ToArray());
            else if (InstanceFromExpected(expected, ts) is { } fromContext)
                args = fromContext.Arguments;
            else
                args = [];

            if (args.Length != ts.Generics.Length)
            {
                _de.Report("LYR-SEM0026", Severity.Error, si.Span,
                    $"generic type '{ts.Name}' expects {ts.Generics.Length} type argument(s), got "
                    + $"{args.Length} — write them ('{ts.Name}<…> {{ … }}') or use it where the type "
                    + "is known");
                // Poison instead of checking the fields against UNBOUND parameters: each would
                // add a "cannot assign 'X' to 'T'" behind the one actual cause.
                return LyrType.Error;
            }
            var gi = new GenericInstance(ts, args);
            subst = SubstMap(gi);
            CheckConstraints(ts.Generics, args, si.Span);
            result = gi;
        }
        else
        {
            result = new NamedRef(ts);
            subst = EmptySubst;
        }

        // One value per field: the lowering writes fields by name, and a second write has no slot to
        // land in. The first occurrence stands; each repeat is reported at its own span, and its
        // value is still checked below so faults inside it surface too.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in si.Fields)
        {
            if (!seen.Add(field.Name))
                _de.Report("LYR-SEM0070", Severity.Error, field.Span,
                    $"duplicate field '{field.Name}' in initializer for '{ts.Name}'");
            if (ts.Members.LookupLocal(field.Name) is FieldSymbol fs)
            {
                // The initializer field names the declared field, and until now that reference was
                // resolved and dropped — 'x = 1' is a use of 'x' the same way the initializer's
                // head is a use of the type, and every-place-this-name-occurs was blind to it.
                _result.BindRef(field, fs);

                var ft = Substitute(FieldType(fs), subst);
                var given = prechecked is not null && prechecked.TryGetValue(field, out var early)
                    ? early : CheckExpr(field.Value, scope, ft);
                CheckAssignable(field.Value, given, ft, field.Span);
            }
            else
            {
                _de.Report("LYR-SEM0015", Severity.Error, field.Span, $"'{ts.Name}' has no field '{field.Name}'");
                CheckExpr(field.Value, scope);
            }
        }

        ReportOmittedFields(si.Span, ts.Name, DeclaredFields(ts.Declaration), seen);
        return result;
    }

    /// <summary>
    /// The type arguments of <c>Pair&lt;_, string&gt; { first = 3, second = "x" }</c> (design/v5/spec/03
    /// T8): what is written stands, a placeholder comes from the context when the position names
    /// an instance, else from the field values — each checked with as much of its field type as
    /// is known, its type binding what is open, the way a call's arguments bind its parameters.
    /// A list without a placeholder never infers from the values ('P { v = 1 }' with no context is
    /// an error, not a guess); with one, the writer has asked for exactly that.
    /// </summary>
    /// <param name="prechecked">The values checked on the way, so the caller checks none twice;
    /// <c>null</c> when the context answered.</param>
    private LyrType[] FillPlaceholders(StructInitExpr si, TypeSymbol ts, LyrType? expected,
        SymbolTable scope, out Dictionary<StructInitField, LyrType>? prechecked)
    {
        prechecked = null;
        var written = si.TypeArguments.Select(a => IsPlaceholder(a) ? null : ResolveType(a, scope)).ToArray();
        if (written.Length != ts.Generics.Length)
            return written.Select(a => a ?? LyrType.Error).ToArray(); // the count is reported by the caller

        if (InstanceFromExpected(expected, ts) is { } fromContext)
            return written.Select((a, i) => a ?? fromContext.Arguments[i]).ToArray();

        var map = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < written.Length; i++)
            if (written[i] is { } bound) map[ts.Generics[i]] = bound;

        prechecked = new Dictionary<StructInitField, LyrType>(ReferenceEqualityComparer.Instance);
        var reported = _de.Diagnostics.Count;
        foreach (var field in si.Fields)
        {
            if (ts.Members.LookupLocal(field.Name) is not FieldSymbol fs || prechecked.ContainsKey(field)) continue;
            var declared = FieldType(fs);
            var partial = Substitute(declared, map);
            var given = CheckExpr(field.Value, scope, HasOpenParam(partial, map) ? null : partial);
            prechecked[field] = given;
            UnifyInfer(declared, given, map, field.Value.Span);
        }

        // A value that fixes no type on its own ('[]', 'null') binds the parameter to a type with
        // a hole in it; that is not an answer, and it is reported as none — unless the value was
        // already reported, and a second line would be noise behind the cause.
        var filled = new LyrType[written.Length];
        for (var i = 0; i < filled.Length; i++)
        {
            if (written[i] is { } bound) filled[i] = bound;
            else if (map.TryGetValue(ts.Generics[i], out var inferred) && !ContainsError(inferred)) filled[i] = inferred;
            else
            {
                if (_de.Diagnostics.Count == reported)
                    _de.Report("LYR-SEM0060", Severity.Error, si.TypeArguments[i].Span,
                        $"cannot infer type argument '{ts.Generics[i].Name}' for '{ts.Name}' — no field "
                        + "value determines it; write it");
                filled[i] = LyrType.Error;
            }
        }
        return filled;
    }

    /// <summary>The fields a type declares, in declaration order; empty for anything else.</summary>
    private static IReadOnlyList<FieldDecl> DeclaredFields(Node? declaration) => declaration switch
    {
        ClassDecl c => c.Members.OfType<FieldDecl>().ToArray(),
        StructDecl v => v.Members.OfType<FieldDecl>().ToArray(),
        _ => [],
    };

    /// <summary>
    /// An initializer that leaves a field without a default unset (<c>LYR-SEM0106</c>).
    ///
    /// <para>IT WAS THE LOWERING'S JOB, and the lowering has no business refusing a program: the
    /// same omission answered <c>LYR-IR0001</c> with the note "this compiler version cannot lower
    /// it yet", which is a promise about a future release for a program that will never be valid.
    /// §12.1 reserves that code for VALID Lyric, so the check belongs here — where it also reaches
    /// <c>lyrc check</c>, which never lowers and therefore used to answer "ok".</para>
    ///
    /// <para>EVERY missing field at once, not the first. The lowering threw on the first one it
    /// met, so a three-field omission took three compiles to find; a checker that can see all of
    /// them has no reason to hand them out one at a time.</para>
    /// </summary>
    private void ReportOmittedFields(Span span, string owner, IReadOnlyList<FieldDecl> declared,
        HashSet<string> given)
    {
        // A '?T' field has the default 'null' without saying so (design/v5/spec/02 M14 I1).
        var missing = declared.Where(f => f.Default is null && f.Type is not NullableType && !given.Contains(f.Name))
            .Select(f => f.Name).ToArray();
        if (missing.Length == 0) return;

        _de.Report("LYR-SEM0106", Severity.Error, span, missing.Length == 1
            ? $"initializer for '{owner}' omits field '{missing[0]}', which has no default"
            : $"initializer for '{owner}' omits {missing.Length} fields with no default: "
              + string.Join(", ", missing.Select(m => $"'{m}'")));
    }

    // Path resolution for a struct initializer: runs through modules AND type members, meaning enum
    // variants. Yields the final symbol plus, for an enum variant, the surrounding enum type.
    /// <summary>
    /// <c>Pair&lt;int&gt;</c> as the target of a member access.
    ///
    /// <para>The result is a <see cref="NonValueType"/>, as for an ordinary type name: a type is not
    /// a value, and writing it alone gives <c>LYR-SEM0052</c>. It carries the resolved instance
    /// along.</para>
    /// </summary>
    private LyrType CheckTypePath(TypePathExpr tp, SymbolTable scope, LyrType? expected)
    {
        var (sym, _) = ResolveInitPath(tp.Path, scope);
        if (sym is FunctionSymbol fs) return CheckInstantiatedFunction(tp, fs, scope, expected);
        if (sym is not TypeSymbol ts)
        {
            foreach (var a in tp.TypeArguments) ResolveType(a, scope);
            return Report(tp.Span, "LYR-SEM0011", $"unknown type '{string.Join('.', tp.Path)}'",
                tp.Path is [var single]
                    ? NameSuggestion.Note(single, NamesIn(scope, typesOnly: true))
                    : null);
        }

        _result.BindRef(tp, ts);

        var args = FillDefaults(ts, tp.TypeArguments.Select(a => ResolveType(a, scope)).ToArray());
        if (args.Length != ts.Generics.Length)
        {
            _de.Report("LYR-SEM0026", Severity.Error, tp.Span,
                $"generic type '{ts.Name}' expects {ts.Generics.Length} type argument(s), "
                + $"got {args.Length}");
            return new NonValueType(ts, "type");
        }

        CheckConstraints(ts.Generics, args, tp.Span);
        return new NonValueType(ts, "type", new GenericInstance(ts, args));
    }

    /// <summary>
    /// <c>ident&lt;int&gt;</c> as a value (design/v5/spec/03 T17): the INSTANCE of a generic function
    /// is a function value like any monomorphic function, where the bare generic name is none
    /// (<c>LYR-SEM0052</c>, §8.1). The arguments are written; a placeholder among them is filled
    /// from the function type this position expects (<c>let f: fn(int) -&gt; int = ident&lt;_&gt;</c>,
    /// T8), and one nothing determines is reported, as at a call.
    /// </summary>
    private LyrType CheckInstantiatedFunction(TypePathExpr tp, FunctionSymbol fs, SymbolTable scope,
        LyrType? expected)
    {
        // The import shell stays the bound symbol where there is one, as CheckIdentifier binds it,
        // so unused-import accounting sees this reference too.
        var shell = tp.Path is [var single] ? scope.Lookup(single) : null;
        _result.BindRef(tp, shell is ImportBindingSymbol ? shell : fs);

        // A method reached through its type: static, it is a function like any other; with a
        // receiver, nothing here supplies the object — the sentence its monomorphic twin
        // 'Counter.bump' gets, the value form being 'obj.bump'.
        if (!fs.IsStatic && MemberOwnerOf(tp.Path, scope) is not null)
            return Report(tp.Span, "LYR-SEM0055",
                $"'{fs.Name}' is an instance method and needs a receiver — call it on a value, "
                + $"or declare it 'static fn {fs.Name}(…)'");

        if (tp.TypeArguments.Length != fs.Generics.Length)
        {
            foreach (var a in tp.TypeArguments) if (!IsPlaceholder(a)) ResolveType(a, scope);
            return Report(tp.Span, "LYR-SEM0026", fs.Generics.Length == 0
                ? $"'{fs.Name}' is not generic and takes no type arguments"
                : $"generic function '{fs.Name}' expects {fs.Generics.Length} type argument(s), "
                  + $"got {tp.TypeArguments.Length}");
        }

        var declared = FnTypeOf(fs);
        var map = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < fs.Generics.Length; i++)
            if (!IsPlaceholder(tp.TypeArguments[i]))
                map[fs.Generics[i]] = ResolveType(tp.TypeArguments[i], scope);

        // What is written binds first (the rule of the call); the expected function type fills
        // the rest, and only the rest, because UnifyInfer never overwrites.
        if (expected is FnType wanted) UnifyInfer(declared, wanted, map, tp.Span);
        foreach (var generic in fs.Generics)
            if (!map.ContainsKey(generic))
                return Report(tp.Span, "LYR-SEM0060",
                    $"cannot infer type argument '{generic.Name}' for '{fs.Name}' — this position "
                    + "expects no particular function type; write it");

        CheckInferredConstraints(fs.Generics, map, tp.Span);

        // Which instance is meant is settled here, in declaration order, as for a call.
        _result.SetTypeArguments(tp, fs.Generics.Select(g => map[g]).ToArray());
        return Substitute(declared, map);
    }

    /// <summary>The type whose member the last segment of <paramref name="path"/> is, when the
    /// segment before it names a type; <c>null</c> for a free function, through a module or not.</summary>
    private static TypeSymbol? MemberOwnerOf(string[] path, SymbolTable scope)
    {
        if (path.Length < 2) return null;
        Symbol? cur = scope.Lookup(path[0]);
        if (cur is ImportBindingSymbol ib0) cur = ib0.Target;
        for (var i = 1; i < path.Length - 1 && cur is not null; i++)
        {
            cur = cur switch
            {
                ModuleSymbol mod => mod.Members.LookupLocal(path[i]),
                TypeSymbol t => t.Members.LookupLocal(path[i]),
                _ => null,
            };
            if (cur is ImportBindingSymbol ib) cur = ib.Target;
        }
        return cur as TypeSymbol;
    }

    /// <summary>The <c>_</c> of a type argument list (design/v5/spec/03 T8): a position the
    /// inference fills. Only that — anywhere else <see cref="ResolveType"/> refuses it.</summary>
    private static bool IsPlaceholder(TypeNode node) =>
        node is NamedType { Path: ["_"], TypeArguments.Length: 0 };

    private (Symbol? sym, TypeSymbol? owner) ResolveInitPath(string[] path, SymbolTable scope)
    {
        // A public type of std.core is visible without an import (design/v5/spec/10 U-series), in
        // an initializer as in a type position: 'Exception { text = "…" }'. The scope wins.
        var cur = scope.Lookup(path[0])
            ?? (_comp.FindModule(["std", "core"])?.Members.LookupLocal(path[0]) is TypeSymbol { Visibility: Visibility.Public } core ? core : null);
        if (cur is ImportBindingSymbol ib0) cur = ib0.Target;
        TypeSymbol? owner = null;
        for (var i = 1; i < path.Length && cur is not null; i++)
        {
            owner = cur as TypeSymbol;
            cur = cur switch
            {
                ModuleSymbol mod => mod.Members.LookupLocal(path[i]),
                TypeSymbol t => t.Members.LookupLocal(path[i]),
                _ => null
            };
            if (cur is ImportBindingSymbol ib) cur = ib.Target;
        }
        return (cur, cur is EnumVariantSymbol ? owner : null);
    }

    // The expected type (Shape, ?Shape, Opt<int>) yields the enum definition plus its instance.
    private static (TypeSymbol def, GenericInstance? instance)? EnumFromExpected(LyrType? expected)
    {
        var t = expected is Optional o ? o.Inner : expected;
        return t switch
        {
            NamedRef { Symbol: { Kind: TypeSymbolKind.Enum } d } => (d, null),
            GenericInstance { Definition.Kind: TypeSymbolKind.Enum } gi => (gi.Definition, gi),
            _ => null
        };
    }

    private static GenericInstance? ExpectedInstance(LyrType? expected, TypeSymbol owner) =>
        EnumFromExpected(expected) is { } x && ReferenceEquals(x.def, owner) ? x.instance : null;

    /// <summary>
    /// The instance the context asks for, when it is an instance of <paramref name="owner"/>.
    ///
    /// <para>The counterpart of <see cref="ExpectedInstance"/> for a struct or class initializer.
    /// The definition must MATCH: an expectation of <c>Box&lt;int&gt;</c> says nothing about a
    /// <c>Pair { … }</c>, and taking its arguments anyway would silently build the wrong instance.
    /// </para>
    /// </summary>
    private static GenericInstance? InstanceFromExpected(LyrType? expected, TypeSymbol owner)
    {
        var t = expected is Optional o ? o.Inner : expected;
        return t is GenericInstance gi && ReferenceEquals(gi.Definition, owner) ? gi : null;
    }

    private LyrType CheckVariantInit(StructInitExpr si, EnumVariantSymbol ev, TypeSymbol enumTs,
        GenericInstance? instance, SymbolTable scope)
    {
        _result.BindRef(si, ev);
        var v = (EnumVariant)ev.Declaration!;

        // WRITTEN arguments beat the context: 'Ev<int>.Hit { … }' says itself which instance is
        // meant, and that holds where there is no context at all ('let e = …').
        if (si.TypeArguments.Length > 0 && enumTs.Generics.Length > 0)
        {
            var written = si.TypeArguments.Select(a => ResolveType(a, scope)).ToArray();
            if (written.Length != enumTs.Generics.Length)
                _de.Report("LYR-SEM0026", Severity.Error, si.Span,
                    $"generic enum '{enumTs.Name}' expects {enumTs.Generics.Length} type "
                    + $"argument(s), got {written.Length}");
            else
            {
                CheckConstraints(enumTs.Generics, written, si.Span);
                instance = new GenericInstance(enumTs, written);
            }
        }

        var subst = instance is not null ? SubstMap(instance) : EmptySubst;

        LyrType result;
        if (instance is not null) result = instance;
        else if (enumTs.Generics.Length > 0 || si.TypeArguments.Length > 0)
        {
            // A generic enum without a context instance, or with type arguments on the variant.
            _de.Report("LYR-SEM0026", Severity.Error, si.Span,
                $"generic enum '{enumTs.Name}' expects {enumTs.Generics.Length} type argument(s) "
                + "— write them ('Enum<int>.Variant { … }') or use it where the type is known");
            result = LyrType.Error;
        }
        else result = new NamedRef(enumTs);

        if (v.StructFields is not { } decls)
        {
            _de.Report("LYR-SEM0031", Severity.Error, si.Span, v.TupleFields is not null
                ? $"variant '{ev.Name}' has a tuple payload — construct it as '{ev.Name}(…)'"
                : $"variant '{ev.Name}' has no payload");
            foreach (var f in si.Fields) CheckExpr(f.Value, scope);
            return result;
        }

        // One value per field, as for a struct or class initializer.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in si.Fields)
        {
            if (!seen.Add(field.Name))
                _de.Report("LYR-SEM0070", Severity.Error, field.Span,
                    $"duplicate field '{field.Name}' in initializer for variant '{ev.Name}'");
            if (Array.Find(decls, d => d.Name == field.Name) is { } fd)
            {
                var ft = Substitute(ResolveType(fd.Type, enumTs.Members), subst);
                CheckAssignable(field.Value, CheckExpr(field.Value, scope, ft), ft, field.Span);
            }
            else
            {
                _de.Report("LYR-SEM0015", Severity.Error, field.Span, $"variant '{ev.Name}' has no field '{field.Name}'");
                CheckExpr(field.Value, scope);
            }
        }

        ReportOmittedFields(si.Span, ev.Name, decls, seen);
        return result;
    }

    /// <summary>
    /// An <c>if</c> as an expression, with flow narrowing in both branches, like the statement form.
    /// </summary>
    /// <remarks>
    /// <para>Without the narrowing, <c>if (a == null) 0 else a</c> would be a type error
    /// (<c>LYR-SEM0001</c>) although the statement form <c>if (a == null) { return 0; } return a;</c>
    /// works next to it, for the same proof about the same value.</para>
    /// </remarks>
    private LyrType CheckIfExpr(IfExpr iff, SymbolTable scope, LyrType? expected = null)
    {
        CheckCondition(iff.Condition, scope);

        var (thenFacts, elseFacts) = NarrowingFacts(iff.Condition);
        var snapshot = new Dictionary<Symbol, LyrType>(_narrowed, ReferenceEqualityComparer.Instance);

        Apply(thenFacts);
        var thenT = CheckExpr(iff.Then, scope, expected);

        // Back to the state BEFORE the then branch: what held there is precisely what does not hold
        // in the else branch.
        _narrowed = new Dictionary<Symbol, LyrType>(snapshot, ReferenceEqualityComparer.Instance);
        Apply(elseFacts);
        var elseT = CheckExpr(iff.Else, scope, expected);

        _narrowed = snapshot;

        // With a context the arms check AGAINST it and the expression HAS it (§3.1/§6.9 since
        // 2.1): an unsuffixed literal arm adapts, and each arm meets the context as an
        // ordinary assignment. Unification among the arms is the contextless rule.
        if (expected is not null && !expected.IsError)
        {
            CheckAssignable(iff.Then, thenT, expected, iff.Then.Span);
            CheckAssignable(iff.Else, elseT, expected, iff.Else.Span);
            return expected;
        }

        return Unify(iff.Then, thenT, iff.Else, elseT, iff.Span);
    }

    /// <summary>
    /// A <c>null</c> branch makes the other one optional: <c>if (c) 5 else null</c> is <c>?int</c>.
    /// Yields <c>null</c> when neither side is the <c>null</c> literal.
    ///
    /// <para>The case does not go through <c>IsAssignable</c> in EITHER direction: <c>null</c> is no
    /// <c>int</c>, and <c>int</c> is no <c>null</c>. The widening rule <c>T</c> to <c>?T</c> applies
    /// only where a target type already stands — an assignment, a parameter, a return. In an arm
    /// unification there is none; the result type arises here.</para>
    ///
    /// <para>One function rather than two, because the <c>if</c> expression and the <c>match</c>
    /// expression have separate unifications.</para>
    /// </summary>
    private static LyrType? WidenAgainstNull(LyrType a, LyrType b)
    {
        if (a is NullType && b is not NullType) return b is Optional ? b : new Optional(b);
        if (b is NullType && a is not NullType) return a is Optional ? a : new Optional(a);
        return null;   // the null/null case too: there the `Equal` above already decided
    }

    private LyrType Unify(Expr ae, LyrType a, Expr be, LyrType b, Span span)
    {
        if (LyrType.Equal(a, b)) return a;
        if (a.IsError) return b;
        if (b.IsError) return a;
        // A diverging branch contributes nothing (§6.9): 'if (c) v else throw e' is the type of 'v'.
        if (a is NeverType) return b;
        if (b is NeverType) return a;

        // A `null` branch makes the other one optional: `if (c) 5 else null` is `?int`.
        //
        // The case does not go through `IsAssignable` in either direction: `null` is not `int`, and
        // `int` is not `null`. The widening rule `T` -> `?T` applies only where a target type already
        // stands — an assignment, a parameter, a return. In an arm unification there is none.
        //
        // `?T` against `T` needs no line of its own: `IsAssignable` carries the widening there,
        // because one of the two sides is the finished target type.
        if (WidenAgainstNull(a, b) is { } widened) return widened;

        // WITHOUT the literal rule. An arm is not an adaptation context (§3.1), so a literal here
        // does not take the other arm's type — and letting `IsAssignable` say yes on that ground
        // alone would only CHECK the fit while nothing records it: the literal keeps its default
        // type, and the lowering stores a `const i64` into the slot of the type this returns.
        // `UnifyArms` asks the same question for `match` and never had the hole.
        if (IsAssignable(be, b, a, coercionSite: false)) return a;
        if (IsAssignable(ae, a, b, coercionSite: false)) return b;
        _de.Report("LYR-SEM0016", Severity.Error, span, $"incompatible branch types: '{TypeFacts.Display(a)}' vs '{TypeFacts.Display(b)}'");
        return a;
    }

    private List<LyrType> CheckMatch(Node match, Expr scrutinee, MatchArm[] arms, SymbolTable scope, bool asExpression,
        LyrType? expected = null)
    {
        var st = CheckExpr(scrutinee, scope);
        var bodies = new List<LyrType>();
        var patternsClean = true;
        foreach (var arm in arms)
        {
            var armScope = new SymbolTable(scope);
            var before = _de.Diagnostics.Count;
            BindPattern(arm.Pattern, st, armScope);
            if (_de.Diagnostics.Count > before) patternsClean = false;
            if (arm.Guard is not null) CheckCondition(arm.Guard, armScope);
            switch (arm.Body)
            {
                case Block b:
                {
                    var savedTail = _tailExpected;
                    _tailExpected = asExpression ? expected : null;
                    CheckBlock(b, armScope);
                    _tailExpected = savedTail;

                    if (b.Tail is { } tail)
                    {
                        // A block arm WITH a tail delivers the tail's value (§6.9): it takes part
                        // in the unification exactly as an expression arm does. In a match
                        // STATEMENT the value goes nowhere, so the tail is held to the expression
                        // statement rule — a bare value there is the same mistake as 'x;'.
                        var tt = _result.TypeOf(tail.Expr);
                        if (!asExpression)
                        {
                            if (Unmarked(tail.Expr) is not (CallExpr or AssignExpr or ResumeExpr or ThrowExpr or ErrorExpr))
                                _de.Report("LYR-SEM0022", Severity.Error, tail.Span,
                                    "expression statement has no effect (only calls, assignments and resume are allowed)");
                            break;
                        }
                        if (expected is not null && !expected.IsError)
                            CheckAssignable(tail.Expr, tt, expected, tail.Span);
                        bodies.Add(tt);
                        break;
                    }

                    // Without a tail a block has no value: in a match EXPRESSION it has to leave
                    // the function on every path, and it contributes nothing to the unification.
                    if (asExpression && !Flow.AlwaysReturns(b, _result))
                        _de.Report("LYR-SEM0033", Severity.Error, arm.Span,
                            "a block arm of a match expression must return or throw on every path, "
                            + "or end in a tail expression without ';' that is the arm's value");
                    break;
                }
                case Expr e:
                    var bt = CheckExpr(e, armScope, expected);
                    // With a context the arm checks AGAINST it (§3.1/§6.9 since 2.1): a
                    // literal arm adapts, a misfit is the assignment error at the arm.
                    if (expected is not null && !expected.IsError)
                        CheckAssignable(e, bt, expected, e.Span);
                    bodies.Add(bt);
                    break;
            }
        }
        // Faulty patterns would only produce follow-up noise, so exhaustiveness is skipped then.
        if (patternsClean && !st.IsError)
            CheckExhaustiveness(match, st, arms, CatchSetOf(scrutinee));
        return bodies;
    }

    // --- exhaustiveness: enum variants, bool and ?T are enumerated, while open types (int, string,
    // --- …) need a '_' or binding arm. Guards do not count. ---

    /// <summary>
    /// An arm no value reaches is a warning (08 Y6; Rust's <c>unreachable_patterns</c>): every
    /// arm after an unguarded arm that matches everything, and an unguarded arm that repeats a
    /// unit variant, a literal or <c>null</c> an unguarded arm above already tests. A warning
    /// and not an error, as the specification says; a typo in a bare name gets this beside the
    /// unused binding it leaves.
    /// </summary>
    private void CheckReachability(LyrType scrutinee, MatchArm[] arms)
    {
        MatchArm? everything = null;
        var tested = new Dictionary<string, MatchArm>(StringComparer.Ordinal);
        foreach (var arm in arms)
        {
            if (everything is { } above)
            {
                _de.Report("LYR-SEM0112", Severity.Warning, arm.Pattern.Span,
                    "unreachable arm: the arm above matches every value", new DiagnosticNote(above.Pattern.Span, "this one"));
                continue;
            }

            if (ExactKey(arm.Pattern) is { } key)
            {
                if (tested.TryGetValue(key, out var earlier))
                {
                    _de.Report("LYR-SEM0112", Severity.Warning, arm.Pattern.Span,
                        $"unreachable arm: '{key}' is matched by an arm above", new DiagnosticNote(earlier.Pattern.Span, "this one"));
                    continue;
                }
                if (arm.Guard is null) tested[key] = arm;
            }

            if (arm.Guard is null && IsIrrefutable(arm.Pattern, scrutinee)) everything = arm;
        }
    }

    /// <summary>A pattern that tests one value and nothing else, as its text: a unit variant,
    /// a literal, <c>null</c>. Anything with a binding or a payload is not one.</summary>
    private static string? ExactKey(Pattern pattern) => pattern switch
    {
        VariantPattern { TupleElements: null, StructFields: null } v => "." + v.Path[^1],
        LiteralPattern { Literal: NullLiteralExpr } => "null",
        LiteralPattern { Literal: IntLiteralExpr n } => n.Value.ToString(),
        LiteralPattern { Literal: UnaryExpr { Operator: UnaryOp.Neg, Operand: IntLiteralExpr n } } => "-" + n.Value,
        LiteralPattern { Literal: StringLiteralExpr s } => "\"" + s.Value + "\"",
        LiteralPattern { Literal: CharLiteralExpr c } => "'" + char.ConvertFromUtf32(c.CodePoint) + "'",
        LiteralPattern { Literal: BoolLiteralExpr b } => b.Value ? "true" : "false",
        _ => null,
    };

    private void CheckExhaustiveness(Node match, LyrType scrutinee, MatchArm[] arms, LyrType[]? set = null)
    {
        CheckReachability(scrutinee, arms);
        var pats = new List<Pattern>();
        foreach (var arm in arms)
            if (arm.Guard is null) Flatten(arm.Pattern, pats);
        var missing = set is not null ? MissingFromSet(set, pats, scrutinee) : MissingCases(scrutinee, pats);
        if (missing.Count == 0)
        {
            _result.MarkMatchExhaustive(match);
            return;
        }
        // The witness is the point: a name for what is missing beats a count of it. "no arm
        // matches 'Some(false)'" says which value falls through and what to write; "missing
        // case(s): 'Some'" said only that something about 'Some' was wrong, and for a nested
        // payload it was not even that.
        var what = missing is ["_"]
            ? "add a '_' or binding arm to cover the remaining values"
            : missing is [var only]
                ? $"no arm matches '{only}'"
                : $"no arm matches {string.Join(", ", missing.Select(m => $"'{m}'"))}";
        _de.Report("LYR-SEM0050", Severity.Error, match.Span,
            $"match on '{TypeFacts.Display(scrutinee)}' is not exhaustive — {what}");
    }

    /// <summary>The set a catch binding carries (05 E2 K7), when the scrutinee is one: the binding
    /// of a set clause or of a clause without a type — a 'let', so it holds what the clause took.</summary>
    private LyrType[]? CatchSetOf(Expr scrutinee) =>
        scrutinee is IdentifierExpr id && _result.RefOf(id) is LocalSymbol { Declaration: CatchClause clause }
            ? _result.CatchSet(clause) : null;

    /// <summary>What the arms leave of a catch binding's set (K7): every type of the set no
    /// unguarded type pattern covers — the type itself, or an interface it conforms to — as the
    /// pattern to add. A default covers all of it.</summary>
    private List<string> MissingFromSet(LyrType[] set, List<Pattern> pats, LyrType scrutinee)
    {
        if (pats.Any(p => IsIrrefutable(p, scrutinee, nested: false))) return [];
        var tested = pats.OfType<TypePattern>().Select(p => _result.TypeTested(p)).OfType<LyrType>().ToList();
        return set.Where(t => !tested.Any(u => ThrownCoveredBy(t, u, _currentModule)))
            .Select(t => $"_: {TypeFacts.Display(t)}").ToList();
    }

    private static void Flatten(Pattern p, List<Pattern> into)
    {
        if (p is OrPattern o) foreach (var a in o.Alternatives) Flatten(a, into);
        else into.Add(p);
    }

    /// <summary>
    /// What the arms leave uncovered, as WITNESS PATTERNS: values written the way a pattern is
    /// written, so the answer names what to add rather than what is wrong. Empty means exhaustive.
    ///
    /// <para>Exact where it answers at all. A variant with ONE payload field recurses into that
    /// field, so <c>Some(true)</c> and <c>None</c> over an <c>Opt&lt;bool&gt;</c> report
    /// <c>Some(false)</c>. A variant with several fields is reported as a whole when nothing
    /// covers it and considered covered otherwise — a partial answer per column needs the
    /// pattern matrix, and a witness that turns out to BE covered is worse than none.</para>
    ///
    /// <para><paramref name="nested"/> travels with the meaning of a bare name: at the top of a
    /// match a name over a <c>?T</c> leaves <c>null</c> uncovered, inside a payload it binds the
    /// whole optional and covers it (§7.6).</para>
    /// </summary>
    private List<string> MissingCases(LyrType type, List<Pattern> pats, bool nested = false)
    {
        if (pats.Any(p => IsIrrefutable(p, type, nested))) return [];
        switch (type)
        {
            case Optional o:
            {
                var missing = new List<string>();
                if (!pats.Any(IsNullPattern)) missing.Add("null");
                missing.AddRange(MissingCases(o.Inner, pats.Where(p => !IsNullPattern(p)).ToList(), nested));
                return missing;
            }
            case PrimitiveType { Kind: PrimitiveKind.Bool }:
            {
                var missing = new List<string>();
                if (!pats.Any(p => p is LiteralPattern { Literal: BoolLiteralExpr { Value: true } })) missing.Add("true");
                if (!pats.Any(p => p is LiteralPattern { Literal: BoolLiteralExpr { Value: false } })) missing.Add("false");
                return missing;
            }
            case ArrayOf array:
                return MissingArrayCases(array, pats);
            case SliceOf view:
                return MissingArrayCases(new ArrayOf(view.Element), pats);
            case InlineArrayOf inline:
                return MissingArrayCases(new ArrayOf(inline.Element), pats, inline.Length);
            case TupleOf tuple:
                return MissingTupleCases(tuple, pats);
            default:
                if (EnumDefOf(type) is { Declaration: EnumDecl ed } enumTs)
                    return MissingVariants(type, ed, enumTs, pats);
                // A sealed interface's conformers are a closed set (04 D8): type patterns
                // cover it; an open interface needs the default.
                if (TypeFacts.SymbolOf(type) is { Kind: TypeSymbolKind.Interface } sealedIface && IsSealed(sealedIface))
                    return MissingConformers(sealedIface, pats);
                return ["_"]; // an open type is coverable only by a default
        }
    }

    /// <summary>
    /// A TUPLE scrutinee: <c>match ((e, m))</c> over <c>(E, int)</c>.
    ///
    /// <para>There was no case for this at all, so a tuple fell to the default and demanded a
    /// <c>_</c> arm however completely the arms covered it. The lowering has known the form since
    /// 4.4; only the coverage did not.</para>
    ///
    /// <para>DECIDED ONLY WHERE THE DISTINCTIONS LIVE IN ONE COLUMN, and that restriction is the
    /// point rather than an oversight. With one refutable column the other columns are wildcards
    /// in every row, so coverage of the tuple IS coverage of that column — the reduction is exact.
    /// With two, it is not: <c>(A, true)</c> beside <c>(B, false)</c> covers each column and
    /// leaves <c>(A, false)</c> open, so checking columns independently would ACCEPT a match with
    /// a hole. Deciding that properly is the pattern matrix of Maranget's algorithm, and half of
    /// one would be worse than none — a wrong "exhaustive" removes the last arm's test in the
    /// lowering, which turns a missing case into whatever the previous arm did.</para>
    ///
    /// <para>So two or more testing columns keep today's answer: a <c>_</c> or binding arm. That
    /// refuses a match a person may see as complete, which is a cost — but it is the cost that was
    /// already being paid for every tuple, and nothing that compiles today stops compiling.</para>
    /// </summary>
    private List<string> MissingTupleCases(TupleOf tuple, List<Pattern> pats)
    {
        var rows = pats.OfType<TuplePattern>()
            .Where(t => t.Elements.Length == tuple.Elements.Length)
            .ToList();
        if (rows.Count == 0) return ["_"];

        var testing = new List<int>();
        for (var i = 0; i < tuple.Elements.Length; i++)
        {
            var element = tuple.Elements[i];
            if (rows.Any(r => !IsIrrefutable(r.Elements[i], element, nested: true)))
                testing.Add(i);
        }

        // Every row is wildcards throughout: the first one already matches everything.
        if (testing.Count == 0) return [];
        if (testing.Count > 1) return ["_"];

        var column = testing[0];
        var missing = MissingCases(tuple.Elements[column],
            rows.Select(r => r.Elements[column]).ToList(), nested: true);

        return missing.Select(m => TupleWitness(m, column, tuple.Elements.Length)).ToList();
    }

    /// <summary>A hole in one column, written as the tuple it stands for: <c>(_, E.B, _)</c>. The
    /// witness is what makes the diagnostic worth reading, so it keeps its shape here too.</summary>
    private static string TupleWitness(string missing, int column, int arity)
    {
        var parts = new string[arity];
        for (var i = 0; i < arity; i++) parts[i] = i == column ? missing : "_";
        return "(" + string.Join(", ", parts) + ")";
    }

    /// <summary>The variants no arm covers, each as a witness. A variant with one payload field
    /// is followed into that field, which is where a nested hole usually is.</summary>
    private List<string> MissingVariants(LyrType type, EnumDecl ed, TypeSymbol enumTs, List<Pattern> pats)
    {
        var subst = type is GenericInstance gi ? SubstMap(gi) : EmptySubst;
        var missing = new List<string>();

        foreach (var variant in ed.Variants)
        {
            var rows = pats.Where(p => NamesVariant(p, variant.Name, enumTs)).ToList();
            if (rows.Count == 0) { missing.Add(WitnessOf(variant)); continue; }
            if (rows.Any(p => CoveredVariant(p, type, enumTs) == variant.Name)) continue;

            // One payload field: the hole is inside it, and the recursion names it.
            if (variant.TupleFields is { Length: 1 } fields)
            {
                var column = new List<Pattern>();
                foreach (var row in rows)
                    if (row is VariantPattern { TupleElements: [var only] }) Flatten(only, column);
                var fieldType = Substitute(ResolveType(fields[0], enumTs.Members), subst);
                foreach (var witness in MissingCases(fieldType, column, nested: true))
                    missing.Add($".{variant.Name}({witness})");
                continue;
            }

            missing.Add(WitnessOf(variant));
        }
        return missing;
    }

    /// <summary>Does this pattern name that variant at all — covering it or only partly?</summary>
    private bool NamesVariant(Pattern p, string variant, TypeSymbol enumTs) => p switch
    {
        VariantPattern v => v.Path[^1] == variant,
        _ => false,
    };

    /// <summary>A variant written as a pattern, with its payload left open — in the dotted form,
    /// since a bare name would be a binding (08 Y6).</summary>
    private static string WitnessOf(EnumVariant variant) => variant switch
    {
        { TupleFields: { } tuple } => $".{variant.Name}({string.Join(", ", tuple.Select(_ => "_"))})",
        { StructFields: not null } => "." + variant.Name + " { … }",
        _ => "." + variant.Name,
    };

    /// <summary>
    /// The lengths an array match leaves open. An arm whose fixed positions all bind without
    /// testing covers its length exactly, or every length from there up when it carries a rest;
    /// the answer is the smallest length nothing covers, written as a pattern.
    /// </summary>
    private List<string> MissingArrayCases(ArrayOf array, List<Pattern> pats, int? fixedLength = null)
    {
        var exact = new HashSet<int>();
        var open = int.MaxValue; // the smallest length from which on everything is covered

        foreach (var p in pats)
        {
            if (p is not ArrayPattern ap) continue;
            var positions = ap.Elements.Where(e => e is not RestPattern).ToArray();

            // A test inside a position says nothing about the LENGTH class: '[0, y]' covers
            // some arrays of length two, not all of them.
            if (!positions.All(e => IsIrrefutable(e, array.Element, nested: true))) continue;

            if (ap.Elements.Any(e => e is RestPattern)) open = Math.Min(open, positions.Length);
            else exact.Add(positions.Length);
        }

        // An inline array has one length (A4): only that class needs covering.
        if (fixedLength is { } only)
            return exact.Contains(only) || only >= open ? []
                : [only == 0 ? "[]" : "[" + string.Join(", ", Enumerable.Repeat("_", only)) + "]"];

        for (var n = 0; n < open && n <= 64; n++)
            if (!exact.Contains(n))
                return [n == 0 ? "[]" : "[" + string.Join(", ", Enumerable.Repeat("_", n)) + "]"];

        return [];
    }

    // Which variant does this pattern cover completely, with an irrefutable payload?
    private string? CoveredVariant(Pattern p, LyrType scrutinee, TypeSymbol enumTs)
    {
        switch (p)
        {
            case VariantPattern v when VariantOf(enumTs, v.Path[^1]) is { } ev
                && VariantCovered(v, ev, scrutinee, enumTs):
                return ev.Name;
            default:
                return null;
        }
    }

    private bool VariantCovered(VariantPattern v, EnumVariantSymbol ev, LyrType scrutinee, TypeSymbol enumTs)
    {
        var variant = (EnumVariant)ev.Declaration!;
        var subst = scrutinee is GenericInstance gi ? SubstMap(gi) : EmptySubst;
        if (v.TupleElements is { } elems)
        {
            if (variant.TupleFields is not { } fields || fields.Length != elems.Length) return false;
            for (var i = 0; i < elems.Length; i++)
                if (!IsIrrefutable(elems[i], Substitute(ResolveType(fields[i], enumTs.Members), subst), nested: true))
                    return false;
            return true;
        }
        if (v.StructFields is { } fps)
        {
            if (variant.StructFields is null) return false;
            foreach (var fp in fps)
            {
                if (fp.Pattern is null) continue; // the short form only binds, so it is irrefutable
                var fd = Array.Find(variant.StructFields, f => f.Name == fp.Name);
                if (fd is null || !IsIrrefutable(fp.Pattern, Substitute(ResolveType(fd.Type, enumTs.Members), subst), nested: true))
                    return false;
            }
            return true;
        }
        return variant.TupleFields is null && variant.StructFields is null; // a qualified unit variant
    }

    // Does this pattern match EVERY value of the type?
    private bool IsIrrefutable(Pattern p, LyrType type, bool nested = false)
    {
        switch (p)
        {
            case WildcardPattern:
                return true;
            case OrPattern o:
                return o.Alternatives.Any(a => IsIrrefutable(a, type, nested));
            case BindingPattern b:
                // At the top a name binds the present half of a '?T' and leaves null uncovered;
                // nested it binds the whole optional (BindPattern) and covers it.
                if (type is Optional opt) return nested && BindsWholeOptional(b, opt);
                return true; // a bare name binds, always (08 Y6)
            // An array pattern only covers everything when it tests no length: '[..]' and
            // '[..rest]' match every array, anything else asks how many elements there are.
            case ArrayPattern ap:
                return ap.Elements is [RestPattern];
            case TuplePattern t:
                if (type is not TupleOf tt || tt.Elements.Length != t.Elements.Length) return false;
                for (var i = 0; i < t.Elements.Length; i++)
                    if (!IsIrrefutable(t.Elements[i], tt.Elements[i], nested: true)) return false;
                return true;
            case VariantPattern v:
                if (EnumDefOf(type) is { Declaration: EnumDecl ed } enumTs)
                    return ed.Variants.Length == 1 && VariantOf(enumTs, v.Path[^1]) is { } ev
                        && VariantCovered(v, ev, type, enumTs);
                return StructDestructureIrrefutable(v, type);
            default:
                return false; // a literal or a range
        }
    }

    private bool StructDestructureIrrefutable(VariantPattern v, LyrType type)
    {
        var (def, subst) = DefinitionOf(type);
        if (def is null || v.StructFields is null || v.Path[^1] != def.Name) return false;
        foreach (var fp in v.StructFields)
        {
            if (fp.Pattern is null) continue;
            if (def.Members.LookupLocal(fp.Name) is not FieldSymbol fs) return false;
            if (!IsIrrefutable(fp.Pattern, Substitute(FieldType(fs), subst), nested: true)) return false;
        }
        return true;
    }

    private LyrType UnifyArms(List<LyrType> bodies, Span span, string what = "match arms")
    {
        if (bodies.Count == 0) return LyrType.Error;
        var result = bodies[0];
        for (var i = 1; i < bodies.Count; i++)
        {
            if (bodies[i].IsError) continue;
            if (result.IsError) { result = bodies[i]; continue; }
            // A diverging arm ('throw', 'panic') contributes nothing (§6.9): it never delivers a
            // value the others would have to agree with. All arms diverging is 'never'.
            if (bodies[i] is NeverType) continue;
            if (result is NeverType) { result = bodies[i]; continue; }
            if (LyrType.Equal(result, bodies[i])) continue;
            if (WidenAgainstNull(result, bodies[i]) is { } widened) { result = widened; continue; }
            _de.Report("LYR-SEM0016", Severity.Error, span, $"{what} have incompatible types: '{TypeFacts.Display(result)}' vs '{TypeFacts.Display(bodies[i])}'");
            break;
        }
        return result;
    }

    // Pattern bindings: every form types its bindings against the scrutinee type. Non-null patterns
    // on a ?T match against T ('null => …, u => u.name'), and errors poison their sub-bindings, so arm
    // bodies do not cascade.
    /// <summary>
    /// <c>let (a, b) = pair;</c>
    ///
    /// <para>The initializer has to yield a tuple of matching arity; the names are bound by the same
    /// routine a <c>match</c> arm uses. Two ways to bind names from a pattern would be two
    /// opportunities for different answers.</para>
    /// </summary>
    private void CheckDestructuring(DestructuringStmt stmt, SymbolTable scope)
    {
        var declared = stmt.Type is null ? null : ResolveType(stmt.Type, scope);
        var actual = CheckExpr(stmt.Initializer, scope, declared);

        if (declared is not null) CheckAssignable(stmt.Initializer, actual, declared, stmt.Span);
        var source = declared ?? actual;

        if (source.IsError) return; // already reported

        // A FAILED DESTRUCTURING STILL BINDS ITS NAMES, at the error type. Without it the names
        // do not exist at all, and every use of them downstream is a second diagnostic about the
        // first one's consequence: 'let (a, b) = 5;' answered SEM0058 and then "unknown
        // identifier 'a'" and "unknown identifier 'b'", of which only the first was the mistake.
        // BindPattern poisons a whole pattern from an error scrutinee, nesting included.
        if (source is not TupleOf tuple)
        {
            Report(stmt.Initializer.Span, "LYR-SEM0058",
                $"cannot destructure '{TypeFacts.Display(source)}' — only tuples can be taken apart");
            BindPattern(stmt.Pattern, LyrType.Error, scope, stmt.IsMutable);
            return;
        }

        if (tuple.Elements.Length != stmt.Pattern.Elements.Length)
        {
            Report(stmt.Pattern.Span, "LYR-SEM0058",
                $"the pattern binds {stmt.Pattern.Elements.Length} name(s), but "
                + $"'{TypeFacts.Display(tuple)}' has {tuple.Elements.Length} element(s)");
            BindPattern(stmt.Pattern, LyrType.Error, scope, stmt.IsMutable);
            return;
        }

        BindPattern(stmt.Pattern, tuple, scope, stmt.IsMutable);
    }

    private void BindPattern(Pattern pattern, LyrType scrutinee, SymbolTable scope,
        bool mutable = false, bool nested = false)
    {
        if (scrutinee.IsError) { BindPoison(pattern, scope); return; }

        // A non-null pattern over a '?T' matches against 'T' (§7.6) — with one exception: a NAME
        // in nested position ('Some(v)' over 'Opt<?int>', '(a, b)' over '(?int, int)') binds the
        // '?T' as it is and covers it. At the top of a match the null arm is mandatory anyway, so
        // the name after it means "the present rest" and narrows; inside a payload there is no
        // such arm, and a name that silently refused null would leave a hole no arm could name.
        // Rust's 'Some(v)' binds whatever is inside for the same reason.
        if (scrutinee is Optional opt && pattern is not (WildcardPattern or OrPattern) && !IsNullPattern(pattern)
            && !(nested && BindsWholeOptional(pattern, opt)))
        {
            BindPattern(pattern, opt.Inner, scope, mutable, nested);
            return;
        }

        switch (pattern)
        {
            case LiteralPattern lit:
                CheckLiteralPattern(lit, scrutinee, scope);
                return;

            case TypePattern tp:
            {
                // 'c: Circle' (03 T11): the scrutinee is an interface value, the type one it
                // may hold; the name is bound as that type.
                var tested = ResolveType(tp.Type, scope);
                _result.RecordTypeTested(tp, tested);
                if (!tested.IsError && !scrutinee.IsError)
                {
                    if (TypeFacts.SymbolOf(scrutinee) is not { Kind: TypeSymbolKind.Interface })
                        _de.Report("LYR-SEM0131", Severity.Error, tp.Span,
                            $"a type pattern asks an interface value what it holds; a '{TypeFacts.Display(scrutinee)}' is known already");
                    else CheckTestTarget(tested, tp.Type.Span);
                }
                if (tp.Name is { } bound)
                {
                    var tl = new LocalSymbol(bound, tested, mutable, tp);
                    DeclareBinding(scope, tl, tp.NameSpan);
                    _result.BindRef(tp, tl);
                }
                return;
            }

            case BindingPattern b:
                // A bare name is a binding, always (08 Y6). One that spells a variant of the
                // scrutinee's enum would match everything under the variant's name: refused,
                // with the spelling that tests the variant. Lyric 4 read it as the variant.
                if (EnumDefOf(scrutinee) is { } be && VariantOf(be, b.Name) is not null)
                    _de.Report("LYR-SEM0111", Severity.Error, b.Span,
                        $"'{b.Name}' here is a binding that matches every value, and '{be.Name}' has a variant "
                        + $"of that name — a variant is written '.{b.Name}' or '{be.Name}.{b.Name}'");
                var local = new LocalSymbol(b.Name, scrutinee, mutable, b);
                DeclareBinding(scope, local, b.Span);
                _result.BindRef(b, local); // for definite-assignment analysis
                return;

            case TuplePattern t:
                if (scrutinee is TupleOf tup && tup.Elements.Length == t.Elements.Length)
                {
                    for (var i = 0; i < t.Elements.Length; i++)
                        BindPattern(t.Elements[i], tup.Elements[i], scope, mutable, nested: true);
                    return;
                }
                Report(t.Span, "LYR-SEM0029", scrutinee is TupleOf other
                    ? $"tuple pattern has {t.Elements.Length} element(s), but '{TypeFacts.Display(scrutinee)}' has {other.Elements.Length}"
                    : $"tuple pattern cannot match '{TypeFacts.Display(scrutinee)}'");
                BindPoison(t, scope);
                return;

            case ArrayPattern ap:
                BindArrayPattern(ap, scrutinee, scope, mutable);
                return;

            case RestPattern:
                // Only inside an array pattern, where BindArrayPattern takes it; anywhere else
                // the parser never produces one.
                return;

            case VariantPattern v:
                BindVariantPattern(v, scrutinee, scope, mutable);
                return;

            case RangePattern r:
                CheckRangePattern(r, scrutinee, scope);
                return;

            case OrPattern o:
                BindOrPattern(o, scrutinee, scope, nested);
                return;
            // wildcard or error: no binding
        }
    }

    /// <summary>Is this a plain name over a '?T' in nested position — one that binds the whole
    /// optional rather than its present half?</summary>
    private static bool BindsWholeOptional(Pattern pattern, Optional opt) => pattern is BindingPattern;

    /// <summary><c>[a, b]</c> over a <c>T[]</c>: every fixed position binds a <c>T</c>, a named
    /// rest binds a <c>T[]</c> of its own. The length is a TEST, so the pattern is refutable
    /// unless it is nothing but a rest.</summary>
    private void BindArrayPattern(ArrayPattern ap, LyrType scrutinee, SymbolTable scope, bool mutable)
    {
        if (scrutinee is not (ArrayOf or SliceOf or InlineArrayOf))
        {
            Report(ap.Span, "LYR-SEM0029",
                $"array pattern cannot match '{TypeFacts.Display(scrutinee)}' — only an array has elements at positions");
            BindPoison(ap, scope);
            return;
        }
        var array = scrutinee switch
        {
            ArrayOf whole => whole,
            SliceOf view => new ArrayOf(view.Element),
            _ => new ArrayOf(((InlineArrayOf)scrutinee).Element),
        };

        foreach (var element in ap.Elements)
        {
            // '..rest' binds a view of the elements it covers (03 T13 A2), never a copy.
            if (element is RestPattern { Name: { } restName } rest)
            {
                var local = new LocalSymbol(restName, new SliceOf(array.Element), mutable, rest);
                DeclareBinding(scope, local, rest.Span);
                _result.BindRef(rest, local);
                continue;
            }
            if (element is RestPattern) continue;
            BindPattern(element, array.Element, scope, mutable, nested: true);
        }
    }

    private void CheckLiteralPattern(LiteralPattern lit, LyrType scrutinee, SymbolTable scope)
    {
        if (lit.Literal is NullLiteralExpr)
        {
            if (scrutinee is not Optional)
                _de.Report("LYR-SEM0029", Severity.Error, lit.Span,
                    $"'null' pattern cannot match non-nullable '{TypeFacts.Display(scrutinee)}'");
            return;
        }
        var lt = CheckExpr(lit.Literal, scope);
        if (!lt.IsError && !IsAssignable(lit.Literal, lt, scrutinee))
        {
            _de.Report("LYR-SEM0029", Severity.Error, lit.Span,
                $"literal pattern of type '{TypeFacts.Display(lt)}' cannot match '{TypeFacts.Display(scrutinee)}'");
            return;
        }
        // The literal IS of the scrutinee's type (§3.1): '1' over an 'int8' is an int8, and the
        // side table has to say so, or the lowering compares an i8 against an i64 constant.
        AdaptLiteralType(lit.Literal, scrutinee);
    }

    private void CheckRangePattern(RangePattern r, LyrType scrutinee, SymbolTable scope)
    {
        var lo = CheckExpr(r.Low, scope);
        var hi = CheckExpr(r.High, scope);
        if (lo.IsError || hi.IsError) return;
        var comparable = TypeFacts.IsNumeric(scrutinee) || scrutinee is PrimitiveType { Kind: PrimitiveKind.Char };
        if (!comparable || !IsAssignable(r.Low, lo, scrutinee) || !IsAssignable(r.High, hi, scrutinee))
        {
            _de.Report("LYR-SEM0029", Severity.Error, r.Span,
                $"range pattern of '{TypeFacts.Display(lo)}'..'{TypeFacts.Display(hi)}' cannot match '{TypeFacts.Display(scrutinee)}'");
            return;
        }
        AdaptLiteralType(r.Low, scrutinee);
        AdaptLiteralType(r.High, scrutinee);
    }

    private void BindVariantPattern(VariantPattern v, LyrType scrutinee, SymbolTable scope, bool mutable = false)
    {
        if (EnumDefOf(scrutinee) is { } enumTs)
        {
            // A qualified path (Shape.Circle) has to point at EXACTLY this enum.
            if (v.Path.Length > 1
                && !ReferenceEquals(ResolveNamePath(v.Path, v.Path.Length - 1, scope), enumTs))
            {
                Report(v.Span, "LYR-SEM0029",
                    $"pattern path '{string.Join('.', v.Path[..^1])}' does not refer to matched enum '{enumTs.Name}'");
                BindPoison(v, scope);
                return;
            }

            // A pattern's path is strings, like a NamedType's: its qualifier has no node, so
            // the import it steps through is recorded the way a type path's is — without this,
            // an import mentioned only in match arms warned as unused.
            if (v.Path.Length > 1 && scope.Lookup(v.Path[0]) is { } head)
                _binding.MarkQualifier(v.Span.File, head);
            if (VariantOf(enumTs, v.Path[^1]) is not { } ev)
            {
                Report(v.Span, "LYR-SEM0031", $"enum '{enumTs.Name}' has no variant '{v.Path[^1]}'");
                BindPoison(v, scope);
                return;
            }
            _result.BindRef(v, ev);
            BindVariantPayload(v, ev, scrutinee, enumTs, scope, mutable);
            return;
        }

        // struct and class destructuring: Point { x, y }
        var (def, subst) = DefinitionOf(scrutinee);
        if (def is null)
        {
            Report(v.Span, "LYR-SEM0029", $"pattern cannot destructure '{TypeFacts.Display(scrutinee)}'");
            BindPoison(v, scope);
            return;
        }
        if (!ReferenceEquals(ResolveNamePath(v.Path, v.Path.Length, scope), def))
        {
            Report(v.Span, "LYR-SEM0029",
                $"pattern names '{string.Join('.', v.Path)}', but the matched value is '{TypeFacts.Display(scrutinee)}'");
            BindPoison(v, scope);
            return;
        }
        if (v.TupleElements is not null)
        {
            Report(v.Span, "LYR-SEM0031", $"'{def.Name}' is not an enum variant — destructure fields with '{def.Name} {{ … }}'");
            BindPoison(v, scope);
            return;
        }
        _result.BindRef(v, def);
        foreach (var fp in v.StructFields ?? [])
        {
            if (def.Members.LookupLocal(fp.Name) is FieldSymbol fs)
            {
                BindFieldPattern(fp, Substitute(FieldType(fs), subst), scope, mutable);
            }
            else
            {
                _de.Report("LYR-SEM0015", Severity.Error, fp.Span, $"'{def.Name}' has no field '{fp.Name}'");
                BindPoisonField(fp, scope);
            }
        }
    }

    private void BindVariantPayload(VariantPattern v, EnumVariantSymbol ev, LyrType scrutinee,
        TypeSymbol enumTs, SymbolTable scope, bool mutable = false)
    {
        var variant = (EnumVariant)ev.Declaration!;
        var subst = scrutinee is GenericInstance gi ? SubstMap(gi) : EmptySubst;

        if (v.TupleElements is { } elems)
        {
            if (variant.TupleFields is not { } fields)
            {
                Report(v.Span, "LYR-SEM0031", variant.StructFields is not null
                    ? $"variant '{ev.Name}' has named fields — destructure with '{ev.Name} {{ … }}'"
                    : $"variant '{ev.Name}' has no payload");
                BindPoison(v, scope);
                return;
            }
            if (fields.Length != elems.Length)
            {
                Report(v.Span, "LYR-SEM0031",
                    $"variant '{ev.Name}' has {fields.Length} payload element(s), pattern has {elems.Length}");
                BindPoison(v, scope);
                return;
            }
            for (var i = 0; i < elems.Length; i++)
                BindPattern(elems[i], Substitute(ResolveType(fields[i], enumTs.Members), subst), scope, mutable, nested: true);
            return;
        }

        if (v.StructFields is { } fps)
        {
            if (variant.StructFields is not { } decls)
            {
                Report(v.Span, "LYR-SEM0031", variant.TupleFields is not null
                    ? $"variant '{ev.Name}' has a tuple payload — destructure with '{ev.Name}(…)'"
                    : $"variant '{ev.Name}' has no payload");
                BindPoison(v, scope);
                return;
            }
            foreach (var fp in fps)
            {
                if (Array.Find(decls, f => f.Name == fp.Name) is { } fd)
                {
                    BindFieldPattern(fp, Substitute(ResolveType(fd.Type, enumTs.Members), subst), scope, mutable);
                }
                else
                {
                    _de.Report("LYR-SEM0031", Severity.Error, fp.Span, $"variant '{ev.Name}' has no field '{fp.Name}'");
                    BindPoisonField(fp, scope);
                }
            }
            return;
        }

        // A qualified unit variant (Shape.Empty) must have no payload.
        if (variant.TupleFields is not null || variant.StructFields is not null)
            Report(v.Span, "LYR-SEM0031", $"variant '{ev.Name}' carries a payload — destructure it");
    }

    /// <summary>Declares a name a pattern binds, and refuses a second binding of the same name in
    /// the SAME pattern.
    ///
    /// <para>Both declaration sites used to ignore the answer, so the second binding was dropped
    /// and the first won in silence: <c>P { n = x, m = x }</c> over <c>P { n = 7, m = 9 }</c>
    /// answered 7, and <c>E.B(x, x)</c> the same. A binding the program writes and the compiler
    /// discards is the shape this project refuses on sight, and the mirror image already does —
    /// <c>LYR-SEM0070</c> refuses a duplicate field in an INITIALIZER.</para>
    ///
    /// <para>Shadowing an outer name stays legal: an arm binds into a scope of its own, and only a
    /// clash within one table reaches here. Or-pattern alternatives bind into tables of their own
    /// too, so <c>E.A(x) | E.B(x)</c> — where the name MUST repeat — is untouched.</para></summary>
    private void DeclareBinding(SymbolTable scope, LocalSymbol local, Core.Span span)
    {
        if (scope.TryDeclare(local)) return;

        _de.Report("LYR-SEM0097", Severity.Error, span,
            $"'{local.Name}' is already bound in this pattern — a name binds once, and the second "
            + "one would be dropped");
    }

    private void BindFieldPattern(FieldPattern fp, LyrType type, SymbolTable scope, bool mutable = false)
    {
        if (fp.Pattern is not null) { BindPattern(fp.Pattern, type, scope, mutable, nested: true); return; }
        var local = new LocalSymbol(fp.Name, type, mutable, fp); // short form: the field name binds
        DeclareBinding(scope, local, fp.Span);
        _result.BindRef(fp, local);
    }

    private void BindPoisonField(FieldPattern fp, SymbolTable scope)
    {
        if (fp.Pattern is not null) { BindPoison(fp.Pattern, scope); return; }
        var local = new LocalSymbol(fp.Name, LyrType.Error, false, fp);
        scope.TryDeclare(local);
        _result.BindRef(fp, local);
    }

    // Or-pattern: every alternative binds into a table of its own, and all of them have to bind the
    // same names with the same types. The bindings of the FIRST alternative become the arm scope,
    // consistently with the definite-assignment analysis, which uses alternative 0.
    private void BindOrPattern(OrPattern o, LyrType scrutinee, SymbolTable scope, bool nested = false)
    {
        if (o.Alternatives.Length == 0) return;
        var tables = new List<SymbolTable>(o.Alternatives.Length);
        foreach (var alt in o.Alternatives)
        {
            var t = new SymbolTable(scope);
            BindPattern(alt, scrutinee, t, nested: nested);
            tables.Add(t);
        }
        var reference = tables[0].Symbols.OfType<LocalSymbol>().ToList();
        for (var i = 1; i < tables.Count; i++)
        {
            var alt = tables[i].Symbols.OfType<LocalSymbol>().ToList();
            foreach (var name in reference.Select(s => s.Name).Union(alt.Select(s => s.Name)))
            {
                var a = reference.Find(s => s.Name == name);
                var b = alt.Find(s => s.Name == name);
                if (a is null || b is null)
                    _de.Report("LYR-SEM0032", Severity.Error, o.Alternatives[i].Span,
                        $"or-pattern alternatives bind different variables: '{name}' is not bound in every alternative");
                else if (!a.Type.IsError && !b.Type.IsError && !LyrType.Equal(a.Type, b.Type))
                    _de.Report("LYR-SEM0032", Severity.Error, o.Alternatives[i].Span,
                        $"'{name}' is bound as '{TypeFacts.Display(a.Type)}' and as '{TypeFacts.Display(b.Type)}' in different or-pattern alternatives");
            }
        }
        foreach (var s in reference) scope.TryDeclare(s);

        // The arm body refers to alternative 0's symbols, so every later alternative's binding
        // node is pointed at the SAME symbol: the lowering then stores into one slot whichever
        // alternative matched, and the unused-name analysis sees one name with its uses rather
        // than a second, never-read one per alternative.
        for (var i = 1; i < o.Alternatives.Length; i++)
            RebindToCanonical(o.Alternatives[i], reference);
    }

    private void RebindToCanonical(Node? node, List<LocalSymbol> canonical)
    {
        switch (node)
        {
            case BindingPattern b when _result.RefOf(b) is LocalSymbol l:
                if (canonical.Find(c => c.Name == l.Name) is { } cb) _result.BindRef(b, cb);
                return;
            case TypePattern tp when _result.RefOf(tp) is LocalSymbol tl:
                if (canonical.Find(c => c.Name == tl.Name) is { } ctp) _result.BindRef(tp, ctp);
                return;
            case FieldPattern { Pattern: null } f when _result.RefOf(f) is LocalSymbol l:
                if (canonical.Find(c => c.Name == l.Name) is { } cf) _result.BindRef(f, cf);
                return;
            case FieldPattern f: RebindToCanonical(f.Pattern, canonical); return;
            case TuplePattern t: foreach (var e in t.Elements) RebindToCanonical(e, canonical); return;
            case VariantPattern v:
                foreach (var e in v.TupleElements ?? []) RebindToCanonical(e, canonical);
                foreach (var f in v.StructFields ?? []) RebindToCanonical(f, canonical);
                return;
            case OrPattern o: foreach (var a in o.Alternatives) RebindToCanonical(a, canonical); return;
        }
    }

    // --- pattern helpers ---

    private static bool IsNullPattern(Pattern p) => p is LiteralPattern { Literal: NullLiteralExpr };

    private static TypeSymbol? EnumDefOf(LyrType t) => t switch
    {
        NamedRef { Symbol.Kind: TypeSymbolKind.Enum } nr => nr.Symbol,
        GenericInstance { Definition.Kind: TypeSymbolKind.Enum } gi => gi.Definition,
        _ => null
    };

    private static EnumVariantSymbol? VariantOf(TypeSymbol enumTs, string name) =>
        enumTs.Members.LookupLocal(name) as EnumVariantSymbol;

    private static (TypeSymbol? def, Dictionary<GenericParamSymbol, LyrType> subst) DefinitionOf(LyrType t) => t switch
    {
        NamedRef nr => (nr.Symbol, EmptySubst),
        GenericInstance gi => (gi.Definition, SubstMap(gi)),
        _ => (null, EmptySubst)
    };

    // Resolves the first count segments of a pattern path through the scope: modules and imports.
    private static Symbol? ResolveNamePath(string[] path, int count, SymbolTable scope)
    {
        var cur = scope.Lookup(path[0]);
        if (cur is ImportBindingSymbol ib0) cur = ib0.Target;
        for (var i = 1; i < count && cur is not null; i++)
        {
            cur = cur is ModuleSymbol mod ? mod.Members.LookupLocal(path[i]) : null;
            if (cur is ImportBindingSymbol ib) cur = ib.Target;
        }
        return cur;
    }

    private static readonly Dictionary<GenericParamSymbol, LyrType> EmptySubst = new(ReferenceEqualityComparer.Instance);

    private void BindPoison(Pattern pattern, SymbolTable scope)
    {
        switch (pattern)
        {
            case BindingPattern b:
                var lb = new LocalSymbol(b.Name, LyrType.Error, false, b);
                scope.TryDeclare(lb);
                _result.BindRef(b, lb);
                return;
            case TypePattern { Name: { } tname } tp:
                var ltp = new LocalSymbol(tname, LyrType.Error, false, tp);
                scope.TryDeclare(ltp);
                _result.BindRef(tp, ltp);
                return;
            case VariantPattern v:
                foreach (var sub in v.TupleElements ?? []) BindPoison(sub, scope);
                foreach (var f in v.StructFields ?? [])
                {
                    if (f.Pattern is not null) BindPoison(f.Pattern, scope);
                    else
                    {
                        var fl = new LocalSymbol(f.Name, LyrType.Error, false, f);
                        scope.TryDeclare(fl);
                        _result.BindRef(f, fl);
                    }
                }
                return;
            case ArrayPattern ap:
                foreach (var sub in ap.Elements)
                {
                    if (sub is RestPattern { Name: { } n } rest)
                    {
                        var lr = new LocalSymbol(n, LyrType.Error, false, rest);
                        scope.TryDeclare(lr);
                        _result.BindRef(rest, lr);
                    }
                    else BindPoison(sub, scope);
                }
                return;
            case TuplePattern t: foreach (var sub in t.Elements) BindPoison(sub, scope); return;
            case OrPattern o: if (o.Alternatives.Length > 0) BindPoison(o.Alternatives[0], scope); return;
        }
    }

    // resume co: yields the value of the next yield.
    /// <summary>
    /// <c>comptime e</c>: <c>e</c> is checked exactly as it would be without the prefix — the
    /// value is the same — and then held to what the evaluator can honour. It runs the
    /// expression in a VM with no capability and no frame of the enclosing function, so a
    /// local, a parameter or <c>this</c> has nothing to stand on there; a lambda inside would be
    /// a value the module cannot hold; and the result has to be a literal the lowering can
    /// write back, which is a scalar, a <c>bool</c>, a <c>char</c> or a <c>string</c>. A nested
    /// <c>comptime</c> is simply evaluated as part of the outer one.
    /// </summary>
    private LyrType CheckComptime(ComptimeExpr ct, SymbolTable scope, LyrType? expected)
    {
        var type = CheckExpr(ct.Inner, scope, expected);
        if (type.IsError) return type;

        if (type is not PrimitiveType { Kind: not PrimitiveKind.Void })
            return Report(ct.Span, "LYR-SEM0100",
                $"a 'comptime' expression has to produce a scalar, a bool, a char or a string — this one "
                + $"is '{TypeFacts.Display(type)}', which the compiled module has no way to hold as a literal");

        var pure = true;
        void Walk(Node? node)
        {
            switch (node)
            {
                case null: return;
                case ThisExpr t:
                    pure = false;
                    Report(t.Span, "LYR-SEM0100", "'this' cannot be used in a 'comptime' expression — "
                        + "it is evaluated before any receiver exists");
                    return;
                case IdentifierExpr id when _result.RefOf(id) is LocalSymbol or ParameterSymbol:
                    pure = false;
                    Report(id.Span, "LYR-SEM0100", $"'{id.Name}' is a local of the enclosing function and "
                        + "cannot be used in a 'comptime' expression — only module-level names can");
                    return;
                case LambdaExpr lam:
                    pure = false;
                    Report(lam.Span, "LYR-SEM0100", "a lambda cannot be used in a 'comptime' expression");
                    return;
                case AssignExpr a:
                    pure = false;
                    Report(a.Span, "LYR-SEM0100", "an assignment cannot be used in a 'comptime' expression");
                    return;
                default:
                    foreach (var child in AstChildren.Of(node)) Walk(child);
                    return;
            }
        }
        Walk(ct.Inner);

        if (pure) _result.ComptimeSites.Add(ct);
        return type;
    }

    private LyrType CheckResume(ResumeExpr re, SymbolTable scope)
    {
        var t = CheckExpr(re.Coroutine, scope);
        if (t is CoroutineOf { Throws: { } thrown }) _result.MarkThrowingPull(re, thrown);
        return t switch
        {
            CoroutineOf co => co.Yield,
            ErrorType => LyrType.Error,
            _ => Report(re.Span, "LYR-SEM0040", $"'resume' needs a Coroutine<T>, got '{TypeFacts.Display(t)}'")
        };
    }

    // A lambda with bidirectional inference: unannotated parameters take the context FnType, and the
    // return context — an annotation before the context — types the body. Block lambdas yield values
    // through 'return' only; without an annotation or a context the type is inferred from the body's
    // returns (v1.13), and a non-void one requires return coverage.
    private LyrType CheckLambda(LambdaExpr lam, SymbolTable scope, LyrType? expected = null)
    {
        // A trailing lambda has the implicit 'it' — one parameter, or none when the context
        // takes none: 'run { … }' beside 'xs.map { it * 2 }'. The parameter node is dropped
        // then, and the lowering skips an unbound implicit parameter.
        var parameters = lam.Parameters;
        if (parameters is [{ Implicit: true }] && expected is FnType { Parameters.Length: 0 })
            parameters = [];
        if (parameters is [{ Implicit: true }] && expected is not FnType { Parameters.Length: 1 })
        {
            Report(lam.Span, "LYR-SEM0045",
                "a trailing lambda takes one parameter, 'it' — the context has to expect a function of one parameter");
            expected = null;
        }

        var expFn = expected is FnType ef && ef.Parameters.Length == parameters.Length ? ef : null;

        // What the lambda throws (05 E2 K3, 08 Y11 F7): the set it writes; else the one its position
        // expects, which the body is then held to; else — no position, or an expected set still
        // waiting for this lambda to bind its type parameter — what the body lets escape, read once
        // the body is checked.
        var thrown = lam.Throws is { } written ? ResolveThrownSet(written, scope)
            : expFn is not null && !expFn.Throws.Any(ContainsTypeParam) ? expFn.Throws : null;

        var savedYield = _currentYield;
        var savedReturn = _currentReturn;
        var savedInference = _returnInference;
        var savedLoops = _loops;
        _loops = new(); // its loops are its own (08 Y11 F5)
        _currentYield = null; // a lambda is no coroutine: a yield inside it is the DYNAMIC kind
                              // (§10a) even when the lambda stands inside a coroutine body —
                              // which chain it meets is decided by who calls it, at runtime
        _returnInference = null; // this lambda's returns are its own, never the outer collection's

        var lambdaScope = new SymbolTable(scope);
        var pTypes = new LyrType[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            LyrType pt;
            if (p.Type is not null) pt = ResolveType(p.Type, scope);
            else if (expFn is not null) pt = expFn.Parameters[i];
            else pt = Report(p.Span, "LYR-SEM0045",
                $"lambda parameter '{p.Name}' needs a type annotation (no context type available)");
            var ps = new ParameterSymbol(p.Name, pt, p);
            // A pattern parameter's '_' is not a name in scope, so there is nothing to declare
            // and nothing to collide.
            if (p.Pattern is null && !lambdaScope.TryDeclare(ps))
                ReportDuplicateParameter(p.Name, p.Span,
                    Array.Find(parameters, q => q.Name == p.Name && !ReferenceEquals(q, p))?.Span);
            _result.BindRef(p, ps); // for definite-assignment analysis: lambda parameters are assigned
            pTypes[i] = pt;

            // '((k, v)) => …': the parameter has its slot, the pattern binds the names from it.
            if (p.Pattern is { } pattern) BindIrrefutable(pattern, pt, lambdaScope, "a lambda parameter");
        }

        var contextRet = lam.ReturnType is not null ? ResolveType(lam.ReturnType, scope) : expFn?.Return;
        // A context return with type parameters still UNBOUND (map(xs, (x) => …), where U is open) is
        // not checked against: the body's actual type binds U in phase C.
        var openGeneric = lam.ReturnType is null && contextRet is not null && ContainsTypeParam(contextRet);
        LyrType ret;
        switch (lam.Body)
        {
            case Expr e:
                var bt = CheckExpr(e, lambdaScope, openGeneric ? null : contextRet);
                if (contextRet is null || openGeneric) ret = bt;
                else
                {
                    ret = contextRet;
                    if (!TypeFacts.IsVoid(contextRet)) // a void context discards the value: () => doStuff()
                        CheckAssignable(e, bt, contextRet, e.Span);
                }
                break;
            case Block b:
            {
                // A tail is 'return tail;' at the block's end (§6.9, §7.3): it counts as a value
                // return for the void default, joins the inferred returns, checks against the
                // context, and covers the block's end.
                var savedTail = _tailExpected;
                if (contextRet is null && !openGeneric && !HasValueReturn(b) && b.Tail is null)
                {
                    // A block lambda without context that returns no value becomes void, so
                    // side-effect closures (`() => { doStuff(); }`) need no `: void`.
                    ret = LyrType.Void;
                    _currentReturn = LyrType.Void;
                    _tailExpected = null;
                    CheckBlock(b, lambdaScope);
                }
                else if (contextRet is null || openGeneric)
                {
                    // Inference from the body's returns (v1.13), with the same unification match
                    // arms use. The collection diverts every 'return' in THIS lambda; a nested
                    // lambda saves and restores the list, so its returns stay its own.
                    var collected = new List<LyrType>();
                    _returnInference = collected;
                    _currentReturn = LyrType.Error; // nothing reads it while collecting
                    _tailExpected = null;
                    CheckBlock(b, lambdaScope);
                    _returnInference = null;
                    if (b.Tail is { } inferredTail) collected.Add(_result.TypeOf(inferredTail.Expr));
                    ret = collected.Count == 0
                        ? LyrType.Void // openGeneric without a value return: U binds to void
                        : UnifyArms(collected, lam.Span, "the returns of this block lambda");
                    if (!TypeFacts.IsVoid(ret) && !ret.IsError && b.Tail is null && !Flow.AlwaysReturns(b, _result))
                        _de.Report("LYR-SEM0046", Severity.Error, lam.Span,
                            "a non-void block lambda must return or throw on every path");
                }
                else
                {
                    ret = contextRet;
                    _currentReturn = contextRet; // 'return' belongs to the LAMBDA, not to the enclosing function
                    _tailExpected = TypeFacts.IsVoid(contextRet) ? null : contextRet;
                    CheckBlock(b, lambdaScope);
                    if (b.Tail is { } contextTail && !TypeFacts.IsVoid(contextRet)) // a void context discards the value
                        CheckAssignable(contextTail.Expr, _result.TypeOf(contextTail.Expr), contextRet, contextTail.Span);
                    if (!TypeFacts.IsVoid(contextRet) && !contextRet.IsError && b.Tail is null && !Flow.AlwaysReturns(b, _result))
                        _de.Report("LYR-SEM0046", Severity.Error, lam.Span, contextRet is NeverType
                            ? "a block lambda returning 'never' must end every path in a throw, a panic or a call that does not return"
                            : "a non-void block lambda must return or throw on every path");
                }
                _tailExpected = savedTail;
                break;
            }
            default:
                ret = LyrType.Error;
                break;
        }

        _currentReturn = savedReturn;
        _currentYield = savedYield;
        _returnInference = savedInference;
        _loops = savedLoops;

        RecordCaptures(lam);
        return new FnType(pTypes, ret) { Throws = thrown ?? EscapingOf(lam.Body) };
    }

    /// <summary>What a lambda's body lets escape (05 E2 K3): the exception analysis's own walk, run
    /// muted, as for a clause without a type (<see cref="Reaching"/>) — one notion of a site.</summary>
    private LyrType[] EscapingOf(Node body)
    {
        using (_de.Mute())
            return new ExceptionAnalyzer(_comp, _result, _de, ThrownCoveredBy, ErrorRoot).Escaping(body, _currentModule);
    }

    // Does the block return a VALUE on any path (`return expr;`)? Descends through the statement
    // structure but NOT into nested lambdas, whose returns belong to them. For the void default:
    // only valueless block lambdas without context may become void.
    private static bool HasValueReturn(Stmt s) => s switch
    {
        ReturnStmt r => r.Value is not null,
        Block b => b.Statements.Any(HasValueReturn),
        IfStmt f => HasValueReturn(f.Then) || (f.Else is not null && HasValueReturn(f.Else)),
        WhileStmt w => HasValueReturn(w.Body),
        DoWhileStmt d => HasValueReturn(d.Body),
        ForInStmt fo => HasValueReturn(fo.Body),
        DeferStmt de => HasValueReturn(de.Body),
        LetPatternStmt lp => lp.Else is not null && HasValueReturn(lp.Else),
        TryStmt t => HasValueReturn(t.Body) || t.Catches.Any(c => HasValueReturn(c.Body)),
        MatchStmt m => m.Arms.Any(a => a.Body is Block ab && HasValueReturn(ab)),
        ExprStmt { Expr: LoopExpr l } => HasValueReturn(l.Body),
        BindingStmt { Initializer: LoopExpr bl } => HasValueReturn(bl.Body),
        _ => false
    };

    private static bool ContainsTypeParam(LyrType t) => t switch
    {
        TypeParamType => true,
        AssocOf a => ContainsTypeParam(a.Base),
        Optional o => ContainsTypeParam(o.Inner),
        ArrayOf a => ContainsTypeParam(a.Element),
        SliceOf s => ContainsTypeParam(s.Element),
        InlineArrayOf ia => ContainsTypeParam(ia.Element),
        TupleOf tu => tu.Elements.Any(ContainsTypeParam),
        FnType f => ContainsTypeParam(f.Return) || f.Parameters.Any(ContainsTypeParam) || f.Throws.Any(ContainsTypeParam),
        GenericInstance gi => gi.Arguments.Any(ContainsTypeParam),
        RangeOf r => ContainsTypeParam(r.Element),
        CoroutineOf c => ContainsTypeParam(c.Yield),
        _ => false
    };

    // Implicit captures: every referenced local and parameter whose declaration lies OUTSIDE the
    // lambda, plus the use of 'this'. A side table for closure lifting.
    private void RecordCaptures(LambdaExpr lam)
    {
        var captured = new List<Symbol>();
        var seen = new HashSet<Symbol>(ReferenceEqualityComparer.Instance);
        var capturesThis = false;

        void WalkNode(Node? node)
        {
            switch (node)
            {
                case null: return;
                case ThisExpr: capturesThis = true; return;
                case IdentifierExpr id:
                    if (_result.RefOf(id) is { } sym && sym is LocalSymbol or ParameterSymbol
                        && !DeclaredInside(sym) && seen.Add(sym))
                        captured.Add(sym);
                    return;
                case LambdaExpr inner: // nested: the span test sorts out inner declarations
                    WalkNode(inner.Body);
                    return;
                case Block b: foreach (var s in b.Statements) WalkNode(s); return;
                case BindingStmt bd: WalkNode(bd.Initializer); WalkNode(bd.Cleanup); return;
                case DestructuringStmt ds: WalkNode(ds.Initializer); return;
                case LetPatternStmt lp: WalkNode(lp.Initializer); WalkNode(lp.Else); return;
                case LetCondExpr lc: WalkNode(lc.Initializer); return;
                case IfStmt f: WalkNode(f.Condition); WalkNode(f.Then); WalkNode(f.Else); return;
                case WhileStmt w: WalkNode(w.Condition); WalkNode(w.Body); return;
                case DoWhileStmt d: WalkNode(d.Body); WalkNode(d.Condition); return;
                case ForInStmt fo: WalkNode(fo.Iterable); WalkNode(fo.Body); return;
                case ReturnStmt r: WalkNode(r.Value); return;
                case BreakStmt br: WalkNode(br.Value); return;
                case YieldStmt y: WalkNode(y.Value); return;
                case ThrowStmt t: WalkNode(t.Value); return;
                case DeferStmt de: WalkNode(de.Body); return;
                case ExprStmt es: WalkNode(es.Expr); return;
                case TryStmt tr:
                    WalkNode(tr.Body);
                    foreach (var c in tr.Catches) WalkNode(c.Body);
                    return;
                case MatchStmt m:
                    WalkNode(m.Scrutinee);
                    foreach (var arm in m.Arms) { WalkNode(arm.Guard); WalkNode(arm.Body); }
                    return;
                case MatchExpr ma:
                    WalkNode(ma.Scrutinee);
                    foreach (var arm in ma.Arms) { WalkNode(arm.Guard); WalkNode(arm.Body); }
                    return;
                case UnaryExpr u: WalkNode(u.Operand); return;
                case PostfixExpr p: WalkNode(p.Operand); return;
                case BinaryExpr b2: WalkNode(b2.Left); WalkNode(b2.Right); return;
                case AssignExpr a: WalkNode(a.Target); WalkNode(a.Value); return;
                case RangeExpr r2: WalkNode(r2.Low); WalkNode(r2.High); return;
                case CastExpr c2: WalkNode(c2.Operand); return;
                case TypeTestExpr tt2: WalkNode(tt2.Operand); return;
                case CallExpr call: WalkNode(call.Callee); foreach (var a in call.Arguments) WalkNode(a); return;
                case IndexExpr ix: WalkNode(ix.Target); WalkNode(ix.Index); return;
                case MemberExpr mem: WalkNode(mem.Target); return;
                case ResumeExpr re: WalkNode(re.Coroutine); return;
                case ComptimeExpr ct: WalkNode(ct.Inner); return;
                case ThrowExpr te: WalkNode(te.Value); return;
                case TryExpr tr: WalkNode(tr.Value); foreach (var c in tr.Catches) WalkNode(c.Body); return;
                case TailExprStmt tail: WalkNode(tail.Expr); return;
                case ArrayLitExpr arr: foreach (var e in arr.Elements) WalkNode(e); return;
                case TupleLitExpr tu: foreach (var e in tu.Elements) WalkNode(e); return;
                case StructInitExpr si: foreach (var f in si.Fields) WalkNode(f.Value); return;
                case WithExpr w: WalkNode(w.Target); foreach (var f in w.Fields) WalkNode(f.Value); return;
                case InterpolatedStringExpr fs:
                    foreach (var seg in fs.Segments) if (seg is InterpHole h) WalkNode(h.Expr);
                    return;
                case IfExpr iff: WalkNode(iff.Condition); WalkNode(iff.Then); WalkNode(iff.Else); return;
                case LoopExpr lp: WalkNode(lp.Body); return;
            }
        }

        bool DeclaredInside(Symbol sym) =>
            sym.Declaration is { } d && d.Span.File == lam.Span.File
            && d.Span.Start >= lam.Span.Start && d.Span.End <= lam.Span.End;

        WalkNode(lam.Body);
        if (captured.Count > 0 || capturesThis)
            _result.SetCaptures(lam, captured, capturesThis);

        // Captured 'var' bindings are shared rather than copied, so they need a cell. Conservatively:
        // what counts is not WHETHER the closure writes, but that it could.
        foreach (var symbol in captured)
            if (symbol is LocalSymbol { IsMutable: true }) _result.MarkBoxed(symbol);
    }

    // --- arithmetic, assignability, literal fit ---

    /// <summary>
    /// Brings two numeric operands to one type; an untyped literal adapts to the other.
    /// </summary>
    /// <remarks>
    /// <para>The adaptation is RECORDED here, not only checked. With <c>LiteralAdaptsTo</c> alone the
    /// sema accepts <c>a + 1</c> with <c>a: int8</c> but still notes <c>int</c> for the <c>1</c>, and
    /// the lowering puts a <c>const i64</c> next to an i8 operand — a compiler crash in the IR
    /// verifier with "operand types differ".</para>
    /// <para><see cref="AdaptLiteralType"/> describes the same error for the assignment path.</para>
    /// </remarks>
    private LyrType? UnifyNumeric(Expr le, LyrType l, Expr re, LyrType r)
    {
        if (LyrType.Equal(l, r) && TypeFacts.IsNumeric(l)) return l;

        if (TypeFacts.IsNumeric(l) && l is PrimitiveType pl && LiteralAdaptsTo(re, pl))
        {
            AdaptLiteralType(re, pl);   // r adapts to l
            return l;
        }

        if (TypeFacts.IsNumeric(r) && r is PrimitiveType pr && LiteralAdaptsTo(le, pr))
        {
            AdaptLiteralType(le, pr);   // l adapts to r
            return r;
        }

        return null;
    }

    private void CheckAssignable(Expr expr, LyrType from, LyrType to, Span span)
    {
        // A value of an interface arises here (04 D9): an interface a table cannot serve — a
        // member naming 'Self' beyond the receiver, a static, a generic one — is a constraint
        // and no type for a value; said where the value would come to be.
        if (TypeFacts.KindOf(to) == TypeSymbolKind.Interface && !LyrType.Equal(from, to) && !from.IsError
            && TypeFacts.SymbolOf(to) is { } boxedInto && !ValueUsable(boxedInto, out var why))
        {
            _de.Report("LYR-SEM0126", Severity.Error, span,
                $"'{boxedInto.Name}' is usable as a constraint only, not as the type of a value — {why}; "
                + $"write 'fn f<T :: [{boxedInto.Name}]>(…)'");
            return;
        }
        if (AdaptEmptyArray(expr, to)) return;

        // An error INSIDE one of the types means the cause is already reported, by the poison rule.
        // Testing only whether the type ITSELF is an ErrorType lets a 'fn(int) -> <error>' through and
        // produces "cannot assign 'fn(int) -> <error>' to 'fn(int) -> U'", which says nothing to the
        // reader and buries the actual message next to it.
        if (ContainsError(from) || ContainsError(to)) return;

        if (!IsAssignable(expr, from, to))
        {
            var fromText = TypeFacts.Display(from);
            var toText = TypeFacts.Display(to);
            // "cannot assign 'T' to 'T'" explains nothing. When the DISPLAY collides the types
            // differ by identity: two declarations sharing a name, or a generic reaching
            // itself at a larger type — which monomorphization refuses (§8.1).
            var hint = fromText == toText
                ? " — two different types share this name (declared in different scopes, or a "
                  + "generic call instantiating itself at a larger type, which cannot be "
                  + "monomorphized)"
                : "";
            _de.Report("LYR-SEM0001", Severity.Error, span,
                $"cannot assign '{fromText}' to '{toText}'{hint}");
            return;
        }

        AdaptLiteralType(expr, to);
    }

    /// <summary>
    /// An untyped literal IS of the target type; it is not converted to it. The side table therefore
    /// has to say so as well, or every later stage takes `let x: int8 = 5` for an `int`.
    /// </summary>
    /// <remarks>Without this step the sema checks the literal fit correctly but keeps noting the
    /// default type; the lowering then produces a `const i64` and pushes it into an i8 slot.</remarks>
    private void AdaptLiteralType(Expr expr, LyrType to)
    {
        while (to is Optional optional) to = optional.Inner; // T to ?T: the literal takes T

        if (to is not PrimitiveType target || !LiteralAdaptsTo(expr, target)) return;

        _result.SetType(expr, target);
        // '-5' is UnaryExpr(Neg, IntLiteral 5); both nodes carry the adapted type.
        if (expr is UnaryExpr { Operator: UnaryOp.Neg } negated)
            _result.SetType(negated.Operand, target);
    }


    /// <summary>
    /// A value type must not contain itself, not even indirectly (design/v5/spec/02 M13).
    ///
    /// <para>A struct holds its fields BY SIZE and an enum its payloads (01 V2, V6), so a value
    /// type that contains itself would be infinitely large. Rust reports "recursive type has
    /// infinite size", C# reports CS0523. For a reference, <c>class Node { next: Node }</c> is
    /// fine: a field holds one machine word.</para>
    ///
    /// <para>What holds a value by size: a field of a struct, a payload of an enum variant, and
    /// through them an optional (<c>?Node</c> is the node and a flag) and a tuple. What breaks
    /// the chain is a reference: a class — <c>Box&lt;T&gt;</c> of <c>std.core</c> is the one made
    /// for it — or an array. Lyric 4 checked structs only and looked through no optional: its
    /// values were heap objects, and <c>enum Tree { Node(Tree, Tree) }</c> ran.</para>
    ///
    /// <para>Without this check the type table would loop forever: a class terminates through
    /// its pre-assigned id, but a value type needs its layout complete before it is finished.</para>
    /// </summary>
    private void CheckValueTypeIsFinite(Decl decl, string name, string kind, ModuleSymbol module)
    {
        if (module.Members.LookupLocal(name) is not TypeSymbol self) return;

        // The path is carried along so the message can name the cycle rather than only its existence:
        // for 'A contains B contains A' that is the whole difference.
        var path = new List<string> { name };
        if (FindValueCycle(self, self, new HashSet<TypeSymbol>(ReferenceEqualityComparer.Instance), path))
            _de.Report("LYR-SEM0056", Severity.Error, decl.Span,
                $"{kind} '{name}' contains itself ({string.Join(" -> ", path)}) and would have "
                + "infinite size — a value type cannot hold itself; put the recursive part behind "
                + "a class, such as 'Box<T>' of std.core");
    }

    private bool FindValueCycle(TypeSymbol root, TypeSymbol current,
        HashSet<TypeSymbol> visited, List<string> path)
    {
        if (!visited.Add(current)) return false;

        foreach (var held in HeldByValue(current.Declaration))
        {
            var bound = _binding.Resolve(held);
            if (bound is ImportBindingSymbol import) bound = import.Target;
            if (bound is not TypeSymbol { Kind: TypeSymbolKind.Struct or TypeSymbolKind.Enum } nested) continue;

            path.Add(nested.Name);
            if (ReferenceEquals(nested, root) || FindValueCycle(root, nested, visited, path))
                return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    /// <summary>The named types a struct or an enum holds by value: its fields or payloads,
    /// looked through an optional and a tuple. An array and a function type are references and
    /// end the walk; so does a class, which the caller sees by its kind.</summary>
    private static IEnumerable<NamedType> HeldByValue(Node? declaration)
    {
        IEnumerable<TypeNode> written = declaration switch
        {
            StructDecl s => s.Members.OfType<FieldDecl>().Select(f => f.Type),
            EnumDecl e => e.Variants.SelectMany(v =>
                (v.TupleFields ?? []).Concat((v.StructFields ?? []).Select(f => f.Type))),
            _ => [],
        };

        static IEnumerable<NamedType> Inside(TypeNode type) => type switch
        {
            NamedType named => [named],
            NullableType optional => Inside(optional.Inner),
            TupleType tuple => tuple.Elements.SelectMany(Inside),
            ArrayType { Length: not null } inline => Inside(inline.Element), // T[N] holds by value (A4)
            _ => [],
        };

        return written.SelectMany(Inside);
    }

    /// <param name="coercionSite">Whether the position is a coercion site — an assignment, an
    /// argument, a return, an initializer — where an unsuffixed literal takes the target's type
    /// (§3.1; the caller records the adaptation afterwards) and an integer widens losslessly
    /// (design/v5/spec/03 T1c; the lowering converts). False where the answer only decides a type
    /// and nothing writes it back — an arm unification — because a yes on either ground alone
    /// leaves the value at its own type while the result claims the target's; coercion happens
    /// after the unification, never inside it (T8).</param>
    private bool IsAssignable(Expr expr, LyrType from, LyrType to, bool coercionSite = true)
    {
        if (from.IsError || to.IsError) return true;      // poison: no follow-up errors
        if (from is NeverType) return true;               // the bottom type: panic(...) fits anywhere
        if (LyrType.Equal(from, to)) return true;
        if (to is Optional inner)                          // T to ?T, widening
            return from is NullType || IsAssignable(expr, from, inner.Inner, coercionSite);
        if (from is NullType) return false;

        // A coroutine that cannot throw fits where one that may is expected: the target promises
        // its readers a 'try', and a value that never throws keeps that promise. The way back is
        // the hole this rule exists for and stays refused — a throwing coroutine in a plain slot
        // is exactly how the demand used to disappear (#73).
        if (to is CoroutineOf { Throws: not null } wanted && from is CoroutineOf { Throws: null } given)
            return LyrType.Equal(given.Yield, wanted.Yield);

        // A function value that throws less fits where one may throw more (05 E2 K6): the same
        // parameters and return — no variance (03 T17) — and its set covered by the target's. Free
        // at run time, since every function value takes the error slot; never the way back, which
        // is how the demand to mark a call would vanish.
        if (to is FnType wantedFn && from is FnType givenFn && FitsFunctionType(givenFn, wantedFn)) return true;

        if (coercionSite && to is SliceOf view && from is ArrayOf whole && LyrType.Equal(whole.Element, view.Element))
            return true;                                                                    // T[] to Slice<T>, A2
        if (coercionSite && to is SliceOf viewOfInline && from is InlineArrayOf inlineArray
            && LyrType.Equal(inlineArray.Element, viewOfInline.Element))
        {
            // T[N] to Slice<T> (A4): where the array lies in the heap; otherwise the refusal is
            // reported here, with its reason, and no second message follows.
            ViewOfInline(expr, inlineArray, expr.Span);
            return true;
        }
        if (coercionSite && to is PrimitiveType pt && LiteralAdaptsTo(expr, pt)) return true; // literal fit
        if (coercionSite && TypeFacts.Widens(from, to)) return true;                           // int8 to int, T1c
        if (ImplementsInterface(from, to)) return true;   // T to I when T :: [I]
        // Every struct, class and enum value is an 'Any' at the transition (03 T10): the empty
        // interface asks nothing, so nothing has to be declared.
        if (IsAny(to) && TypeFacts.SymbolOf(from) is { Kind: TypeSymbolKind.Struct or TypeSymbolKind.Class or TypeSymbolKind.Enum })
            return true;
        // A child interface value where a parent is expected (04 D10): a coercion, the parent's
        // table found at the transition through the concrete type's conformance list.
        if (TypeFacts.KindOf(from) == TypeSymbolKind.Interface && TypeFacts.KindOf(to) == TypeSymbolKind.Interface
            && TypeFacts.SymbolOf(from) is { } child && TypeFacts.SymbolOf(to) is { } parent && !ReferenceEquals(child, parent)
            && ImplementsWithExtensions(child, parent, to, from is GenericInstance childInstance ? SubstMap(childInstance) : EmptySubst))
            return true;
        return false;
    }

    /// <summary>Whether a function value of type <paramref name="given"/> may stand where
    /// <paramref name="wanted"/> is expected (05 E2 K6): one signature, its set covered by the
    /// wanted one's — equal sets included.</summary>
    private bool FitsFunctionType(FnType given, FnType wanted) =>
        given.Parameters.Length == wanted.Parameters.Length
        && given.Parameters.Zip(wanted.Parameters).All(p => LyrType.Equal(p.First, p.Second))
        && LyrType.Equal(given.Return, wanted.Return)
        && given.Throws.All(t => wanted.Throws.Any(w => ThrownCoveredBy(t, w, _currentModule)));

    /// <summary>
    /// Nominal subtyping: a value may stand wherever one of its declared interfaces is expected.
    ///
    /// <para>In THIS direction only. The way back — an interface to a class — would be a downcast, and
    /// the language has none: <c>as</c> converts between numeric types only. An interface value
    /// therefore needs no runtime type check.</para>
    ///
    /// <para>The question itself is answered by <see cref="Conformance"/>, the same place the
    /// conformance check and the IR lowering ask.</para>
    /// </summary>
    /// <summary>
    /// Whether an interface can be the type of a VALUE (design/v5/spec/04 D9): not when a member
    /// names <c>Self</c> outside the receiver, is static, or is generic — a table slot holds one
    /// function for every conformer, and such a member has no one function. Such an interface is
    /// a constraint, and the diagnostic says so where a value of it would arise.
    /// </summary>
    private bool ValueUsable(TypeSymbol iface, out string reason)
    {
        foreach (var part in Conformance.WithParents(iface, _binding))
            foreach (var symbol in part.Members.Symbols)
            {
                // A value of it would need the associated type fixed, 'Iterator<Item = int>' (03
                // T6, 04 D9); such values come with the iterators of M8a. Asked before 'Self',
                // which 'Self.Item' mentions as well.
                if (symbol is AssociatedTypeSymbol assoc)
                { reason = $"it declares the associated type '{assoc.Name}', and a value of it would fix that type — not yet"; return false; }
                if (symbol is not FunctionSymbol fn) continue;
                if (fn.IsStatic) { reason = $"it declares the static member '{fn.Name}'"; return false; }
            }
        foreach (var part in Conformance.WithParents(iface, _binding))
            foreach (var symbol in part.Members.Symbols)
            {
                if (symbol is not FunctionSymbol fn) continue;
                // D9 names generic members too; the 4.x standard library reaches 'Iterator<T>'
                // values with a generic 'map' (05 §5.2a of the 4.x text, monomorphized and not
                // overridable), and it compiles until M8a rewrites it. That clause waits there.
                if (part.SelfParam is { } self && FnTypeOf(fn) is { } signature
                    && signature.Parameters.Append(signature.Return).Any(t => MentionsParam(t, self)))
                { reason = $"its member '{fn.Name}' names 'Self'"; return false; }
            }
        reason = "";
        return true;
    }

    private static bool MentionsParam(LyrType type, GenericParamSymbol param) => type switch
    {
        TypeParamType tp => ReferenceEquals(tp.Param, param),
        AssocOf a => MentionsParam(a.Base, param),
        Optional o => MentionsParam(o.Inner, param),
        ArrayOf a => MentionsParam(a.Element, param),
        SliceOf s => MentionsParam(s.Element, param),
        InlineArrayOf ia => MentionsParam(ia.Element, param),
        TupleOf tu => tu.Elements.Any(e => MentionsParam(e, param)),
        FnType f => f.Parameters.Any(p => MentionsParam(p, param)) || MentionsParam(f.Return, param)
                    || f.Throws.Any(t => MentionsParam(t, param)),
        GenericInstance g => g.Arguments.Any(a => MentionsParam(a, param)),
        RangeOf r => MentionsParam(r.Element, param),
        CoroutineOf c => MentionsParam(c.Yield, param),
        _ => false,
    };

    private bool ImplementsInterface(LyrType from, LyrType to)
    {
        // The target may be a generic interface ('Src<int>'), in which case it is a GenericInstance
        // rather than a NamedRef. Without this case every assignment to a generic interface is a type
        // error and 'Iterator<T>' is unusable.
        if (TypeFacts.KindOf(to) != TypeSymbolKind.Interface) return false;
        var target = TypeFacts.SymbolOf(to)!;

        // Conformance may be DECLARED ('class P :: [I]') or come from a visible 'extend P :: [I]'.
        // Both are the same question, so one function answers it.
        //
        // 'to' is passed on as a whole rather than only its symbol: 'Src<int>' and 'Src<string>' are
        // the same symbol and different types. Without that, an assignment to a generic interface is
        // unsound.
        if (TypeFacts.SymbolOf(from) is { } source)
            return ImplementsWithExtensions(source, target, to,
                from is GenericInstance instance ? SubstMap(instance) : EmptySubst);

        // A type parameter satisfies what its constraints demand. It has no symbol of its own, so it
        // stands here beside rather than inside SymbolOf.
        return from is TypeParamType parameter
               && parameter.Param.Constraints.Any(c =>
                   Conformance.InterfaceOf(c, _binding) is { } it && ReferenceEquals(it, target)
                   && Matches(ResolveType(c, _currentModule?.Members ?? _comp.Builtins),
                       EmptySubst, to));
    }

    private static bool LiteralAdaptsTo(Expr expr, PrimitiveType target)
    {
        if (TryUntypedIntLiteral(expr, out var negative, out var magnitude))
        {
            if (TypeFacts.IsInteger(target)) return TypeFacts.IntLiteralFits(negative, magnitude, target.Kind);
            // An integer literal adapts to a float only EXACTLY (§3.1): 2^53+1 meeting a
            // 'float' is the ordinary assignment error, never a silent rounding.
            if (TypeFacts.IsFloat(target)) return TypeFacts.IntLiteralExactInFloat(magnitude, target.Kind);
        }
        if (IsUntypedFloatLiteral(expr) && TypeFacts.IsFloat(target)) return true;
        return false;
    }

    private static bool TryUntypedIntLiteral(Expr expr, out bool negative, out ulong magnitude)
    {
        switch (expr)
        {
            case IntLiteralExpr { Suffix: null } n: negative = false; magnitude = n.Value; return true;
            case UnaryExpr { Operator: UnaryOp.Neg, Operand: IntLiteralExpr { Suffix: null } n }:
                negative = true; magnitude = n.Value; return true;
            default: negative = false; magnitude = 0; return false;
        }
    }

    private static bool IsUntypedFloatLiteral(Expr expr) => expr switch
    {
        FloatLiteralExpr { Suffix: null } => true,
        UnaryExpr { Operator: UnaryOp.Neg, Operand: FloatLiteralExpr { Suffix: null } } => true,
        _ => false
    };

    // --- type resolution: TypeNode to LyrType ---

    private LyrType ResolveType(TypeNode node, SymbolTable scope)
    {
        if (++_depth > MaxNesting) return Deeper(node.Span);
        try { return ResolveTypeInner(node, scope); }
        finally { _depth--; }
    }

    private LyrType ResolveTypeInner(TypeNode node, SymbolTable scope)
    {
        switch (node)
        {
            case NamedType n:
                // The placeholder stands only where an inference fills it — the type arguments of
                // a call, an initializer, an instantiated function — and those sites take it
                // before resolving. 'let x: List<_> = …' is not among them (T8: a binding's
                // type is written or omitted).
                if (IsPlaceholder(n))
                    return Report(n.Span, "LYR-SEM0117",
                        "'_' stands for a type argument the call infers; here nothing infers it — "
                        + "write the type, or omit the annotation");
                var sym = _binding.Resolve(n) ?? ResolveTypePath(n.Path, scope);

                // Recorded in the SAME table the resolver writes into. The resolver binds the type
                // names it walks — those in declarations — while the ones inside a function body
                // are reached only from here, and they were resolved and then dropped. One question
                // ("what does this type name refer to") answered by one table, whoever asked it.
                if (sym is not null && _binding.Resolve(n) is null) _binding.Bind(n, sym);

                // 'T.Item' (03 T6): a two-segment path whose head is a type or a type parameter,
                // not a module, names an associated type. The resolver bound such a node to its
                // HEAD (a module path is bound to the type it reaches), and a signature is
                // resolved against the builtins' scope, so the head is read off the binding when
                // the scope does not have it.
                if (n.Path.Length == 2 && n.TypeArguments.Length == 0
                    && (scope.Lookup(n.Path[0]) ?? (sym?.Name == n.Path[0] ? sym : null)) is { } pathHead
                    && (pathHead is ImportBindingSymbol { Target: not ModuleSymbol } or GenericParamSymbol or TypeSymbol))
                    return ResolveAssociated(pathHead, n.Path[1], n.Span);
                if (sym is ImportBindingSymbol ibt) sym = ibt.Target;
                if (sym is null)
                    return Report(n.Span, "LYR-SEM0011", $"unresolved type '{string.Join('.', n.Path)}'");
                // 'Self' in the body of a generic type is the type at its own parameters (03 T5).
                if (n.Path is ["Self"] && n.TypeArguments.Length == 0 && sym is TypeSymbol { Generics.Length: > 0 } selfType)
                    return new GenericInstance(selfType, selfType.Generics.Select(g => (LyrType)new TypeParamType(g)).ToArray());
                if (sym is GenericParamSymbol gp) return new TypeParamType(gp);
                if (ReferenceEquals(sym, _slice)) // Slice<T> becomes the internal SliceOf
                {
                    if (n.TypeArguments.Length != 1)
                        return Report(n.Span, "LYR-SEM0026",
                            $"'Slice' expects exactly 1 type argument, got {n.TypeArguments.Length}");
                    return new SliceOf(ResolveType(n.TypeArguments[0], scope));
                }
                if (ReferenceEquals(sym, _coroutine)) // Coroutine<T> becomes the internal CoroutineOf
                {
                    if (n.TypeArguments.Length != 1)
                        return Report(n.Span, "LYR-SEM0026",
                            $"'Coroutine' expects exactly 1 type argument, got {n.TypeArguments.Length}");
                    return new CoroutineOf(ResolveType(n.TypeArguments[0], scope));
                }
                if (sym is TypeSymbol { Kind: not (TypeSymbolKind.Builtin or TypeSymbolKind.Alias) } gts
                    && (gts.Generics.Length > 0 || n.TypeArguments.Length > 0))
                {
                    // A non-generic interface fixed only, 'Iterator<Item = int>', is an instance
                    // too: the fixation is part of the type (03 T6).
                    return MakeGenericInstance(gts, n, scope);
                }
                return SymbolToType(sym, scope, n.Span);
            case ThrowingType tt:
            {
                var carried = ResolveType(tt.Inner, scope);
                if (carried.IsError) return carried;

                // Only a coroutine. Everything else runs at its CALL, and a function's own
                // 'throws' already says so there; a type-level one would be a second spelling for
                // the same fact — and on a value that never runs anything, no spelling at all.
                // Once per node: a parameter or field type is resolved both while its declaration
                // is checked and again through the function type at every call, and the reader
                // does not need the same sentence twice.
                if (carried is not CoroutineOf co)
                    return _reportedThrows.Add(tt) ? Report(tt.Span, "LYR-SEM0084",
                        $"'throws' belongs to a coroutine type, and '{TypeFacts.Display(carried)}' "
                        + "is not one — a call declares what it throws in its own signature")
                        : LyrType.Error;

                return co with { Throws = ThrownTypeOf(tt.Thrown, scope) };
            }
            case NullableType nn: return new Optional(ResolveType(nn.Inner, scope));
            case ArrayType { Length: { } n } ia: return new InlineArrayOf(ResolveType(ia.Element, scope), n);
            case ArrayType a: return new ArrayOf(ResolveType(a.Element, scope));
            case TupleType t: return new TupleOf(t.Elements.Select(e => ResolveType(e, scope)).ToArray()) { Labels = t.Labels };
            case FunctionType f:
                return new FnType(f.Parameters.Select(p => ResolveType(p, scope)).ToArray(), ResolveType(f.ReturnType, scope))
                    { Throws = f.Throws is { } thrown ? ResolveThrownSet(thrown, scope) : [] };
            default: return LyrType.Error; // ErrorType
        }
    }

    /// <summary>
    /// The type an alias names. An alias is a NAME for a type, not a type of its own, so it is
    /// replaced by what it names wherever it stands.
    ///
    /// <para>Guarded against a cycle. <c>type A = B; type B = A;</c> expands forever, and the result
    /// was not a diagnostic but a STACK OVERFLOW — which .NET cannot catch, so the compiler process
    /// died instead of the compilation failing. Reported once per alias: the cycle is a property of
    /// the declaration, and one message per use site would be the same fault repeated.</para>
    /// </summary>
    private LyrType ExpandAlias(TypeSymbol alias, SymbolTable scope)
    {
        if (alias.Declaration is not TypeAliasDecl decl) return LyrType.Error;

        if (!_expanding.Add(alias))
        {
            if (_cyclic.Add(alias))
                Report(decl.Span, "LYR-SEM0064",
                    $"the type alias '{alias.Name}' expands to itself; an alias names a type and " +
                    "cannot be defined through itself");
            return LyrType.Error;
        }

        try
        {
            var underlying = ResolveType(decl.Aliased, scope);
            if (!decl.IsOpaque) return underlying;
            if (underlying.IsError) return LyrType.Error;

            // An OPAQUE alias (v1.15) keeps its identity instead of expanding: the underlying
            // travels along for the layout and for 'as', but nothing else looks through. An
            // opaque over an opaque flattens to the innermost layout under THIS identity.
            var layout = underlying is OpaqueRef o ? o.Underlying : underlying;
            return new OpaqueRef(alias, layout);
        }
        finally { _expanding.Remove(alias); }
    }

    private LyrType SymbolToType(Symbol sym, SymbolTable scope, Span span) => sym switch
    {
        TypeSymbol { Kind: TypeSymbolKind.Builtin } t => TypeFacts.FromBuiltinName(t.Name) ?? LyrType.Error,
        TypeSymbol { Kind: TypeSymbolKind.Alias } t => ExpandAlias(t, scope),
        GenericParamSymbol g => new TypeParamType(g),
        TypeSymbol t => new NamedRef(t),
        ImportBindingSymbol ib => SymbolToType(ib.Target, scope, span),

        // Deliberately silent: an external import is opaque by design, and an error symbol was
        // reported where the resolution failed.
        ExternalSymbol => LyrType.Error,
        ErrorSymbol => LyrType.Error,

        // A name that resolved to something that is NOT a type — a module, a function, a
        // constant. Reported HERE, because an ErrorType may only ever mean "already reported":
        // the silent branch this replaces let 'import std.string;' plus 'let s: string' reach
        // the lowering with an unreported error type, and the lowering threw. The module case
        // names the common trap: a bare import binds the module under its last segment, which
        // can shadow a builtin type.
        ModuleSymbol m => Report(span, "LYR-SEM0011",
            $"'{m.FullName}' is a module, not a type — its import binds the name "
            + $"'{m.Name}'; import selectively or use 'as' to keep the type reachable"),
        _ => Report(span, "LYR-SEM0011", $"'{sym.Name}' is not a type"),
    };

    // Stack<int> becomes a GenericInstance. The arity is checked here; constraint satisfaction runs
    // through the conformance model.
    private LyrType MakeGenericInstance(TypeSymbol ts, NamedType n, SymbolTable scope)
    {
        // 'Iterator<Item = int>' (03 T6): a named argument fixes an associated type of an
        // interface; the positional ones are the type arguments.
        var positional = new List<LyrType>();
        List<(AssociatedTypeSymbol, LyrType)>? fixations = null;
        for (var i = 0; i < n.TypeArguments.Length; i++)
        {
            var resolved = ResolveType(n.TypeArguments[i], scope);
            if (n.ArgumentNames is { } names && i < names.Length && names[i] is { } fixes)
            {
                if (ts.Kind == TypeSymbolKind.Interface && FindAssociated(ts, fixes) is { } member)
                    (fixations ??= new()).Add((member, resolved));
                else // the type is its own error: nothing further is asked of it
                    return Report(n.TypeArguments[i].Span, "LYR-SEM0128",
                        $"'{ts.Name}' declares no associated type '{fixes}' to fix");
                continue;
            }
            positional.Add(resolved);
        }
        var args = FillDefaults(ts, positional.ToArray());
        if (ts.Generics.Length != args.Length)
            _de.Report("LYR-SEM0026", Severity.Error, n.Span,
                $"generic type '{ts.Name}' expects {ts.Generics.Length} type argument(s), got {args.Length}");
        if (args.Length == 0 && fixations is null) return new NamedRef(ts);
        return new GenericInstance(ts, args) { Fixations = fixations?.ToArray() };
    }

    /// <summary>
    /// A trailing parameter left unwritten takes its default (03 T18), resolved in the
    /// declaration's scope — where <c>Self</c> and the earlier parameters stand — the earlier
    /// ones then meaning what was written: <c>Add</c> is <c>Add&lt;Self&gt;</c>, <c>Map&lt;K, V&gt;</c>
    /// is <c>Map&lt;K, V, DefaultHasher&gt;</c>. A list the defaults cannot complete is returned
    /// as it is, for the arity error of the site.
    /// </summary>
    private LyrType[] FillDefaults(TypeSymbol ts, LyrType[] args)
    {
        if (args.Length >= ts.Generics.Length
            || !ts.Generics.Skip(args.Length).All(g => g.Declaration is GenericParam { Default: not null }))
            return args;
        var filled = new List<LyrType>(args);
        for (var i = args.Length; i < ts.Generics.Length; i++)
        {
            var fallback = ((GenericParam)ts.Generics[i].Declaration!).Default!;
            var earlier = new Dictionary<GenericParamSymbol, LyrType>(ReferenceEqualityComparer.Instance);
            for (var j = 0; j < i; j++) earlier[ts.Generics[j]] = filled[j];
            filled.Add(Substitute(ResolveType(fallback, DeclarationScope(ts)), earlier));
        }
        return filled.ToArray();
    }

    // Compact path resolution for body types, which the resolver has not bound.
    private Symbol? ResolveTypePath(string[] path, SymbolTable scope)
    {
        // A public type of 'std.core' is visible without an import (10 U-series): the last
        // answer, after the scope — the resolver's rule, repeated for the names only the sema
        // reaches.
        var head = scope.Lookup(path[0]) ?? (path.Length == 1 ? CoreType(path[0]) : null);
        if (head is null || path.Length == 1) return head;
        for (var i = 1; i < path.Length && head is ImportBindingSymbol { Target: ModuleSymbol mod }; i++)
            head = mod.Members.LookupLocal(path[i]);
        return head;
    }

    // --- helpers ---

    private static LyrType IntSuffixType(IntSuffix s) => new PrimitiveType(s switch
    {
        IntSuffix.I8 => PrimitiveKind.Int8, IntSuffix.I16 => PrimitiveKind.Int16, IntSuffix.I32 => PrimitiveKind.Int32, IntSuffix.I64 => PrimitiveKind.Int,
        IntSuffix.U8 => PrimitiveKind.Uint8, IntSuffix.U16 => PrimitiveKind.Uint16, IntSuffix.U32 => PrimitiveKind.Uint32, _ => PrimitiveKind.Uint
    });

    private static LyrType FloatSuffixType(FloatSuffix s) =>
        new PrimitiveType(s == FloatSuffix.F32 ? PrimitiveKind.Float32 : PrimitiveKind.Float);

    /// <summary>A public type of <c>std.core</c>, visible without an import (10 U-series).</summary>
    private TypeSymbol? CoreType(string name) =>
        _comp.FindModule(["std", "core"])?.Members.LookupLocal(name) is TypeSymbol { Visibility: Visibility.Public } core ? core : null;

    /// <summary>A public type or function of <c>std.core</c>, visible without an import (10 U5: the
    /// prelude carries what the language itself reaches for — the synthesized conformances do).</summary>
    private Symbol? CoreMember(string name) =>
        _comp.FindModule(["std", "core"])?.Members.LookupLocal(name) switch
        {
            TypeSymbol { Visibility: Visibility.Public } type => type,
            FunctionSymbol { Visibility: Visibility.Public } fn => fn,
            _ => null,
        };

    /// <summary>The overloads of a name in <c>std.core</c>, where the scope has none.</summary>
    private IReadOnlyList<FunctionSymbol> CoreOverloads(string name) =>
        _comp.FindModule(["std", "core"])?.Members.OverloadsLocal(name).Where(f => f.Visibility == Visibility.Public).ToList() ?? [];

    /// <summary>THE <c>Any</c> of <c>std.core</c> (03 T10), by identity.</summary>
    private bool IsAny(LyrType type) =>
        TypeFacts.SymbolOf(type) is { } ts && ReferenceEquals(ts, _comp.FindModule(["std", "core"])?.Members.LookupLocal("Any"));

    /// <summary>Whether this is THE <c>Deprecated</c> struct of <c>std.core</c> — by identity,
    /// the same rule the WarningAnalyzer applies when it reads the attribute.</summary>
    private bool IsCanonicalDeprecated(TypeSymbol ts) =>
        ReferenceEquals(ts,
            _comp.FindModule(["std", "core"])?.Members.LookupLocal("Deprecated"));

    private LyrType Report(Span span, string code, string message)
    {
        _de.Report(code, Severity.Error, span, message);
        return LyrType.Error;
    }

    /// <summary>As <see cref="Report"/>, with an optional note — the shape a "did you mean"
    /// arrives in, which is a note or nothing.</summary>
    private LyrType Report(Span span, string code, string message, DiagnosticNote? note)
    {
        if (note is { } n) _de.Report(code, Severity.Error, span, message, n);
        else _de.Report(code, Severity.Error, span, message);
        return LyrType.Error;
    }

    /// <summary>Every name visible from a scope, outermost last. For an unknown TYPE the
    /// candidates narrow to what could stand in a type position.</summary>
    private static IEnumerable<string> NamesIn(SymbolTable scope, bool typesOnly)
    {
        for (var table = scope; table is not null; table = table.Parent)
            foreach (var symbol in table.Symbols)
            {
                var target = symbol is ImportBindingSymbol shell ? shell.Target : symbol;
                if (!typesOnly || target is TypeSymbol or GenericParamSymbol or ExternalSymbol)
                    yield return symbol.Name;
            }
    }

    private void BadOp(Span span, string op, LyrType t) =>
        _de.Report("LYR-SEM0003", Severity.Error, span, $"operator '{op}' is not applicable to '{TypeFacts.Display(t)}'");

    /// <summary>
    /// The operator did not apply. Silent when either side CARRIES an error — the operand's own
    /// diagnostic is the one to act on, and '<error>[]' in a sentence about an operator is noise
    /// following it. <see cref="LyrType.IsError"/> alone is not enough: an error inside an array
    /// or a tuple reaches here as a whole type that is not itself the error.
    /// </summary>
    private LyrType BadBinary(BinaryExpr b, LyrType l, LyrType r)
    {
        if (ContainsError(l) || ContainsError(r)) return LyrType.Error;

        var hint = b.Operator switch
        {
            // The two places a reader most likely expected a conversion the language does not
            // make (03 T1c, T1e): the hint names the explicit way.
            BinaryOp.AddWrap or BinaryOp.SubWrap or BinaryOp.MulWrap when !TypeFacts.IsInteger(l) || !TypeFacts.IsInteger(r)
                => " — the wrap operators are for integer types only",
            _ when TypeFacts.IsChar(l) != TypeFacts.IsChar(r) && (TypeFacts.IsNumeric(l) || TypeFacts.IsNumeric(r))
                => " — a 'char' is not a number; 'as uint32' reaches its scalar value",
            _ when TypeFacts.IsNumeric(l) && TypeFacts.IsNumeric(r)
                => " — the operands of an operator have one type; convert with 'as' (an integer widens only at an assignment, an argument or a return)",
            _ => "",
        };
        _de.Report("LYR-SEM0003", Severity.Error, b.Span,
            $"operator '{OperatorText(b.Operator)}' is not applicable to '{TypeFacts.Display(l)}' and '{TypeFacts.Display(r)}'{hint}");
        return LyrType.Error;
    }
}
