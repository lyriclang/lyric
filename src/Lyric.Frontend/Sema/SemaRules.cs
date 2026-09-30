using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>
/// Structural sema rules, read-only on <see cref="TypeResult"/> and <see cref="BindingResult"/>:
/// the lvalue and mutability check, the rule that an `ExprStmt` is a call or an assignment,
/// interface conformance (`::`), the signature rules, and the `main` contract.
/// </summary>
public sealed class SemaRules
{
    private readonly Compilation _comp;
    private readonly BindingResult _binding;
    private readonly TypeResult _types;
    private readonly DiagnosticEngine _de;
    private readonly bool _singleProgram;
    private bool _thisMut; // inside a 'mut fn' method?

    /// <summary>The labeled loops enclosing the statement being walked, innermost last. A label
    /// is no symbol — it shares no namespace with values — and is scoped to its loop's body.</summary>
    private readonly List<LoopLabel> _labels = new();

    private sealed class LoopLabel(string name, Span span)
    {
        public string Name => name;
        public Span Span => span;
        public bool Used;
    }

    public SemaRules(Compilation comp, BindingResult binding, TypeResult types, DiagnosticEngine de,
        bool singleProgram = true)
    {
        _comp = comp;
        _binding = binding;
        _types = types;
        _de = de;
        _singleProgram = singleProgram;
    }

    public void Run()
    {
        foreach (var module in _comp.Modules)
            foreach (var decl in _comp.AstOf(module).Declarations)
                CheckDecl(decl);
        CheckMain();
    }

    private void CheckDecl(Decl decl)
    {
        switch (decl)
        {
            case FunctionDecl fn:
                CheckSignature(fn, isMethod: false);
                RunBody(fn);
                break;
            case StructDecl s:
                CheckTypeDecl(s.Members.OfType<FunctionDecl>());
                CheckMutHasATarget(s.Name, s.Interfaces, s.Members);
                break;
            case ClassDecl c:
                CheckTypeDecl(c.Members.OfType<FunctionDecl>());
                break;
            case EnumDecl e:
                CheckTypeDecl(e.Methods);
                CheckMutHasATarget(e.Name, e.Interfaces, e.Methods);
                break;
            case InterfaceDecl i: foreach (var m in i.Members) { CheckSignature(m, true); RunBody(m); } break;
            case ExtendDecl x: foreach (var m in x.Methods) { CheckSignature(m, true); RunBody(m); } break;
        }
    }

    // Conformance, meaning the signature match, is done by the TypeChecker; only the signature rules
    // and the bodies happen here.
    private void CheckTypeDecl(IEnumerable<FunctionDecl> methods)
    {
        foreach (var fn in methods) { CheckSignature(fn, isMethod: true); RunBody(fn); }
    }

    /// <summary>
    /// A <c>mut fn</c> needs something to write (design/v5/spec/02 M4): a <c>var</c> field of
    /// its type, or <c>this</c> as a whole. On a type that has neither, the word promises a
    /// write that cannot happen — and costs its callers a <c>var</c> root for nothing.
    ///
    /// <para>Two methods keep the word without a target of their own: one that an interface of
    /// the type declares <c>mut</c> (the signature has to match; an empty iterator's
    /// <c>next</c> writes nothing and says <c>mut</c> all the same), and one that calls a
    /// <c>mut fn</c> on <c>this</c>, which writes through the callee.</para>
    ///
    /// <para>Asked of VALUES only, a struct and an enum. On a class the word costs a caller
    /// nothing (a reference is always a place), and a class without a <c>var</c> field changes
    /// all the same when it holds a <c>let</c> reference to an object that does — a stack over
    /// a list says <c>mut fn pop</c> and means it. Whether M4 wants that refused is the
    /// maintainer's question; until it is answered, a class is not asked.</para>
    /// </summary>
    private void CheckMutHasATarget(string owner, TypeNode[] interfaces, IEnumerable<Decl> members)
    {
        var list = members.ToList();
        if (list.OfType<FieldDecl>().Any(f => f.IsVar)) return;

        HashSet<string>? demanded = null;
        foreach (var method in list.OfType<FunctionDecl>())
        {
            if (!method.IsMut || method.IsStatic || method.Body is null) continue;
            if (WritesThis(method.Body)) continue;

            demanded ??= interfaces
                .Select(t => Conformance.InterfaceOf(t, _binding)).OfType<TypeSymbol>()
                .SelectMany(i => Conformance.WithParents(i, _binding))
                .SelectMany(i => (i.Declaration as InterfaceDecl)?.Members ?? [])
                .Where(m => m.IsMut).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            if (demanded.Contains(method.Name)) continue;

            _de.Report("LYR-SEM0023", Severity.Error, method.NameSpan,
                $"'mut fn {method.Name}' has nothing to write: '{owner}' has no 'var' field, and the method does not replace 'this'");
        }
    }

