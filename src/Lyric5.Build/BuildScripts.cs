using System.Security.Cryptography;
using System.Text;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>What the program's build scripts asked of its build (design/v5/spec/11 W3 BS2): C to
/// compile with the program, each with its flags, and libraries to link.</summary>
public sealed record ScriptDirectives(IReadOnlyList<CUnit> Sources, IReadOnlyList<string> Libraries)
{
    public static readonly ScriptDirectives None = new([], []);
}

/// <summary>
/// The packages' build scripts (design/v5/spec/11 W3 BS1–BS6; M8b S13a): a package's
/// <c>build.lyr</c> is an ordinary program that runs before the package compiles. It does things
/// — writes modules into <c>gen/</c>, names C to compile and libraries to link — and defines
/// nothing (P1: the manifest is read, never run).
///
/// <para>A script is a program of its own (BS3): its module is <c>build</c>, a file
/// <c>build/x.lyr</c> beside it the module <c>build.x</c>, std its only library, and nothing of it
/// is compiled into the package. It is built for the host in the debug profile under the package's
/// <c>out/build-script/</c>, and runs with the package's root as its working directory; what the
/// build is comes in its environment, what it asks goes back as directives in a file the toolchain
/// names (<c>std.build</c>) — its own output stays its own, kept in <c>output.txt</c> and shown
/// where it fails.</para>
///
/// <para>WHEN (BS4): before the compile, where the script — <c>build.lyr</c>, <c>build/</c>, std, the
/// toolchain, the target, the profile — changed, where a file it named with
/// <c>rerunIfChanged</c> changed, or where <c>gen/</c> is not as it left it; else what its last run
/// asked stands. WHO (BS5): the root package's script always; a dependency's only where the root
/// manifest's <c>[trust] build-scripts</c> names it (pnpm 10), else the build stops before it.</para>
/// </summary>
public static class BuildScripts
{
    /// <summary>The directives of every script of the program's packages — dependencies first, by
    /// name, the root's last —, or the exit code of a build that stops here, reported.</summary>
    public static (int Exit, ScriptDirectives? Directives) Run(Project project, Target target, BuildProfile profile,
        CCompiler compiler, string version, TextWriter error)
    {
        if (project.Graph is not { } graph) return (0, ScriptDirectives.None);
        var root = graph.Root;
        var sources = new List<CUnit>();
        var libraries = new List<string>();
        var order = graph.Packages.Values.Where(m => m.Name != root.Name).OrderBy(m => m.Name, StringComparer.Ordinal).Append(root);
        foreach (var manifest in order)
        {
            if (manifest.BuildScript is null) continue;
            if (manifest.Name != root.Name && !root.TrustedScripts.Contains(manifest.Name))
            {
                var (file, line) = DeclaredAt(graph, manifest.Name) ?? (root.File, 1);
                error.WriteLine(new ManifestException("LYR-PKG0012", file, line, 1,
                    $"package '{manifest.Name}' has a build script, and a dependency's script runs only where the root trusts it: " +
                    $"[trust] build-scripts = [\"{manifest.Name}\"] in {Path.GetFileName(root.File)} (11 W3 BS5)").Render());
                return (1, null);
            }
            var (exit, directives) = RunOne(manifest, graph.Scripts.GetValueOrDefault(manifest.Name), target, profile, compiler, version, error);
            if (directives is null) return (exit, null);

            // compile-c's sources with this package's c-flags; link-lib's libraries once each
            var flags = directives.Where(d => d.Word == "c-flag").Select(d => d.Text).ToList();
            foreach (var (word, text) in directives)
            {
                switch (word)
                {
                    case "compile-c":
                        sources.Add(new CUnit(Path.GetFullPath(text, manifest.Root), [], [manifest.Root], ExtraFlags: flags));
                        break;
                    case "link-lib":
                        if (!libraries.Contains(text)) libraries.Add(text);
                        break;
                    case "warn":
                        error.WriteLine($"warning[LYR-BLD0003]: {manifest.Name}'s build.lyr: {text}");
                        break;
                }
            }
        }
        return (0, new ScriptDirectives(sources, libraries));
    }

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
        Target target, BuildProfile profile, CCompiler compiler, string version, TextWriter error)
    {
        var dir = Path.Combine(manifest.Root, "out", "build-script");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(manifest.GenRoot);
        var state = Path.Combine(dir, "state");
        var key = ScriptKey(manifest, own, target, profile, version);
        if (State.Read(state) is { } last && last.Key == key && last.Fresh(manifest)) return (0, last.Directives);

        // the script, a program of its own (BS3): `build` and `build/`, its [build-dependencies]
        var script = new Project(manifest.BuildScript!, "build", "build", dir, null)
        {
            Graph = own,
            ModuleRoots = new Dictionary<string, string> { ["build"] = Path.Combine(manifest.Root, "build") },
        };
        var (built, executable) = Pipeline.Build(new BuildRequest(script, BuildProfile.Of(Lyric5.Toolchain.Profile.Debug), Target.Host,
            compiler, version), error);
        if (built != 0 || executable is null)
        {
            error.WriteLine($"error[LYR-BLD0002]: {manifest.Name}'s build.lyr does not build, so the package does not (11 W3 BS1)");
            return (built == 0 ? 1 : built, null);
        }

        var file = Path.Combine(dir, "directives");
        File.WriteAllText(file, "");
        var environment = new Dictionary<string, string>
        {
            ["LYRIC_BUILD_TARGET"] = target.Triple,
            ["LYRIC_BUILD_PROFILE"] = profile.Name,
            ["LYRIC_BUILD_OUT_DIR"] = Path.Combine(manifest.Root, "out"),
            ["LYRIC_BUILD_GEN_DIR"] = manifest.GenRoot,
            ["LYRIC_BUILD_DIRECTIVES"] = file,
        };
        ProcessRunner.Result ran;
        try
        {
            ran = ProcessRunner.Run(executable, [], TimeSpan.FromMinutes(10), manifest.Root, environment);
        }
        catch (TimeoutException)
        {
            error.WriteLine($"error[LYR-BLD0002]: {manifest.Name}'s build.lyr did not end within 10 minutes");
            return (2, null);
        }
        File.WriteAllText(Path.Combine(dir, "output.txt"), ran.Stdout + ran.Stderr);
        if (ran.ExitCode != 0)
        {
            error.WriteLine($"error[LYR-BLD0002]: {manifest.Name}'s build.lyr ended with exit {ran.ExitCode}, and the package is not built; what it wrote:");
            error.Write(ran.Stdout);
            error.Write(ran.Stderr);
            return (2, null);
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

    /// <summary>What the script is and what it builds for (BS4): its files, std, the toolchain, the
    /// compiler that builds it, the target and the profile it is told.</summary>
    private static string ScriptKey(Manifest manifest, PackageGraph? own, Target target, BuildProfile profile, string version)
    {
        var parts = new List<string> { File.ReadAllText(manifest.BuildScript!), version, Pipeline.CompilerKey, target.Triple, profile.Name };
        var modules = Path.Combine(manifest.Root, "build");
        if (Directory.Exists(modules)) Pipeline.AddTree(parts, modules);
        // its own dependencies, each by its manifest and its sources (BS4)
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

    /// <summary>A directory's tree as a key — what the script left in <c>gen/</c>.</summary>
    private static string Tree(string directory)
    {
        if (!Directory.Exists(directory)) return "-";
        var parts = new List<string>();
        Pipeline.AddTree(parts, directory);
        return Pipeline.Key([.. parts]);
    }

    /// <summary>What the last run left: the script's key, the inputs it named with the content they
    /// had, the tree of <c>gen/</c>, and its directives — a line each.</summary>
    private sealed record State(string Key, List<(string Word, string Text)> Directives)
    {
        public Dictionary<string, string> Inputs { get; init; } = new(StringComparer.Ordinal);
        public string Gen { get; init; } = "-";

        public bool Fresh(Manifest manifest) =>
            Gen == Tree(manifest.GenRoot)
            && Inputs.All(input => Content(Path.GetFullPath(input.Key, manifest.Root)) == input.Value);

        public void Write(string file, Manifest manifest)
        {
            var text = new StringBuilder();
            text.Append("key\t").Append(Key).Append('\n');
            text.Append("gen\t").Append(Tree(manifest.GenRoot)).Append('\n');
            foreach (var (word, path) in Directives.Where(d => d.Word == "rerun-if-changed").Distinct())
                text.Append("input\t").Append(Content(Path.GetFullPath(path, manifest.Root))).Append('\t').Append(path).Append('\n');
            foreach (var (word, value) in Directives) text.Append("directive\t").Append(word).Append('\t').Append(value).Append('\n');
            File.WriteAllText(file, text.ToString());
        }

        public static State? Read(string file)
        {
            if (!File.Exists(file)) return null;
            string? key = null, gen = null;
            var inputs = new Dictionary<string, string>(StringComparer.Ordinal);
            var directives = new List<(string, string)>();
            foreach (var line in File.ReadAllLines(file))
            {
                var parts = line.Split('\t', 3);
                switch (parts)
                {
                    case ["key", var k]: key = k; break;
                    case ["gen", var g]: gen = g; break;
                    case ["input", var content, var path]: inputs[path] = content; break;
                    case ["directive", var word, var text]: directives.Add((word, text)); break;
                }
            }
            return key is null ? null : new State(key, directives) { Inputs = inputs, Gen = gen ?? "-" };
        }
    }
}
