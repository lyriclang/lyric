using System.Diagnostics;
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
/// The pipeline source to AST to symbols to types to IR to <c>.lyrbc</c> bytes, as a library.
///
/// <para>One front end for the whole toolchain. With a copy of the preamble per command, only one
/// of them wired up the <see cref="Compilation.ModuleLoader"/>, and <c>check</c> silently treated
/// every standard library import as opaque.</para>
///
/// <para>This class NEVER RENDERS ITSELF. It collects into
/// <see cref="CompileResult.Diagnostics"/> and leaves the output to the caller;
/// <see cref="DiagnosticEngine.RenderText"/> says each finding once, so a second call after more
/// reports adds those alone.</para>
/// </summary>
public static class SourceCompiler
{
    /// <summary>
    /// Everything a build does except writing the bytes. The basis of <c>lyrc check</c>.
    ///
    /// <para>Lowering is part of it: a limit the backend cannot express is reported as
    /// <c>LYR-ICE0002</c>, and stopping after the sema would let <c>check</c> answer 'ok' for a
    /// program that <c>build</c> rejects.</para>
    /// </summary>
    public static CompileResult Check(string path, CompilerOptions? options = null) =>
        Check(ScriptSource.FromDisk(path), options);

    /// <summary>Up to the mid-level IR. The basis of <c>lyrc lower</c>.</summary>
    public static CompileResult Lower(string path, CompilerOptions? options = null) =>
        Lower(ScriptSource.FromDisk(path), options);

    /// <inheritdoc cref="Check(string, CompilerOptions?)"/>
    public static CompileResult Check(ScriptSource source, CompilerOptions? options = null) =>
        Run(source, Stage.Check, options ?? new CompilerOptions());

    /// <inheritdoc cref="Lower(string, CompilerOptions?)"/>
    public static CompileResult Lower(ScriptSource source, CompilerOptions? options = null) =>
        Run(source, Stage.Lower, options ?? new CompilerOptions());

    /// <summary>The program's entry module with its macros expanded (<c>lyric expand</c>):
    /// resolved and expanded, not checked; <see cref="CompileResult.Expanded"/> holds the tree, the
    /// parsed one where the program has no site.</summary>
    public static CompileResult Expand(ScriptSource source, CompilerOptions? options = null) =>
        Run(source, Stage.Expand, options ?? new CompilerOptions());

    private enum Stage { Check, Lower, Expand }