    /// <summary>Does this body replace <c>this</c> as a whole, or call a <c>mut fn</c> on it?</summary>
    private bool WritesThis(Node node) => node switch
    {
        AssignExpr { Target: ThisExpr } => true,
        CallExpr { Callee: MemberExpr { Target: ThisExpr } callee }
            when (_types.RefOf(callee) is ImportBindingSymbol { Target: var target } ? target : _types.RefOf(callee))
                is FunctionSymbol { Declaration: FunctionDecl { IsMut: true } } => true,
        _ => AstChildren.Of(node).Any(WritesThis),
    };

    // --- signature rules ---

    private void CheckSignature(FunctionDecl fn, bool isMethod)
    {
        if (fn.IsMut && !isMethod)
            _de.Report("LYR-SEM0023", Severity.Error, fn.Span, $"'mut' is only allowed on methods, not on free function '{fn.Name}'");

        var ps = fn.Parameters;
        for (var i = 0; i < ps.Length; i++)
            if (ps[i].IsParams)
            {
                if (i != ps.Length - 1)
                    _de.Report("LYR-SEM0024", Severity.Error, ps[i].Span, "'params' must be the last parameter");
                if (ps[i].Type is not ArrayType)
                    _de.Report("LYR-SEM0024", Severity.Error, ps[i].Span, "'params' requires an array type");
            }

        var seenDefault = false;
        foreach (var p in ps)
        {
            if (p.Default is not null) seenDefault = true;
            else if (seenDefault && !p.IsParams)
                _de.Report("LYR-SEM0025", Severity.Error, p.Span, $"required parameter '{p.Name}' follows a default parameter");
        }
    }

    // --- the main contract ---

    private void CheckMain()
    {
        var mains = _comp.Modules
            .SelectMany(m => _comp.AstOf(m).Declarations.OfType<FunctionDecl>())
            .Where(f => f.Name == "main")
            .ToList();

        foreach (var main in mains)
            if (!ValidMain(main))
                _de.Report("LYR-SEM0021", Severity.Error, main.Span,
                    "'main' must be 'fn main(): int' or 'fn main(args: string[]): int'");

        // One 'main' per EXECUTABLE. A workspace compilation holds several programs side by side,
        // and there a second 'main' is the entry point of another script, not a duplicate.
        if (!_singleProgram) return;

        for (var i = 1; i < mains.Count; i++)
            _de.Report("LYR-SEM0021", Severity.Error, mains[i].Span, "duplicate 'main' function");
    }

    private static bool ValidMain(FunctionDecl fn)
    {
        if (!IsNamed(fn.ReturnType, "int")) return false;
        // The entry point declares nothing (§9.2): a throws clause on main would let an
        // exception escape the program as LYR-VM0010 — the panic that is supposed to be
        // unreachable from source.
        if (fn.Throws is not null) return false;
        return fn.Parameters.Length switch
        {
            0 => true,
            1 => fn.Parameters[0].Type is ArrayType a && IsNamed(a.Element, "string"),
            _ => false
        };
    }

    private static bool IsNamed(TypeNode? t, string name) => t is NamedType n && n.Path is [var only] && only == name;

