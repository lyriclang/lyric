using System.Security.Cryptography;
using System.Text;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Ir;
using Lyric5.Compiler;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>What a build is asked for: the project, the profile, the target, the C compiler, and
/// the toolchain version every cache key carries.</summary>
public sealed record BuildRequest(Project Project, BuildProfile Profile, Target Target, CCompiler Compiler, string ToolchainVersion);

/// <summary>
/// From a <c>.lyr</c> to a binary: the front end, the subset gate, the C
/// emitter, the runtime archive, the C compiler, the linker — each step behind a content key, so
/// an unchanged program compiles nothing twice. Diagnostics go to <c>error</c>;
/// the exit codes are the driver's: 1 for a program the compiler refuses, 2 for a build
/// that could not run at all.
/// </summary>
public static class Pipeline
{
    /// <summary>Where <c>lyric5</c> finds the seed of the Lyric 5 standard library: beside itself.</summary>
    public static string StdlibRoot => Path.Combine(AppContext.BaseDirectory, "stdlib5");

    /// <summary>A target and a profile as the compile-time interpreter answers them: a
    /// profile is a debug one where it descends from the built-in <c>debug</c>, as the default does.</summary>
    internal static Lyric.Ir.Interp.BuildFacts FactsOf(Target target, BuildProfile? profile) =>
        Lyric.Ir.Interp.BuildFacts.Of(target.Os switch
        {
            TargetOs.Linux => "linux",
            TargetOs.MacOs => "macos",
            TargetOs.Windows => "windows",
            _ => "other",
        }, target.Arch, (profile?.Base ?? Lyric5.Toolchain.Profile.Debug) == Lyric5.Toolchain.Profile.Debug);

    /// <summary>The version the toolchain claims — three numbers, no dev stamp —, which a
    /// <c>@Deprecated</c>'s <c>until</c> comes due against: it falls due on the branch
    /// that raises the version, before the tag exists.</summary>
    internal static string ClaimedVersion { get; } =
        typeof(Pipeline).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "5.0.0";

    /// <summary>Where a binary's names lie: beside it, <c>app.lyric-names</c> — a line per function,
    /// its C name, a tab and its Lyric name.</summary>
    public static string NamesOf(string executable) => executable + ".lyric-names";

    /// <summary>The front end and the gate on a project's source: the IR, or <c>null</c> after
    /// reporting. A profile that denies warnings fails a compilation that warns. The IR
    /// optimizer — inlining, scalar replacement, devirtualization — runs where the profile
    /// optimizes C at <c>-O2</c> and above: release, and a profile of the
    /// manifest's that asks as much; debug and the sanitizers' profiles see the code as written.</summary>
    public static CompileResult? Compile(Project project, TextWriter error, BuildProfile? profile = null, Target? target = null)
    {
        var result = SourceCompiler.Lower(SourceOf(project), OptionsFor(project, profile, target));
        if (!result.Render(error) || result.Ir is null) return null;
        if (SubsetGate.Check(result.Ir, result.Diagnostics)) return result;
        result.Diagnostics.RenderText(error);
        return null;
    }

    /// <summary><c>lyric expand</c>: the program's entry module after its macros'
    /// expansion, as source on <paramref name="output"/> - the results in the sites' places, the
    /// rest as written, in the formatter's form; a program without a site prints as it is. The
    /// program is resolved and expanded, not checked: what the expansion needs is reported, the
    /// rest is <c>build</c>'s.</summary>
    public static int Expand(Project project, TextWriter output, TextWriter error, BuildProfile? profile = null, Target? target = null)
    {
        // the scripts first, for gen/, as an emission does
        if (CCompiler.Locate() is { } compiler)
        {
            var (ran, _) = BuildScripts.Run(project, Target.Host, profile ?? BuildProfile.Of(Lyric5.Toolchain.Profile.Debug), compiler,
                ScriptVersion, error);
            if (ran != 0) return ran;
        }
        var result = SourceCompiler.Expand(SourceOf(project), OptionsFor(project, profile, target));
        if (!result.Render(error) || result.Expanded is null) return 1;
        output.WriteLine(Lyric.Formatting.AstFormatter.FormatDeclarations(result.Expanded.Declarations));
        return 0;
    }

