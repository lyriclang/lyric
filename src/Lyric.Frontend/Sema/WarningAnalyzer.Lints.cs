using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>
/// The lints of the groups <c>unused</c> and <c>correctness</c> beyond the warnings the analyzer
/// had already (spec 16 §5). Each is deliberately narrow: it says what is wrong without fail, and
/// leaves out what it cannot see without a flow analysis it does not have — a lint that cries wolf
/// is turned off, and then it says nothing at all.
/// </summary>
internal sealed partial class WarningAnalyzer
{
    private readonly Dictionary<FileId, ModuleSymbol> _moduleOfFile = [];
    private readonly HashSet<IdentifierExpr> _writeOnly = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Param, Symbol> _paramSymbols = new(ReferenceEqualityComparer.Instance);

    private void RunLints()
    {
        foreach (var module in _comp.Modules)
            if (module.Path.Length > 0) _moduleOfFile[_comp.AstOf(module).Span.File] = module;
        foreach (var (node, symbol) in _types.AllReferences)
            if (node is Param p && ReferenceEquals(symbol.Declaration, node)) _paramSymbols[p] = symbol;

        foreach (var module in _comp.Modules)
        {
            if (_comp.Unwatched(module) || module.Path.Length == 0) continue;
            var ast = _comp.AstOf(module);
            var modules = ModuleNames(ast);
            foreach (var decl in ast.Declarations)
                foreach (var fn in FunctionsOf(decl)) LintFunction(fn, decl, modules);
        }
        WarnUnusedAssignments();
        WarnDeadCode();
        WarnForeignConformances();
    }

    /// <summary>A declaration's functions and methods, with bodies.</summary>
    private static IEnumerable<FunctionDecl> FunctionsOf(Decl decl) => decl switch
    {
        FunctionDecl f => [f],
        StructDecl s => s.Members.OfType<FunctionDecl>(),
        ClassDecl c => c.Members.OfType<FunctionDecl>(),
        EnumDecl e => e.Methods,
        InterfaceDecl i => i.Members,
        ExtendDecl x => x.Methods,
        _ => [],
    };