    // --- lvalue and mutability, plus the ExprStmt rule ---

    private void RunBody(FunctionDecl fn)
    {
        if (fn.Body is null) return;
        var saved = _thisMut;
        _thisMut = fn.IsMut;
        WalkStmt(fn.Body);
        _thisMut = saved;
    }

    private void WalkStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Block b: foreach (var s in b.Statements) WalkStmt(s); break;
            case ExprStmt es: CheckExprStmt(es); WalkExpr(es.Expr); break;
            case TailExprStmt tail: WalkExpr(tail.Expr); break;
            case BindingStmt bd: if (bd.Initializer is not null) WalkExpr(bd.Initializer); break;
            case DestructuringStmt d: WalkExpr(d.Initializer); break;
            case LetPatternStmt lp: WalkExpr(lp.Initializer); if (lp.Else is not null) WalkStmt(lp.Else); break;
            case IfStmt f: WalkExpr(f.Condition); WalkStmt(f.Then); if (f.Else is not null) WalkStmt(f.Else); break;
            case WhileStmt w:
                WalkExpr(w.Condition);
                WalkLoopBody(w.Body, w.Label, w.LabelSpan);
                break;
            case DoWhileStmt d:
                WalkLoopBody(d.Body, d.Label, d.LabelSpan);
                WalkExpr(d.Condition);
                break;
            case ForInStmt fo:
                WalkExpr(fo.Iterable);
                WalkLoopBody(fo.Body, fo.Label, fo.LabelSpan);
                break;
            case BreakStmt br when br.Label is { } target: ResolveLabel(target, br.LabelSpan, "break"); break;
            case ContinueStmt co when co.Label is { } target: ResolveLabel(target, co.LabelSpan, "continue"); break;
            case ReturnStmt r: if (r.Value is not null) WalkExpr(r.Value); break;
            case ThrowStmt t: WalkExpr(t.Value); break;
            case YieldStmt y: if (y.Value is not null) WalkExpr(y.Value); break;
            case DeferStmt de: WalkStmt(de.Body); break;
            case TryStmt tr: CheckTry(tr); WalkStmt(tr.Body); foreach (var c in tr.Catches) WalkStmt(c.Body); break;
            case MatchStmt m:
                WalkExpr(m.Scrutinee);
                foreach (var arm in m.Arms)
                {
                    if (arm.Guard is not null) WalkExpr(arm.Guard);
                    if (arm.Body is Block ab) WalkStmt(ab); else if (arm.Body is Expr ae) WalkExpr(ae);
                }
                break;
        }
    }

    /// <summary>The body of a loop, under its label when it has one. A label repeating an
    /// enclosing one is refused: 'break outer' would have two candidates, and picking the nearer
    /// one silently is how the far one stops being reachable. A label nothing jumps to is a
    /// warning, like an unused binding.</summary>
    private void WalkLoopBody(Block body, string? label, Span labelSpan)
    {
        if (label is null) { WalkStmt(body); return; }

        if (_labels.Any(l => l.Name == label))
            _de.Report("LYR-SEM0102", Severity.Error, labelSpan,
                $"label '{label}' already names an enclosing loop — a jump to it would be ambiguous");

        var entry = new LoopLabel(label, labelSpan);
        _labels.Add(entry);
        WalkStmt(body);
        _labels.RemoveAt(_labels.Count - 1);

        if (!entry.Used)
            _de.Report("LYR-SEM0103", Severity.Warning, labelSpan,
                $"label '{label}' is never used — no 'break {label}' or 'continue {label}' names it");
    }

    private void ResolveLabel(string label, Span span, string keyword)
    {
        for (var i = _labels.Count - 1; i >= 0; i--)
            if (_labels[i].Name == label) { _labels[i].Used = true; return; }
        _de.Report("LYR-SEM0101", Severity.Error, span,
            $"no enclosing loop is labeled '{label}' — '{keyword} {label}' names a loop this statement stands in");
    }

    // try/catch structure: at least one catch, and a catch-all without a type only as the last clause.
    private void CheckTry(TryStmt tr)
    {
        if (tr.Catches.Length == 0)
            _de.Report("LYR-SEM0036", Severity.Error, tr.Span,
                "'try' needs at least one 'catch' ('finally' does not exist — use 'defer')");
        for (var i = 0; i < tr.Catches.Length - 1; i++)
            if (tr.Catches[i].BindingType is null)
                _de.Report("LYR-SEM0035", Severity.Error, tr.Catches[i].Span,
                    "catch-all must be the last catch clause");
    }

    private void CheckExprStmt(ExprStmt es)
    {
        var ok = es.Expr is CallExpr or AssignExpr or ResumeExpr or ThrowExpr
            or PostfixExpr { Operator: PostfixOp.Inc or PostfixOp.Dec } or ErrorExpr;
        if (!ok)
            _de.Report("LYR-SEM0022", Severity.Error, es.Span, "expression statement has no effect (only calls, assignments and resume are allowed)");
    }

    private void WalkExpr(Expr expr)
    {
        switch (expr)
        {
            case AssignExpr a:
                if (!_types.TypeOf(a.Target).IsError && WhyNotWritable(a.Target) is { } reason)
                    _de.Report("LYR-SEM0019", Severity.Error, a.Target.Span, $"cannot assign to this target — {reason}");
                WalkExpr(a.Value);
                WalkExpr(a.Target);
                return;

            // A 'mut fn' writes its receiver, so the receiver is a place that is written: the
            // same rule as an assignment into it (design/v5/spec/02 M4).
            case CallExpr { Callee: MemberExpr callee } call:
                CheckMutCall(callee, call);
                break;

            // '++' and '--' READ AND WRITE. Checked only as assignments, they were the one way past
            // §7.1: 'let x = 1; x++;' compiled without a word and answered 2.
            case PostfixExpr { Operator: PostfixOp.Inc or PostfixOp.Dec } p:
                CheckIncrementTarget(p.Operand);
                WalkExpr(p.Operand);
                return;

            case UnaryExpr { Operator: UnaryOp.PreInc or UnaryOp.PreDec } u:
                CheckIncrementTarget(u.Operand);
                WalkExpr(u.Operand);
                return;

            // A lambda BODY is a body: the same rules hold inside it. Reached only through
            // 'Children', it was never walked at all, and an assignment to a captured 'let' went
            // to the lowering, which has no diagnostic for it and threw.
            case LambdaExpr lambda:
                if (lambda.Body is Block block) WalkStmt(block);
                else if (lambda.Body is Expr body) WalkExpr(body);
                return;

            // The BLOCK arms of a match expression, for the same reason: 'Children' collects the
            // expression arms, and a block arm assigning to a 'let' passed unseen.
            case MatchExpr match:
                WalkExpr(match.Scrutinee);
                foreach (var arm in match.Arms)
                {
                    if (arm.Guard is not null) WalkExpr(arm.Guard);
                    if (arm.Body is Block armBlock) WalkStmt(armBlock);
                    else if (arm.Body is Expr armExpr) WalkExpr(armExpr);
                }
                return;
        }

        foreach (var child in Children(expr)) WalkExpr(child);
    }

    /// <summary>The target of <c>++</c> or <c>--</c>, which is written as much as read.</summary>
    private void CheckIncrementTarget(Expr operand)
    {
        if (_types.TypeOf(operand).IsError || WhyNotWritable(operand) is not { } reason) return;
        _de.Report("LYR-SEM0019", Severity.Error, operand.Span,
            $"cannot increment or decrement this target — {reason}");
    }

    /// <summary>
    /// Whether an expression names a place that may be written, as the reason it may not —
    /// <c>null</c> when it may (design/v5/spec/02 M2, M3, M4, M9).
    ///
    /// <para>ONE RULE FOR STRUCTS AND CLASSES: a field is written only when it is declared
    /// <c>var</c>, and only through a root that may be written. The root of <c>a.b[i].c</c> is
    /// its first binding, and a <b>reference starts the chain anew</b>: behind a class value or
    /// an array the object is the place, whatever the binding that holds the reference says —
    /// <c>let c: Cls; c.count = 1</c> writes the object, <c>let xs: P[]; xs[0].x = 1</c> the
    /// element. A struct is its storage, so a <c>let</c> freezes it to the bottom, and a
    /// parameter is a <c>let</c>.</para>
    ///
    /// <para><c>this</c> is a place only inside a <c>mut fn</c>, for a class as for a struct:
    /// the word is part of the method's contract and is kept at the site that would break it.
    /// Lyric 4 enforced it on structs only.</para>
    /// </summary>
    private string? WhyNotWritable(Expr expr)
    {
        switch (expr)
        {
            case IdentifierExpr id:
                return _types.RefOf(id) switch
                {
                    LocalSymbol { IsMutable: true } => null,
                    ParameterSymbol => $"'{id.Name}' is a parameter, and a parameter is a 'let' binding; copy it into a 'var' to change it",
                    LocalSymbol => $"'{id.Name}' is bound with 'let'; declare it 'var' to write it",
                    _ => $"'{id.Name}' is not a variable",
                };

            case ThisExpr:
                // 'this = value' replaces a VALUE as a whole, a struct or an enum. Anything else
                // is the reference (or the scalar copy) the caller handed in, and is not rebound.
                if (TypeFacts.KindOf(_types.TypeOf(expr)) is not (TypeSymbolKind.Struct or TypeSymbolKind.Enum))
                    return "'this' is replaced as a whole only in a 'mut fn' of a struct or an enum";
                return _thisMut ? null : NotMutReason;

            case MemberExpr m:
            {
                // 'Type.NAME' names a 'static let', and that is a constant (Lyric 4 let the
                // assignment through: nothing asked what a member of a TYPE is).
                var named = _types.RefOf(m.Target);
                if (named is ImportBindingSymbol { Target: var imported }) named = imported;
                if (named is TypeSymbol holder)
                    return holder.Members.LookupLocal(m.Member) is GlobalSymbol
                        ? $"'{m.Member}' is a 'static let' of '{holder.Name}', a constant"
                        : $"'{m.Member}' is not a place in '{holder.Name}'";

                var baseType = _types.TypeOf(m.Target);
                if (baseType.IsError) return null; // poison: no follow-up error

                // An instance of a generic type behaves like its definition: 'Box<int>' is a
                // class when 'Box' is one.
                var kind = TypeFacts.KindOf(baseType);
                if (kind is not (TypeSymbolKind.Class or TypeSymbolKind.Struct))
                    return "only a 'var' field of a struct or a class is written";

                var owner = TypeFacts.SymbolOf(baseType)!;
                if (owner.Members.LookupLocal(m.Member) is not FieldSymbol { Declaration: FieldDecl field })
                    return $"'{m.Member}' is not a field of '{owner.Name}'";
                if (!field.IsVar)
                    return $"field '{m.Member}' of '{owner.Name}' is not declared 'var'";

                return WhyNotAPlace(m.Target);
            }

            // An element is writable as soon as the container is a REFERENCE, exactly like a class
            // field. A 'let' pins the name, not the object behind it.
            case IndexExpr ix:
                return IsIndexableTarget(_types.TypeOf(ix.Target)) ? null : "the target is not indexable";

            default:
                return "it is not a place a value is stored in";
        }
    }

    private const string NotMutReason = "'this' is written, and the method is not declared 'mut fn'";

    /// <summary>
    /// Whether the value an expression names may be changed IN PLACE — the base of a field write
    /// or the receiver of a <c>mut fn</c> — as the reason it may not.
    ///
    /// <para>A reference is always a place: the object behind it is written, and only
    /// <c>this</c> keeps the <c>mut fn</c> rule. A struct is a place when the expression that
    /// names it is: a <c>var</c> binding, <c>this</c> in a <c>mut fn</c>, a <c>var</c> field
    /// of a place, an array element. Anything else is a temporary — the result of a call, an
    /// element a container's <c>get</c> handed out — and writing into it would change a copy
    /// nobody reads (M4): refused, never copied in silence.</para>
    /// </summary>
    private string? WhyNotAPlace(Expr expr)
    {
        var type = _types.TypeOf(expr);
        if (type.IsError) return null;

        var reference = TypeFacts.KindOf(type) is TypeSymbolKind.Class or TypeSymbolKind.Interface
                        || type is ArrayOf or CoroutineOf;
        if (reference)
            return expr is ThisExpr && !_thisMut ? NotMutReason : null;

        switch (expr)
        {
            case ThisExpr:
                return _thisMut ? null : NotMutReason;
            case IdentifierExpr or MemberExpr:
                return WhyNotWritable(expr);
            case IndexExpr ix when _types.TypeOf(ix.Target) is ArrayOf or ErrorType:
                return null; // an array element is a place in the array's block
            case IndexExpr:
                return "the element is a copy a 'get' handed out, not a place; assign the whole element instead";
            default:
                return "the value is a temporary; bind it to a 'var' first";
        }
    }

    /// <summary>
    /// A call of a <c>mut fn</c> writes its receiver (M4): the receiver has to be a place, by
    /// the rule a field write follows. Transitively, too — a method that is not <c>mut</c>
    /// may not call a <c>mut fn</c> on <c>this</c>, or the word would promise nothing.
    /// </summary>
    private void CheckMutCall(MemberExpr callee, CallExpr call)
    {
        var bound = _types.RefOf(callee);
        if (bound is ImportBindingSymbol import) bound = import.Target;
        if (bound is not FunctionSymbol { Declaration: FunctionDecl { IsMut: true, IsStatic: false } method }) return;
        if (WhyNotAPlace(callee.Target) is not { } reason) return;

        _de.Report("LYR-SEM0019", Severity.Error, call.Span,
            $"cannot call 'mut fn {method.Name}' on this receiver — {reason}");
    }

    /// <summary>An array, or a type satisfying <c>Indexable&lt;T&gt;</c>. Both are references, so the
    /// element is writable: a <c>let</c> pins the name, not the object behind it.
    ///
    /// <para>The interface's <c>set</c> setter is a <c>mut fn</c>, which costs nothing here.</para>
    /// </summary>
    private bool IsIndexableTarget(LyrType type)
    {
        if (type is ArrayOf or ErrorType) return true;
        if (_types.Indexable is not { } indexable) return false;

        return TypeFacts.SymbolOf(type) is { } symbol
               && Conformance.Implements(symbol, indexable, _binding);
    }

    // The direct child expressions, used to spot nested assignments.
    private static IEnumerable<Expr> Children(Expr e) => e switch
    {
        UnaryExpr u => [u.Operand],
        PostfixExpr p => [p.Operand],
        ResumeExpr re => [re.Coroutine],
        ComptimeExpr ct => [ct.Inner],
        ThrowExpr te => [te.Value],
        BinaryExpr b => [b.Left, b.Right],
        RangeExpr r => [r.Low, r.High],
        CastExpr c => [c.Operand],
        CallExpr call => [call.Callee, .. call.Arguments],
        IndexExpr ix => [ix.Target, ix.Index],
        MemberExpr m => [m.Target],
        ArrayLitExpr arr => arr.Elements,
        TupleLitExpr tu => tu.Elements,
        StructInitExpr si => si.Fields.Select(f => f.Value),
        InterpolatedStringExpr fs => fs.Segments.OfType<InterpHole>().Select(h => h.Expr),
        IfExpr iff => [iff.Condition, iff.Then, iff.Else],
        // MatchExpr and LambdaExpr are handled in WalkExpr: their block arms and block bodies are
        // STATEMENTS, which this list cannot carry.
        AssignExpr a => [a.Target, a.Value],
        _ => []
    };
}