    private static ScriptSource SourceOf(Project project) =>
        project.EntryText is { } text
            ? ScriptSource.FromText(project.Module, text)
            : ScriptSource.FromDisk(project.Source, project.Module);

    private static CompilerOptions OptionsFor(Project project, BuildProfile? profile, Target? target)
    {
        // A package's modules by their paths, a single file with std alone.
        return new CompilerOptions
        {
            StdlibRoot = StdlibRoot, PackageRoots = project.PackageRoots,
            PackageDependencies = project.DeclaredDependencies,
            PackageTestRoots = project.TestRoot is { } tests ? new Dictionary<string, string> { [project.Name] = tests } : null,
            PackageGenRoots = GenRoots(project),
            Optimize = profile is { Opt: >= 2 },
            // 'comptime' computes as the binary would, each site within the root
            // manifest's budget
            OverflowChecks = profile?.OverflowChecks ?? true,
            ComptimeBudget = project.Manifest?.ComptimeBudget ?? Lyric.Ir.Interp.ComptimeEvaluator.DefaultBudget,
            // '@callerFile' names a source as the binary's backtrace does
            PlaceNames = Places(project),
            ToolchainVersion = ClaimedVersion,
            LintLevels = LintLevelsOf(project.LintLevels, project.Name, profile), RootPackage = project.Name,
            WarnDeps = project.LintArgs.WarnDeps,
            // what 'target' and 'profile' hold: the build's, a cross-build's target
            Build = FactsOf(target ?? Target.Host, profile),
        };
    }

    /// <summary>The program's C as <c>--emit c</c> writes it in the debug profile, with the standard
    /// library read from <paramref name="stdlibRoot"/> and no build script run: in the caller's
    /// process, for a check that compares two versions of std. <c>null</c> where the program does
    /// not compile; its findings go to <paramref name="error"/>.</summary>
    public static string? EmitC(Project project, string stdlibRoot, TextWriter error)
    {
        var result = SourceCompiler.Lower(SourceOf(project), OptionsFor(project, null, null) with { StdlibRoot = stdlibRoot });
        if (!result.Render(error) || result.Ir is null) return null;
        if (!SubsetGate.Check(result.Ir, result.Diagnostics))
        {
            result.Diagnostics.RenderText(error);
            return null;
        }
        return CEmitter.Join(CEmitter.Emit(result.Ir, result.Sources, stdlibRoot, overflowChecks: true));
    }

    /// <summary><c>--emit ir</c> and <c>--emit c</c>: the text on <c>output</c>, the C as the
    /// profile has it emitted.</summary>
    public static int Emit(Project project, string what, TextWriter output, TextWriter error, BuildProfile? profile = null,
        Target? target = null)
    {
        // the scripts first, for gen/; their C is the build's, not the emission's
        if (CCompiler.Locate() is { } compiler)
        {
            var (ran, _) = BuildScripts.Run(project, Target.Host, profile ?? BuildProfile.Of(Lyric5.Toolchain.Profile.Debug), compiler,
                ScriptVersion, error);
            if (ran != 0) return ran;
        }
        var result = Compile(project, error, profile, target);
        if (result is null) return 1;
        output.Write(what == "ir"
            ? IrPrinter.Dump(result.Ir!)
            : CEmitter.Join(CEmitter.Emit(result.Ir!, result.Sources, StdlibRoot, profile?.OverflowChecks ?? true)));
        return 0;
    }

