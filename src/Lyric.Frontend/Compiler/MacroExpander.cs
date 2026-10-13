using Lyric.AST;
using Lyric.Core;
using Lyric.Ir;
using Lyric.Ir.Interp;
using Lyric.Ir.Lowering;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Compiler;

/// <summary>
/// The expansion of macros (design/v5/spec/13 M9b P3, P4, P6, P7; 08 Y12): every site
/// (<see cref="MacroCallExpr"/>) of every module is replaced by what its macro gives back, before
/// the module is checked. A round: the compilation is analyzed once more with the sites as holes
/// (its findings buffered; those in a macro's module are the round's, the rest the final check's),
/// the macros of the round are lowered as the roots of a hoisting pass, the interpreter runs each
/// site's macro with the site's trees as values, the results go through the bridge into the
/// modules' trees, and a fresh compilation is resolved over them. A site whose macro's module has
/// sites of its own waits for a later round (the modules in import order, P6); a site inside
/// another's argument comes back inside the result and is expanded in the round after (outer
/// before inner). A program without a site never comes here.
/// </summary>
public static class MacroExpander
{
    /// <summary>Rounds before a result holding sites again is called a loop (P6, the depth
    /// limit): every round is a full analysis, so the bound is small.</summary>
    public const int MaxRounds = 32;

    /// <summary>A site: its module, the call, and the statement it is where it is one
    /// (<c>name!(...);</c> alone on its line), which a result of statements replaces.</summary>
    public sealed record Site(ModuleSymbol Module, MacroCallExpr Call, ExprStmt? Statement);

    /// <summary>The sites of the compiled modules (not the standard library's, which writes
    /// none), outermost only: an inner one travels with its argument.</summary>
    public static List<Site> Sites(Compilation compilation)
    {
        var found = new List<Site>();
        foreach (var module in compilation.Modules)
            if (!compilation.IsNative(module)) Walk(compilation.AstOf(module), null, module, found);
        return found;
    }

    private static void Walk(Node node, Node? parent, ModuleSymbol module, List<Site> found)
    {
        if (node is MacroCallExpr call)
        {
            found.Add(new Site(module, call, parent as ExprStmt));
            return;
        }
        foreach (var child in AstChildren.Of(node)) Walk(child, node, module, found);
    }

