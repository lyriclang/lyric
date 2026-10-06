using System.Reflection;
using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

/// <summary>
/// The Lyric 5 driver: one binary with verbs (design/v5/spec/11 C1–C3), strict about what it
/// does not know (exit 2), reporting what the program did wrong with exit 1, passing a program's
/// own exit through (C5). The verbs grow milestone by milestone; today: <c>build</c> (a binary,
/// or <c>--emit ir|c</c>), <c>run</c>, <c>version</c>, <c>help</c> (M2), <c>update</c>,
/// <c>clean</c>, <c>metadata</c>, <c>add</c>, <c>remove</c> (M7), <c>test</c> (M8a).
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) return Usage(Console.Out, 0);
            return args[0] switch
            {
                "--version" or "-V" or "version" => Version(),
                "--help" or "-h" or "help" => Usage(Console.Out, 0),
                "build" => Build(args[1..], run: false),
                "run" => Build(args[1..], run: true),
                "test" => Test(args[1..]),
                "update" => Update(args[1..]),
                "clean" => Clean(args[1..]),
                "metadata" => Metadata(args[1..]),
                "add" => Add(args[1..]),
                "remove" => Remove(args[1..]),
                _ => Unknown($"unknown verb '{args[0]}'"),
            };
        }
        catch (Exception crash)
        {
            // The toolchain's own failure (11 G4): not the program's fault, and worth a report.
            Console.Error.WriteLine($"error[LYR-ICE0001]: the toolchain failed: {crash.GetType().Name}: {crash.Message}");
            Console.Error.WriteLine("  = help: this is a bug in lyric5 — please report it with the program that triggered it");
            Console.Error.WriteLine(crash.StackTrace);
            return 101;
        }
    }

    private static int Usage(TextWriter output, int exit)
    {
        output.WriteLine("usage: lyric5 <verb> [options]");
        output.WriteLine();
        output.WriteLine("  build [<file.lyr>] [options]   compile the package here — its src/main.lyr and its [[bin]]");
        output.WriteLine("                                 programs —, or the file, to binaries under out/");
        output.WriteLine("  run [<file.lyr>] [options] [-- args]");
        output.WriteLine("                                 build, then run the binary with the arguments");
        output.WriteLine("  update [<package>…] [options]  read the packages from git anew — all, or those named —");
        output.WriteLine("                                 and write lyric.lock (options: -C <dir>, --offline)");
        output.WriteLine("  add <name> --path <dir> | --git <url> [--tag|--branch|--rev <r>]");
        output.WriteLine("                                 write the dependency into lyric.toml (-C <dir>, --offline)");
        output.WriteLine("  remove <name>                  take the dependency out of lyric.toml (-C <dir>, --offline)");
        output.WriteLine("  test [options] [-f <text>]     build the package's tests and run them, each in a task of");
        output.WriteLine("                                 its own — those whose name holds <text>, with -f");
        output.WriteLine("  clean [-C <dir>]               remove the package's out/");
        output.WriteLine("  metadata [options]             the package as JSON: its graph, programs, profiles, targets");
        output.WriteLine("                                 (options: -C <dir>, --offline, --json)");
        output.WriteLine("  version                        the toolchain and the C compiler it found");
        output.WriteLine("  help                           this");
        output.WriteLine();
        output.WriteLine("options of build and run:");
        output.WriteLine("  --profile <name>               the build profile: debug, release, asan, tsan, or one of the");
        output.WriteLine("                                 manifest's (default: LYRIC_PROFILE, else debug)");
        output.WriteLine("  --opt <0-3>, --[no-]lto, --[no-]debug-info, --[no-]deny-warnings,");
        output.WriteLine("  --[no-]overflow-checks, --[no-]fast-math");
        output.WriteLine("                                 a field of the profile, for this build");
        output.WriteLine("  --target <triple>              a Tier 1 target (default: this machine)");
        output.WriteLine("  --emit ir|c                    print the IR or the C instead of building");
        output.WriteLine("  -C <dir>                       look for the package from <dir> instead of here");
        output.WriteLine("  --bin <name>                   the package's program to build, run or print");
        output.WriteLine("  --offline                      fetch nothing: packages from git come from the cache");
        output.WriteLine("  --locked                       take lyric.lock as it is: build, run and test fail where");
        output.WriteLine("                                 the manifests read otherwise, and write no lock");
        output.WriteLine();
        output.WriteLine("The Lyric 5 command line, in development (design/v5/spec/13).");
        return exit;
    }

    private static int Unknown(string what)
    {
        Console.Error.WriteLine($"error[LYR-CLI0003]: {what}");
        Console.Error.WriteLine("  = help: lyric5 help lists what exists");
        return 2;
    }

    // --- build and run -------------------------------------------------------------------------

    private static readonly string[] ValueOptions = ["--emit", "--profile", "--target", "-C", "--opt", "--bin"];

    /// <summary>The profile's fields a flag sets for one build, <c>--x</c> on and <c>--no-x</c> off:
    /// the field flag over the profile (11 C4, W2 P3).</summary>
    private static readonly (string Flag, Func<BuildProfile, bool, BuildProfile> Set)[] FieldFlags =
    [
        ("lto", (p, on) => p with { Lto = on }),
        ("debug-info", (p, on) => p with { DebugInfo = on }),
        ("deny-warnings", (p, on) => p with { DenyWarnings = on }),
        ("overflow-checks", (p, on) => p with { OverflowChecks = on }),
        ("fast-math", (p, on) => p with { FastMath = on }),
    ];

    /// <summary>The options without a value.</summary>
    private static readonly string[] Flags =
        ["--offline", "--locked", .. FieldFlags.SelectMany(f => new[] { "--" + f.Flag, "--no-" + f.Flag })];

    private static int Build(string[] args, bool run)
    {
        var verb = run ? "run" : "build";
        string? file = null;
        var values = new Dictionary<string, string>();
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var programArgs = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--")
            {
                if (!run) return Unknown("'--' separates the program's arguments, which only 'run' passes on");
                programArgs.AddRange(args[(i + 1)..]);
                break;
            }
            if (Flags.Contains(arg))
            {
                flags.Add(arg);
                continue;
            }
            var option = ValueOptions.FirstOrDefault(o => arg == o || arg.StartsWith(o + "=", StringComparison.Ordinal));
            if (option is not null)
            {
                string? value = arg == option ? (i + 1 < args.Length ? args[++i] : null) : arg[(option.Length + 1)..];
                if (value is null) return Unknown($"'{option}' needs a value");
                values[option] = value;
            }
            else if (arg.StartsWith('-')) return Unknown($"unknown option '{arg}' for '{verb}'");
            else if (file is null) file = arg;
            else return Unknown($"'{verb}' takes one file, got '{file}' and '{arg}'");
        }
        // The package here or above, or the file named — a module of its package, or a package of
        // its own (07 M7a; 11 C8).
        // '-C <dir>' (11 C3): where the search for the package starts, and what a relative file is
        // relative to — the process's own directory stays as it is.
        var baseDirectory = Path.GetFullPath(values.GetValueOrDefault("-C", Directory.GetCurrentDirectory()));
        if (!Directory.Exists(baseDirectory))
        {
            Console.Error.WriteLine($"error[LYR-CLI0001]: no such directory '{baseDirectory}'");
            return 2;
        }
        if (file is not null) file = Path.GetFullPath(file, baseDirectory);
        // '--offline' (11 W2 P9): what is read from git comes from the user's cache, nothing is fetched.
        var offline = flags.Contains("--offline");
        // '--locked' (07 M7-9): the lock as it is — what it does not hold, or holds besides, refused.
        var locked = flags.Contains("--locked");

        // The programs this command is about: a package's every one (P2), or the file's.
        List<Project> programs;
        LibraryException? library = null;
        try
        {
            if (file is null)
            {
                if (Project.ProgramsOf(baseDirectory, offline, locked) is not { } package)
                {
                    Console.Error.WriteLine("error[LYR-CLI0004]: no lyric.toml here or above");
                    Console.Error.WriteLine($"  = help: lyric5 {verb} <file.lyr> builds one file alone");
                    return 2;
                }
                programs = [.. package];
            }
            else
            {
                if (values.ContainsKey("--bin")) return Unknown("'--bin' names a program of the package; a file is a program by itself");
                if (!File.Exists(file))
                {
                    Console.Error.WriteLine($"error[LYR-CLI0001]: no such file '{file}'");
                    return 2;
                }
                programs = [Project.ForFile(file, offline, locked)];
            }
        }
        catch (ManifestException refused)
        {
            Console.Error.WriteLine(refused.Render());
            return refused.Exit;
        }
        catch (LibraryException refused)
        {
            // 'build' checks a library (07 B6) — under its profile, below —; there is nothing to
            // run, and nothing to print.
            if (run || values.ContainsKey("--emit"))
            {
                Console.Error.WriteLine($"error[LYR-CLI0005]: {refused.Message}");
                return 2;
            }
            library = refused;
            programs = [];
        }

        // The toolchain the packages ask for (11 W2 P12): one below it refuses them.
        try { ToolchainCheck.Check(library?.Graph ?? programs[0].Graph, DisplayVersion()); }
        catch (ManifestException refused)
        {
            Console.Error.WriteLine(refused.Render());
            return refused.Exit;
        }

        // Which program (P2; 11 C8): '--bin' names one; 'run' and '--emit' take the package's
        // src/main.lyr — or its only program —; 'build' builds every one.
        if (library is null && file is null)
        {
            if (values.TryGetValue("--bin", out var bin))
            {
                var named = programs.FirstOrDefault(p => p.BinaryName == bin);
                if (named is null)
                    return Unknown($"no program '{bin}' in package '{programs[0].Name}': {string.Join(", ", programs.Select(p => p.BinaryName))}");
                programs = [named];
            }
            else if (run || values.ContainsKey("--emit"))
            {
                var main = programs.FirstOrDefault(p => p.Binary is null) ?? (programs.Count == 1 ? programs[0] : null);
                if (main is null)
                {
                    Console.Error.WriteLine($"error[LYR-CLI0007]: package '{programs[0].Name}' has several programs and no src/main.lyr: "
                                            + string.Join(", ", programs.Select(p => p.BinaryName)));
                    Console.Error.WriteLine($"  = help: --bin <name> names the one to {(run ? "run" : "print")}");
                    return 2;
                }
                programs = [main];
            }
        }

        if (ProfileOf(values, flags, library?.Manifest ?? programs[0].Manifest) is not { } profile) return 2;

        if (library is not null) return Pipeline.CheckLibrary(library, Console.Error, profile);

        if (values.TryGetValue("--emit", out var emit))
        {
            if (emit is not ("ir" or "c")) return Unknown($"unknown emission '{emit}': ir, c");
            if (run) return Unknown("'--emit' prints instead of building; use 'build'");
            return Pipeline.Emit(programs[0], emit, Console.Out, Console.Error, profile);
        }

        Target target;
        try { target = values.TryGetValue("--target", out var triple) ? Target.Parse(triple) : Target.Host; }
        catch (ArgumentException)
        {
            return Unknown($"unknown target '{values["--target"]}': {string.Join(", ", Target.Tier1.Select(t => t.Triple))}");
        }

        if (CompilerFor(profile) is not { } compiler) return 2;

        string? executable = null;
        foreach (var program in programs)
        {
            var (exit, built) = Pipeline.Build(new BuildRequest(program, profile, target, compiler, DisplayVersion()), Console.Error);
            if (exit != 0 || built is null) return exit;
            executable = built;
        }
        if (!run) return 0;
        if (!target.IsHost)
        {
            Console.Error.WriteLine($"error[LYR-CLI0003]: '{target.Triple}' is not this machine; the binary is at {executable}");
            return 2;
        }
        return ProcessRunner.RunInherited(executable!, programArgs);
    }

    /// <summary>The profile (11 W2 P3): '--profile', else LYRIC_PROFILE, else debug — built in, or
    /// the root manifest's; a field flag changes a field for this build alone. <c>null</c> after
    /// reporting a command line that asks for none.</summary>
    private static BuildProfile? ProfileOf(Dictionary<string, string> values, HashSet<string> flags, Manifest? manifest)
    {
        var profileName = values.GetValueOrDefault("--profile")
                          ?? (Environment.GetEnvironmentVariable("LYRIC_PROFILE") is { Length: > 0 } fromEnvironment ? fromEnvironment : "debug");
        BuildProfile profile;
        try { profile = Profiles.Resolve(manifest, profileName); }
        catch (ArgumentException unknown)
        {
            Unknown(unknown.Message);
            return null;
        }
        if (values.TryGetValue("--opt", out var opt))
        {
            if (opt is not ("0" or "1" or "2" or "3"))
            {
                Unknown($"'--opt' is a level from 0 to 3, not '{opt}'");
                return null;
            }
            profile = profile with { Opt = opt[0] - '0' };
        }
        foreach (var (field, set) in FieldFlags)
        {
            bool on = flags.Contains("--" + field), off = flags.Contains("--no-" + field);
            if (on && off)
            {
                Unknown($"'--{field}' and '--no-{field}' ask for opposite things");
                return null;
            }
            if (on || off) profile = set(profile, on);
        }
        return profile;
    }

    /// <summary>The C compiler a profile builds with (01 C7, C8); <c>null</c> after reporting that
    /// there is none.</summary>
    private static CCompiler? CompilerFor(BuildProfile profile)
    {
        var compiler = profile.RequiresClang ? CCompiler.Locate(CCompilerKind.Clang) : CCompiler.Locate();
        if (compiler is null)
            Console.Error.WriteLine(profile.RequiresClang
                ? $"error[LYR-CLI0002]: the {profile.Name} profile needs clang (01 C7), and none was found"
                : "error[LYR-CLI0002]: no C compiler found — install zig (recommended), clang or gcc, or set LYRIC_CC");
        return compiler;
    }

    // --- test ----------------------------------------------------------------------------------

    private static readonly string[] TestValueOptions = ["--profile", "-C", "--opt", "-f", "--filter"];

    /// <summary>
    /// <c>lyric test</c> (design/v5/spec/10 B12, 11 C2): the package's tests — every function it
    /// marks <c>@Test</c>, under <c>src/</c> and under <c>tests/</c> — built into one program for
    /// this machine and run, one after another, each in a task of its own; the program's exit is
    /// the command's (C5): 0 when every test held, 1 when one failed or the program was refused.
    /// </summary>
    private static int Test(string[] args)
    {
        var values = new Dictionary<string, string>();
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (Flags.Contains(arg))
            {
                flags.Add(arg);
                continue;
            }
            var option = TestValueOptions.FirstOrDefault(o => arg == o || arg.StartsWith(o + "=", StringComparison.Ordinal));
            if (option is null)
                return Unknown(arg.StartsWith('-') ? $"unknown option '{arg}' for 'test'" : $"'test' runs a package's tests and takes no file, got '{arg}'");
            string? value = arg == option ? (i + 1 < args.Length ? args[++i] : null) : arg[(option.Length + 1)..];
            if (value is null) return Unknown($"'{option}' needs a value");
            values[option == "--filter" ? "-f" : option] = value;
        }
        var baseDirectory = Path.GetFullPath(values.GetValueOrDefault("-C", Directory.GetCurrentDirectory()));
        if (!Directory.Exists(baseDirectory))
        {
            Console.Error.WriteLine($"error[LYR-CLI0001]: no such directory '{baseDirectory}'");
            return 2;
        }

        Project project;
        try
        {
            if (Project.TestsOf(baseDirectory, flags.Contains("--offline"), flags.Contains("--locked")) is not { } found)
            {
                Console.Error.WriteLine("error[LYR-CLI0004]: no lyric.toml here or above");
                Console.Error.WriteLine("  = help: lyric5 test runs a package's tests");
                return 2;
            }
            project = found;
            ToolchainCheck.Check(project.Graph, DisplayVersion());
        }
        catch (ManifestException refused)
        {
            Console.Error.WriteLine(refused.Render());
            return refused.Exit;
        }

        if (ProfileOf(values, flags, project.Manifest) is not { } profile) return 2;
        if (CompilerFor(profile) is not { } compiler) return 2;
        return TestRun.Run(project, profile, compiler, DisplayVersion(), values.GetValueOrDefault("-f"), Console.Out, Console.Error);
    }

    // --- update --------------------------------------------------------------------------------

    /// <summary>
    /// <c>lyric update [&lt;package&gt;…]</c> (design/v5/spec/07 P4, P5): the packages from git
    /// read anew — every one, or those named; the others stay at their locked commits — and
    /// <c>lyric.lock</c> written. A branch moves to its head, a moved tag to its commit; a version
    /// moves only with the manifests that ask for it.
    /// </summary>
    private static int Update(string[] args)
    {
        var names = new List<string>();
        string? directory = null;
        var offline = false;
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == "--offline") offline = true;
            else if (arg == "-C" || arg.StartsWith("-C=", StringComparison.Ordinal))
            {
                directory = arg == "-C" ? (i + 1 < args.Length ? args[++i] : null) : arg[3..];
                if (directory is null) return Unknown("'-C' needs a value");
            }
            else if (arg.StartsWith('-')) return Unknown($"unknown option '{arg}' for 'update'");
            else names.Add(arg);
        }
        var start = Path.GetFullPath(directory ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(start))
        {
            Console.Error.WriteLine($"error[LYR-CLI0001]: no such directory '{start}'");
            return 2;
        }
        if (Project.FindManifest(start) is not { } file)
        {
            Console.Error.WriteLine("error[LYR-CLI0004]: no lyric.toml here or above");
            return 2;
        }
        try
        {
            var manifest = Manifest.Read(file);
            var before = LockFile.Read(LockFile.For(manifest));
            bool Named(string name) => names.Count == 0 || names.Contains(name);
            var graph = Project.Resolve(manifest, offline, refresh: Named, keep: entry => !Named(entry.Name));
            ToolchainCheck.Check(graph, DisplayVersion());
            var unknown = names.Where(n => graph.Locked.All(entry => entry.Name != n)).ToList();
            if (unknown.Count > 0)
                return Unknown($"no package '{unknown[0]}' is read from git here"
                               + (graph.Locked.Count > 0 ? $": {string.Join(", ", graph.Locked.Select(e => e.Name).Distinct())}" : ""));
            Report(before, graph.Locked);
            return 0;
        }
        catch (ManifestException refused)
        {
            Console.Error.WriteLine(refused.Render());
            return refused.Exit;
        }
    }

    /// <summary>What the update changed in the lock, a line per revision of a repository.</summary>
    private static void Report(IReadOnlyList<Locked> before, IReadOnlyList<Locked> after)
    {
        var old = before.ToDictionary(e => (e.Name, e.Git));
        var changed = false;
        foreach (var entry in after)
        {
            if (!old.Remove((entry.Name, entry.Git), out var was))
                Console.Out.WriteLine($"  added    {entry.Name} {entry.Version} ({entry.Git}) {entry.Commit[..12]}");
            else if (was.Commit != entry.Commit)
                Console.Out.WriteLine($"  updated  {entry.Name} {entry.Version} ({entry.Git}) {was.Commit[..12]} -> {entry.Commit[..12]}");
            else continue;
            changed = true;
        }
        foreach (var gone in LockFile.Sorted(old.Values))
        {
            Console.Out.WriteLine($"  removed  {gone.Name} {gone.Version} ({gone.Git})");
            changed = true;
        }
        if (!changed) Console.Out.WriteLine(after.Count == 0 ? "no package is read from git" : "lyric.lock is up to date");
    }

    // --- clean and metadata ------------------------------------------------------------------

    /// <summary>The options of a verb about the package of a directory: <c>-C</c> and the flags it
    /// takes. <c>null</c> after refusing a word, with the exit code in <paramref name="exit"/>.</summary>
    private static (string Start, HashSet<string> Flags)? PackageOptions(string verb, string[] args, string[] flags, out int exit)
    {
        exit = 0;
        string? directory = null;
        var given = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (flags.Contains(arg)) given.Add(arg);
            else if (arg == "-C" || arg.StartsWith("-C=", StringComparison.Ordinal))
            {
                directory = arg == "-C" ? (i + 1 < args.Length ? args[++i] : null) : arg[3..];
                if (directory is null) { exit = Unknown("'-C' needs a value"); return null; }
            }
            else { exit = Unknown($"unknown option '{arg}' for '{verb}'"); return null; }
        }
        var start = Path.GetFullPath(directory ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(start))
        {
            Console.Error.WriteLine($"error[LYR-CLI0001]: no such directory '{start}'");
            exit = 2;
            return null;
        }
        return (start, given);
    }

    /// <summary><c>lyric clean</c> (11 W2 P5): the package's <c>out/</c> removed — after the build
    /// that writes into it, if one does, is done.</summary>
    private static int Clean(string[] args)
    {
        if (PackageOptions("clean", args, [], out var refused) is not { } options) return refused;
        if (Project.FindManifest(options.Start) is not { } file)
        {
            Console.Error.WriteLine("error[LYR-CLI0004]: no lyric.toml here or above");
            return 2;
        }
        var outDir = Path.Combine(Path.GetDirectoryName(file)!, "out");
        if (!Directory.Exists(outDir))
        {
            Console.Out.WriteLine("nothing to clean");
            return 0;
        }
        using (OutLock.Take(outDir, Console.Error))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(outDir))
            {
                if (Path.GetFileName(entry) == ".lock") continue;
                if (Directory.Exists(entry)) DeleteTree(entry);
                else File.Delete(entry);
            }
        }
        DeleteTree(outDir);
        Console.Out.WriteLine($"removed {outDir}");
        return 0;
    }

    /// <summary>Read-only files too: a cache can hold them.</summary>
    private static void DeleteTree(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    /// <summary><c>lyric metadata</c> (11 W2 P10): the package as one JSON object on standard
    /// output — <c>--json</c> names the one form there is.</summary>
    private static int Metadata(string[] args)
    {
        if (PackageOptions("metadata", args, ["--offline", "--json"], out var refused) is not { } options) return refused;
        if (Project.FindManifest(options.Start) is null)
        {
            Console.Error.WriteLine("error[LYR-CLI0004]: no lyric.toml here or above");
            return 2;
        }
        try
        {
            IReadOnlyList<Project> programs;
            PackageGraph graph;
            try
            {
                programs = Project.ProgramsOf(options.Start, options.Flags.Contains("--offline"))!;
                graph = programs[0].Graph!;
            }
            catch (LibraryException library)
            {
                programs = [];
                graph = library.Graph;
            }
            Console.Out.WriteLine(global::Lyric5.Build.Metadata.Json(graph, programs, DisplayVersion()));
            return 0;
        }
        catch (ManifestException manifest)
        {
            Console.Error.WriteLine(manifest.Render());
            return manifest.Exit;
        }
    }

    // --- add and remove ------------------------------------------------------------------------

    /// <summary>
    /// <c>lyric add &lt;name&gt; --path &lt;dir&gt; | --git &lt;url&gt; [--tag|--branch|--rev &lt;r&gt;]</c>
    /// (11 C2): the dependency written into <c>[dependencies]</c> as one line, every other line as it
    /// was, and the graph read with it — a graph that refuses the dependency leaves the manifest as
    /// it was. The path is written relative to the manifest.
    /// </summary>
    private static int Add(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string? name = null;
        var offline = false;
        string[] valued = ["--path", "--git", "--tag", "--branch", "--rev", "-C"];
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            var option = valued.FirstOrDefault(o => arg == o || arg.StartsWith(o + "=", StringComparison.Ordinal));
            if (arg == "--offline") offline = true;
            else if (option is not null)
            {
                var given = arg == option ? (i + 1 < args.Length ? args[++i] : null) : arg[(option.Length + 1)..];
                if (given is null) return Unknown($"'{option}' needs a value");
                values[option] = given;
            }
            else if (arg.StartsWith('-')) return Unknown($"unknown option '{arg}' for 'add'");
            else if (name is null) name = arg;
            else return Unknown($"'add' takes one name, got '{name}' and '{arg}'");
        }
        if (name is null) return Unknown("'add' needs the dependency's name: lyric5 add geo --path ../geo");
        var revisions = new[] { "--tag", "--branch", "--rev" }.Where(values.ContainsKey).ToList();
        if (values.ContainsKey("--path") == values.ContainsKey("--git"))
            return Unknown("'add' reads a dependency from a directory or from a repository: --path or --git, one of them");
        if (revisions.Count > 0 && !values.ContainsKey("--git")) return Unknown($"'{revisions[0]}' names a revision of a repository: it goes with --git");
        if (revisions.Count > 1) return Unknown($"'{revisions[0]}' and '{revisions[1]}': a dependency reads one revision");

        var start = Path.GetFullPath(values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (Project.FindManifest(start) is not { } file)
        {
            Console.Error.WriteLine("error[LYR-CLI0004]: no lyric.toml here or above");
            return 2;
        }
        var root = Path.GetDirectoryName(file)!;
        string value;
        if (values.TryGetValue("--path", out var path))
        {
            // Relative to where the command looks from (-C), as a file 'build' is given is.
            var relative = Path.GetRelativePath(root, Path.GetFullPath(path, start)).Replace('\\', '/');
            value = $"{{ path = {ManifestEdit.Quote(relative)} }}";
        }
        else
        {
            var revision = revisions.Count == 0 ? "" : $", {revisions[0][2..]} = {ManifestEdit.Quote(values[revisions[0]])}";
            value = $"{{ git = {ManifestEdit.Quote(values["--git"])}{revision} }}";
        }
        return EditDependencies(file, offline, text => ManifestEdit.Add(text, name, value), $"added {name} = {value}");
    }

    /// <summary><c>lyric remove &lt;name&gt;</c> (11 C2): the dependency's line taken out of
    /// <c>[dependencies]</c>, and the graph — the lock with it — read again.</summary>
    private static int Remove(string[] args)
    {
        // The name, wherever it stands among the options.
        var names = new List<string>();
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "-C" && i + 1 < args.Length) { rest.Add(args[i]); rest.Add(args[++i]); }
            else if (args[i].StartsWith('-')) rest.Add(args[i]);
            else names.Add(args[i]);
        }
        if (names.Count != 1) return Unknown("'remove' takes the dependency's name, one: lyric5 remove geo");
        var name = names[0];
        if (PackageOptions("remove", [.. rest], ["--offline"], out var refused) is not { } options) return refused;
        if (Project.FindManifest(options.Start) is not { } file)
        {
            Console.Error.WriteLine("error[LYR-CLI0004]: no lyric.toml here or above");
            return 2;
        }
        return EditDependencies(file, options.Flags.Contains("--offline"),
            text => ManifestEdit.Remove(text, name)
                    ?? throw new ArgumentException($"no dependency '{name}' in {file}"),
            $"removed {name}");
    }

    /// <summary>The manifest edited, then read with its graph; what refuses the edit puts the
    /// manifest back as it was.</summary>
    private static int EditDependencies(string file, bool offline, Func<string, string> edit, string done)
    {
        var before = File.ReadAllText(file);
        string after;
        try { after = edit(before); }
        catch (ArgumentException unknown) { return Unknown(unknown.Message); }
        catch (InvalidOperationException other)
        {
            Console.Error.WriteLine($"error[LYR-CLI0008]: {other.Message}");
            return 2;
        }
        File.WriteAllText(file, after);
        try
        {
            var graph = Project.Resolve(Manifest.Read(file), offline);
            ToolchainCheck.Check(graph, DisplayVersion());
        }
        catch (ManifestException refused)
        {
            File.WriteAllText(file, before);
            Console.Error.WriteLine(refused.Render());
            return refused.Exit;
        }
        Console.Out.WriteLine(done);
        return 0;
    }

    // --- version -------------------------------------------------------------------------------

    private static int Version()
    {
        Console.Out.WriteLine($"lyric {DisplayVersion()}");
        Console.Out.WriteLine("  edition     5");
        Console.Out.WriteLine($"  c compiler  {DescribeCompiler(CCompiler.Locate())}");
        return 0;
    }

    /// <summary>
    /// The version a person reads: the informational version, which a dev build stamps as
    /// <c>5.0.0-dev.&lt;date&gt;+&lt;sha&gt;</c>. A local build carries the SDK's full source revision
    /// after the '+'; it is shortened to the seven characters a commit is usually named by.
    /// </summary>
    public static string DisplayVersion()
    {
        var stamped = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "5.0.0";
        var plus = stamped.IndexOf('+');
        if (plus < 0) return stamped;
        var revision = stamped[(plus + 1)..];
        return revision.Length > 7 && revision.All(char.IsAsciiHexDigit)
            ? stamped[..(plus + 1)] + revision[..7]
            : stamped;
    }

    private static string DescribeCompiler(CCompiler? compiler) =>
        compiler?.ToString() ?? "none found — install zig (recommended), clang or gcc";
}
