using Lyric.AST;
using Lyric.Core;
using Lyric.Ir;
using Lyric.Ir.Interp;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
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
///
/// <para>What a macro gives back stands in a file of its own (S5, P7): the result is written as
/// source and read again, so its nodes have places in a synthesized file whose origin is the
/// site - an error in expanded code is said at the site, with the line of the expansion it stood
/// on and the quote it came from. What the caller wrote keeps the places it was written at.</para>
/// </summary>
public static class MacroExpander
{
    /// <summary>Rounds before a result holding sites again is called a loop (P6, the depth
    /// limit): every round is a full analysis, so the bound is small.</summary>
    public const int MaxRounds = 32;

    /// <summary>A site: its module, the call, and the statement it is where it is one
    /// (<c>name!(...);</c> alone on its line), which a result of statements replaces.</summary>
    public sealed record Site(ModuleSymbol Module, MacroCallExpr? Call, ExprStmt? Statement, Decl? Target, AttributeNode? Attribute)
    {
        public string Name => Call?.Name ?? Attribute!.Path[^1];
        public Span Span => Call?.Span ?? Attribute!.Span;
        public Span NameSpan => Call?.NameSpan ?? Attribute!.NameSpan;
        /// <summary>Where the note of a diagnostic of the expansion points: the '!', or the '@' path.</summary>
        public Span BangSpan => Call?.BangSpan ?? Attribute!.PathSpan;
        public string Written => Call is not null ? Name + "!" : "@" + Name;
    }

    /// <summary>The sites of the compiled modules (not the standard library's, which writes
    /// none), outermost only: an inner one travels with its argument. An attribute naming a
    /// macro on a top-level declaration is a site as well (13 M9b S4).</summary>
    public static List<Site> Sites(Compilation compilation)
    {
        var found = new List<Site>();
        foreach (var module in compilation.Modules)
        {
            if (compilation.IsNative(module)) continue;
            var ast = compilation.AstOf(module);
            foreach (var decl in ast.Declarations)
                foreach (var attribute in AttributesOf(decl))
                    if (attribute.Path.Length == 1 && module.Members.Lookup(attribute.Path[0])
                            is FunctionSymbol { IsMacro: true } or ImportBindingSymbol { Target: FunctionSymbol { IsMacro: true } })
                        found.Add(new Site(module, null, null, decl, attribute));
            Walk(ast, null, module, found);
        }
        return found;
    }

    private static AttributeNode[] AttributesOf(Decl decl) => decl switch
    {
        FunctionDecl f => f.Attributes,
        StructDecl s => s.Attributes,
        ClassDecl c => c.Attributes,
        EnumDecl e => e.Attributes,
        InterfaceDecl i => i.Attributes,
        ExtendDecl x => x.Attributes,
        GlobalBindingDecl g => g.Attributes,
        TypeAliasDecl a => a.Attributes,
        _ => [],
    };

    /// <summary>The declaration without the attribute that is its site (T5: the macro sees the
    /// rest, and the rest stays).</summary>
    private static Decl WithoutAttribute(Decl decl, AttributeNode attribute)
    {
        AttributeNode[] Rest(AttributeNode[] all) => [.. all.Where(a => !ReferenceEquals(a, attribute))];
        return decl switch
        {
            FunctionDecl f => f with { Attributes = Rest(f.Attributes) },
            StructDecl s => s with { Attributes = Rest(s.Attributes) },
            ClassDecl c => c with { Attributes = Rest(c.Attributes) },
            EnumDecl e => e with { Attributes = Rest(e.Attributes) },
            InterfaceDecl i => i with { Attributes = Rest(i.Attributes) },
            ExtendDecl x => x with { Attributes = Rest(x.Attributes) },
            GlobalBindingDecl g => g with { Attributes = Rest(g.Attributes) },
            TypeAliasDecl a => a with { Attributes = Rest(a.Attributes) },
            _ => decl,
        };
    }