    /// <summary>The names whole-module imports bind in a file: <c>import std.io;</c> binds <c>io</c>.</summary>
    private static Dictionary<string, string> ModuleNames(Module ast)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var import in ast.Declarations.OfType<ImportDecl>())
            switch (import.Clause)
            {
                case null: names[import.Path[^1]] = string.Join('.', import.Path); break;
                case ImportAlias alias: names[alias.Alias] = string.Join('.', import.Path); break;
            }
        return names;
    }

    /// <summary>unused-generic: a function's type parameter that nothing in it names. A type's are not
    /// looked at: a type may carry a parameter only its name uses, a phantom (<c>Join&lt;A, B&gt;</c>).</summary>
    private void WarnUnusedGenerics(GenericParam[] generics, Node scope)
    {
        if (generics.Length == 0) return;
        var named = new HashSet<string>(StringComparer.Ordinal);
        void Collect(Node node)
        {
            if (node is NamedType { Path: [var single] }) named.Add(single);
            foreach (var child in AstChildren.Of(node))
                if (child is not GenericParam) Collect(child);
        }
        Collect(scope);
        // a parameter another's constraint names is used through it
        foreach (var g in generics) foreach (var c in g.Constraints) Collect(c);
        foreach (var g in generics)
            if (!named.Contains(g.Name) && !g.Name.StartsWith('_'))
                _de.Report("LYR-SEM0200", Severity.Warning, g.NameSpan,
                    $"the type parameter '{g.Name}' is never used",
                    new DiagnosticNote("take it out, or name it where the declaration needs it"));
    }

    // ─── a function's lints ───────────────────────────────────────────────

    private void LintFunction(FunctionDecl fn, Decl owner, Dictionary<string, string> modules)
    {
        if (fn.Body is null || fn.IsMacro) return;
        WarnUnusedGenerics(fn.Generics, fn);
        WarnUnusedParameters(fn, owner);
        WarnExitCodes(fn, owner);

        // the scopes for shadowed-local: the parameters, then each block
        var scopes = new List<Dictionary<string, Span>> { fn.Parameters.ToDictionary(p => p.Name, p => p.NameSpan, StringComparer.Ordinal) };
        foreach (var p in fn.Parameters) WarnModuleShadowing(p.Name, p.NameSpan, modules);
        Visit(fn.Body, scopes, modules, implicitIt: 0);
    }

    /// <summary>unused-parameter: a parameter the body never names. A method of a type that conforms
    /// to something — its signature is the interface's — is exempt, and so is a name that begins
    /// with <c>_</c>, the way to say it on purpose.</summary>
    private void WarnUnusedParameters(FunctionDecl fn, Decl owner)
    {
        var conforms = owner switch
        {
            StructDecl s => s.Interfaces.Length > 0,
            ClassDecl c => c.Interfaces.Length > 0,
            EnumDecl e => e.Interfaces.Length > 0,
            ExtendDecl x => x.Interfaces.Length > 0,
            InterfaceDecl => true,
            _ => false,
        };
        if (conforms) return;
        // a parameter another's attribute names — '@callerExpr(x)' — is used there
        var named = new HashSet<string>(StringComparer.Ordinal);
        void Names(Node node)
        {
            if (node is IdentifierExpr id) named.Add(id.Name);
            foreach (var child in AstChildren.Of(node)) Names(child);
        }
        foreach (var q in fn.Parameters) foreach (var attribute in q.Attributes) Names(attribute);
        foreach (var p in fn.Parameters)
        {
            if (p.Name.StartsWith('_') || named.Contains(p.Name)) continue;
            if (!_paramSymbols.TryGetValue(p, out var symbol) || _used.Contains(symbol)) continue;
            _de.Report("LYR-SEM0198", Severity.Warning, p.NameSpan, $"the parameter '{p.Name}' is never used",
                new DiagnosticNote($"name it '_{p.Name}' when it is deliberately unused"));
        }
    }

    /// <summary>exit-code-range: <c>main</c> returns an <c>int</c> literal the process cannot carry —
    /// an exit code is 0 to 255, the rest is taken modulo 256.</summary>
    private void WarnExitCodes(FunctionDecl fn, Decl owner)
    {
        if (fn.Name != "main" || owner is not FunctionDecl || fn.ReturnType is not NamedType { Path: ["int"] }) return;
        void Check(Expr? value)
        {
            long? constant = value switch
            {
                IntLiteralExpr lit => (long)lit.Value,
                UnaryExpr { Operator: UnaryOp.Neg, Operand: IntLiteralExpr neg } => -(long)neg.Value,
                _ => null,
            };
            if (constant is { } code && (code < 0 || code > 255))
                _de.Report("LYR-SEM0210", Severity.Warning, value!.Span,
                    $"an exit code is 0 to 255 — {code} leaves the process as {((code % 256) + 256) % 256}");
        }
        void Walk(Node node)
        {
            switch (node)
            {
                case ReturnStmt r: Check(r.Value); break;
                case LambdaExpr: return;
            }
            foreach (var child in AstChildren.Of(node)) Walk(child);
        }
        Walk(fn.Body!);
        if (fn.Body!.Statements is [.., TailExprStmt tail]) Check(tail.Expr);
    }

    /// <summary>module-shadowing: a local named as a module the file imports whole.</summary>
    private void WarnModuleShadowing(string name, Span at, Dictionary<string, string> modules)
    {
        if (modules.TryGetValue(name, out var module))
            _de.Report("LYR-SEM0207", Severity.Warning, at, $"'{name}' hides the module '{module}' imported as '{name}'");
    }

    /// <summary>The walk over a body for the lints that read expressions and scopes.</summary>
    private void Visit(Node node, List<Dictionary<string, Span>> scopes, Dictionary<string, string> modules, int implicitIt)
    {
        switch (node)
        {
            case Block block:
                scopes.Add(new Dictionary<string, Span>(StringComparer.Ordinal));
                foreach (var stmt in block.Statements) Visit(stmt, scopes, modules, implicitIt);
                scopes.RemoveAt(scopes.Count - 1);
                return;
            case BindingStmt b:
                if (b.Initializer is { } init) Visit(init, scopes, modules, implicitIt);
                Declare(b.Name, b.NameSpan, scopes, modules);
                return;
            case ForInStmt f:
                Visit(f.Iterable, scopes, modules, implicitIt);
                scopes.Add(new Dictionary<string, Span>(StringComparer.Ordinal));
                Declare(f.Variable, f.NameSpan, scopes, modules);
                Visit(f.Body, scopes, modules, implicitIt);
                scopes.RemoveAt(scopes.Count - 1);
                return;
            case LambdaExpr lam:
            {
                var isImplicit = lam is { Form: LambdaForm.Trailing, Parameters: [{ Name: "it" }] };
                if (isImplicit && implicitIt > 0 && Names(lam.Body, "it"))
                    _de.Report("LYR-SEM0209", Severity.Warning, new Span(lam.Span.File, lam.Span.Start, lam.Span.Start + 1),
                        "this block's 'it' hides the 'it' of the block around it",
                        new DiagnosticNote("name the parameter of one of them: '{ x => … }'"));
                scopes.Add(lam.Parameters.Where(p => p.Name != "it" || !isImplicit)
                    .ToDictionary(p => p.Name, p => p.Span, StringComparer.Ordinal));
                Visit(lam.Body, scopes, modules, isImplicit ? implicitIt + 1 : implicitIt);
                scopes.RemoveAt(scopes.Count - 1);
                return;
            }
            case IfStmt i: WarnAssignmentCondition(i.Condition); break;
            case WhileStmt w: WarnAssignmentCondition(w.Condition); break;
            case DoWhileStmt d: WarnAssignmentCondition(d.Condition); break;
            case IfExpr ie: WarnAssignmentCondition(ie.Condition); break;
            case AssignExpr a:
                if (a.Operator is null && a.Target is IdentifierExpr written) _writeOnly.Add(written);
                if (a.Operator is null && (Same(a.Target, a.Value)
                        || a.Value is PostfixExpr { Operator: PostfixOp.Inc or PostfixOp.Dec } post && Same(a.Target, post.Operand)))
                    _de.Report("LYR-SEM0203", Severity.Warning, a.Span, "this assigns a value to itself — nothing changes",
                        a.Value is PostfixExpr bumped
                            ? DiagnosticNote.Suggest("'x = x++' keeps the old value: write 'x++' alone",
                                new Replacement(a.Span, _comp.Sources.GetText(a.Span.File)[bumped.Span.Start..bumped.Span.End], Applicability.Safe))
                            : DiagnosticNote.Suggest("assign what was meant, or take the statement out",
                                SourceEdits.DeleteStatement(_comp.Sources, a.Span, Applicability.NeedsReview)));
                // the target is written, not read: the walk goes on in the value alone
                if (a.Operator is null && a.Target is IdentifierExpr)
                {
                    Visit(a.Value, scopes, modules, implicitIt);
                    return;
                }
                break;
            case BinaryExpr bin:
                WarnComparison(bin);
                break;
            case IntLiteralExpr lit when lit.Suffix is null
                                         && _types.TypeOf(lit) is PrimitiveType { Kind: not PrimitiveKind.Int } adapted
                                         && TypeFacts.IsInteger(adapted):
                _de.Report("LYR-SEM0208", Severity.Warning, lit.Span, $"this literal is an '{TypeFacts.Display(adapted)}' here, from its context");
                break;
        }
        foreach (var child in AstChildren.Of(node)) Visit(child, scopes, modules, implicitIt);
    }

    private static bool Names(Node node, string name) =>
        node is IdentifierExpr { Name: var n } && n == name || AstChildren.Of(node).Any(c => c is not LambdaExpr && Names(c, name));

    /// <summary>shadowed-local and module-shadowing at a binding.</summary>
    private void Declare(string name, Span at, List<Dictionary<string, Span>> scopes, Dictionary<string, string> modules)
    {
        if (name == "_") return;
        for (var i = scopes.Count - 2; i >= 0; i--)
            if (scopes[i].TryGetValue(name, out var outer))
            {
                _de.Report("LYR-SEM0206", Severity.Warning, at, $"'{name}' hides the local of the same name around it",
                    new DiagnosticNote(outer, "the local it hides"));
                break;
            }
        WarnModuleShadowing(name, at, modules);
        scopes[^1][name] = at;
    }

    /// <summary>assignment-in-condition: <c>if (x = y)</c>, where <c>==</c> was meant; a second pair
    /// of parentheses, <c>if ((x = y))</c>, says it is meant.</summary>
    private void WarnAssignmentCondition(Expr condition)
    {
        if (condition is not AssignExpr { Operator: null } assign) return;
        var text = _comp.Sources.GetText(assign.Span.File);
        var before = assign.Span.Start - 1;
        while (before >= 0 && char.IsWhiteSpace(text[before])) before--;
        if (before >= 0 && text[before] == '(')
        {
            var outer = before - 1;
            while (outer >= 0 && char.IsWhiteSpace(text[outer])) outer--;
            if (outer >= 0 && text[outer] == '(') return;
        }
        _de.Report("LYR-SEM0202", Severity.Warning, assign.Span, "this condition assigns — was '==' meant?",
            new DiagnosticNote("write '==' to compare, or '((…))' to assign on purpose"));
    }

    /// <summary>comparison-always and float-equality.</summary>
    private void WarnComparison(BinaryExpr bin)
    {
        var left = _types.TypeOf(bin.Left);
        var right = _types.TypeOf(bin.Right);
        if (bin.Operator is BinaryOp.Eq or BinaryOp.Ne && TypeFacts.IsFloat(left) && TypeFacts.IsFloat(right))
            _de.Report("LYR-SEM0205", Severity.Warning, bin.Span,
                $"'{(bin.Operator == BinaryOp.Eq ? "==" : "!=")}' on floats compares the bits a computation happened to round to",
                new DiagnosticNote("compare the difference with a tolerance"));

        static bool Unsigned(LyrType t) => t is PrimitiveType { Kind: PrimitiveKind.Uint or PrimitiveKind.Uint8 or PrimitiveKind.Uint16 or PrimitiveKind.Uint32 };
        static bool Zero(Expr e) => e is IntLiteralExpr { Value: 0 };
        bool? always = bin.Operator switch
        {
            BinaryOp.Ge when Unsigned(left) && Zero(bin.Right) => true,
            BinaryOp.Lt when Unsigned(left) && Zero(bin.Right) => false,
            BinaryOp.Le when Zero(bin.Left) && Unsigned(right) => true,
            BinaryOp.Gt when Zero(bin.Left) && Unsigned(right) => false,
            _ => null,
        };
        if (always is { } answer)
            _de.Report("LYR-SEM0204", Severity.Warning, bin.Span,
                $"this comparison is always {(answer ? "true" : "false")}: an unsigned value is never below 0");
    }

    /// <summary>Whether two expressions name the same place: one local, or the same member chain on it.</summary>
    private bool Same(Expr a, Expr b) => (a, b) switch
    {
        (IdentifierExpr x, IdentifierExpr y) => x.Name == y.Name && ReferenceEquals(_types.RefOf(x), _types.RefOf(y)) && _types.RefOf(x) is not null,
        (MemberExpr x, MemberExpr y) => x.Member == y.Member && Same(x.Target, y.Target),
        _ => false,
    };

    // ─── across the compilation ───────────────────────────────────────────

    /// <summary>unused-assignment: a <c>var</c> written and never read — every use of it is the
    /// target of an <c>=</c>. What a store overwrites before a read is a flow question this pass
    /// does not ask.</summary>
    private void WarnUnusedAssignments()
    {
        var uses = new Dictionary<Symbol, List<Node>>(ReferenceEqualityComparer.Instance);
        foreach (var (node, symbol) in _types.AllReferences)
        {
            if (symbol is not LocalSymbol { IsMutable: true } || ReferenceEquals(symbol.Declaration, node)) continue;
            if (!uses.TryGetValue(symbol, out var list)) uses[symbol] = list = [];
            list.Add(node);
        }
        foreach (var (symbol, list) in uses)
        {
            if (symbol.Declaration is not BindingStmt binding || binding.Name == "_" || _nativeFiles.Contains(binding.Span.File)) continue;
            if (!_moduleOfFile.ContainsKey(binding.Span.File)) continue;
            if (list.All(n => n is IdentifierExpr id && _writeOnly.Contains(id)))
                _de.Report("LYR-SEM0199", Severity.Warning, binding.NameSpan,
                    $"'{binding.Name}' is assigned, and no value it holds is ever read",
                    new DiagnosticNote("take out the assignments, or read what they store"));
        }
    }

    /// <summary>dead-code: a declaration of a module that is not <c>pub</c>, carries no attribute, and
    /// nothing in the compilation names — <c>main</c> aside.</summary>
    private void WarnDeadCode()
    {
        var reached = new HashSet<Symbol>(_used, ReferenceEqualityComparer.Instance);
        foreach (var symbol in _used)
            for (var s = symbol; s is ImportBindingSymbol shell; s = shell.Target)
                reached.Add(shell.Target);
        foreach (var module in _comp.Modules)
        {
            // std's declarations the compiler and the runtime call by name have no use it can see
            if (_comp.Unwatched(module) || _comp.IsNative(module) || module.Path.Length == 0) continue;
            var ast = _comp.AstOf(module);
            if (_comp.Sources.OriginOf(ast.Span.File) is not null) continue;
            var overloaded = ast.Declarations.OfType<FunctionDecl>().GroupBy(f => f.Name)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var decl in ast.Declarations)
            {
                var (visibility, attributes) = decl switch
                {
                    FunctionDecl f when !f.IsMacro => (f.Visibility, f.Attributes),
                    StructDecl s => (s.Visibility, s.Attributes),
                    ClassDecl c => (c.Visibility, c.Attributes),
                    EnumDecl e => (e.Visibility, e.Attributes),
                    InterfaceDecl i => (i.Visibility, i.Attributes),
                    TypeAliasDecl t => (t.Visibility, t.Attributes),
                    _ => (VisibilityWord.Pub, []),
                };
                if (visibility == VisibilityWord.Pub || attributes.Length > 0 || decl is not INamedDecl named) continue;
                if (named.Name is "main" || named.Name.StartsWith('_') || overloaded.Contains(named.Name)) continue;
                if (module.Members.LookupLocal(named.Name) is not { } symbol || reached.Contains(symbol)) continue;
                _de.Report("LYR-SEM0201", Severity.Warning, named.NameSpan,
                    $"'{named.Name}' is never used",
                    new DiagnosticNote("take it out, or make it 'pub' where another package uses it"));
            }
        }
    }

    /// <summary>foreign-conformance: a conformance block for another package's type to another
    /// package's interface — two such blocks in two packages would both claim it.</summary>
    private void WarnForeignConformances()
    {
        string? PackageOf(Symbol? symbol)
        {
            // an imported name is the declaration it names, not the import that brings it
            while (symbol is ImportBindingSymbol shell) symbol = shell.Target;
            return symbol?.Declaration is { } declared && _moduleOfFile.TryGetValue(declared.Span.File, out var module)
                ? module.Path[0]
                : symbol is null ? null : "std";
        }
        foreach (var module in _comp.Modules)
        {
            if (_comp.Unwatched(module) || module.Path.Length == 0) continue;
            var own = module.Path[0];
            foreach (var extend in _comp.AstOf(module).Declarations.OfType<ExtendDecl>())
            {
                var target = PackageOf(_binding.Resolve(extend.Target) ?? _types.RefOf(extend.Target));
                if (target is null || target == own) continue;
                foreach (var iface in extend.Interfaces)
                    if (PackageOf(_binding.Resolve(iface) ?? _types.RefOf(iface)) is { } theirs && theirs != own)
                        _de.Report("LYR-SEM0211", Severity.Warning, iface.Span,
                            "this conformance is of another package's type to another package's interface",
                            new DiagnosticNote("a second package with the same block would claim it as well: conform in the type's or the interface's package"));
            }
        }
    }
}
