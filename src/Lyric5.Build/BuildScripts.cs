using System.Security.Cryptography;
using System.Text;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>A C library a build script compiles with the program (<c>std.build.cLibrary</c>): its
/// sources — patterns of the package or of a tree —, its include directories, definitions and flags,
/// as the script wrote them.</summary>
public sealed record ScriptLibrary(string Name, IReadOnlyList<string> Sources, IReadOnlyList<string> Include,
    IReadOnlyList<string> Defines, IReadOnlyList<string> Flags);

/// <summary>What the program's build scripts asked of its build: by package, what its native part
/// links besides the manifest's — libraries by name, archives, frameworks, names of
/// <c>pkg-config</c> — and the C libraries it compiles; the directories the link searches, and the
/// files that go beside each binary — full paths, each once.</summary>
public sealed record ScriptDirectives(IReadOnlyDictionary<string, NativePart> Parts,
    IReadOnlyDictionary<string, IReadOnlyList<ScriptLibrary>> Libraries, IReadOnlyList<string> SearchDirs, IReadOnlyList<string> Beside)
{
    public static readonly ScriptDirectives None =
        new(new Dictionary<string, NativePart>(), new Dictionary<string, IReadOnlyList<ScriptLibrary>>(), [], []);

    /// <summary>What the package's script adds to its native part; nothing where it has none.</summary>
    public NativePart PartOf(string package) => Parts.GetValueOrDefault(package) ?? NativePart.None;

    /// <summary>The C libraries the package's script compiles.</summary>
    public IReadOnlyList<ScriptLibrary> LibrariesOf(string package) => Libraries.GetValueOrDefault(package) ?? [];
}

/// <summary>
/// The packages' build scripts: a package's
/// <c>build.lyr</c> is an ordinary program that runs before the package compiles. It does things
/// — writes modules into <c>gen/</c>, names C to compile and libraries to link — and defines
/// nothing (the manifest is read, never run).
///
/// <para>A script is a program of its own: its module is <c>build</c>, a file
/// <c>build/x.lyr</c> beside it the module <c>build.x</c>, std its only library, and nothing of it
/// is compiled into the package. It is built for the host in the debug profile under the package's
/// <c>out/build-script/</c>, and runs with the package's root as its working directory; what the
/// build is comes in its environment, what it asks goes back as directives in a file the toolchain
/// names (<c>std.build</c>) — its own output stays its own, kept in <c>output.txt</c> and shown
/// where it fails.</para>
///
/// <para>When: before the compile, where the script — <c>build.lyr</c>, <c>build/</c>, std, the
/// toolchain, the target, the profile — changed, where a file it named with
/// <c>rerunIfChanged</c> changed, where a variable it named with <c>rerunIfEnvChanged</c> has another
/// value, or where <c>gen/</c> is not as it left it; else what its last run asked stands. Which: the root package's script always; a dependency's only where the root
/// manifest's <c>[trust] build-scripts</c> names it (pnpm 10), else the build stops before it.</para>
/// </summary>
/// <summary>A script's request the build cannot follow.</summary>
internal sealed class ScriptRefusal(string message) : Exception(message);

public static class BuildScripts
{
    /// <summary>The directives of every script of the program's packages — dependencies first, by
    /// name, the root's last —, or the exit code of a build that stops here, reported. Then the
    /// packages' bindings for the target, which a script's <c>gen/</c> may feed: what runs before the
    /// compiler, run where everything that compiles runs it.</summary>
    public static (int Exit, ScriptDirectives? Directives) Run(Project project, Target target, BuildProfile profile,
        CCompiler compiler, string version, TextWriter error)
    {
        var (exit, directives) = RunScripts(project, target, profile, compiler, version, error);
        if (directives is null) return (exit, null);
        var bound = Bindings.Make(project, target, compiler, version, error);
        return bound == 0 ? (exit, directives) : (bound, null);
    }