    /// <summary>Expands every site. <paramref name="rebuild"/> makes a compilation whose modules
    /// are the given trees (by module name) and resolves it. The compilation and binding the
    /// check goes on with, or <c>null</c> when a site was reported.</summary>
    public static (Compilation Compilation, BindingResult Binding)? Expand(Compilation compilation, BindingResult binding,
        DiagnosticEngine diagnostics, SourceManager sources, CompilerOptions options, EmbedReader? embed,
        Func<IReadOnlyDictionary<string, Module>, (Compilation, BindingResult)> rebuild)
    {
        var trees = new Dictionary<string, Module>(StringComparer.Ordinal);
        // the macros each file used, by name: an import a site expanded is a used import, which
        // the final analysis cannot see in the expanded tree
        var uses = new List<(string Module, FileId File, string Name)>();
        for (var round = 0; ; round++)
        {
            var sites = Sites(compilation);
            if (sites.Count == 0)
            {
                foreach (var (moduleName, file, name) in uses)
                    if (compilation.Modules.FirstOrDefault(m => m.FullName == moduleName) is { } home
                        && home.Members.LookupLocal(name) is { } symbol)
                        binding.MarkQualifier(file, symbol);
                return (compilation, binding);
            }
            if (Environment.GetEnvironmentVariable("LYRIC_EXPAND_DEBUG") is not null)
                Console.Error.WriteLine($"--- round {round}: {sites.Count} sites in {string.Join(",", sites.Select(x => x.Module.FullName))}");
            if (round == MaxRounds)
            {
                diagnostics.Report("LYR-CT0007", Severity.Error, sites[0].Call.Span,
                    $"the expansion of '{sites[0].Call.Name}!' did not end after {MaxRounds} rounds: a macro's result holds a site again and again");
                return null;
            }

            // each site's macro, and the module that writes it
            var planned = new List<(Site Site, FunctionSymbol Macro, ModuleSymbol Home)>();
            var failed = false;
            foreach (var site in sites)
            {
                switch (site.Module.Members.Lookup(site.Call.Name))
                {
                    case ImportBindingSymbol { Target: FunctionSymbol { IsMacro: true } imported }:
                        planned.Add((site, imported, HomeOf(compilation, imported)));
                        uses.Add((site.Module.FullName, site.Call.Span.File, site.Call.Name));
                        break;
                    case FunctionSymbol { IsMacro: true } own:
                        diagnostics.Report("LYR-SEM0189", Severity.Error, site.Call.Span,
                            $"'{own.Name}!' is written in the module that declares the macro: a macro expands in the modules that import it, and its own module checks it first (13 M9b P6) - move the call, or the macro");
                        failed = true;
                        break;
                    case null:
                        diagnostics.Report("LYR-SEM0190", Severity.Error, site.Call.NameSpan,
                            $"no macro named '{site.Call.Name}' is in scope: '{site.Call.Name}!' expands a macro, which a module imports as it imports a function");
                        failed = true;
                        break;
                    default:
                        diagnostics.Report("LYR-SEM0190", Severity.Error, site.Call.NameSpan,
                            $"'{site.Call.Name}' is no macro: '{site.Call.Name}!' expands one, and this name is something else");
                        failed = true;
                        break;
                }
            }
            if (failed) return null;

            // a macro whose module holds sites of its own runs when those are gone (P6)
            var busy = sites.Select(s => s.Module).ToHashSet();
            var ready = planned.Where(p => !busy.Contains(p.Home)).ToList();
            if (ready.Count == 0)
            {
                var first = planned[0];
                diagnostics.Report("LYR-CT0007", Severity.Error, first.Site.Call.Span,
                    $"'{first.Macro.Name}!' cannot be expanded: its module '{first.Home.FullName}' holds sites of macros whose modules wait for it in turn");
                return null;
            }

            // the analysis of the round: its errors in a macro's module are the round's
            var buffered = new DiagnosticEngine(sources);
            var types = Semantics.Analyze(compilation, binding, buffered);
            var homeFiles = ready.Select(p => compilation.AstOf(p.Home).Span.File).ToHashSet();
            var inHomes = buffered.Diagnostics.Where(d => d.Severity == Severity.Error && homeFiles.Contains(d.Span.File)).ToList();
            if (inHomes.Count > 0)
            {
                foreach (var d in inHomes) diagnostics.Report(d);
                return null;
            }

            var table = new ComptimeTable { Hoist = true, MacroRoots = ready.Select(p => p.Macro).ToHashSet(), Unexpanded = busy };
            var hoisted = ModuleLowerer.Lower(compilation, binding, types, diagnostics,
                optimize: false, libraryRoots: false, passes: options.Passes, comptime: table);
            if (hoisted is null || diagnostics.HasErrors) return null;

            var interpreter = new Interpreter(hoisted, options.ComptimeBudget, options.OverflowChecks, embed, options.Build, sources);
            var bridge = new SyntaxBridge(hoisted, sources);
            var replacements = new Dictionary<Node, Node>(ReferenceEqualityComparer.Instance);
            var splices = new Dictionary<Stmt, Stmt[]>(ReferenceEqualityComparer.Instance);
            foreach (var (site, macro, home) in ready)
            {
                var name = NameMangling.ForFunction(home, macro.Name);
                var index = hoisted.Functions.FindIndex(f => f.Name == name);
                if (index < 0) index = hoisted.Functions.FindIndex(f => f.Name.StartsWith(name + "$", StringComparison.Ordinal));
                if (index < 0) throw new InternalCompilationException($"macro '{macro.Name}' of '{home.FullName}' was not lowered for its expansion");
                var function = hoisted.Functions[index];
                bridge.At = site.Call.Span;
                try
                {
                    if (Arguments(site, macro, function, hoisted, bridge, diagnostics) is not { } args) { failed = true; continue; }
                    interpreter.Refill();
                    var result = interpreter.Run(new FunctionId(index), args);
                    if (!Splice(site, macro, result, function.ReturnType, hoisted, bridge, replacements, splices, diagnostics)) failed = true;
                }
                catch (ComptimeFailure failure)
                {
                    if (failure.Code == "LYR-MAC0001")
                        diagnostics.Report(failure.Code, Severity.Error, failure.At == default ? site.Call.Span : failure.At, failure.Message,
                            [new DiagnosticNote(site.Call.BangSpan, $"in the expansion of '{macro.Name}!'")]);
                    else
                        ComptimeEvaluator.Report(diagnostics, failure, site.Call.Span);
                    failed = true;
                }
            }
            if (failed) return null;

            foreach (var module in ready.Select(p => p.Site.Module).Distinct())
            {
                trees[module.FullName] = AstRewriter.Rewrite(compilation.AstOf(module),
                    n => replacements.GetValueOrDefault(n), s => splices.GetValueOrDefault(s));
                if (Environment.GetEnvironmentVariable("LYRIC_EXPAND_DEBUG") is not null)
                    Console.Error.WriteLine($"--- expanded {module.FullName} (round {round}):\n{Formatting.AstFormatter.FormatDeclarations(trees[module.FullName].Declarations)}");
            }
            (compilation, binding) = rebuild(trees);
            if (diagnostics.HasErrors) return null;
        }
    }

