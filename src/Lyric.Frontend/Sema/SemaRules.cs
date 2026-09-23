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
            case StructDecl s: CheckTypeDecl(s.Members.OfType<FunctionDecl>()); break;
            case ClassDecl c: CheckTypeDecl(c.Members.OfType<FunctionDecl>()); break;
            case EnumDecl e: CheckTypeDecl(e.Methods); break;
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
            _de.Report("LYR-SEM0099", Severity.Error, labelSpan,
                $"label '{label}' already names an enclosing loop — a jump to it would be ambiguous");

        var entry = new LoopLabel(label, labelSpan);
        _labels.Add(entry);
        WalkStmt(body);
        _labels.RemoveAt(_labels.Count - 1);

        if (!entry.Used)
            _de.Report("LYR-SEM0100", Severity.Warning, labelSpan,
                $"label '{label}' is never used — no 'break {label}' or 'continue {label}' names it");
    }

    private void ResolveLabel(string label, Span span, string keyword)
    {
        for (var i = _labels.Count - 1; i >= 0; i--)
            if (_labels[i].Name == label) { _labels[i].Used = true; return; }
        _de.Report("LYR-SEM0098", Severity.Error, span,
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
                if (!_types.TypeOf(a.Target).IsError && !IsMutableLvalue(a.Target))
                    _de.Report("LYR-SEM0019", Severity.Error, a.Target.Span, "cannot assign to this target (not a mutable lvalue)");
                WalkExpr(a.Value);
                WalkExpr(a.Target);
                return;

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
        if (_types.TypeOf(operand).IsError || IsMutableLvalue(operand)) return;
        _de.Report("LYR-SEM0019", Severity.Error, operand.Span,
            "cannot increment or decrement this target (not a mutable lvalue)");
    }

    private bool IsMutableLvalue(Expr expr) => expr switch
    {
        IdentifierExpr id => _types.RefOf(id) is LocalSymbol { IsMutable: true },
        MemberExpr m => IsFieldMutable(m),
        // An element is writable as soon as the container is a REFERENCE, exactly like a class
        // field. A 'let' pins the name, not the object behind it.
        IndexExpr ix => IsIndexableTarget(_types.TypeOf(ix.Target)),
        _ => false
    };

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

    private bool IsFieldMutable(MemberExpr m)
    {
        var baseType = _types.TypeOf(m.Target);
        if (baseType.IsError) return true; // poison: no follow-up error

        // An instance of a generic type behaves like its definition: 'Box<int>' is a class when
        // 'Box' is one.
        var kind = TypeFacts.KindOf(baseType);

        if (kind == TypeSymbolKind.Class) return true;               // class fields are always mutable

        // Struct fields likewise, except on 'this' inside a non-'mut' method: that a non-'mut'
        // method does not touch its own receiver is the promise of 'mut fn', and '_thisMut' is what
        // enforces it.
        if (kind == TypeSymbolKind.Struct)
            return m.Target is not ThisExpr || _thisMut;

        return false;
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