    private static CompileResult Run(ScriptSource source, Stage stage, CompilerOptions options)
    {
        var report = options.Progress;
        var sources = SourcesFor(options);
        var diagnostics = new DiagnosticEngine(sources);

        report?.BeginPhase(Phase.Read, source.DisplayName);
        if (source.Open(sources, diagnostics) is not { } id)
        {
            report?.EndPhase();
            return new CompileResult(sources, diagnostics, null);
        }
        report?.EndPhase();

        report?.BeginPhase(Phase.Parse, source.DisplayName);
        var parsedEntry = ParsedModule.Parse(sources, id, diagnostics);
        var entry = parsedEntry.Ast;
        report?.EndPhase();

        // The module loader times itself: Compilation.Resolve loads the imported modules
        // internally, so the load/resolve boundary is not observable from outside. The wrapper
        // subtracts its own duration from the resolve time.
        var loaderTime = TimeSpan.Zero;
        var loaded = new List<string>();

        var stdlib = BuildModuleLoader(sources, diagnostics, options, source.BaseDirectory);

        // what the loader gave, by module name: a rebuild after a macro's expansion
        // serves it again, with the expanded tree where there is one, and parses nothing twice
        var parsedModules = new Dictionary<string, LoadedModule>(StringComparer.Ordinal);
        Compilation Build(IReadOnlyDictionary<string, Module>? trees)
        {
            var built = new Compilation(sources, diagnostics)
            {
                Lyric5Modules = options.PackageRoots is not null,
                Library = options.Library,
                WarnNative = options.WarnDeps,
                PackageDependencies = options.PackageDependencies,
                ModuleOnDisk = ModuleProbe(options),
                // The standard library is ordinary Lyric source and is loaded on demand.
                ModuleLoader = modulePath =>
                {
                    var name = string.Join('.', modulePath);
                    if (parsedModules.TryGetValue(name, out var again))
                        return trees is not null && trees.TryGetValue(name, out var expanded) ? again with { Ast = expanded } : again;
                    report?.UpdateDetail(name);

                    var started = Stopwatch.GetTimestamp();
                    var result = stdlib(modulePath);
                    loaderTime += Stopwatch.GetElapsedTime(started);

                    if (result is not null)
                    {
                        loaded.Add(name);
                        parsedModules[name] = result;
                    }
                    return result;
                },
            };
            var entryName = source.ModuleName ?? "main";
            var entryTree = trees is not null && trees.TryGetValue(entryName, out var expandedEntry) ? expandedEntry : entry;
            built.Entry = built.AddModule(entryTree, source.ModuleName, documentation: parsedEntry.Documentation);
            return built;
        }
        var compilation = Build(null);

        // 'std' is the standard library's name: a single file named std.lyr
        // would claim it — a package cannot, its manifest refuses the name.
        if (options.PackageRoots is not null && source.ModuleName is "std")
            diagnostics.Report("LYR-RES0013", Severity.Error, new Span(id, 0, 0),
                "'std' is the standard library's name — a single file named std.lyr would claim it; rename the file");

        report?.BeginPhase(Phase.Load);
        var resolveStarted = Stopwatch.GetTimestamp();
        CompilerPosition.Begin(sources);
        var binding = compilation.Resolve();
        var resolveTime = Stopwatch.GetElapsedTime(resolveStarted);
        report?.EndPhase(loaderTime);
        report?.ReportPhase(Phase.Resolve, ModuleCount(loaded), resolveTime - loaderTime);

        var embed = EmbedReaderFor(sources, options);

        // macros: every site is expanded before its module is checked; a program
        // without a site pays nothing
        if (compilation.Lyric5Modules && !diagnostics.HasErrors && MacroExpander.Sites(compilation).Count > 0)
        {
            report?.BeginPhase(Phase.Check, "macros");
            var expanded = MacroExpander.Expand(compilation, binding, diagnostics, sources, options, embed, trees =>
            {
                var again = Build(trees);
                CompilerPosition.Begin(sources);
                return (again, again.Resolve());
            });
            report?.EndPhase();
            if (expanded is null) return new CompileResult(sources, diagnostics, null);
            (compilation, binding) = expanded.Value;
            entry = compilation.AstOf(compilation.Entry!);
        }
        if (stage == Stage.Expand) return new CompileResult(sources, diagnostics, null) { Expanded = diagnostics.HasErrors ? null : entry };

        options.Cancellation.ThrowIfCancellationRequested();
        report?.BeginPhase(Phase.Check, ModuleCount(loaded));
        var types = Semantics.Analyze(compilation, binding, diagnostics);
        report?.EndPhase();
        options.Cancellation.ThrowIfCancellationRequested();

        // From here on every exit carries it. A program WITH errors still has a model, and that is
        // the case an editor cares about most: the text under a cursor is usually mid-edit.
        var model = new SemanticModel(compilation, entry, binding, types);

        // On a faulty AST any lowering result would be guesswork.
        if (diagnostics.HasErrors)
            return new CompileResult(sources, diagnostics, null, model);

        // 'comptime' sites: a lowering that hoists each into a function of its own
        // — the module's only roots, so it holds what the sites reach —, the IR interpreter over
        // it, and the real lowering writes each site's value as a constant. Lyric 5's: the 4.x
        // tools lower a site as its inner expression, as they did since their VM went.
        types.SourcePlace = SourcePlaceFor(sources, options);
        if (!EvaluateSites(compilation, binding, types, diagnostics, options, embed, report, out var comptime))
            return new CompileResult(sources, diagnostics, null, model);
        WarnDeprecated(compilation, binding, types, diagnostics, options, comptime);
        if (diagnostics.HasErrors) return new CompileResult(sources, diagnostics, null, model); // an 'until' come due
        if (!options.Lower) return Unlowered(compilation, types, diagnostics, options, sources, model, embed, comptime);

        // Lowering limits arrive as LYR-ICE0002 in the same engine and are rendered with file, line and
        // column like any other error.
        //
        // The verifier runs INSIDE the lowering now, and twice — once on what the lowering produced,
        // once on what the optimizations left of it. Neither point is reachable from out here, so
        // the lowering times itself and this caller subtracts, the same arrangement as the module
        // loader inside 'Resolve' above.
        report?.BeginPhase(Phase.Lower);
        var timings = new LoweringTimings();
        var lowering = Stopwatch.StartNew();
        // A Lyric 5 program roots in its entry's 'main': a 'pub'
        // function nothing reaches is no part of the binary. The 4.x tools keep a library's roots.
        var ir = ModuleLowerer.Lower(compilation, binding, types, diagnostics,
            optimize: options.Optimize, libraryRoots: !compilation.Lyric5Modules, passes: options.Passes,
            comptime: comptime, timings: timings);
        var lowered = lowering.Elapsed;
        if (ir is not null) report?.UpdateDetail(FunctionCount(ir));
        report?.EndPhase(lowered - timings.Verification);
        if (timings.Verified && ir is not null)
            report?.ReportPhase(Phase.Verify,
                timings.Runs > 1 ? $"{FunctionCount(ir)}, {timings.Runs} runs" : FunctionCount(ir),
                timings.Verification);
        // the lints' levels (16 §5), once every warning is in
        if (compilation.Lyric5Modules)
            LintFilter.Apply(compilation, types, diagnostics, StringFieldsOf(types, comptime), options.LintLevels, options.RootPackage, options.WarnDeps);
        return new CompileResult(sources, diagnostics, ir, model)
            { Embedded = embed.Read, ComptimeValues = comptime?.Values, Inactive = comptime?.Inactive };
    }

    /// <summary>
    /// <c>@Deprecated</c> in Lyric 5: its uses warn with what its value says —
    /// computed now, so <c>message</c> may be any string a <c>comptime</c> site makes —, and its
    /// <c>until</c> comes due against the version the toolchain claims. The 4.x tools read the
    /// literals in the warning pass itself.
    /// </summary>
    private static void WarnDeprecated(Compilation compilation, BindingResult binding, TypeResult types,
        DiagnosticEngine diagnostics, CompilerOptions options, ComptimeTable? comptime)
    {
        if (!compilation.Lyric5Modules) return;
        new WarningAnalyzer(compilation, binding, types, diagnostics)
            .RunDeprecated(StringFieldsOf(types, comptime), options.ToolchainVersion ?? ToolchainVersion.Value);
    }

    /// <summary>An attribute's string fields by name, as the compiler computed its value.</summary>
    private static Func<AttributeNode, IReadOnlyDictionary<string, string>?> StringFieldsOf(TypeResult types, ComptimeTable? comptime) =>
        attribute =>
        {
            if (types.AttributeValueOf(attribute) is not { } site
                || comptime?.Values?.GetValueOrDefault(types.ComptimeSites.IndexOf(site)) is not StructConst value
                || (types.RefOf(attribute) as TypeSymbol)?.Declaration is not StructDecl declared)
                return null;
            // the struct's fields in their order, which is the order of the value's (its layout)
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var names = declared.Members.OfType<FieldDecl>().Select(f => f.Name).ToArray();
            for (var i = 0; i < Math.Min(names.Length, value.Fields.Length); i++)
                if (value.Fields[i] is StringConst text) fields[names[i]] = text.Value;
            return fields;
        };