    private static (int Exit, ScriptDirectives? Directives) RunScripts(Project project, Target target, BuildProfile profile,
        CCompiler compiler, string version, TextWriter error)
    {
        if (project.Graph is not { } graph) return (0, ScriptDirectives.None);
        var root = graph.Root;
        var parts = new Dictionary<string, NativePart>(StringComparer.Ordinal);
        var cLibraries = new Dictionary<string, IReadOnlyList<ScriptLibrary>>(StringComparer.Ordinal);
        var searchDirs = new List<string>();
        var beside = new List<(string Path, string Package)>();
        var order = graph.Packages.Values.Where(m => m.Name != root.Name).OrderBy(m => m.Name, StringComparer.Ordinal).Append(root);
        // a script's 'warn' is the lint 'build-script-warning', at its package's level (spec 16 §5);
        // a dependency's script warns with --warn-deps alone, and never as an error
        var levels = Pipeline.LintLevelsOf(project.LintLevels, root.Name, profile);
        var scriptWarning = Lyric.Core.DiagnosticCatalog.Entries["LYR-BLD0003"];
        foreach (var manifest in order)
        {
            if (manifest.BuildScript is null) continue;
            if (manifest.Name != root.Name && !root.TrustedScripts.Contains(manifest.Name))
            {
                var (file, line) = DeclaredAt(graph, manifest.Name) ?? (root.File, 1);
                Say.Manifest(error, new ManifestException("LYR-PKG0012", file, line, 1,
                    $"package '{manifest.Name}' has a build script, and a dependency's script runs only where the root trusts it: " +
                    $"[trust] build-scripts = [\"{manifest.Name}\"] in {Path.GetFileName(root.File)}"));
                return (1, null);
            }
            // a tree the script asks for is read then, as one the native part names (spec 15 §5.3)
            string Vendor(string name) => manifest.Vendor.Any(t => t.Name == name) && graph.Trees is { } trees
                ? trees.Directory(manifest, name)
                : throw new ScriptRefusal($"{manifest.Name}'s build.lyr asks for the source tree '{name}', which its [native.vendor] does not hold");
            int exit;
            List<(string Word, string Text)>? directives;
            try { (exit, directives) = RunOne(manifest, graph.Scripts.GetValueOrDefault(manifest.Name), target, profile, compiler, version, Vendor, error); }
            catch (ScriptRefusal refused)
            {
                Say.Error(error, "LYR-BLD0002", refused.Message);
                return (1, null);
            }
            catch (ManifestException refused)
            {
                Say.Manifest(error, refused);
                return (refused.Exit, null);
            }
            if (directives is null) return (exit, null);

            // what the script adds to the native part, with the manifest's rules for its paths
            List<string> libs = [], archives = [], frameworks = [], pkgConfig = [];
            var compiled = new List<ScriptLibrary>();
            (string Name, List<string> Sources, List<string> Include, List<string> Defines, List<string> Flags)? library = null;
            string? Refusal(string what, string path) =>
                NativePart.ScriptPathRefusal(manifest, path) is { } why ? $"{manifest.Name}'s build.lyr names '{path}' {what}, and it {why}" : null;
            foreach (var (word, text) in directives)
            {
                string? wrong = null;
                switch (word)
                {
                    case "link-lib":
                        if (!libs.Contains(text)) libs.Add(text);
                        break;
                    case "link-archive":
                        // an archive the script made — under out/, or anywhere — is its own: by its full
                        // path, no file of the package's content asked of it
                        var archive = NativePart.TreePath(text) is not null ? text : Path.GetFullPath(text, manifest.Root);
                        if (NativePart.TreePath(text) is not null) wrong = Refusal("in linkArchive", text);
                        if (!archives.Contains(archive)) archives.Add(archive);
                        break;
                    case "link-framework":
                        if (!frameworks.Contains(text)) frameworks.Add(text);
                        break;
                    case "pkg-config":
                        if (!pkgConfig.Contains(text)) pkgConfig.Add(text);
                        break;
                    case "c-library":
                        library = (text, [], [], [], []);
                        break;
                    case "c-source" when library is { } open:
                        wrong = Refusal("as a source of its C library", text);
                        open.Sources.Add(text);
                        break;
                    case "c-include" when library is { } open:
                        wrong = Refusal("as an include directory of its C library", text);
                        open.Include.Add(text);
                        break;
                    case "c-define" when library is { } open:
                        open.Defines.Add(text);
                        break;
                    case "c-flag" when library is { } open:
                        open.Flags.Add(text);
                        break;
                    case "c-end" when library is { } done:
                        compiled.Add(new ScriptLibrary(done.Name, done.Sources, done.Include, done.Defines, done.Flags));
                        library = null;
                        break;
                    case "link-search":
                        var dir = Path.GetFullPath(text, manifest.Root);
                        if (!searchDirs.Contains(dir)) searchDirs.Add(dir);
                        break;
                    case "beside-binary":
                        var file = Path.GetFullPath(text, manifest.Root);
                        if (!beside.Any(b => b.Path == file)) beside.Add((file, manifest.Name));
                        break;
                    case "warn":
                        var foreign = manifest.Name != root.Name;
                        if (foreign && !project.LintArgs.WarnDeps) break;
                        var (level, origin) = (levels.GetValueOrDefault(manifest.Name) ?? Lyric.Core.LintSettings.Default).LevelOf(scriptWarning);
                        // another package's levels never fail this build
                        if (foreign && level == Lyric.Core.LintLevel.Deny) level = Lyric.Core.LintLevel.Warn;
                        var said = $"{manifest.Name}'s build.lyr: {text}";
                        if (level == Lyric.Core.LintLevel.Warn) Say.Warning(error, "LYR-BLD0003", said);
                        else if (level == Lyric.Core.LintLevel.Deny)
                        {
                            Say.ErrorWithNotes(error, "LYR-BLD0003", said,
                                [new Lyric.Core.DiagnosticNote($"'build-script-warning' is denied by {origin ?? "its own level"}")]);
                            return (1, null);
                        }
                        break;
                }
                if (wrong is not null)
                {
                    Say.Error(error, "LYR-BLD0002", wrong);
                    return (1, null);
                }
            }
            parts[manifest.Name] = NativePart.None with { Libs = libs, Archives = archives, Frameworks = frameworks, PkgConfig = pkgConfig };
            cLibraries[manifest.Name] = compiled;
        }
        // what goes beside the binary is there, and no two files take one name there
        foreach (var (file, package) in beside)
            if (!File.Exists(file))
            {
                Say.Error(error, "LYR-BLD0006", $"{package}'s build.lyr puts '{file}' beside the binary, and there is no such file");
                return (1, null);
            }
        foreach (var clash in beside.GroupBy(b => Path.GetFileName(b.Path), FileNames).Where(g => g.Count() > 1))
        {
            Say.Error(error, "LYR-BLD0006",
                $"two files named '{clash.Key}' would lie beside the binary: {string.Join(" and ", clash.Select(b => $"'{b.Path}' ({b.Package})"))}");
            return (1, null);
        }
        return (0, new ScriptDirectives(parts, cLibraries, searchDirs, [.. beside.Select(b => b.Path)]));
    }