    private static ModuleSymbol HomeOf(Compilation compilation, FunctionSymbol macro)
    {
        var file = macro.Declaration?.Span.File ?? default;
        foreach (var module in compilation.Modules)
            if (compilation.AstOf(module).Span.File == file) return module;
        throw new InternalCompilationException($"the macro '{macro.Name}' belongs to no module of the compilation");
    }

    /// <summary>What a parameter or a result is, by the name of its type in <c>std.syntax</c>:
    /// <c>Expr</c>, <c>Block</c>, <c>Ident</c>, <c>Literal</c>, <c>Type</c>, <c>Stmt</c>, <c>Stmt[]</c>,
    /// <c>Decl</c>, <c>Decl[]</c>; something else for anything the sema should have refused.</summary>
    private static string KindOf(IrType type, IrModule module) => type switch
    {
        IrEnumType e => module.Types[e.Type.Value].Name,
        IrStructType s => module.Types[s.Type.Value].Name,
        IrArrayType { Element: IrEnumType e } => module.Types[e.Type.Value].Name + "[]",
        _ => "?",
    };

    /// <summary>The site's arguments as the macro's parameters take them (P4): each written
    /// argument to the next parameter by its kind, the trailing block to a last <c>Block</c> or
    /// <c>Stmt[]</c> parameter; or <c>null</c>, reported.</summary>
    private static Value[]? Arguments(Site site, FunctionSymbol macro, IrFunction function, IrModule module, SyntaxBridge bridge, DiagnosticEngine diagnostics)
    {
        var written = site.Call.Arguments;
        var values = new Value[function.ParamCount];
        var next = 0;
        var trailingUsed = false;
        for (var i = 0; i < function.ParamCount; i++)
        {
            var type = function.Locals[i].Type;
            var kind = KindOf(type, module);
            var last = i == function.ParamCount - 1;
            if (last && site.Call.Trailing is { } block && next == written.Length && kind is "Block" or "Stmt[]")
            {
                values[i] = kind == "Block" ? bridge.BlockValue(block, type) : bridge.Array(block.Statements, type, bridge.Stmt);
                trailingUsed = true;
                continue;
            }
            if (next >= written.Length)
            {
                diagnostics.Report("LYR-SEM0191", Severity.Error, site.Call.Span,
                    $"'{macro.Name}!' takes {function.ParamCount} argument{(function.ParamCount == 1 ? "" : "s")}, {written.Length} {(written.Length == 1 ? "is" : "are")} written"
                    + (kind is "Block" or "Stmt[]" ? $" - the last, '{kind}', may stand as a trailing block" : ""));
                return null;
            }
            var argument = written[next++];
            var value = Convert(argument, kind, type, bridge);
            if (value is null)
            {
                diagnostics.Report("LYR-SEM0191", Severity.Error, argument.Span,
                    $"'{macro.Name}!' takes {Describe(kind)} here, and this argument is no {Describe(kind)}");
                return null;
            }
            values[i] = value.Value;
        }
        if (next < written.Length || (site.Call.Trailing is not null && !trailingUsed))
        {
            diagnostics.Report("LYR-SEM0191", Severity.Error, site.Call.Span,
                $"'{macro.Name}!' takes {function.ParamCount} argument{(function.ParamCount == 1 ? "" : "s")}, more are written"
                + (site.Call.Trailing is not null && !trailingUsed ? " - the trailing block needs a last parameter of type 'Block' or 'Stmt[]'" : ""));
            return null;
        }
        return values;
    }