    /// <summary>
    /// The packages' lint levels for a build under <paramref name="profile"/>: a profile that denies
    /// warnings — <c>denyWarnings</c>, <c>--deny-warnings</c> — is <c>-D warnings</c> for the root
    /// package, in front of the command line's own (spec 16 §5): each warning is an error, and its
    /// note names the profile.
    /// </summary>
    internal static IReadOnlyDictionary<string, LintSettings> LintLevelsOf(IReadOnlyDictionary<string, LintSettings> levels,
        string root, BuildProfile? profile)
    {
        if (profile is not { DenyWarnings: true }) return levels;
        var denied = new Dictionary<string, LintSettings>(levels, StringComparer.Ordinal);
        var own = levels.GetValueOrDefault(root) ?? LintSettings.Default;
        denied[root] = new LintSettings(
            [new LintSettings.Rule(DiagnosticCatalog.AllLints, LintLevel.Deny, $"denyWarnings of the profile '{profile.Name}'"), .. own.CommandLine],
            own.Manifest);
        return denied;
    }

    /// <summary>The binary's path, or the exit code of the failure.</summary>
    public static (int Exit, string? Executable) Build(BuildRequest request, TextWriter error)
    {
        var project = request.Project;
        // One build at a time writes into out/; a second one waits here.
        using var held = OutLock.Take(project.OutDir, error);
        Directory.CreateDirectory(project.CacheDir);

        // The packages' build scripts before anything is compiled: what they write into
        // gen/ is a source of the program, what they ask is the build's.
        var (scripted, directives) = BuildScripts.Run(project, request.Target, request.Profile, request.Compiler,
            request.ToolchainVersion, error);
        if (directives is null) return (scripted, null);

        // The C, keyed by the sources, the toolchain and the compiler that emits it: the same
        // program emits the same C, so the front end runs only for a program that changed. The
        // module's unit is named by that key; a generic instance's unit by its own content,
        // under 'units/', where nothing else changes — so its object survives every edit that
        // leaves the instance alone. The list of units is written last: its presence says the C is
        // complete.
        // The C is the profile's too where the profile changes it: without overflow checks the
        // arithmetic wraps.
        // And what decides the findings is part of it: a build that is up to date runs no
        // front end and says nothing, so '-D warnings' after a plain build would pass on the plain
        // build's C — the command line's lint levels, '--warn-deps' and the profile's denyWarnings.
        var key = Key(SourcesKey(project), request.ToolchainVersion, CEmitter.Version, CompilerIdentity,
            request.Profile.OverflowChecks ? "checked" : "wrapping",
            project.LintArgs.Fingerprint, request.Profile.DenyWarnings ? "deny-warnings" : "");
        var manifest = Path.Combine(project.CacheDir, $"{project.BinaryName}-{key}.units");
        // What 'embed' read is an input the key cannot name before the front
        // end runs: the list beside the units says which, and a file that changed sends the
        // program through the front end again — into a unit named by what it read.
        var embeds = manifest + ".embeds";
        if (File.Exists(manifest) && !EmbedsHold(embeds)) File.Delete(manifest);
        // What the front end said is kept beside the units and said again by a build that finds
        // them: an up-to-date build says the warnings the one that made it said.
        var said = manifest + ".said";
        // The C name of each function and type and the Lyric one it stands for, beside the binary:
        // what a debugger shows a frame and a value as (lyric dap, lyric.lldbinit) — the C name is
        // not read back, '.' and '_' are both '_' in it.
        var names = manifest + ".names";
        List<string> sources;
        if (File.Exists(manifest))
        {
            DiagnosticStore.Replay(said, error);
            sources = File.ReadAllLines(manifest).Where(line => line.Length > 0).ToList();
        }
        else
        {
            var result = Compile(project, error, request.Profile, request.Target);
            if (result is null) return (1, null);
            var read = result.Embedded.Select(file => $"{FileKey(file)} {file}").ToList();
            var unitKey = read.Count == 0 ? key : Key(key, string.Join("\n", read));
            var paths = new List<string>();
            foreach (var unit in CEmitter.Emit(result.Ir!, result.Sources, StdlibRoot, request.Profile.OverflowChecks))
            {
                var path = unit.Instance is null
                    ? Path.Combine(project.CacheDir, $"{project.BinaryName}-{unitKey}.c")
                    : Path.Combine(project.CacheDir, "units", $"inst-{Key(Placeless(project, unit.Text))}.c");
                WriteOnce(path, unit.Text);
                paths.Add(path);
            }
            WriteOnce(names, string.Concat(CEmitter.DebugNames(result.Ir!, result.Sources).Select(line => line + "\n")));
            if (read.Count > 0) File.WriteAllLines(embeds, read);
            else File.Delete(embeds);
            // findings that cannot be read back — in a synthesized file — leave the units unlisted:
            // the next build runs the front end again and says them itself
            if (DiagnosticStore.Save(said, result.Sources, result.Diagnostics.Diagnostics))
                WriteOnce(manifest, string.Join("\n", paths) + "\n");
            sources = paths;
        }

        try
        {
            var archive = RuntimeArchive.For(request.Compiler, request.Target, request.Profile, request.ToolchainVersion);
            var build = new CBuild(request.Compiler, request.Target, request.Profile, project.CacheDir);
            var runtimeRoot = RuntimeArchive.SourceRoot()!;
            // The program's units carry what the profile asks of the program alone (fast-math), and
            // every unit names its paths by the packages' names, not by where they lie.
            var paths = PathMaps(project, runtimeRoot);
            var units = sources.Select(path => new CUnit(path, [], [RuntimeLayout.IncludeDir(runtimeRoot)],
                ExtraFlags: [.. request.Profile.ProgramFlags, .. paths])).ToList();
            var (native, libraries) = Native(project, request.Target, paths);
            foreach (var library in directives.Libraries)
                if (!libraries.Contains(library)) libraries.Add(library);
            var scripts = directives.Sources.Select(unit => unit with { ExtraFlags = [.. unit.ExtraFlags ?? [], .. paths] }).ToList();
            var objects = build.Compile([.. units, .. native, .. scripts]);
            var executable = project.Executable(request.Profile, request.Target);
            string[] inputs = [.. objects, archive];
            var record = Path.Combine(project.CacheDir, $"{project.BinaryName}-{Key(executable)}.link");
            var linkPaths = new LinkPaths(directives.SearchDirs, OwnDirFirst: directives.Beside.Count > 0);
            if (!UpToDate(executable, record, inputs, libraries, linkPaths))
            {
                build.LinkExecutable(inputs, executable, libraries, linkPaths);
                File.WriteAllText(record, LinkLines(inputs, libraries, linkPaths));
            }
            if (File.Exists(names)) File.Copy(names, NamesOf(executable), overwrite: true);
            PutBeside(directives.Beside, executable);
            return (0, executable);
        }
        catch (CBuildException failure)
        {
            Say.Error(error, "LYR-BLD0001", failure.Message);
            return (2, null);
        }
    }