    /// <summary>File names as the systems compare them: Windows and macOS's default volumes
    /// do not tell 'SDL3.dll' from 'sdl3.dll'.</summary>
    internal static readonly StringComparer FileNames =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>Where the graph's manifests declare the package — the root's first, for the line a
    /// refusal points at.</summary>
    private static (string File, int Line)? DeclaredAt(PackageGraph graph, string name)
    {
        foreach (var manifest in graph.Packages.Values.OrderBy(m => m == graph.Root ? 0 : 1).ThenBy(m => m.Name, StringComparer.Ordinal))
            if (manifest.Dependencies.FirstOrDefault(d => d.Name == name) is { } dependency)
                return (manifest.File, dependency.Line);
        return null;
    }

    /// <summary>One package's script: what its last run asked where nothing it depends on changed,
    /// else built, run and read anew; <c>null</c> with the exit code after reporting.</summary>
    private static (int Exit, List<(string Word, string Text)>? Directives) RunOne(Manifest manifest, PackageGraph? own,
        Target target, BuildProfile profile, CCompiler compiler, string version, Func<string, string> vendor, TextWriter error)
    {
        var dir = Path.Combine(manifest.Root, "out", "build-script");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(manifest.GenRoot);
        var state = Path.Combine(dir, "state");
        var key = ScriptKey(manifest, own, target, profile, version);
        if (State.Read(state) is { } last && last.Key == key && last.Fresh(manifest)) return (0, last.Directives);

        // the script, a program of its own: `build` and `build/`, its [build-dependencies]
        var script = new Project(manifest.BuildScript!, "build", "build", dir, null)
        {
            Graph = own,
            ModuleRoots = new Dictionary<string, string> { ["build"] = Path.Combine(manifest.Root, "build") },
        };
        var (built, executable) = Pipeline.Build(new BuildRequest(script, BuildProfile.Of(Lyric5.Toolchain.Profile.Debug), Target.Host,
            compiler, version), error);
        if (built != 0 || executable is null)
        {
            Say.Error(error, "LYR-BLD0002", $"{manifest.Name}'s build.lyr does not build, so the package does not");
            return (built == 0 ? 1 : built, null);
        }

        var file = Path.Combine(dir, "directives");
        var (cc, ar, cflags) = CEnv(compiler, target, profile, dir);
        var environment = new Dictionary<string, string>
        {
            ["LYRIC_BUILD_TARGET"] = target.Triple,
            ["LYRIC_BUILD_HOST"] = Target.Host.Triple,
            ["LYRIC_BUILD_PROFILE"] = profile.Name,
            ["LYRIC_BUILD_OUT_DIR"] = Path.Combine(manifest.Root, "out"),
            ["LYRIC_BUILD_GEN_DIR"] = manifest.GenRoot,
            ["LYRIC_BUILD_DIRECTIVES"] = file,
            ["LYRIC_BUILD_CC"] = cc,
            ["LYRIC_BUILD_AR"] = ar,
            ["LYRIC_BUILD_CFLAGS"] = cflags,
        };
        ProcessRunner.Result ran;
        // a script that asks for a source tree not yet read stops; it runs again with the tree there
        for (var asked = 0; ; asked++)
        {
            File.WriteAllText(file, "");
            try
            {
                ran = ProcessRunner.Run(executable, [], TimeSpan.FromMinutes(10), manifest.Root, environment);
            }
            catch (TimeoutException)
            {
                Say.Error(error, "LYR-BLD0002", $"{manifest.Name}'s build.lyr did not end within 10 minutes");
                // the package's own code failed, as one that does not compile: exit 1, not the environment's 2
                return (1, null);
            }
            if (ran.ExitCode != VendorExit || asked > manifest.Vendor.Count) break;
            var wanted = File.ReadAllLines(file).LastOrDefault(l => l.StartsWith("vendor-dir\t", StringComparison.Ordinal));
            if (wanted is null) break;
            var name = wanted["vendor-dir\t".Length..];
            environment[$"LYRIC_BUILD_VENDOR_{name.ToUpperInvariant().Replace('-', '_')}"] = vendor(name);
        }
        File.WriteAllText(Path.Combine(dir, "output.txt"), ran.Stdout + ran.Stderr);
        if (ran.ExitCode != 0)
        {
            // what the script wrote comes after the finding as it was written, or, in the JSON form, as its note
            var wrote = ran.Stdout + ran.Stderr;
            var message = $"{manifest.Name}'s build.lyr ended with exit {ran.ExitCode}, and the package is not built";
            if (Lyric.Core.Rendering.RenderStyle.Current.Events is not null)
                Say.ErrorWithNotes(error, "LYR-BLD0002", message, wrote.Length > 0 ? [new Lyric.Core.DiagnosticNote($"what it wrote:\n{wrote}")] : []);
            else
            {
                Say.Error(error, "LYR-BLD0002", wrote.Length > 0 ? message + "; what it wrote:" : message);
                error.Write(wrote);
            }
            return (1, null);
        }
        var directives = new List<(string, string)>();
        foreach (var line in File.ReadAllLines(file))
        {
            var tab = line.IndexOf('\t');
            if (tab > 0) directives.Add((line[..tab], line[(tab + 1)..]));
        }
        new State(key, directives).Write(state, manifest);
        return (0, directives);
    }