    /// <summary>'embed' reads relative to the module that calls, within its package's
    /// directory: the one a package's source root lies in.</summary>
    private static EmbedReader EmbedReaderFor(SourceManager sources, CompilerOptions options) =>
        new(sources.GetPath,
            options.PackageRoots?.Values.Select(root => Path.GetDirectoryName(Path.GetFullPath(root))!).ToList() ?? []);

    /// <summary>Where a span begins as the binary names it: a standard-library source
    /// relative to the library's parent directory, as its <c>#line</c> has it; a package's under
    /// the name the build writes for the package's directory (<see cref="CompilerOptions.PlaceNames"/>);
    /// anything else as the source manager has it. Forward slashes on every system, so a program
    /// that prints its <c>@callerFile</c> prints the same on each.</summary>
    private static Func<Span, (string File, int Line)> SourcePlaceFor(SourceManager sources, CompilerOptions options)
    {
        var stdlib = Path.GetFullPath(options.StdlibRoot ?? StdlibLoader.DefaultRoot());
        var places = options.PlaceNames ?? [];
        return span =>
        {
            var path = sources.GetPath(span.File);
            return (PlaceName(path, stdlib, places).Replace('\\', '/'), sources.LocateStart(span).Line);
        };
    }

    private static string PlaceName(string path, string stdlib, IReadOnlyList<(string From, string To)> places)
    {
        if (!Path.IsPathRooted(path)) return path;
        var full = Path.GetFullPath(path);
        if (Inside(full, stdlib)) return Path.GetRelativePath(Path.GetDirectoryName(stdlib) ?? "", full);
        foreach (var (from, to) in places)
            if (Inside(full, from)) return Path.Combine(to, Path.GetRelativePath(from, full));
        return path;
    }