    /// <summary>The native parts of the program's packages for the target: their C
    /// sources, each with its package's include directories, and their libraries, each once.</summary>
    private static (List<CUnit> Units, List<string> Libraries) Native(Project project, Target target, IReadOnlyList<string> paths)
    {
        var units = new List<CUnit>();
        var libraries = new List<string>();
        if (project.Graph is not { } graph) return (units, libraries);
        foreach (var manifest in graph.Packages.Values.OrderBy(m => m.Name, StringComparer.Ordinal))
        {
            var part = manifest.NativeOn(target.Os);
            var include = part.Include.Select(directory => Path.Combine(manifest.Root, directory)).ToList();
            units.AddRange(part.SourceFiles(manifest.Root).Select(file => new CUnit(file, [], include, ExtraFlags: paths)));
            foreach (var library in part.Libs)
                if (!libraries.Contains(library)) libraries.Add(library);
        }
        return (units, libraries);
    }

    /// <summary>
    /// The same bytes from wherever the package lies (as Go's <c>-trimpath</c>): each path a
    /// binary would carry — a <c>#line</c>'s file, the C file's own, the runtime's headers — written
    /// as the package's name or <c>lyric</c> instead (<c>-ffile-prefix-map</c>). The project's root
    /// first, then each package outside it: a prefix inside another would be mapped twice.
    /// </summary>
    private static List<string> PathMaps(Project project, string runtimeRoot) =>
        [.. Places(project).Append((From: runtimeRoot, To: "lyric"))
            .Select(m => $"-ffile-prefix-map={Path.TrimEndingDirectorySeparator(m.From)}={m.To}")];

