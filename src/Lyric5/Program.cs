using System.Reflection;
using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

/// <summary>
/// The Lyric 5 driver: one binary with verbs (design/v5/spec/11 C1–C3), strict about what it
/// does not know (exit 2), reporting what the program did wrong with exit 1, passing a program's
/// own exit through (C5). The verbs grow milestone by milestone; today: <c>build</c> (a binary,
/// or <c>--emit ir|c</c>), <c>run</c>, <c>version</c>, <c>help</c> (M2).
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
        output.WriteLine("  build [<file.lyr>] [options]   compile the package here (src/main.lyr), or the file,");
        output.WriteLine("                                 to a binary under out/");
        output.WriteLine("  run [<file.lyr>] [options] [-- args]");
        output.WriteLine("                                 build, then run the binary with the arguments");
        output.WriteLine("  version                        the toolchain and the C compiler it found");
        output.WriteLine("  help                           this");
        output.WriteLine();
        output.WriteLine("options of build and run:");
        output.WriteLine("  --profile debug|release        the build profile (default: debug)");
        output.WriteLine("  --target <triple>              a Tier 1 target (default: this machine)");
        output.WriteLine("  --emit ir|c                    print the IR or the C instead of building");
        output.WriteLine("  -C <dir>                       look for the package from <dir> instead of here");
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

    private static readonly string[] ValueOptions = ["--emit", "--profile", "--target", "-C"];

    private static int Build(string[] args, bool run)
    {
        var verb = run ? "run" : "build";
        string? file = null;
        var values = new Dictionary<string, string>();
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

        Project project;
        try
        {
            if (file is null)
            {
                if (Project.ForDirectory(baseDirectory) is not { } package)
                {
                    Console.Error.WriteLine("error[LYR-CLI0004]: no lyric.toml here or above");
                    Console.Error.WriteLine($"  = help: lyric5 {verb} <file.lyr> builds one file alone");
                    return 2;
                }
                project = package;
            }
            else
            {
                if (!File.Exists(file))
                {
                    Console.Error.WriteLine($"error[LYR-CLI0001]: no such file '{file}'");
                    return 2;
                }
                project = Project.ForFile(file);
            }
        }
        catch (ManifestException refused)
        {
            Console.Error.WriteLine(refused.Render());
            return 1;
        }
        catch (InvalidOperationException library)
        {
            Console.Error.WriteLine($"error[LYR-CLI0005]: {library.Message}");
            return 2;
        }

        if (values.TryGetValue("--emit", out var emit))
        {
            if (emit is not ("ir" or "c")) return Unknown($"unknown emission '{emit}': ir, c");
            if (run) return Unknown("'--emit' prints instead of building; use 'build'");
            return Pipeline.Emit(project, emit, Console.Out, Console.Error);
        }

        var profileName = values.GetValueOrDefault("--profile", "debug");
        Profile? profile = profileName switch
        {
            "debug" => Profile.Debug,
            "release" => Profile.Release,
            "asan" => Profile.Asan,
            "tsan" => Profile.Tsan,
            _ => null,
        };
        if (profile is null) return Unknown($"unknown profile '{profileName}': debug, release, asan, tsan");

        Target target;
        try { target = values.TryGetValue("--target", out var triple) ? Target.Parse(triple) : Target.Host; }
        catch (ArgumentException)
        {
            return Unknown($"unknown target '{values["--target"]}': {string.Join(", ", Target.Tier1.Select(t => t.Triple))}");
        }

        var compiler = profile.Value.RequiresClang() ? CCompiler.Locate(CCompilerKind.Clang) : CCompiler.Locate();
        if (compiler is null)
        {
            Console.Error.WriteLine(profile.Value.RequiresClang()
                ? $"error[LYR-CLI0002]: the {profile.Value.Name()} profile needs clang (01 C7), and none was found"
                : "error[LYR-CLI0002]: no C compiler found — install zig (recommended), clang or gcc, or set LYRIC_CC");
            return 2;
        }

        var (exit, executable) = Pipeline.Build(new BuildRequest(project, profile.Value, target, compiler, DisplayVersion()), Console.Error);
        if (exit != 0 || executable is null) return exit;
        if (!run) return 0;
        if (!target.IsHost)
        {
            Console.Error.WriteLine($"error[LYR-CLI0003]: '{target.Triple}' is not this machine; the binary is at {executable}");
            return 2;
        }
        return ProcessRunner.RunInherited(executable, programArgs);
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