    private static bool Inside(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative != "." && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    /// <summary>
    /// The 'comptime' sites of a Lyric 5 compilation evaluated: a
    /// lowering that hoists each into a function of its own — the module's only roots, so it holds
    /// what the sites reach —, the IR interpreter over it. <paramref name="comptime"/> holds the
    /// values the real lowering writes as constants; <c>null</c> where there is no site, or for
    /// the 4.x tools, which lower a site as its inner expression. False after reporting.
    /// </summary>
    private static bool EvaluateSites(Compilation compilation, BindingResult binding, TypeResult types,
        DiagnosticEngine diagnostics, CompilerOptions options, EmbedReader embed, TerminalOutput? report,
        out ComptimeTable? comptime)
    {
        comptime = null;
        if (!compilation.Lyric5Modules || types.ComptimeSites.Count == 0) return true;
        report?.BeginPhase(Phase.Lower);
        var hoisting = new ComptimeTable { Hoist = true };
        var hoisted = ModuleLowerer.Lower(compilation, binding, types, diagnostics,
            optimize: false, libraryRoots: false, passes: options.Passes, comptime: hoisting);
        report?.EndPhase();
        if (hoisted is null || diagnostics.HasErrors) return false;

        // '@When' first: what it leaves out is checked, but its sites are not evaluated
        // — a site of another target's code may read what this one does not have
        var when = WhenSites(compilation, types);
        // the standard library's sites run under the default budget, not the program's
        var stdFiles = compilation.Modules.Where(compilation.IsNative).Select(m => compilation.AstOf(m).Span.File).ToHashSet();
        bool Standard(int site) => stdFiles.Contains(types.ComptimeSites[site].Span.File);
        // an editor's budget caps the program's: its analysis is asked again at the next keystroke
        var quiet = options.EditorBudget is { } cap && cap < options.ComptimeBudget;
        var budget = quiet ? options.EditorBudget!.Value : options.ComptimeBudget;
        var chosen = when.Count == 0 ? new Dictionary<int, IrConstValue>()
            : ComptimeEvaluator.Evaluate(hoisted, types, diagnostics, budget, options.OverflowChecks,
                embed, options.Build, include: when.ContainsKey, standard: Standard, quiet: quiet);
        if (chosen is null) return false;
        var inactive = new Inactivity();
        foreach (var (site, (decl, file)) in when)
            if (chosen.GetValueOrDefault(site) is StructConst { Fields: [BoolConst { Value: false }, ..] })
                if (decl is null) inactive.Module(file); else inactive.Declaration(decl);
        var rest = ComptimeEvaluator.Evaluate(hoisted, types, diagnostics, budget, options.OverflowChecks,
            embed, options.Build, include: i => !when.ContainsKey(i) && !inactive.Covers(types.ComptimeSites[i].Span) && !(hoisting.Skipped?.Contains(i) ?? false),
            standard: Standard, quiet: quiet);
        if (rest is null) return false;
        var values = new Dictionary<int, IrConstValue>(rest);
        foreach (var (site, value) in chosen) values[site] = value;
        InactiveUses.Check(compilation, binding, types, diagnostics, inactive, values);
        if (diagnostics.HasErrors) return false;
        comptime = new ComptimeTable { Values = values, Inactive = inactive, Skipped = hoisting.Skipped };
        return true;
    }

    /// <summary>The sites of the <c>@When</c>s of <c>std.core</c>: each with the declaration
    /// it sits on, or <c>null</c> and the module's file for one on the module header.</summary>
    private static Dictionary<int, (Decl? Decl, FileId File)> WhenSites(Compilation compilation, TypeResult types)
    {
        var found = new Dictionary<int, (Decl?, FileId)>();
        if (compilation.FindModule(["std", "core"])?.Members.LookupLocal("When") is not TypeSymbol when) return found;
        void Note(AttributeNode[] attributes, Decl? decl, FileId file)
        {
            foreach (var attribute in attributes)
                if (ReferenceEquals(types.RefOf(attribute), when) && types.AttributeValueOf(attribute) is { } site)
                    found[types.ComptimeSites.IndexOf(site)] = (decl, file);
        }
        foreach (var module in compilation.Modules)
        {
            var ast = compilation.AstOf(module);
            Note(ast.Attributes, null, ast.Span.File);
            foreach (var decl in ast.Declarations)
                Note(decl switch
                {
                    FunctionDecl f => f.Attributes,
                    StructDecl s => s.Attributes,
                    ClassDecl c => c.Attributes,
                    EnumDecl e => e.Attributes,
                    InterfaceDecl i => i.Attributes,
                    TypeAliasDecl a => a.Attributes,
                    GlobalBindingDecl g => g.Attributes,
                    ExtendDecl x => x.Attributes,
                    _ => [],
                }, decl, ast.Span.File);
        }
        return found;
    }

    /// <summary>
    /// The module loader every run uses: 'std' from the standard library, a segment the host
    /// claimed from one of its native roots, everything else from the program's directory.
    ///
    /// <para>The split is by module path rather than by trying one root and then the other. A
    /// precedence rule would let a file at '&lt;program&gt;/std/io/console.lyr' shadow the standard
    /// library silently, and silently is the part that makes it a trap.</para>
    /// </summary>
    private static Func<string[], LoadedModule?> BuildModuleLoader(
        SourceManager sources, DiagnosticEngine diagnostics, CompilerOptions options,
        string fallbackSourceRoot)
    {
        options.Cancellation.ThrowIfCancellationRequested();
        var fromStdlib = StdlibLoader.ForRoot(options.StdlibRoot ?? StdlibLoader.DefaultRoot(),
            sources, diagnostics, options.SourceOverlay, SeedOf(options));
        var fromProject = StdlibLoader.ForProject(
            options.SourceRoot ?? fallbackSourceRoot, sources, diagnostics, options.SourceOverlay);

        // Roots the host declares native, keyed by the segment they own. An SDK ships its
        // declarations as .lyr files and says which prefix they live under.
        var nativeRoots = options.NativeRoots?.ToDictionary(
            entry => entry.Key,
            entry => StdlibLoader.ForRoot(entry.Value, sources, diagnostics, options.SourceOverlay),
            StringComparer.Ordinal);

        // Other projects this one stands on, keyed the same way. Their modules are NOT native:
        // a dependency is somebody's ordinary Lyric, and a body it forgot is an error there as
        // it is here. Its own native roots arrive through the table above, which the project
        // file has already merged.
        var dependencyRoots = options.DependencyRoots?.ToDictionary(
            entry => entry.Key,
            entry => StdlibLoader.ForProject(entry.Value, sources, diagnostics, options.SourceOverlay),
            StringComparer.Ordinal);

        // Packages: the name in front, the file under the source root without it, its name
        // exactly as the module path writes it on every platform.
        var packages = options.PackageRoots?.ToDictionary(
            entry => entry.Key,
            entry => (Root: entry.Value,
                Load: StdlibLoader.ForProject(entry.Value, sources, diagnostics, options.SourceOverlay)),
            StringComparer.Ordinal);
        // A package's tests/: 'app.tests.x' is tests/x.lyr — under 'lyric test' alone.
        var tests = options.PackageTestRoots?.ToDictionary(
            entry => entry.Key,
            entry => (Root: entry.Value,
                Load: StdlibLoader.ForProject(entry.Value, sources, diagnostics, options.SourceOverlay)),
            StringComparer.Ordinal);
        // A package's gen/: 'app.gen.x' is gen/x.lyr, what its build
        // script wrote — a module of the package as any other.
        var generated = options.PackageGenRoots?.ToDictionary(
            entry => entry.Key,
            entry => (Root: entry.Value,
                Load: StdlibLoader.ForProject(entry.Value, sources, diagnostics, options.SourceOverlay)),
            StringComparer.Ordinal);

        var loader = (string[] modulePath) =>
        {
            if (modulePath is ["std", ..]) return fromStdlib(modulePath);

            if (packages is not null)
            {
                // 'lyric test' refuses a module that lies under both before it compiles (RES0015).
                if (modulePath is [var owner, "tests", _, ..] && tests is not null
                    && tests.TryGetValue(owner, out var testRoot) && NamedExactly(testRoot.Root, modulePath[2..])
                    && File.Exists(Path.Combine([testRoot.Root, .. modulePath[2..^1], modulePath[^1] + ".lyr"])))
                    return testRoot.Load(modulePath[2..]);
                if (modulePath is [var maker, "gen", _, ..] && generated is not null
                    && generated.TryGetValue(maker, out var genRoot) && NamedExactly(genRoot.Root, modulePath[2..])
                    && File.Exists(Path.Combine([genRoot.Root, .. modulePath[2..^1], modulePath[^1] + ".lyr"])))
                    return genRoot.Load(modulePath[2..]);
                if (modulePath.Length == 0 || !packages.TryGetValue(modulePath[0], out var package)) return null;
                // The root module: 'src/lib.lyr' is the module of the
                // package's own name, and there is no module '<package>.lib' beside it.
                if (modulePath is [_, Compilation.RootModuleFile]) return null;
                string[] file = modulePath.Length == 1 ? [Compilation.RootModuleFile] : modulePath[1..];
                return NamedExactly(package.Root, file) ? package.Load(file) : null;
            }

            if (nativeRoots is not null && modulePath.Length > 0
                && nativeRoots.TryGetValue(modulePath[0], out var native))
                return native(modulePath);

            if (dependencyRoots is not null && modulePath.Length > 0
                && dependencyRoots.TryGetValue(modulePath[0], out var dependency))
                return dependency(modulePath);

            return fromProject(modulePath);
        };

        // Supplied modules first, then disk. Chained rather than a second loader mechanism:
        // 'Compilation' knows exactly one delegate.
        var provided = options.NativeModules;
        if (provided is { Count: > 0 })
        {
            var fromDisk = loader;
            loader = modulePath =>
            {
                var name = string.Join('.', modulePath);
                if (!provided.TryGetValue(name, out var text)) return fromDisk(modulePath);

                var id = sources.AddVirtual(name, text);
                var parsed = ParsedModule.Parse(sources, id, diagnostics);
                return new LoadedModule(parsed.Ast, IsNative: true, parsed.Documentation);
            };
        }

        return loader;
    }

    /// <summary>A compilation's source manager: the seed's fork where the options hold a seed of
    /// the library they read, a fresh one otherwise.</summary>
    private static SourceManager SourcesFor(CompilerOptions options) =>
        SeedOf(options) is { } seed ? seed.Fork() : new SourceManager { Lyric5 = options.PackageRoots is not null };

    /// <summary>The options' seed, where it is a Lyric 5 compilation's of the library the options
    /// read.</summary>
    private static StdlibSeed? SeedOf(CompilerOptions options) =>
        options.StdlibSeed is { } seed && options.PackageRoots is not null
        && seed.Serves(options.StdlibRoot ?? StdlibLoader.DefaultRoot()) ? seed : null;

    /// <summary>The end of an editor's check, where <see cref="CompilerOptions.Lower"/> is off: the
    /// lints' levels over what was said, no IR. What only the lowering finds — a monomorphization
    /// that does not end (<c>LYR-SEM0197</c>) — is the build's to say.</summary>
    private static CompileResult Unlowered(Compilation compilation, TypeResult types, DiagnosticEngine diagnostics,
        CompilerOptions options, SourceManager sources, SemanticModel model, EmbedReader embed, ComptimeTable? comptime)
    {
        if (compilation.Lyric5Modules)
            LintFilter.Apply(compilation, types, diagnostics, StringFieldsOf(types, comptime), options.LintLevels, options.RootPackage, options.WarnDeps);
        return new CompileResult(sources, diagnostics, null, model)
            { Embedded = embed.Read, ComptimeValues = comptime?.Values, Inactive = comptime?.Inactive };
    }

    /// <summary>
    /// Everything <see cref="Check(ScriptSource, CompilerOptions?)"/> does, over every root of a
    /// project at once: ONE compilation, one source manager, one model.
    ///
    /// <para>One compilation rather than one per root, because symbols are identity objects. An
    /// editor asking "who uses this function" needs the uses in OTHER files bound to the same
    /// symbol the declaration produced, and two compilations of the same text produce two symbol
    /// worlds with nothing to compare.</para>
    ///
    /// <para>Every root is registered before anything resolves, so an import between two roots
    /// finds the module already there instead of reading its file a second time. A root's name
    /// comes from its header when it has one, and from the caller otherwise —
    /// <see cref="ScriptSource.ModuleName"/> is where a caller says what a headerless file is
    /// called, and for a file under the source root that must be the same derivation the import
    /// path makes in the other direction, or the import will not find it.</para>
    ///
    /// <para>Checked as a workspace, not as an executable: two roots may both declare 'main',
    /// because each is the entry point of its own program (see
    /// <see cref="Semantics.Analyze"/>).</para>
    /// </summary>
    public static CompileResult CheckProject(
        IReadOnlyList<ScriptSource> roots, CompilerOptions? options = null)
    {
        options ??= new CompilerOptions();
        var sources = SourcesFor(options);
        var diagnostics = new DiagnosticEngine(sources);

        var loader = BuildModuleLoader(sources, diagnostics, options,
            roots.Count > 0 ? roots[0].BaseDirectory : Directory.GetCurrentDirectory());

        var compilation = new Compilation(sources, diagnostics)
        {
            ModuleLoader = loader,
            Lyric5Modules = options.PackageRoots is not null,
            WarnNative = options.WarnDeps,
            PackageDependencies = options.PackageDependencies,
            ModuleOnDisk = ModuleProbe(options),
        };

        var times = new PhaseClock();
        Module? entry = null;
        foreach (var root in roots)
        {
            if (root.Open(sources, diagnostics) is not { } id) continue;

            var parsed = ParsedModule.Parse(sources, id, diagnostics);

            // The header wins over the caller's derivation. A file whose header disagrees with its
            // path registers under the name it claims; an import of the path-derived name then
            // reloads the file and reports the mismatch (LYR-RES0006), the same way it would were
            // the file not a root.
            // Lyric 5's names its module by the path alone, a header or none
            var name = parsed.Ast.Header is not null && !compilation.Lyric5Modules ? null : root.ModuleName;

            compilation.AddModule(parsed.Ast, name, documentation: parsed.Documentation);
            entry ??= parsed.Ast;
        }

        // No root could even be opened. The diagnostics say why, and there is no module to hang a
        // model on.
        if (entry is null) return new CompileResult(sources, diagnostics, null);
        times.Mark(PhaseClock.Parse);

        CompilerPosition.Begin(sources);
        var binding = compilation.Resolve();
        times.Mark(PhaseClock.Resolve);
        options.Cancellation.ThrowIfCancellationRequested();
        var types = Semantics.Analyze(compilation, binding, diagnostics, singleProgram: false);
        times.Mark(PhaseClock.Check);
        options.Cancellation.ThrowIfCancellationRequested();

        var model = new SemanticModel(compilation, entry, binding, types);

        if (diagnostics.HasErrors)
            return new CompileResult(sources, diagnostics, null, model) { Times = times.Phases };

        // Every build that lowers evaluates — a library by itself too; an
        // attribute's value is read from here ('lyric test' its '@Test').
        var embed = EmbedReaderFor(sources, options);
        types.SourcePlace = SourcePlaceFor(sources, options);
        var evaluated = EvaluateSites(compilation, binding, types, diagnostics, options, embed, report: null, out var comptime);
        times.Mark(PhaseClock.Evaluate);
        if (!evaluated) return new CompileResult(sources, diagnostics, null, model) { Times = times.Phases };
        WarnDeprecated(compilation, binding, types, diagnostics, options, comptime);
        if (diagnostics.HasErrors) return new CompileResult(sources, diagnostics, null, model) { Times = times.Phases }; // an 'until' come due
        options.Cancellation.ThrowIfCancellationRequested();
        if (!options.Lower)
        {
            var unlowered = Unlowered(compilation, types, diagnostics, options, sources, model, embed, comptime);
            times.Mark(PhaseClock.Lint);
            return unlowered with { Times = times.Phases };
        }

        var ir = ModuleLowerer.Lower(compilation, binding, types, diagnostics,
            optimize: options.Optimize, libraryRoots: true, passes: options.Passes, comptime: comptime);
        times.Mark(PhaseClock.Lower);
        if (ir is null) return new CompileResult(sources, diagnostics, null, model) { Times = times.Phases };
        if (compilation.Lyric5Modules)
            LintFilter.Apply(compilation, types, diagnostics, StringFieldsOf(types, comptime), options.LintLevels, options.RootPackage, options.WarnDeps);
        times.Mark(PhaseClock.Lint);

        return new CompileResult(sources, diagnostics, ir, model)
            { Embedded = embed.Read, ComptimeValues = comptime?.Values, Inactive = comptime?.Inactive, Times = times.Phases };
    }

    /// <summary>
    /// Whether the file of a module path is named exactly as the path writes it: on a file
    /// system that ignores case, <c>import app.Util</c> would otherwise read <c>util.lyr</c> on one
    /// machine and nothing on the next. Segment by segment, by the names the directories list.
    /// </summary>
    private static bool NamedExactly(string root, string[] path)
    {
        var directory = root;
        for (var i = 0; i < path.Length; i++)
        {
            var name = i == path.Length - 1 ? path[i] + ".lyr" : path[i];
            if (!Directory.Exists(directory)) return false;
            var found = Directory.EnumerateFileSystemEntries(directory, name, new EnumerationOptions
            {
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = false,
            }).Select(Path.GetFileName).ToList();
            if (found.Count > 0 && !found.Contains(name, StringComparer.Ordinal)) return false;
            directory = Path.Combine(directory, name);
        }
        return true;
    }

    /// <summary>Whether a package of the program holds the module of a path as a file, named
    /// exactly (<see cref="Compilation.ModuleOnDisk"/>); <c>null</c> without packages. The root
    /// module's file is the module of the package's name, not one named <c>lib</c>.</summary>
    private static Func<string[], bool>? ModuleProbe(CompilerOptions options) =>
        options.PackageRoots is not { } roots
            ? null
            : path => path.Length > 1 && roots.TryGetValue(path[0], out var root)
                      && path is not [_, Compilation.RootModuleFile]
                      && File.Exists(Path.Combine([root, .. path[1..^1], path[^1] + ".lyr"]))
                      && NamedExactly(root, path[1..]);

    private static string ModuleCount(List<string> loaded) =>
        loaded.Count == 0 ? "1 module" : $"{loaded.Count + 1} modules";

    private static string FunctionCount(IrModule ir) =>
        ir.Functions.Count == 1 ? "1 function" : $"{ir.Functions.Count} functions";

    /// <summary>
    /// Reads a file into a fresh <see cref="SourceManager"/>: the shared preamble of the debug
    /// commands <c>tokenize</c> and <c>parse</c>, which branch off before the resolver and do not
    /// go through <see cref="Run"/>.
    /// </summary>
    public static (SourceManager Sources, DiagnosticEngine Diagnostics, FileId Id) Read(string path)
    {
        var sources = new SourceManager();
        var diagnostics = new DiagnosticEngine(sources);
        try
        {
            return (sources, diagnostics, sources.AddFromDisk(path));
        }
        catch
        {
            diagnostics.Report(CliDiagnostics.FileUnreadable, Severity.Error, default,
                $"failed to read file: {path}");
            return (sources, diagnostics, FileId.None);
        }
    }
}

/// <summary>
/// What a compiler run needs besides the source file.
///
/// <para>A record rather than a growing parameter list.</para>
/// </summary>
/// <summary>
/// The front end's answer about a program, as opposed to its output.
///
/// <para>The three tables belong together and are useless apart: a symbol from
/// <see cref="Binding"/> is looked up in <see cref="Types"/>, and both are keyed by nodes that only
/// <see cref="Compilation"/> can hand out. Passing them as one value keeps a caller from holding a
/// binding table from one run beside a type table from another.</para>
/// </summary>
/// <param name="Entry">The module that was compiled, as opposed to the ones it imported.</param>
public sealed record SemanticModel(
    Compilation Compilation,
    Module Entry,
    BindingResult Binding,
    TypeResult Types)
{
    /// <summary>
    /// What was written above each declaration, across every module that was read.
    ///
    /// <para>Forwarded rather than stored: the compilation gathers it as modules arrive, and a copy
    /// here would be a second table to keep in step with the first.</para>
    /// </summary>
    public DocumentationTable Documentation => Compilation.Documentation;
}

public sealed record CompilerOptions
{
    /// <summary>Where the standard library lives. <c>null</c> means
    /// <c>StdlibLoader.DefaultRoot()</c>: <c>LYRIC_STDLIB</c> or the directory next to the
    /// binary.</summary>
    public string? StdlibRoot { get; init; }