    /// <summary>The names a binary's debug information writes for the places its sources lie —
    /// each package's name, <c>lyric</c> for the runtime —, with the places: what a debugger maps
    /// back to find a source.</summary>
    public static IReadOnlyList<(string Name, string Place)> SourcePlaces(Project project) =>
        [.. Places(project).Select(p => (p.To, Path.TrimEndingDirectorySeparator(p.From))),
         .. RuntimeArchive.SourceRoot() is { } runtime ? new[] { ("lyric", Path.TrimEndingDirectorySeparator(runtime)) } : []];

    /// <summary>Where the program's packages lie, and the name each is written as: the project's
    /// root first, then each package outside it.</summary>
    internal static List<(string From, string To)> Places(Project project)
    {
        var places = new List<(string From, string To)> { (project.Root, project.Name) };
        if (project.Graph is { } graph)
            foreach (var (name, manifest) in graph.Packages.OrderBy(p => p.Key, StringComparer.Ordinal))
                if (!places.Any(m => Inside(manifest.Root, m.From))) places.Add((manifest.Root, name));
        return places;
    }

    /// <summary>A unit's text with the packages' places written as their names — as its #line
    /// directives spell them, backslashes doubled — for a name that does not depend on where the
    /// program lies.</summary>
    private static string Placeless(Project project, string text)
    {
        foreach (var (from, to) in Places(project))
        {
            var root = Path.TrimEndingDirectorySeparator(from);
            text = text.Replace(root.Replace("\\", "\\\\"), to, StringComparison.Ordinal).Replace(root, to, StringComparison.Ordinal);
        }
        return text;
    }

    private static bool Inside(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }

    /// <summary>The compiler, by the identity of its assemblies — the front end's and the
    /// emitter's. A deterministic build gives the same bytes the same id, so a compiler that
    /// changed keys new C even where no version string moved — a change to the lowering alone
    /// once reused the old C — and an unchanged one finds what it emitted before.</summary>
    /// <summary>The compiler's identity, for what else is keyed by it — a build script's state.</summary>
    internal static string CompilerKey => CompilerIdentity;

    /// <summary>The version a build script's key carries where no request names one (<c>--emit</c>).</summary>
    private const string ScriptVersion = "emit";

    /// <summary>Every package's <c>gen/</c> that is there, by name.</summary>
    internal static IReadOnlyDictionary<string, string>? GenRoots(Project project) =>
        project.Graph?.Packages.Where(p => !p.Value.IsScript && Directory.Exists(p.Value.GenRoot))
            .ToDictionary(p => p.Key, p => p.Value.GenRoot, StringComparer.Ordinal);

    private static readonly string CompilerIdentity = string.Join(",",
        typeof(SourceCompiler).Assembly.ManifestModule.ModuleVersionId,
        typeof(CEmitter).Assembly.ManifestModule.ModuleVersionId);

    /// <summary>What the C depends on besides the compiler: the entry module, every source file of
    /// the package — its manifest too — or the single file, and every file of the standard library,
    /// each by its path and its content. Keyed by the entry file alone, a change to an imported
    /// module — or to the standard library beside a toolchain whose version did not move — reused
    /// the old C.</summary>
    private static string SourcesKey(Project project)
    {
        var parts = new List<string> { project.Module };
        if (project.Graph is { } graph)
        {
            // Every package of the graph: a changed dependency builds anew as a changed module does.
            foreach (var (name, manifest) in graph.Packages.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                parts.Add(name);
                parts.Add(File.ReadAllText(manifest.File));
                if (!manifest.IsScript) AddTree(parts, manifest.SourceRoot);
                if (!manifest.IsScript && Directory.Exists(manifest.GenRoot))
                {
                    parts.Add("gen/");
                    AddTree(parts, manifest.GenRoot);
                }
            }
        }
        // A single file — a build script among them, with a graph of its own or none — is its own
        // entry, outside every package's src/, and its module spaces beside it.
        if (project.Graph is null || project.Manifest is null) parts.Add(File.ReadAllText(project.Source));
        if (project.ModuleRoots is { } modules)
            foreach (var (name, root) in modules.OrderBy(m => m.Key, StringComparer.Ordinal))
            {
                parts.Add(name);
                if (Directory.Exists(root)) AddTree(parts, root);
            }
        // The test program: what the toolchain wrote, and the package's tests/ it reads.
        if (project.EntryText is { } text) parts.Add(text);
        if (project.TestRoot is { } tests)
        {
            parts.Add("tests/");
            AddTree(parts, tests);
        }
        AddTree(parts, StdlibRoot);
        return Key([.. parts]);
    }

