using System.Reflection;
using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

/// <summary>
/// The Lyric 5 driver: one binary with verbs (design/v5/spec/11 C1–C3), strict about what it
/// does not know (exit 2), reporting what the program did wrong with exit 1, passing a program's
/// own exit through (C5). The verbs grow milestone by milestone; today: <c>build</c> (a binary,
/// or <c>--emit ir|c</c>), <c>run</c>, <c>version</c>, <c>help</c> (M2), <c>update</c> (M7).
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
                "update" => Update(args[1..]),
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
        ["--offline", .. FieldFlags.SelectMany(f => new[] { "--" + f.Flag, "--no-" + f.Flag })];

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

        // The programs this command is about: a package's every one (P2), or the file's.
        List<Project> programs;
        LibraryException? library = null;
        try
        {
            if (file is null)
            {
                if (Project.ProgramsOf(baseDirectory, offline) is not { } package)
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
                programs = [Project.ForFile(file, offline)];
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

        // The profile (11 W2 P3): '--profile', else LYRIC_PROFILE, else debug — built in, or the
        // root manifest's; a field flag changes a field for this build alone.
        var profileName = values.GetValueOrDefault("--profile")
                          ?? (Environment.GetEnvironmentVariable("LYRIC_PROFILE") is { Length: > 0 } fromEnvironment ? fromEnvironment : "debug");
        BuildProfile profile;
        try { profile = Profiles.Resolve(library?.Manifest ?? programs[0].Manifest, profileName); }
        catch (ArgumentException unknown) { return Unknown(unknown.Message); }
        if (values.TryGetValue("--opt", out var opt))
        {
            if (opt is not ("0" or "1" or "2" or "3")) return Unknown($"'--opt' is a level from 0 to 3, not '{opt}'");
            profile = profile with { Opt = opt[0] - '0' };
        }
        foreach (var (field, set) in FieldFlags)
        {
            bool on = flags.Contains("--" + field), off = flags.Contains("--no-" + field);
            if (on && off) return Unknown($"'--{field}' and '--no-{field}' ask for opposite things");
            if (on || off) profile = set(profile, on);
        }

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

        var compiler = profile.RequiresClang ? CCompiler.Locate(CCompilerKind.Clang) : CCompiler.Locate();
        if (compiler is null)
        {
            Console.Error.WriteLine(profile.RequiresClang
                ? $"error[LYR-CLI0002]: the {profile.Name} profile needs clang (01 C7), and none was found"
                : "error[LYR-CLI0002]: no C compiler found — install zig (recommended), clang or gcc, or set LYRIC_CC");
            return 2;
        }

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