    /// <summary>The standard library parsed once (<see cref="Parsing.StdlibSeed"/>): an editor's
    /// compilations follow each other and need not parse it again. Taken where it is the seed of
    /// <see cref="StdlibRoot"/> and the compilation is Lyric 5's.</summary>
    public Parsing.StdlibSeed? StdlibSeed { get; init; }

    /// <summary>An editor's cap on the budget of each <c>comptime</c> site, below the program's own
    /// (<see cref="ComptimeBudget"/>): a site of the program that runs out of it stays without a
    /// value and without a finding, which the build gives under the program's budget. The
    /// standard library's sites keep theirs.</summary>
    public long? EditorBudget { get; init; }

    /// <summary>Whether the compilation is lowered after it checked. An editor's is not: it shows
    /// what the checker and the evaluation say, and the lowering's findings are the build's.</summary>
    public bool Lower { get; init; } = true;

    /// <summary>Whether the build is a library C links (<c>lyric build --lib</c>): the entry module
    /// is the library's root, which has no <c>main</c> to start from — its exports are the roots.</summary>
    public bool Library { get; init; }

    /// <summary>Stops the compilation between its phases — an editor's, when the text it reads has
    /// changed meanwhile — with an <see cref="OperationCanceledException"/>.</summary>
    public CancellationToken Cancellation { get; init; }

