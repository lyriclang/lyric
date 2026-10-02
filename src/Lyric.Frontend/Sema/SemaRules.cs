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
            {
                CheckDecl(decl);
                CheckNeverPositions(decl, null);
            }
        CheckMain();
        if (_comp.Lyric5Modules) CheckSurfaces();
    }

    // --- the surface rule ---

    /// <summary>
    /// A declaration is no more visible than the types it names (design/v5/spec/07 V2 S2, in
    /// Swift's general form): a <c>pub fn</c> whose result is an <c>internal</c> type hands out what
    /// no other package can name, an <c>internal</c> one taking a <c>private</c> type does the same
    /// inside its package (<c>LYR-SEM0151</c>). Read are the signatures — parameters, result,
    /// thrown types, constraints —, a field's type, an alias's, a variant's payload, a binding's
    /// written or inferred type, a type's constraints and an interface's parents. A member counts
    /// as its own word narrowed to its type's. A conformance's methods answer to their interface,
    /// whose own declaration is checked.
    /// </summary>
    private void CheckSurfaces()
    {
        foreach (var module in _comp.Modules)
        {
            _surfaceModule = module;
            foreach (var decl in _comp.AstOf(module).Declarations)
                switch (decl)
                {
                    case FunctionDecl fn when module.Members.FunctionFor(fn.Name, fn) is { } f:
                        Signature(fn, f.Visibility);
                        break;
                    case GlobalBindingDecl g when module.Members.LookupLocal(g.Name) is GlobalSymbol gs:
                        BindingType(g.Binding, gs, gs.Visibility);
                        break;
                    case TypeAliasDecl a when module.Members.LookupLocal(a.Name) is TypeSymbol alias:
                        Names(a.Aliased, alias.Visibility, $"'{a.Name}'");
                        break;
                    case StructDecl s when module.Members.LookupLocal(s.Name) is TypeSymbol st:
                        TypeSurface(st, s.Generics, [], s.Members);
                        break;
                    case ClassDecl c when module.Members.LookupLocal(c.Name) is TypeSymbol ct:
                        TypeSurface(ct, c.Generics, [], c.Members);
                        break;
                    case EnumDecl e when module.Members.LookupLocal(e.Name) is TypeSymbol et:
                        TypeSurface(et, e.Generics, [], e.Methods);
                        foreach (var v in e.Variants)
                        {
                            foreach (var t in v.TupleFields ?? []) Names(t, et.Visibility, $"'{e.Name}.{v.Name}'");
                            foreach (var f in v.StructFields ?? []) Names(f.Type, et.Visibility, $"'{e.Name}.{v.Name}'");
                        }
                        break;
                    case InterfaceDecl i when module.Members.LookupLocal(i.Name) is TypeSymbol it:
                        TypeSurface(it, i.Generics, i.Interfaces, i.Members.Where(m => !m.IsPrivateHelper).Cast<Decl>().Concat(i.Statics));
                        break;
                    // An inherent block's methods, narrowed to their target; a conformance's are
                    // its interface's.
                    case ExtendDecl { Interfaces.Length: 0 } x
                        when _comp.Extensions.Blocks.FirstOrDefault(b => ReferenceEquals(b.Decl, x)) is { } block:
                        foreach (var m in block.Methods)
                            if (m.Declaration is FunctionDecl md)
                                Signature(md, Narrow(m.Visibility, block.Target?.Visibility ?? Visibility.Public));
                        foreach (var sb in x.Statics)
                            if (block.MethodScope.LookupLocal(sb.Name) is GlobalSymbol gs)
                                BindingType(sb.Binding, gs, Narrow(gs.Visibility, block.Target?.Visibility ?? Visibility.Public), block.Target?.Name);
                        break;
                }
        }
    }

    /// <summary>The module whose declarations the surface rule reads.</summary>
    private ModuleSymbol? _surfaceModule;

    /// <summary>Whether a type in a declaration's surface is narrower than the declaration — one
    /// the declaring module may name at all; a hidden one was refused where it is named.</summary>
    private bool Narrower(TypeSymbol type, Visibility level) =>
        type.Home is not null && type.Visibility < level
        && (_surfaceModule is null || _comp.Visible(type, _surfaceModule));

    /// <summary>A type's own surface: its constraints and, for an interface, its parents at the
    /// type's word; its members at their own, narrowed to it.</summary>
    private void TypeSurface(TypeSymbol type, GenericParam[] generics, TypeNode[] parents, IEnumerable<Decl> members)
    {
        foreach (var g in generics)
            foreach (var c in g.Constraints) Names(c, type.Visibility, $"'{type.Name}'");
        foreach (var p in parents) Names(p, type.Visibility, $"'{type.Name}'");
        foreach (var member in members)
            switch (member)
            {
                case FieldDecl f when type.Members.LookupLocal(f.Name) is FieldSymbol fs:
                    Names(f.Type, Narrow(fs.Visibility, type.Visibility), $"'{type.Name}.{f.Name}'");
                    break;
                case FunctionDecl fn when type.Members.FunctionFor(fn.Name, fn) is { } f:
                    Signature(fn, Narrow(f.Visibility, type.Visibility), type.Name);
                    break;
                case StaticBindingDecl sb when type.Members.LookupLocal(sb.Name) is GlobalSymbol gs:
                    BindingType(sb.Binding, gs, Narrow(gs.Visibility, type.Visibility), type.Name);
                    break;
            }
    }

    private void Signature(FunctionDecl fn, Visibility level, string? owner = null)
    {
        var what = owner is null ? $"'{fn.Name}'" : $"'{owner}.{fn.Name}'";
        foreach (var p in fn.Parameters) Names(p.Type, level, what);
        if (fn.ReturnType is { } ret) Names(ret, level, what);
        foreach (var thrown in fn.Throws?.Types ?? []) Names(thrown, level, what);
        foreach (var g in fn.Generics)
            foreach (var c in g.Constraints) Names(c, level, what);
    }

    /// <summary>A binding's type as written, or as the checker inferred it where none is.</summary>
    private void BindingType(BindingStmt binding, GlobalSymbol symbol, Visibility level, string? owner = null)
    {
        var what = owner is null ? $"'{binding.Name}'" : $"'{owner}.{binding.Name}'";
        if (binding.Type is { } written)
        {
            Names(written, level, what);
            return;
        }
        if (Narrower(_types.TypeOfGlobal(symbol), level) is { } hidden)
            Refuse(binding.NameSpan, what, level, hidden);
    }

    /// <summary>Every type a written type names, at the level of the declaration it stands in.</summary>
    private void Names(TypeNode node, Visibility level, string what)
    {
        switch (node)
        {
            case NamedType n:
                var symbol = _binding.Resolve(n);
                if (symbol is ImportBindingSymbol { Target: var target }) symbol = target;
                if (symbol is TypeSymbol type && Narrower(type, level))
                    Refuse(n.Span, what, level, type);
                foreach (var a in n.TypeArguments) Names(a, level, what);
                break;
            case NullableType o: Names(o.Inner, level, what); break;
            case ThrowingType t:
                Names(t.Inner, level, what);
                if (t.Thrown is { } written) Names(written, level, what);
                break;
            case ArrayType a: Names(a.Element, level, what); break;
            case TupleType t: foreach (var e in t.Elements) Names(e, level, what); break;
            case FunctionType f:
                foreach (var p in f.Parameters) Names(p, level, what);
                Names(f.ReturnType, level, what);
                foreach (var set in f.Throws?.Types ?? []) Names(set, level, what);
                break;
        }
    }

    /// <summary>The first type an inferred type names that is less visible than <paramref name="level"/>.</summary>
    private TypeSymbol? Narrower(LyrType type, Visibility level) => type switch
    {
        NamedRef { Symbol: var s } when Narrower(s, level) => s,
        OpaqueRef { Symbol: var s } when Narrower(s, level) => s,
        GenericInstance g => Narrower(g.Definition, level)
            ? g.Definition : g.Arguments.Select(a => Narrower(a, level)).FirstOrDefault(s => s is not null),
        Optional o => Narrower(o.Inner, level),
        ArrayOf a => Narrower(a.Element, level),
        SliceOf a => Narrower(a.Element, level),
        InlineArrayOf a => Narrower(a.Element, level),
        TupleOf t => t.Elements.Select(e => Narrower(e, level)).FirstOrDefault(s => s is not null),
        FnType f => f.Parameters.Append(f.Return).Concat(f.Throws).Select(e => Narrower(e, level))
            .FirstOrDefault(s => s is not null),
        CoroutineOf c => Narrower(c.Yield, level),
        _ => null,
    };

    private void Refuse(Span at, string what, Visibility level, TypeSymbol type) =>
        _de.Report("LYR-SEM0151", Severity.Error, at,
            $"{what} is {Word(level)}, but it names '{type.Name}', which is {Word(type.Visibility)} — "
            + "a declaration is no more visible than the types it names (07 V2 S2)");

    private static Visibility Narrow(Visibility own, Visibility owner) => own < owner ? own : owner;

    private static string Word(Visibility v) => v switch
    {
        Visibility.Public => "pub",
        Visibility.Internal => "internal",
        _ => "private",
    };

    /// <summary>
    /// <c>never</c> stands only as a return type (design/v5/spec/05 E12): the whole return type of a
    /// function, a lambda or a function type. Anywhere else — a parameter, a binding, a field, an
    /// element, an optional, a type argument, a thrown set, an alias — it would type a value that
    /// cannot exist (<c>LYR-SEM0145</c>); three of those positions crashed the compiler before.
    /// </summary>
    private void CheckNeverPositions(Node node, Node? parent)
    {
        if (node is NamedType { Path: ["never"], TypeArguments.Length: 0 } never && !IsReturnType(never, parent))
            _de.Report("LYR-SEM0145", Severity.Error, never.Span,
                "'never' stands only as a return type — no value of it can exist");
        foreach (var child in AstChildren.Of(node)) CheckNeverPositions(child, node);
    }

    private static bool IsReturnType(TypeNode type, Node? parent) => parent switch
    {
        FunctionDecl f => ReferenceEquals(f.ReturnType, type),
        LambdaExpr l => ReferenceEquals(l.ReturnType, type),
        FunctionType t => ReferenceEquals(t.ReturnType, type),
        _ => false,
    };

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

        // A default may stand at any position (design/v5/spec/04 D5 F3): a call names what follows
        // it. Lyric 4's LYR-SEM0025, "required parameter after a default parameter", is gone.
    }

    // --- the main contract ---

    private void CheckMain()
    {
        // Lyric 5: the entry module's 'main' alone has a contract (07 M7a); a library checked as a
        // workspace has no entry, and its 'main's are ordinary functions.
        if (_comp.Lyric5Modules)
        {
            if (_comp.Entry is { } entry) CheckEntry(entry);
            return;
        }

        var mains = _comp.Modules
            .SelectMany(m => _comp.AstOf(m).Declarations.OfType<FunctionDecl>())
            .Where(f => f.Name == "main")
            .ToList();

        foreach (var main in mains)
            if (!ValidMain(main))
                _de.Report("LYR-SEM0021", Severity.Error, main.Span,
                    "'main' must be 'fn main(): int' or 'fn main(): void', with or without 'args: string[]' "
                    + "— and may declare 'throws'");

        // One 'main' per EXECUTABLE. A workspace compilation holds several programs side by side,
        // and there a second 'main' is the entry point of another script, not a duplicate.
        if (!_singleProgram) return;

        for (var i = 1; i < mains.Count; i++)
            _de.Report("LYR-SEM0021", Severity.Error, mains[i].Span, "duplicate 'main' function");
    }

    /// <summary>
    /// Lyric 5's entry (design/v5/spec/07 M7a, M7b; 08 D18): the program starts at the <c>main</c> of
    /// its entry module, which is <c>fn main(): void</c> or <c>fn main(): int</c>, may declare
    /// <c>throws</c>, and takes no parameters — the arguments come from the standard library. A
    /// <c>main</c> in any other module is an ordinary function: a module may be a library and a
    /// program at once, and only the module a build starts from makes its <c>main</c> the root. The
    /// 4.x rule, one <c>main</c> per compilation, was the VM's (one entry per <c>.lyrbc</c>).
    /// </summary>
    private void CheckEntry(ModuleSymbol entry)
    {
        var ast = _comp.AstOf(entry);
        var mains = ast.Declarations.OfType<FunctionDecl>().Where(f => f.Name == "main").ToList();
        if (mains.Count == 0)
        {
            _de.Report("LYR-SEM0021", Severity.Error, new Span(ast.Span.File, 0, 0),
                $"module '{entry.FullName}' has no 'main' to start the program from (07 M7a)");
            return;
        }

        foreach (var main in mains)
            if (main.Parameters.Length > 0 || !ValidMain(main))
                _de.Report("LYR-SEM0021", Severity.Error, main.Span,
                    "'main' is 'fn main(): void' or 'fn main(): int', and may declare 'throws'; it takes no "
                    + "parameters — the arguments come from the standard library (07 M7b)");

        for (var i = 1; i < mains.Count; i++)
            _de.Report("LYR-SEM0021", Severity.Error, mains[i].Span, "duplicate 'main' function");
    }

    /// <summary>The entry contract (design/v5/spec/08 D18): <c>fn main(): void | int</c>, with or
    /// without <c>args: string[]</c>, and it may declare <c>throws</c> — an error that escapes is
    /// reported with its message and causes, and the process exits with 1 (05 E6 O4).</summary>
    private static bool ValidMain(FunctionDecl fn)
    {
        if (fn.ReturnType is not null && !IsNamed(fn.ReturnType, "int") && !IsNamed(fn.ReturnType, "void")) return false;
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
            case BindingStmt bd:
                if (bd.Initializer is not null) WalkExpr(bd.Initializer);
                if (bd.Cleanup is not null) WalkStmt(bd.Cleanup);
                break;
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
            case BreakStmt br:
                if (br.Label is { } breakLabel) ResolveLabel(breakLabel, br.LabelSpan, "break");
                if (br.Value is not null) WalkExpr(br.Value);
                break;
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
        CheckClauses(tr.Catches);
    }

    /// <summary>The rules of a clause list, the block's or the expression's (05 E9).</summary>
    private void CheckClauses(CatchClause[] catches)
    {
        for (var i = 0; i < catches.Length - 1; i++)
            if (catches[i].TakesAll)
                _de.Report("LYR-SEM0035", Severity.Error, catches[i].Span,
                    "catch-all must be the last catch clause");
    }

    private void CheckExprStmt(ExprStmt es)
    {
        // 'try f();' is the call it marks (05 E4).
        var ok = TypeChecker.Unmarked(es.Expr) is CallExpr or AssignExpr or ThrowExpr or LoopExpr
            or PostfixExpr { Operator: PostfixOp.Inc or PostfixOp.Dec }
            or UnaryExpr { Operator: UnaryOp.PreInc or UnaryOp.PreDec } or ErrorExpr;
        if (!ok)
            _de.Report("LYR-SEM0022", Severity.Error, es.Span, "expression statement has no effect (only calls and assignments are allowed)");
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

            // '&x' hands the call a place it may write (03 §2.3a): the rule of a write. A mark the
            // checker refused — no place parameter takes it — has the error type and one message.
            case UnaryExpr { Operator: UnaryOp.Place } place:
                if (!_types.TypeOf(place).IsError && WhyNotAPlaceArgument(place.Operand) is { } why)
                    _de.Report("LYR-SEM0156", Severity.Error, place.Operand.Span,
                        $"'&' hands the call a place it may write, and this is none — {why}");
                WalkExpr(place.Operand);
                return;

            // A lambda BODY is a body: the same rules hold inside it. Reached only through
            // 'Children', it was never walked at all, and an assignment to a captured 'let' went
            // to the lowering, which has no diagnostic for it and threw.
            case LambdaExpr lambda:
            {
                // Its own loops only (08 Y11 F5): a label around the lambda is out of its reach.
                var outer = _labels.ToList();
                _labels.Clear();
                if (lambda.Body is Block block) WalkStmt(block);
                else if (lambda.Body is Expr body) WalkExpr(body);
                _labels.Clear();
                _labels.AddRange(outer);
                return;
            }

            case LoopExpr loop:
                WalkLoopBody(loop.Body, loop.Label, loop.LabelSpan);
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

            // The clauses of a 'try' expression (05 E4): their bodies are blocks, which 'Children'
            // cannot carry either, and the clause rules are the statement's.
            case TryExpr { Catches.Length: > 0 } tried:
                CheckClauses(tried.Catches);
                WalkExpr(tried.Value);
                foreach (var clause in tried.Catches) WalkStmt(clause.Body);
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
                var referenced = _types.RefOf(id);
                if (referenced is ImportBindingSymbol { Target: var importedGlobal }) referenced = importedGlobal;
                return referenced switch
                {
                    LocalSymbol { IsMutable: true } => null,
                    // '&n: int' (03 §2.3a): the name is the caller's place, written through.
                    ParameterSymbol { IsPlace: true } => null,
                    ParameterSymbol => $"'{id.Name}' is a parameter, and a parameter is a 'let' binding; copy it into a 'var' to change it",
                    LocalSymbol => $"'{id.Name}' is bound with 'let'; declare it 'var' to write it",
                    // A module-level 'var' is written (07 V5 G5); a module-level 'let' is not.
                    GlobalSymbol { Declaration: GlobalBindingDecl { Binding.IsMutable: true } } => null,
                    GlobalSymbol { Declaration: GlobalBindingDecl } => $"'{id.Name}' is a module-level 'let'; declare it 'var' to write it",
                    GlobalSymbol => $"'{id.Name}' is a 'static let', a constant",
                    _ => $"'{id.Name}' is not a variable",
                };

            case ThisExpr:
                // 'this = value' replaces a VALUE as a whole, a struct or an enum. Anything else
                // is the reference (or the scalar copy) the caller handed in, and is not rebound.
                if (TypeFacts.KindOf(_types.TypeOf(expr)) is not (TypeSymbolKind.Struct or TypeSymbolKind.Enum))
                    return "'this' is replaced as a whole only in a 'mut fn' of a struct or an enum";
                return _thisMut ? null : NotMutReason;

            // '?.' reads; it does not assign (design/v5/spec/03 T4 O4). What a write through a
            // chain that may be absent would mean — skip it, or fail — is a door, not a rule.
            case MemberExpr { IsOptional: true }:
                return "'?.' reads a value that may be absent and does not assign through it; narrow first";

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

            // An element of an inline array is written where the array lies (A4): through a
            // 'var' local, a 'var' field of a place, an element of an array — as a struct field is.
            case IndexExpr ix when _types.TypeOf(ix.Target) is InlineArrayOf:
                return WhyNotAPlace(ix.Target);

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
    /// Whether an argument names a place a call may write through (03 §2.3a), as the reason it
    /// does not: the rule of a write, narrower in one point. An element is a place in an array, a
    /// view or an inline array; a container's element is what its <c>get</c> handed out, with no
    /// place of its own to hand over.
    /// </summary>
    private string? WhyNotAPlaceArgument(Expr expr) =>
        expr is IndexExpr ix && _types.TypeOf(ix.Target) is not (ArrayOf or SliceOf or InlineArrayOf or ErrorType)
            ? "an element of a container is a copy its 'get' hands out, with no place of its own"
            : WhyNotWritable(expr);

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
            case IndexExpr ix when _types.TypeOf(ix.Target) is ArrayOf or SliceOf or ErrorType:
                return null; // an array element is a place in the array's block, through a view too (A2)
            // An element of an inline array lies in the value that holds it (A4): a place when
            // that value is one, like a field of a struct.
            case IndexExpr ix when _types.TypeOf(ix.Target) is InlineArrayOf:
                return WhyNotAPlace(ix.Target);
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
        if (type is ArrayOf or SliceOf or ErrorType) return true;
        if (_types.Indexable is not { } indexable) return false;

        return TypeFacts.SymbolOf(type) is { } symbol
               && Conformance.Implements(symbol, indexable, _binding);
    }

    // The direct child expressions, used to spot nested assignments.
    private static IEnumerable<Expr> Children(Expr e) => e switch
    {
        UnaryExpr u => [u.Operand],
        PostfixExpr p => [p.Operand],
        ComptimeExpr ct => [ct.Inner],
        ThrowExpr te => [te.Value],
        TryExpr tr => [tr.Value],
        BinaryExpr b => [b.Left, b.Right],
        RangeExpr r => [r.Low, r.High],
        SliceRangeExpr sr => [.. new[] { sr.Low, sr.High }.OfType<Expr>()],
        CastExpr c => [c.Operand],
        CallExpr call => [call.Callee, .. call.Arguments],
        IndexExpr ix => [ix.Target, ix.Index],
        MemberExpr m => [m.Target],
        ArrayLitExpr arr => arr.Elements,
        TupleLitExpr tu => tu.Elements,
        StructInitExpr si => si.Fields.Select(f => f.Value),
        WithExpr w => [w.Target, .. w.Fields.Select(f => f.Value)],
        InterpolatedStringExpr fs => fs.Segments.OfType<InterpHole>().Select(h => h.Expr),
        IfExpr iff => [iff.Condition, iff.Then, iff.Else],
        // MatchExpr and LambdaExpr are handled in WalkExpr: their block arms and block bodies are
        // STATEMENTS, which this list cannot carry.
        AssignExpr a => [a.Target, a.Value],
        _ => []
    };
}
