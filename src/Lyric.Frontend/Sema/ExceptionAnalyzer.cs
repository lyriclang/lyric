using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>
/// Marked propagation (design/v5/spec/05 E1, E2), a read-only pass after the checker:
///
/// <list type="bullet">
/// <item>every call of a throwing function stands under a <c>try</c> — a <c>try</c> expression,
/// or the body of a <c>try</c> block — of its own function context (<c>LYR-SEM0138</c>): the
/// propagation is seen where it happens;</item>
/// <item>every type a site may throw is covered by a <c>catch</c> of an enclosing block or by the
/// context's <c>throws</c> set (K8, <c>LYR-SEM0034</c>) — a <c>throw</c> needs no mark, but the
/// same cover;</item>
/// <item>a <c>try</c> no error reaches is warned about (<c>LYR-SEM0139</c>): nothing under it
/// throws, or a <c>try</c> inside takes all of it.</item>
/// </list>
///
/// <para>A lambda is a context of its own — its body runs later, outside every <c>try</c> around
/// it — that throws what its type says (K3): the set it writes or its position expects, or the one
/// read off its body. A global's initializer and a default have no handler at all. Coverage is the checker's question: an
/// element covers a thrown type when it IS the type, on the instance, or an interface the type
/// conforms to (<see cref="TypeChecker.ThrownCoveredBy"/>), asked in the module the site stands in.</para>
/// </summary>
internal sealed class ExceptionAnalyzer
{
    private readonly Compilation _comp;
    private readonly TypeResult _types;
    private readonly DiagnosticEngine _de;
    private readonly Func<LyrType, LyrType, ModuleSymbol?, bool> _covers;
    private readonly LyrType? _root; // std.core's Error; null without a standard library

    /// <summary>One function context: the set it may throw, how a message names it, whether it
    /// can declare at all, and the <c>try</c>s open in it, innermost last.</summary>
    private sealed class Context(LyrType[] declared, string name, bool canDeclare)
    {
        public LyrType[] Declared { get; } = declared;
        public string Name { get; } = name;
        public bool CanDeclare { get; } = canDeclare;
        public List<Frame> Frames { get; } = new();
    }

    /// <summary>An open <c>try</c>: a mark over an expression, a block or an expression with its
    /// clauses, or a <c>try?</c> or <c>try!</c>, which takes every error itself. Reached when an
    /// error of a site under it gets that far — no <c>try</c> inside took it.</summary>
    private sealed class Frame(CatchClause[] catches, bool takesAll)
    {
        public CatchClause[] Catches { get; } = catches;
        public bool TakesAll { get; } = takesAll;
        public bool Reached { get; set; }

        /// <summary>Where the types that reach the frame are kept, for <see cref="Escaping"/>.</summary>
        public List<LyrType>? Kept { get; init; }
    }

    private Context _context = new([], "the program", canDeclare: false);
    private ModuleSymbol? _module;

    public ExceptionAnalyzer(Compilation comp, TypeResult types, DiagnosticEngine de,
        Func<LyrType, LyrType, ModuleSymbol?, bool> covers, LyrType? root)
    {
        _root = root;
        _comp = comp;
        _types = types;
        _de = de;
        _covers = covers;
    }

    /// <summary>
    /// What the sites under a try throw past every try inside it — what reaches the try's clauses
    /// (05 E2 K7). The analysis's own walk, under a frame that takes everything and keeps what
    /// reached it; the checker asks it while it checks the clauses, with the diagnostics muted —
    /// the analysis proper runs after the checker and reports.
    /// </summary>
    internal LyrType[] Escaping(Node tried, ModuleSymbol? module)
    {
        _module = module;
        var kept = new List<LyrType>();
        _context = new Context([], "the try", canDeclare: false);
        _context.Frames.Add(new Frame([], takesAll: true) { Kept = kept });
        if (tried is Stmt stmt) AnalyzeStmt(stmt);
        else if (tried is Expr expr) AnalyzeExpr(expr);
        return kept.ToArray();
    }

    public void Run()
    {
        foreach (var module in _comp.Modules)
        {
            _module = module;
            foreach (var decl in _comp.AstOf(module).Declarations)
                AnalyzeDecl(decl);
        }
    }

    private void AnalyzeDecl(Decl decl)
    {
        switch (decl)
        {
            case FunctionDecl fn: AnalyzeFunction(fn); break;
            case StructDecl s: AnalyzeMembers(s.Members); break;
            case ClassDecl c: AnalyzeMembers(c.Members); break;
            case EnumDecl e: foreach (var f in e.Methods) AnalyzeFunction(f); break;
            case InterfaceDecl i: foreach (var f in i.Members) AnalyzeFunction(f); break; // default bodies
            case ExtendDecl x: foreach (var f in x.Methods) AnalyzeFunction(f); break;
            case GlobalBindingDecl { Binding.Initializer: { } init }:
                InContext(new Context([], "a global's initializer", canDeclare: false), () => AnalyzeExpr(init));
                break;
        }
    }