    /// <summary>
    /// A library built by itself: every module under its <c>src/</c> through
    /// the front end and the lowering, one compilation — a check; there is no program to build. Exit
    /// 0 when it holds, 1 after reporting.
    /// </summary>
    public static int CheckLibrary(LibraryException library, TextWriter error, BuildProfile? profile = null, LintArgs? lints = null)
    {
        var manifest = library.Manifest;
        var roots = Directory.Exists(manifest.SourceRoot)
            ? Directory.EnumerateFiles(manifest.SourceRoot, "*.lyr", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(file => ScriptSource.FromDisk(file, Project.ModulePathOf(manifest, file)!))
                .ToList()
            : [];
        // the library's packages at their own levels, the command line's in front of the library's
        var levels = library.Graph.Packages.ToDictionary(p => p.Key,
            p => new LintSettings(p.Key == manifest.Name ? (lints ?? LintArgs.None).Rules : [], p.Value.Lints), StringComparer.Ordinal);
        var options = new CompilerOptions
        {
            StdlibRoot = StdlibRoot, PackageRoots = library.Graph.SourceRoots,
            PackageDependencies = library.Graph.Declared,
            LintLevels = LintLevelsOf(levels, manifest.Name, profile), RootPackage = manifest.Name,
            WarnDeps = (lints ?? LintArgs.None).WarnDeps,
        };
        var result = SourceCompiler.CheckProject(roots, options);
        return result.Render(error) ? 0 : 1;
    }

    /// <summary>
    /// <c>lyric check</c> (spec 14 §1.1): a program through everything its build does before the C —
    /// the build scripts, for what they write into <c>gen/</c>, the front end and its lints,
    /// <c>comptime</c>, the lowering and its gate —, its findings said as <c>build</c> says them; no
    /// C and no binary. Exit 0, or 1 after reporting.
    /// </summary>
    public static int CheckProgram(Project project, BuildProfile profile, Target target, string version, TextWriter error)
    {
        using var held = OutLock.Take(project.OutDir, error);
        if (CCompiler.Locate() is { } compiler)
        {
            var (ran, directives) = BuildScripts.Run(project, target, profile, compiler, version, error);
            if (directives is null) return ran;
        }
        return Compile(project, error, profile, target) is null ? 1 : 0;
    }

    /// <summary>The front end on a program's source, nothing rendered: what <c>lyric fix</c> reads.</summary>
    public static CompileResult Check(Project project) =>
        SourceCompiler.Check(SourceOf(project), OptionsFor(project, null, null));

    /// <summary>The options a program's source is checked under, without a profile: what the
    /// editor checks a single file with.</summary>
    public static CompilerOptions CheckOptions(Project project) => OptionsFor(project, null, null);

    /// <summary>The front end on a library's modules, nothing rendered: what <c>lyric fix</c> reads.</summary>
    public static CompileResult Check(LibraryException library, LintArgs? lints) =>
        CheckPackage(library.Manifest, library.Graph, lints);

    /// <summary>The front end on every module under a package's <c>src/</c>, a program's as a
    /// library's, nothing rendered: what <c>lyric doc</c> reads.</summary>
    public static CompileResult CheckPackage(Manifest manifest, PackageGraph graph, LintArgs? lints = null)
    {
        var roots = Directory.Exists(manifest.SourceRoot)
            ? Directory.EnumerateFiles(manifest.SourceRoot, "*.lyr", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(file => ScriptSource.FromDisk(file, Project.ModulePathOf(manifest, file)!))
                .ToList()
            : [];
        var levels = graph.Packages.ToDictionary(p => p.Key,
            p => new LintSettings(p.Key == manifest.Name ? (lints ?? LintArgs.None).Rules : [], p.Value.Lints), StringComparer.Ordinal);
        return SourceCompiler.CheckProject(roots, new CompilerOptions
        {
            StdlibRoot = StdlibRoot, PackageRoots = graph.SourceRoots,
            PackageDependencies = graph.Declared,
            LintLevels = LintLevelsOf(levels, manifest.Name, null), RootPackage = manifest.Name,
            WarnDeps = (lints ?? LintArgs.None).WarnDeps,
        });
    }

    internal static void AddTree(List<string> parts, string root)
    {
        if (!Directory.Exists(root)) return;
        foreach (var file in Directory.EnumerateFiles(root, "*.lyr", SearchOption.AllDirectories)
                     .Select(file => (Relative: Path.GetRelativePath(root, file).Replace('\\', '/'), Full: file))
                     .OrderBy(file => file.Relative, StringComparer.Ordinal))
        {
            parts.Add(file.Relative);
            parts.Add(File.ReadAllText(file.Full));
        }
    }

    /// <summary>A content-named file is written once: a second writer of the same path writes
    /// the same bytes, and the first to move wins.</summary>
    private static void WriteOnce(string path, string text)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var partial = $"{path}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(partial, text);
        try { File.Move(partial, path, overwrite: false); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException && File.Exists(path)) { File.Delete(partial); }
    }

    /// <summary>A file's content as a key: what an embedded file was when the program read it.</summary>
    private static string FileKey(string file) => System.Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)));

    /// <summary>Whether every file the cached program embedded is still what it read — none
    /// embedded holds trivially.</summary>
    private static bool EmbedsHold(string embeds)
    {
        if (!File.Exists(embeds)) return true;
        foreach (var line in File.ReadAllLines(embeds))
        {
            var at = line.IndexOf(' ');
            if (at < 0) return false;
            var file = line[(at + 1)..];
            if (!File.Exists(file) || FileKey(file) != line[..at]) return false;
        }
        return true;
    }

    /// <summary>A binary is that link when it was linked from these very inputs — the record in the
    /// cache names them — and is newer than each. Newer alone is not enough: the inputs are
    /// content-keyed and cached, so a build that goes back to inputs built earlier — the other
    /// collector's archive, a profile field and back — finds every one older than the
    /// binary the other inputs made.</summary>
    private static bool UpToDate(string executable, string record, IReadOnlyList<string> inputs, IReadOnlyList<string> libraries,
        LinkPaths paths)
    {
        if (!File.Exists(executable)) return false;
        if (!File.Exists(record) || File.ReadAllText(record) != LinkLines(inputs, libraries, paths)) return false;
        var linked = File.GetLastWriteTimeUtc(executable);
        return inputs.All(input => File.GetLastWriteTimeUtc(input) <= linked);
    }

    private static string LinkLines(IEnumerable<string> inputs, IEnumerable<string> libraries, LinkPaths paths) =>
        string.Join("\n", inputs
            .Concat(paths.SearchDirs.Select(dir => "-L" + dir))
            .Concat(libraries.Select(library => "-l" + library))
            .Concat(paths.OwnDirFirst ? ["own-dir-first"] : [])) + "\n";

    /// <summary>The files the build scripts put beside the binary, copied into its directory under
    /// their own names — each only where what lies there is not already the same bytes, so an
    /// up-to-date build touches nothing.</summary>
    private static void PutBeside(IReadOnlyList<string> files, string executable)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(executable))!;
        foreach (var file in files)
        {
            var target = Path.Combine(dir, Path.GetFileName(file));
            if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(file).Length
                && File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(file)))
                continue;
            File.Copy(file, target, overwrite: true);
        }
    }

    internal static string Key(params string[] parts)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var part in parts)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(part));
            sha.AppendData([0]);
        }
        return System.Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }
}