    private static void Walk(Node node, Node? parent, ModuleSymbol module, List<Site> found)
    {
        if (node is MacroCallExpr call)
        {
            found.Add(new Site(module, call, parent as ExprStmt, null, null));
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
                switch (site.Module.Members.Lookup(site.Name))
                {
                    case ImportBindingSymbol { Target: FunctionSymbol { IsMacro: true } imported }:
                        planned.Add((site, imported, HomeOf(compilation, imported)));
                        uses.Add((site.Module.FullName, site.Span.File, site.Name));
                        break;
                    case FunctionSymbol { IsMacro: true } own:
                        diagnostics.Report("LYR-SEM0189", Severity.Error, site.Span,
                            $"'{site.Written}' is written in the module that declares the macro '{own.Name}': a macro expands in the modules that import it, and its own module checks it first (13 M9b P6) - move the site, or the macro");
                        failed = true;
                        break;
                    case null:
                        diagnostics.Report("LYR-SEM0190", Severity.Error, site.NameSpan,
                            $"no macro named '{site.Name}' is in scope: '{site.Written}' expands a macro, which a module imports as it imports a function");
                        failed = true;
                        break;
                    default:
                        diagnostics.Report("LYR-SEM0190", Severity.Error, site.NameSpan,
                            $"'{site.Name}' is no macro: '{site.Written}' expands one, and this name is something else");
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
            var declSplices = new Dictionary<Decl, Decl[]>(ReferenceEqualityComparer.Instance);
            foreach (var (site, macro, home) in ready)
            {
                var name = NameMangling.ForFunction(home, macro.Name);
                var index = hoisted.Functions.FindIndex(f => f.Name == name);
                if (index < 0) index = hoisted.Functions.FindIndex(f => f.Name.StartsWith(name + "$", StringComparison.Ordinal));
                if (index < 0) throw new InternalCompilationException($"macro '{macro.Name}' of '{home.FullName}' was not lowered for its expansion");
                var function = hoisted.Functions[index];
                bridge.At = site.Span;
                try
                {
                    if ((site.Attribute is null ? Arguments(site, macro, function, hoisted, bridge, diagnostics)
                            : AttributeArguments(site, macro, function, hoisted, bridge, diagnostics)) is not { } args) { failed = true; continue; }
                    interpreter.Refill();
                    var result = interpreter.Run(new FunctionId(index), args);
                    var placed = site.Attribute is null
                        ? Splice(site, macro, result, function.ReturnType, hoisted, bridge, replacements, splices, diagnostics, sources)
                        : SpliceDeclaration(site, macro, result, function.ReturnType, hoisted, bridge, replacements, declSplices, diagnostics, sources);
                    if (!placed) failed = true;
                }
                catch (ComptimeFailure failure)
                {
                    if (failure.Code == "LYR-MAC0001")
                        diagnostics.Report(failure.Code, Severity.Error, failure.At == default ? site.Span : failure.At, failure.Message,
                            [new DiagnosticNote(site.BangSpan, $"in the expansion of '{site.Written}'")]);
                    else
                        ComptimeEvaluator.Report(diagnostics, failure, site.Span);
                    failed = true;
                }
            }
            if (failed) return null;

            foreach (var module in ready.Select(p => p.Site.Module).Distinct())
            {
                trees[module.FullName] = AstRewriter.Rewrite(compilation.AstOf(module),
                    n => replacements.GetValueOrDefault(n), s => splices.GetValueOrDefault(s), d => declSplices.GetValueOrDefault(d));
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
        var call = site.Call!;
        var written = call.Arguments;
        var values = new Value[function.ParamCount];
        var next = 0;
        var trailingUsed = false;
        for (var i = 0; i < function.ParamCount; i++)
        {
            var type = function.Locals[i].Type;
            var kind = KindOf(type, module);
            var last = i == function.ParamCount - 1;
            if (last && call.Trailing is { } block && next == written.Length && kind is "Block" or "Stmt[]")
            {
                values[i] = kind == "Block" ? bridge.BlockValue(block, type) : bridge.Array(block.Statements, type, bridge.Stmt);
                trailingUsed = true;
                continue;
            }
            if (next >= written.Length)
            {
                diagnostics.Report("LYR-SEM0191", Severity.Error, site.Span,
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
        if (next < written.Length || (call.Trailing is not null && !trailingUsed))
        {
            diagnostics.Report("LYR-SEM0191", Severity.Error, site.Span,
                $"'{macro.Name}!' takes {function.ParamCount} argument{(function.ParamCount == 1 ? "" : "s")}, more are written"
                + (call.Trailing is not null && !trailingUsed ? " - the trailing block needs a last parameter of type 'Block' or 'Stmt[]'" : ""));
            return null;
        }
        return values;
    }

    /// <summary>An attribute macro's arguments (13 M9b S4): the declaration it sits on, without
    /// this attribute, as the first parameter's kind; then the attribute's positional argument
    /// for the parameters after it. Fields are a data attribute's.</summary>
    private static Value[]? AttributeArguments(Site site, FunctionSymbol macro, IrFunction function, IrModule module, SyntaxBridge bridge, DiagnosticEngine diagnostics)
    {
        var target = site.Target!;
        var attribute = site.Attribute!;
        if (function.ParamCount == 0)
        {
            diagnostics.Report("LYR-SEM0191", Severity.Error, attribute.Span,
                $"'@{macro.Name}' sits on a declaration, and the macro takes nothing: an attribute macro takes the declaration as its first parameter (a 'StructDecl', a 'FnDecl', a 'Decl' ...)");
            return null;
        }
        var first = function.Locals[0].Type;
        var kind = KindOf(first, module);
        var declaration = kind == "Decl" ? bridge.Decl(WithoutAttribute(target, attribute), first) : bridge.DeclPayload(WithoutAttribute(target, attribute), first);
        if (declaration is null)
        {
            diagnostics.Report("LYR-SEM0191", Severity.Error, attribute.Span,
                $"'@{macro.Name}' takes {Describe(kind)} first, and sits on {DescribeDecl(target)}");
            return null;
        }
        if (attribute.Fields.Length > 0)
        {
            diagnostics.Report("LYR-SEM0191", Severity.Error, attribute.Span,
                $"'@{macro.Name}' is a macro, and its arguments stand in parentheses, '@{macro.Name}(...)': fields are a data attribute's");
            return null;
        }
        var values = new Value[function.ParamCount];
        values[0] = declaration.Value;
        Expr[] written = attribute.Positional is { } positional ? [positional] : [];
        for (var i = 1; i < function.ParamCount; i++)
        {
            var type = function.Locals[i].Type;
            var paramKind = KindOf(type, module);
            if (i - 1 >= written.Length)
            {
                diagnostics.Report("LYR-SEM0191", Severity.Error, attribute.Span,
                    $"'@{macro.Name}' takes {function.ParamCount - 1} argument{(function.ParamCount == 2 ? "" : "s")} after the declaration, {written.Length} {(written.Length == 1 ? "is" : "are")} written");
                return null;
            }
            var value = Convert(written[i - 1], paramKind, type, bridge);
            if (value is null)
            {
                diagnostics.Report("LYR-SEM0191", Severity.Error, written[i - 1].Span,
                    $"'@{macro.Name}' takes {Describe(paramKind)} here, and this argument is no {Describe(paramKind)}");
                return null;
            }
            values[i] = value.Value;
        }
        if (written.Length > function.ParamCount - 1)
        {
            diagnostics.Report("LYR-SEM0191", Severity.Error, attribute.Span,
                $"'@{macro.Name}' takes {function.ParamCount - 1} argument{(function.ParamCount == 2 ? "" : "s")} after the declaration, more are written");
            return null;
        }
        return values;
    }

    private static string DescribeDecl(Decl decl) => decl switch
    {
        FunctionDecl => "a function",
        StructDecl => "a struct",
        ClassDecl => "a class",
        EnumDecl => "an enum",
        InterfaceDecl => "an interface",
        ExtendDecl => "an extend block",
        GlobalBindingDecl => "a binding",
        TypeAliasDecl => "a type alias",
        _ => "a declaration",
    };

    /// <summary>An attribute macro's result in the declaration's place: a <c>Decl</c> one for one,
    /// <c>Decl[]</c> spliced into the module.</summary>
    private static bool SpliceDeclaration(Site site, FunctionSymbol macro, Value result, IrType type, IrModule module, SyntaxBridge bridge,
        Dictionary<Node, Node> replacements, Dictionary<Decl, Decl[]> declSplices, DiagnosticEngine diagnostics, SourceManager sources)
    {
        switch (KindOf(type, module))
        {
            case "Decl":
                replacements[site.Target!] = Resynthesize(site, macro, sources, new[] { bridge.DeclOf(result, type) },
                    Formatting.AstFormatter.FormatDeclarations, p => p.ParseModule().Declarations)[0];
                return true;
            case "Decl[]":
                declSplices[site.Target!] = Resynthesize(site, macro, sources, bridge.DeclsOf(result, type),
                    Formatting.AstFormatter.FormatDeclarations, p => p.ParseModule().Declarations);
                return true;
            case var kind:
                diagnostics.Report("LYR-SEM0188", Severity.Error, site.Span,
                    $"'@{macro.Name}' gives '{kind}' back, and an attribute macro gives a declaration or several, which stand in the declaration's place");
                return false;
        }
    }

    /// <summary>The result in a file of its own (13 M9b S5, P7): the nodes a macro gave back,
    /// written as source and read again, so each stands in a synthesized file whose origin is the
    /// site. An error in it is said at the site, with the line it stood on and - where the result
    /// is a quote's - the quote it came from; a warning in it is nobody's. What the caller wrote
    /// - an argument, its spans in the site's file - keeps its spans through <see cref="Zip"/>: an
    /// error in it is the caller's, a warning in it stays. The read-back is the formatter's and
    /// the parser's own round trip; a tree that does not come back as it went is a bug here.</summary>
    private static T[] Resynthesize<T>(Site site, FunctionSymbol macro, SourceManager sources, T[] built,
        Func<IReadOnlyList<T>, string> write, Func<Parser, T[]> read) where T : Node
    {
        if (built.Length == 0) return built;
        var text = write(built);
        var place = sources.LocateStart(site.Span);
        var notes = new List<DiagnosticNote>();
        if (built.Select(n => QuotedNode(n, site, sources)).FirstOrDefault(n => n is not null) is { } quoted)
            notes.Add(new DiagnosticNote(quoted.Span, $"from the quote of '{macro.Name}' here"));
        var id = sources.AddSynthesized($"<expansion of {site.Written} at {Path.GetFileName(sources.GetPath(site.Span.File))}:{place.Line}>",
            text, site.Span, $"in the expansion of '{site.Written}'", notes);
        var engine = new DiagnosticEngine(sources);
        var parsed = read(new Parser(sources, id, engine));
        if (engine.HasErrors)
        {
            var first = engine.Diagnostics.First(d => d.Severity == Severity.Error);
            throw new InternalCompilationException($"the expansion of '{site.Written}' does not read back as it was written: {first.Message}\n{text}");
        }
        if (parsed.Length != built.Length)
            throw new InternalCompilationException($"the expansion of '{site.Written}' read back as {parsed.Length} nodes where {built.Length} were written:\n{text}");
        var zipped = new T[built.Length];
        for (var i = 0; i < built.Length; i++) zipped[i] = (T)Zip(built[i], parsed[i], site);
        return zipped;
    }

    /// <summary>The first node of a tree a quote wrote: its span in a written file that is not the
    /// site's - the macro module's. The quote the expansion came from, for the note.</summary>
    private static Node? QuotedNode(Node node, Site site, SourceManager sources)
    {
        if (node.Span != default && node.Span.File != site.Span.File && sources.OriginOf(node.Span.File) is null) return node;
        foreach (var child in AstChildren.Of(node))
            if (QuotedNode(child, site, sources) is { } found) return found;
        return null;
    }

    /// <summary>The read tree with what the built one knows: a node the caller wrote - its span
    /// in the site's file, and not the site's own - stays as built, with its spans; a node of the
    /// macro's takes its place in the synthesized file, with a quote's <c>QuotedFrom</c>. A literal
    /// a macro built negative reads back as a negation, which is taken as read; a statement whose
    /// expression ends in a block (<c>loop { }</c>, an <c>if</c>) reads back as a tail where it
    /// closes a list, and stays the statement it was built as.</summary>
    private static Node Zip(Node built, Node parsed, Site site)
    {
        if (built.GetType() != parsed.GetType())
        {
            if (built is IntLiteralExpr or FloatLiteralExpr && parsed is UnaryExpr { Operator: UnaryOp.Neg }) return parsed;
            if (built is ExprStmt statement && parsed is TailExprStmt tail)
                return new ExprStmt((Expr)Zip(statement.Expr, tail.Expr, site), tail.Span);
            throw new InternalCompilationException($"the expansion of '{site.Written}' read back as {parsed.GetType().Name} where {built.GetType().Name} was written");
        }
        var builtChildren = AstChildren.Of(built).ToList();
        var parsedChildren = AstChildren.Of(parsed).ToList();
        if (builtChildren.Count != parsedChildren.Count)
            throw new InternalCompilationException($"the expansion of '{site.Written}' read back with {parsedChildren.Count} children of {parsed.GetType().Name} where {builtChildren.Count} were written");
        var written = built.Span != default && built.Span.File == site.Span.File && built.Span != site.Span;
        var root = written ? built : parsed;
        var own = written ? builtChildren : parsedChildren;
        var changed = new Dictionary<Node, Node>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < own.Count; i++)
        {
            var child = Zip(builtChildren[i], parsedChildren[i], site);
            if (!ReferenceEquals(child, own[i])) changed[own[i]] = child;
        }
        var node = changed.Count == 0 ? root : AstRewriter.Rewrite(root, n => ReferenceEquals(n, root) ? null : changed.GetValueOrDefault(n));
        if (written) return node;
        return (built, node) switch
        {
            (IdentifierExpr { QuotedFrom: { } from }, IdentifierExpr id) => id with { QuotedFrom = from },
            (NamedType { QuotedFrom: { } from }, NamedType named) => named with { QuotedFrom = from },
            _ => node,
        };
    }

    private static string Describe(string kind) => kind switch
    {
        "Expr" => "an expression",
        "StructDecl" => "a struct",
        "ClassDecl" => "a class",
        "FnDecl" => "a function",
        "EnumDecl" => "an enum",
        "InterfaceDecl" => "an interface",
        "ExtendDecl" => "an extend block",
        "LetDecl" => "a binding",
        "Decl" => "a declaration",
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
        Dictionary<Node, Node> replacements, Dictionary<Stmt, Stmt[]> splices, DiagnosticEngine diagnostics, SourceManager sources)
    {
        var kind = KindOf(type, module);
        switch (kind)
        {
            case "Expr":
                replacements[site.Call!] = Resynthesize(site, macro, sources, new[] { bridge.ExprOf(result, type) },
                    e => Formatting.AstFormatter.FormatNode(e[0]), p => new[] { p.ParseExpressionOrBlock() })[0];
                return true;
            case "Stmt" or "Stmt[]" or "Block":
            {
                if (site.Statement is null)
                {
                    diagnostics.Report("LYR-SEM0188", Severity.Error, site.Span,
                        $"'{macro.Name}!' gives statements back, and stands where an expression is needed - write it as a statement of its own");
                    return false;
                }
                // statements stand where no value is taken: a tail among them is the statement it is
                Stmt[] spliced = kind switch
                {
                    "Stmt" => [bridge.StmtOf(result, type)],
                    "Block" => [bridge.BlockOf(result, type)],
                    _ => bridge.StmtsOf(result, type),
                };
                splices[site.Statement] = Resynthesize(site, macro, sources,
                    [.. spliced.Select(s => s is TailExprStmt tail ? new ExprStmt(tail.Expr, tail.Span) : s)],
                    Formatting.AstFormatter.FormatStatements, p => p.ParseStatements());
                return true;
            }
            default:
                diagnostics.Report("LYR-SEM0188", Severity.Error, site.Span,
                    $"'{macro.Name}!' gives '{kind}' back, which no site of this form takes: an expression or statements stand here - a macro that gives declarations is an attribute, '@{macro.Name}' on the declaration it rewrites (13 M9b S4)");
                return false;
        }
    }
}