    /// <summary>Where phase reports go. <c>null</c> means nobody is listening, and the compiler
    /// then runs with no output dependency at all, which is what the embedding API needs.
    /// benutzbar haelt.</summary>
    public TerminalOutput? Progress { get; init; }

    /// <summary>
    /// Additional NATIVE modules that do not live on disk, by module path.
    ///
    /// <para>Used by <c>LangVm.RegisterFunction</c>: a host function needs a DECLARATION for the
    /// compiler to know its signature, exactly like every standard library native, which stands as
    /// a bodyless <c>pub fn</c> in a <c>.lyr</c> file. The only difference is that this file lives
    /// in memory.</para>
    ///
    /// <para>They are consulted BEFORE the standard library, so a host module hides one of the same name
    /// on disk rather than the other way round: the host decides what its script sees.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string>? NativeModules { get; init; }

    /// <summary>
    /// Directories whose modules may declare functions WITHOUT A BODY, keyed by the module path
    /// segment they own: <c>["engine"] = "…/sdk"</c> makes <c>engine.input</c> read
    /// <c>…/sdk/engine/input.lyr</c> and treats what it finds as native declarations.
    ///
    /// <para>For an SDK that ships its surface as <c>.lyr</c> files rather than as generated
    /// strings. The host still supplies the implementations, under the same qualified names.</para>
    ///
    /// <para>A ROOT is declared native, never a file: <c>Compilation.IsNative</c> follows the origin
    /// of a module and not its content, or naming a file well enough would be a way into the host.
    /// The segment a root owns is taken out of the program's own directory, which is what makes the
    /// answer unambiguous instead of a matter of precedence.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string>? NativeRoots { get; init; }