    /// <summary>The exit with which <c>std.build.vendorDir</c> stops a script that asks for a source
    /// tree not read yet: the build reads it and runs the script again.</summary>
    private const int VendorExit = 75;

    /// <summary>What <c>std.build.cEnv</c> hands another build system: <c>CC</c> and <c>AR</c> — zig's
    /// as small scripts beside the build script, a program that takes the arguments a C compiler
    /// takes, for the target —, and <c>CFLAGS</c>, the profile's optimization.</summary>
    private static (string CC, string AR, string CFlags) CEnv(CCompiler compiler, Target target, BuildProfile profile, string dir)
    {
        var cflags = profile.Opt > 0 ? $"-O{profile.Opt}" : "-O0 -g";
        if (compiler.Kind != CCompilerKind.Zig) return (compiler.Path, "ar", cflags);
        string Wrapper(string name, string command)
        {
            var windows = OperatingSystem.IsWindows();
            var path = Path.Combine(dir, windows ? name + ".cmd" : name);
            var text = windows
                ? $"@\"{compiler.Path}\" {command} %*\r\n"
                : $"#!/bin/sh\nexec \"{compiler.Path}\" {command} \"$@\"\n";
            if (!File.Exists(path) || File.ReadAllText(path) != text) File.WriteAllText(path, text);
            if (!windows) File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
            return path;
        }
        // zig turns UBSan on by itself at -O0, with a runtime the program's link does not bring: off,
        // as for the build's own C (CBuild.FlagsFor)
        return (Wrapper("cc", $"cc -target {target.Triple} -fno-sanitize=undefined"), Wrapper("ar", "ar"), cflags);
    }