    private static string Describe(string kind) => kind switch
    {
        "Expr" => "an expression",
        "Block" => "a block",
        "Stmt[]" => "a block of statements",
        "Ident" => "a name",
        "Literal" => "a literal",
        "Type" => "a type",
        _ => $"a '{kind}'",
    };

    private static Value? Convert(Expr argument, string kind, IrType type, SyntaxBridge bridge)
    {
        switch (kind)
        {
            case "Expr": return bridge.Expr(argument, type);
            case "Block": return argument is BlockExpr b ? bridge.BlockValue(b.Block, type) : null;
            case "Stmt[]": return argument is BlockExpr bs ? bridge.Array(bs.Block.Statements, type, bridge.Stmt) : null;
            case "Ident": return argument is IdentifierExpr id ? bridge.IdentValue(id.Name, id.Span, type) : null;
            case "Literal": return SyntaxBridge.IsLiteralExpr(argument) ? bridge.LiteralValue(argument, type) : null;
            case "Type": return TypeOf(argument) is { } named ? bridge.Type(named, type) : null;
            default: return null;
        }
    }

    /// <summary>A type written as an argument reads as an expression: a name, a path, a type path
    /// with arguments. The named type it is.</summary>
    private static TypeNode? TypeOf(Expr argument) => argument switch
    {
        IdentifierExpr id => new NamedType([id.Name], [], id.Span) { NameSpan = id.Span },
        TypePathExpr tp => new NamedType(tp.Path, tp.TypeArguments, tp.Span) { NameSpan = tp.NameSpan },
        MemberExpr { Target: var target, Member: var member, IsOptional: false } m when TypeOf(target) is NamedType { TypeArguments.Length: 0 } head =>
            new NamedType([.. head.Path, member], [], m.Span) { NameSpan = m.MemberSpan },
        _ => null,
    };

    /// <summary>The result into the tree: an expression in place of the site, statements in place
    /// of the statement the site is.</summary>
    private static bool Splice(Site site, FunctionSymbol macro, Value result, IrType type, IrModule module, SyntaxBridge bridge,
        Dictionary<Node, Node> replacements, Dictionary<Stmt, Stmt[]> splices, DiagnosticEngine diagnostics)
    {
        var kind = KindOf(type, module);
        switch (kind)
        {
            case "Expr":
                replacements[site.Call] = bridge.ExprOf(result, type);
                return true;
            case "Stmt" or "Stmt[]" or "Block":
            {
                if (site.Statement is null)
                {
                    diagnostics.Report("LYR-SEM0188", Severity.Error, site.Call.Span,
                        $"'{macro.Name}!' gives statements back, and stands where an expression is needed - write it as a statement of its own");
                    return false;
                }
                splices[site.Statement] = kind switch
                {
                    "Stmt" => [bridge.StmtOf(result, type)],
                    "Block" => [bridge.BlockOf(result, type)],
                    _ => bridge.StmtsOf(result, type),
                };
                return true;
            }
            default:
                diagnostics.Report("LYR-SEM0188", Severity.Error, site.Call.Span,
                    $"'{macro.Name}!' gives '{kind}' back, which no site of this form takes: an expression or statements stand here (a declaration macro is an attribute, M9b S4)");
                return false;
        }
    }
}