    /// <summary>
    /// Where the program's own modules are looked up. <c>null</c> means the directory of the entry
    /// file, which is what a program without a project file gets.
    ///
    /// <para>Filled from <see cref="Lyric.Core.ProjectFile"/> by the tools that read one.
    /// Deliberately not discovered here: a script being compiled must not be able to widen what
    /// the compiler looks at by placing a file beside itself, so the decision belongs to the
    /// caller.</para>
    /// </summary>
    public string? SourceRoot { get; init; }

    /// <summary>
    /// Other projects this one imports from, keyed by the module path segment each owns and
    /// naming that project's SOURCE ROOT: <c>["geometry"] = "…/geometry/src"</c> makes
    /// <c>import geometry.shapes</c> read <c>…/geometry/src/geometry/shapes.lyr</c>.
    ///
    /// <para>The flattened closure a <see cref="Lyric.Core.ProjectFile"/> computes, native roots
    /// already taken out into <see cref="NativeRoots"/>. A segment named here is taken out of
    /// <see cref="SourceRoot"/>, as a native segment is.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string>? DependencyRoots { get; init; }

    /// <summary>
    /// Lyric 5's packages, by name, each naming its source root:
    /// <c>["app"] = "…/app/src"</c> makes <c>import app.net.http</c> read
    /// <c>…/app/src/net/http.lyr</c> — the package's name is the first segment of every module
    /// path, its files lie under the root without it.
    ///
    /// <para>Set, it is the whole of what a program reaches besides <c>std</c>: there is no lookup
    /// beside the entry file, and a single file — given none — imports only <c>std</c>. Every module
    /// is named by its path then, and a <c>module</c> header is refused (LYR-RES0008). The 4.x
    /// tools, the language server first, still read headers and leave this unset.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string>? PackageRoots { get; init; }

    /// <summary>What each package declares it imports from, by name;
    /// <c>null</c> where no package rules apply.</summary>
    public IReadOnlyDictionary<string, IReadOnlySet<string>>? PackageDependencies { get; init; }

    /// <summary>The name the binary writes for each directory its sources lie in, as
    /// the build hands the C compiler its <c>-ffile-prefix-map</c>: what <c>@callerFile</c> gives
    /// is then what a backtrace shows, and no machine's path. Without, a source's path as
    /// the source manager has it.</summary>
    public IReadOnlyList<(string From, string To)>? PlaceNames { get; init; }

    /// <summary>The version the toolchain claims, which a <c>@Deprecated</c>'s <c>until</c> comes
    /// due against: <c>lyric5</c>'s own. Without, the 4.x tree's
    /// (<see cref="Lyric.Core.ToolchainVersion.Value"/>).</summary>
    public string? ToolchainVersion { get; init; }

    /// <summary>Each package's lint levels (spec 16 §5), by name: its <c>[lints]</c>, the root's with the
    /// command line's in front.</summary>
    public IReadOnlyDictionary<string, LintSettings>? LintLevels { get; init; }

    /// <summary>The package the build is for: the warnings of the others are not said unless
    /// <see cref="WarnDeps"/>.</summary>
    public string? RootPackage { get; init; }

    /// <summary><c>--warn-deps</c>: the dependencies' warnings are said too.</summary>
    public bool WarnDeps { get; init; }

    /// <summary>
    /// A package's <c>tests/</c>, by the package's name: its files are
    /// modules of the package, <c>tests/math.lyr</c> in <c>app</c> is <c>app.tests.math</c>. Only
    /// <c>lyric test</c> names them; a build of a program reaches no test.
    /// </summary>
    public IReadOnlyDictionary<string, string>? PackageTestRoots { get; init; }

    /// <summary>
    /// A package's <c>gen/</c>, by the package's name: what its build
    /// script wrote, modules of the package — <c>gen/schema.lyr</c> in <c>app</c> is
    /// <c>app.gen.schema</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? PackageGenRoots { get; init; }