    /// <summary>What the script is and what it builds for: its files, std, the toolchain, the
    /// compiler that builds it, the target and the profile it is told.</summary>
    private static string ScriptKey(Manifest manifest, PackageGraph? own, Target target, BuildProfile profile, string version)
    {
        var parts = new List<string> { File.ReadAllText(manifest.BuildScript!), version, Pipeline.CompilerKey, target.Triple, profile.Name };
        var modules = Path.Combine(manifest.Root, "build");
        if (Directory.Exists(modules)) Pipeline.AddTree(parts, modules);
        // its own dependencies, each by its manifest and its sources
        if (own is not null)
            foreach (var (name, package) in own.Packages.Where(p => !p.Value.IsScript).OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                parts.Add(name);
                parts.Add(File.ReadAllText(package.File));
                if (Directory.Exists(package.SourceRoot)) Pipeline.AddTree(parts, package.SourceRoot);
            }
        Pipeline.AddTree(parts, Pipeline.StdlibRoot);
        return Pipeline.Key([.. parts]);
    }

    /// <summary>A file's content as a key, <c>-</c> for one that is not there.</summary>
    private static string Content(string path) =>
        File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "-";

    /// <summary>An environment variable's value as a key, <c>-</c> where it is not set: a hash, so
    /// that what the variable holds — a token, perhaps — is not written under <c>out/</c>.</summary>
    private static string EnvValue(string name) =>
        Environment.GetEnvironmentVariable(name) is { } value
            ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            : "-";

    /// <summary>A directory's tree as a key — what the script left in <c>gen/</c>.</summary>
    private static string Tree(string directory)
    {
        if (!Directory.Exists(directory)) return "-";
        var parts = new List<string>();
        Pipeline.AddTree(parts, directory);
        return Pipeline.Key([.. parts]);
    }

    /// <summary>What the last run left: the script's key, the inputs it named with the content they
    /// had, the environment variables it named with the values they had, the tree of <c>gen/</c>,
    /// and its directives — a line each.</summary>
    private sealed record State(string Key, List<(string Word, string Text)> Directives)
    {
        public Dictionary<string, string> Inputs { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Env { get; init; } = new(StringComparer.Ordinal);
        public string Gen { get; init; } = "-";

        public bool Fresh(Manifest manifest) =>
            Gen == Tree(manifest.GenRoot)
            && Inputs.All(input => Content(Path.GetFullPath(input.Key, manifest.Root)) == input.Value)
            && Env.All(variable => EnvValue(variable.Key) == variable.Value);

        public void Write(string file, Manifest manifest)
        {
            var text = new StringBuilder();
            text.Append("key\t").Append(Key).Append('\n');
            text.Append("gen\t").Append(Tree(manifest.GenRoot)).Append('\n');
            foreach (var (word, path) in Directives.Where(d => d.Word == "rerun-if-changed").Distinct())
                text.Append("input\t").Append(Content(Path.GetFullPath(path, manifest.Root))).Append('\t').Append(path).Append('\n');
            foreach (var (word, name) in Directives.Where(d => d.Word == "rerun-if-env-changed").Distinct())
                text.Append("env\t").Append(EnvValue(name)).Append('\t').Append(name).Append('\n');
            foreach (var (word, value) in Directives) text.Append("directive\t").Append(word).Append('\t').Append(value).Append('\n');
            File.WriteAllText(file, text.ToString());
        }

        public static State? Read(string file)
        {
            if (!File.Exists(file)) return null;
            string? key = null, gen = null;
            var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
            var env = new Dictionary<string, string>(StringComparer.Ordinal);
            var directives = new List<(string, string)>();
            foreach (var line in File.ReadAllLines(file))
            {
                var parts = line.Split('\t', 3);
                switch (parts)
                {
                    case ["key", var k]: key = k; break;
                    case ["gen", var g]: gen = g; break;
                    case ["input", var content, var path]: inputs[path] = content; break;
                    case ["env", var value, var name]: env[name] = value; break;
                    case ["directive", var word, var text]: directives.Add((word, text)); break;
                }
            }
            return key is null ? null : new State(key, directives) { Inputs = inputs, Env = env, Gen = gen ?? "-" };
        }
    }
}