    private void AnalyzeMembers(Decl[] members)
    {
        foreach (var m in members)
            switch (m)
            {
                case FunctionDecl f: AnalyzeFunction(f); break;
                case FieldDecl { Default: { } value }:
                    InContext(new Context([], "a field's default", canDeclare: false), () => AnalyzeExpr(value));
                    break;
            }
    }

    private void AnalyzeFunction(FunctionDecl fn)
    {
        foreach (var p in fn.Parameters)
            if (p.Default is { } value)
                InContext(new Context([], "a parameter's default", canDeclare: false), () => AnalyzeExpr(value));

        if (fn.Body is not { } body) return;
        InContext(new Context(_types.DeclaredThrows(fn), $"'{fn.Name}'", canDeclare: true), () => AnalyzeStmt(body));
    }

    private void InContext(Context context, Action walk)
    {
        var saved = _context;
        _context = context;
        try { walk(); }
        finally { _context = saved; }
    }

    // --- statements ---

    private void AnalyzeStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Block b: foreach (var s in b.Statements) AnalyzeStmt(s); break;
            case TailExprStmt tail: AnalyzeExpr(tail.Expr); break;
            case BindingStmt bd:
                if (bd.Initializer is not null) AnalyzeExpr(bd.Initializer);
                // 'using let': the close it owes the scope, a site of the scope (05 E7 R4) — where
                // the checker took it up: a binding that is no Closeable (SEM0143) owes nothing.
                if (bd.Cleanup is { Body: ExprStmt { Expr: TryExpr { Value: { } close } } } && !_types.TypeOf(close).IsError)
                    AnalyzeStmt(bd.Cleanup);
                break;
            // A destructuring binding REQUIRES its initializer, and that initializer is a call like
            // any other: missing here, a throwing one escaped the walk entirely.
            case DestructuringStmt ds: AnalyzeExpr(ds.Initializer); break;
            case LetPatternStmt lp:
                AnalyzeExpr(lp.Initializer);
                if (lp.Else is not null) AnalyzeStmt(lp.Else);
                break;
            case ExprStmt es: AnalyzeExpr(es.Expr); break;
            case IfStmt f:
                AnalyzeExpr(f.Condition);
                AnalyzeStmt(f.Then);
                if (f.Else is not null) AnalyzeStmt(f.Else);
                break;
            case WhileStmt w: AnalyzeExpr(w.Condition); AnalyzeStmt(w.Body); break;
            case DoWhileStmt d: AnalyzeStmt(d.Body); AnalyzeExpr(d.Condition); break;
            case ForInStmt fo: AnalyzeExpr(fo.Iterable); AnalyzeStmt(fo.Body); break;
            case ReturnStmt r: if (r.Value is not null) AnalyzeExpr(r.Value); break;
            case BreakStmt { Value: { } broken }: AnalyzeExpr(broken); break;
            case YieldStmt y: if (y.Value is not null) AnalyzeExpr(y.Value); break;
            case DeferStmt de: AnalyzeStmt(de.Body); break; // runs in the scope that registered it
            case ThrowStmt t:
                AnalyzeExpr(t.Value);
                Throw(t.Value, t.Span);
                break;
            case TryStmt tr:
            {
                // The block form marks its whole body (05 E4: the block form of the same thing);
                // a clause is not covered by its own try (E9 C6), only by those around it.
                var frame = Open(tr.Catches, takesAll: false);
                try { AnalyzeStmt(tr.Body); }
                finally { Close(); }
                if (!frame.Reached)
                    _de.Report("LYR-SEM0139", Severity.Warning, KeywordOf(tr.Span),
                        "nothing in this 'try' block throws — its 'catch' clauses never run");
                foreach (var c in tr.Catches) AnalyzeStmt(c.Body);
                break;
            }
            case MatchStmt m:
                AnalyzeExpr(m.Scrutinee);
                foreach (var arm in m.Arms) AnalyzeArm(arm);
                break;
        }
    }

    private void AnalyzeArm(MatchArm arm)
    {
        if (arm.Guard is not null) AnalyzeExpr(arm.Guard);
        if (arm.Body is Block b) AnalyzeStmt(b);
        else if (arm.Body is Expr e) AnalyzeExpr(e);
    }

    // --- expressions: calls, pulls and desugared operators are the sites that need a mark ---

    private void AnalyzeExpr(Expr expr)
    {
        switch (expr)
        {
            case CallExpr call:
                AnalyzeCallee(call.Callee);
                foreach (var a in call.Arguments) AnalyzeExpr(a);

                // A pull — 'c.next()' — is the throw site of a coroutine, never the call that
                // built it (#73); the checker marked it, knowing the receiver's type.
                if (_types.ThrownByPull(call.Callee) is { } pulled)
                    Site([pulled], call.Span, "'next()'");
                else
                {
                    // 'Walker.walk(d)' stands for the member call it was checked as (04 D2 R5).
                    var thrown = _types.CallThrows(call);
                    if (thrown.Length == 0 && _types.OperatorCallOf(call) is { } meant) thrown = _types.CallThrows(meant);
                    Site(thrown, call.Span, $"the call to '{CalleeName(call.Callee)}'");
                }
                break;
            case IdentifierExpr or MemberExpr:
                if (expr is MemberExpr m) AnalyzeExpr(m.Target);
                break;
            case LambdaExpr lam:
                // Its own context: the body runs later, outside every try around it, and throws what
                // the lambda's type says (05 E2 K3).
                InContext(new Context(_types.TypeOf(lam) is FnType { Throws: var lambdaSet } ? lambdaSet : [], "the lambda", canDeclare: false), () =>
                {
                    if (lam.Body is Block b) AnalyzeStmt(b);
                    else if (lam.Body is Expr e) AnalyzeExpr(e);
                });
                break;
            case TryExpr tried:
            {
                // 'try?' and 'try!' take every error themselves; the expression form's clauses take
                // what they cover and are not inside their own try (E9 C6), as the block's are not.
                var frame = Open(tried.Catches, takesAll: tried.Kind != TryKind.Propagate);
                try { AnalyzeExpr(tried.Value); }
                finally { Close(); }
                if (!frame.Reached)
                    _de.Report("LYR-SEM0139", Severity.Warning, tried.KeywordSpan, tried.Catches.Length > 0
                        ? "nothing under this 'try' throws — its 'catch' clauses never run"
                        : $"nothing under this '{Spelled(tried.Kind)}' throws — the mark says a call may fail where none can");
                foreach (var c in tried.Catches) AnalyzeStmt(c.Body);
                break;
            }
            case UnaryExpr u: AnalyzeExpr(u.Operand); Operator(u); break;
            case ComptimeExpr ct: AnalyzeExpr(ct.Inner); break;
            case ResumeExpr re:
                AnalyzeExpr(re.Coroutine);
                if (_types.ThrownByPull(re) is { } resumed) Site([resumed], re.Span, "'resume'");
                break;
            case ThrowExpr te:
                AnalyzeExpr(te.Value);
                Throw(te.Value, te.Span);
                break;
            case PostfixExpr p: AnalyzeExpr(p.Operand); break;
            case BinaryExpr b: AnalyzeExpr(b.Left); AnalyzeExpr(b.Right); Operator(b); break;
            case AssignExpr a: AnalyzeExpr(a.Target); AnalyzeExpr(a.Value); Operator(a); break;
            case RangeExpr r: AnalyzeExpr(r.Low); AnalyzeExpr(r.High); break;
            case SliceRangeExpr sr: if (sr.Low is not null) AnalyzeExpr(sr.Low); if (sr.High is not null) AnalyzeExpr(sr.High); break;
            case CastExpr c: AnalyzeExpr(c.Operand); break;
            case TypeTestExpr tt: AnalyzeExpr(tt.Operand); break;
            case IndexExpr ix: AnalyzeExpr(ix.Target); AnalyzeExpr(ix.Index); Operator(ix); break;
            case ArrayLitExpr arr: foreach (var e in arr.Elements) AnalyzeExpr(e); break;
            case TupleLitExpr tu: foreach (var e in tu.Elements) AnalyzeExpr(e); break;
            case StructInitExpr si: foreach (var f in si.Fields) AnalyzeExpr(f.Value); break;
            case WithExpr w: AnalyzeExpr(w.Target); foreach (var f in w.Fields) AnalyzeExpr(f.Value); break;
            case InterpolatedStringExpr fs:
                foreach (var seg in fs.Segments) if (seg is InterpHole h) AnalyzeExpr(h.Expr);
                break;
            case LetCondExpr lc: AnalyzeExpr(lc.Initializer); break;
            case IfExpr iff:
                AnalyzeExpr(iff.Condition); AnalyzeExpr(iff.Then); AnalyzeExpr(iff.Else);
                break;
            case LoopExpr loop: AnalyzeStmt(loop.Body); break;
            case MatchExpr ma:
                AnalyzeExpr(ma.Scrutinee);
                foreach (var arm in ma.Arms) AnalyzeArm(arm);
                break;
        }
    }

    // Callee position: the function reference itself is legitimate; only its sub-expressions run.
    private void AnalyzeCallee(Expr callee)
    {
        if (callee is MemberExpr m) AnalyzeExpr(m.Target);
        else if (callee is not IdentifierExpr) AnalyzeExpr(callee);
    }

    /// <summary>An operator that IS a call — <c>a + b</c> through <c>Add</c>, <c>x[k]</c> through
    /// an index interface (04 D6) — throws what its method throws, and is marked like one.</summary>
    private void Operator(Expr node)
    {
        if (_types.OperatorCallOf(node) is { } call && _types.CallThrows(call) is { Length: > 0 } thrown)
            Site(thrown, node.Span, $"the operator's '{CalleeName(call.Callee)}'");
    }

    // --- sites ---

    private void Throw(Expr value, Span span)
    {
        // A binding that carries a set throws exactly the set (K7, precise rethrow): what reached
        // its clause, not the 'Error' it is typed as.
        if (value is IdentifierExpr id && _types.RefOf(id) is LocalSymbol { Declaration: CatchClause clause }
            && _types.CatchSet(clause) is { } set)
        {
            Site(set, span, "'throw'", needsMark: false);
            return;
        }
        var thrown = _types.TypeOf(value);
        if (thrown is null || thrown.IsError) return;
        // What is no Error was refused where it is thrown (SEM0030); covering it is no question.
        if (_root is not null && !_covers(thrown, _root, _module)) return;
        Site([thrown], span, "'throw'", needsMark: false);
    }

    /// <summary>A throw site: marked — unless it is a <c>throw</c>, which is its own mark — and every
    /// type it may throw covered by a clause around it or by the context's set.</summary>
    private void Site(LyrType[] thrown, Span at, string what, bool needsMark = true)
    {
        if (thrown.Length == 0 || thrown.Any(t => t.IsError)) return;
        if (needsMark && _context.Frames.Count == 0)
        {
            _de.Report("LYR-SEM0138", Severity.Error, at,
                $"{what} throws {SetText(thrown)} — mark it 'try', so the propagation is seen where it happens");
            return;
        }
        foreach (var t in thrown)
        {
            if (Taken(t) || _context.Declared.Any(d => _covers(t, d, _module))) continue;
            _de.Report("LYR-SEM0034", Severity.Error, at, _context.CanDeclare
                ? $"{what} throws '{TypeFacts.Display(t)}', which no 'catch' here and no 'throws' of "
                  + $"{_context.Name} covers — catch it, or add it to the 'throws'"
                : _context.Name == "the lambda"
                    ? $"{what} throws '{TypeFacts.Display(t)}', which no 'catch' in the lambda covers and its type "
                      + (_context.Declared.Length == 0 ? "does not throw" : $"throws only {SetText(_context.Declared)}")
                      + " — catch it inside, or give the lambda a type that throws it"
                    : $"{what} throws '{TypeFacts.Display(t)}', and {_context.Name} cannot throw — catch it there");
        }
    }

    /// <summary>
    /// Does an enclosing <c>try</c> take it — a <c>try?</c> or <c>try!</c>, a catch-all, or a clause
    /// whose type covers it? The error reaches the open <c>try</c>s from the innermost out, up to
    /// and including the one that takes it, and no further.
    /// </summary>
    private bool Taken(LyrType thrown)
    {
        for (var i = _context.Frames.Count - 1; i >= 0; i--)
        {
            var frame = _context.Frames[i];
            frame.Reached = true;
            if (frame.Kept is { } kept && !kept.Any(t => LyrType.Equal(t, thrown))) kept.Add(thrown);
            if (frame.TakesAll) return true;
            foreach (var clause in frame.Catches)
            {
                if (clause.TakesAll) return true;
                if (clause.BindingTypes.Length > 0)
                {
                    if (_types.CatchSet(clause) is not { } set || set.Any(s => _covers(thrown, s, _module))) return true;
                    continue;
                }
                if (_types.CatchType(clause) is not { } caught || _covers(thrown, caught, _module)) return true;
            }
        }
        return false;
    }

    private static string Spelled(TryKind kind) =>
        kind switch { TryKind.Optional => "try?", TryKind.Force => "try!", _ => "try" };

    private Frame Open(CatchClause[] catches, bool takesAll)
    {
        var frame = new Frame(catches, takesAll);
        _context.Frames.Add(frame);
        return frame;
    }

    private void Close() => _context.Frames.RemoveAt(_context.Frames.Count - 1);

    private static Span KeywordOf(Span statement) =>
        new(statement.File, statement.Start, Math.Min(statement.End, statement.Start + "try".Length));

    private static string SetText(LyrType[] set) =>
        set.Length == 1 ? $"'{TypeFacts.Display(set[0])}'" : $"[{string.Join(", ", set.Select(TypeFacts.Display))}]";

    private static string CalleeName(Expr callee) => callee switch
    {
        IdentifierExpr id => id.Name,
        MemberExpr m => m.Member,
        _ => "function"
    };
}