    /// <summary>
    /// Text to use instead of what lies on disk, by absolute file path. A module found at one of
    /// these paths is read from here, and one that exists only here is found all the same.
    ///
    /// <para>An editor holds the authoritative text of every file it has open, saved or not. Without
    /// this, a program is compiled against its own buffer and against the SAVED version of every
    /// module it imports, and the two disagree for as long as an edit is unsaved.</para>
    ///
    /// <para>Supply it with a comparer that matches the platform's idea of path equality; it is
    /// looked up with <see cref="Path.GetFullPath(string)"/> applied to the candidate.</para>
    /// </summary>
    public IReadOnlyDictionary<string, string>? SourceOverlay { get; init; }

    /// <summary>
    /// Whether the SourceMap section is written. Both profiles keep it: a panic that names a line
    /// is worth the bytes, and the moment it is needed is the moment nobody planned for it.
    ///
    /// <para>Turning it off produces exactly the file a build produced before the section existed,
    /// which is what makes stripping a decision with no other consequence.</para>
    /// </summary>
    public bool SourceMap { get; init; } = Profile.Default.SourceMap;

    /// <summary>Whether the DebugInfo section (slot names) and the Names entries no attribute row
    /// demands are written. The debug profile keeps them: the moment a debugger is attached is
    /// the moment nobody planned for it. The release profile drops them.</summary>
    public bool DebugInfo { get; init; } = Profile.Default.DebugInfo;

    /// <summary>Whether the IR optimizations (inlining, scalar replacement, devirtualization)
    /// run. The debug profile turns them off: an inlined callee has no frame to show, and a
    /// scalar-replaced struct no longer exists as one value. The three defaults here are
    /// <see cref="Profile.Default"/>'s, so <c>new CompilerOptions()</c> IS the default profile.</summary>
    public bool Optimize { get; init; } = Profile.Default.Optimize;

    /// <summary>Which of the optimizations run when <see cref="Optimize"/> is on. All of them
    /// unless a diagnostic switch (<c>--no-inline</c> and its siblings) takes one out to bisect a
    /// finding.</summary>
    public IrPasses Passes { get; init; } = IrPasses.All;

    /// <summary>Whether integer overflow panics, the profile's <c>overflowChecks</c>: at
    /// compile time as in the binary, so <c>comptime f()</c> is <c>f()</c>.</summary>
    public bool OverflowChecks { get; init; } = true;

    /// <summary>The steps one <c>comptime</c> site may take.</summary>
    public long ComptimeBudget { get; init; } = ComptimeEvaluator.DefaultBudget;

    /// <summary>What the build is for and as: <c>std.core</c>'s <c>target</c> and
    /// <c>profile</c> — a cross-build's target, not the host's. The host in the debug profile
    /// without.</summary>
    public BuildFacts Build { get; init; } = BuildFacts.Host;
}

/// <summary>
/// What a compiler run leaves behind. <see cref="Ir"/> is <c>null</c> when the lowering was not
/// reached or not requested;
/// </summary>
/// <param name="Model">
/// What the front end knew when it was done: the modules, the resolved names, the types. It is
/// <c>null</c> only when the run stopped before the sema, which happens when the source could not
/// be opened at all.
///
/// <para>Carried out rather than dropped because a batch compile is not the only caller. Everything
/// a tool can say ABOUT a program rather than about its bytes — a type under the cursor, the
/// declaration a name refers to — is in these three tables, and rebuilding them outside would mean
/// running the front end a second time and getting a second answer to the same question.</para>
/// </param>
/// <summary>A stopwatch read at the end of each phase: what each one took since the last.</summary>
internal sealed class PhaseClock
{
    public const string Parse = "parse", Resolve = "resolve", Check = "check", Evaluate = "evaluate",
        Lower = "lower", Lint = "lint";

    private readonly List<(string, TimeSpan)> _phases = [];
    private long _last = Stopwatch.GetTimestamp();

    public IReadOnlyList<(string Phase, TimeSpan Time)> Phases => _phases;

    public void Mark(string phase)
    {
        var now = Stopwatch.GetTimestamp();
        _phases.Add((phase, Stopwatch.GetElapsedTime(_last, now)));
        _last = now;
    }
}

public sealed record CompileResult(
    SourceManager Sources,
    DiagnosticEngine Diagnostics,
    IrModule? Ir,
    SemanticModel? Model = null)
{
    /// <summary>No error was reported. Warnings do not count.</summary>
    public bool Ok => !Diagnostics.HasErrors;

    /// <summary>The files <c>embed</c> read while the program compiled,
    /// absolute: inputs of the build beside its sources.</summary>
    public IReadOnlyList<string> Embedded { get; init; } = [];

    /// <summary>Each 'comptime' site's value by its index in the model's sites, where the
    /// compilation evaluated any — an attribute's among them.</summary>
    public IReadOnlyDictionary<int, IrConstValue>? ComptimeValues { get; init; }

    /// <summary>What the build leaves out: a <c>@Test</c> there is no test of this target.</summary>
    public Lyric.Ir.Lowering.Inactivity? Inactive { get; init; }

    /// <summary>The entry module after the macros' expansion, from
    /// <see cref="SourceCompiler.Expand"/> alone; <c>null</c> after an error.</summary>
    public Module? Expanded { get; init; }

    /// <summary>Where <see cref="SourceCompiler.CheckProject"/>'s time went, phase by phase in the
    /// order they ran, as far as it got; <c>null</c> from the other entries.</summary>
    public IReadOnlyList<(string Phase, TimeSpan Time)>? Times { get; init; }

    /// <summary>Renders every diagnostic exactly once and reports whether the run was clean.
    /// </summary>
    public bool Render(TextWriter error)
    {
        Diagnostics.RenderText(error);
        return Ok;
    }
}
