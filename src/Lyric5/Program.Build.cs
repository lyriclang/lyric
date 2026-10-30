using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

public static partial class Program
{
    // --- build, run, check and expand ----------------------------------------------------------

    /// <summary>What a verb over programs does with them.</summary>
    private enum BuildVerb { Build, Run, Check, Expand }

    /// <summary>The profile's fields a flag sets for one build, <c>--x</c> on and <c>--no-x</c> off:
    /// the field flag over the profile (spec 15 §4).</summary>
    private static readonly (string Flag, Func<BuildProfile, bool, BuildProfile> Set)[] FieldFlags =
    [
        ("lto", (p, on) => p with { Lto = on }),
        ("debug-info", (p, on) => p with { DebugInfo = on }),
        ("deny-warnings", (p, on) => p with { DenyWarnings = on }),
        ("overflow-checks", (p, on) => p with { OverflowChecks = on }),
        ("fast-math", (p, on) => p with { FastMath = on }),
    ];

    /// <summary>
    /// <c>build</c>, <c>run</c>, <c>check</c>, and <c>expand</c>, which chooses a program as
    /// <c>run</c> does and prints its entry module after the macros' expansion instead of building
    /// it. <c>check</c> takes the programs through everything a build does before the C — the build
    /// scripts, the front end, the lints, <c>comptime</c> — and makes neither C nor a binary.
    /// </summary>
    private static int Build(string[] args, LintArgs lints, BuildVerb how)
    {
        var verb = how.ToString().ToLowerInvariant();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        bool run = how == BuildVerb.Run, expand = how == BuildVerb.Expand, check = how == BuildVerb.Check;
        if (Verbs.Read(Verbs.Find(verb)!, args, Unknown, out var refused) is not { } parsed) return refused;
        if (parsed.Rest is not null && !run) return Unknown("'--' separates the program's arguments, which only 'run' passes on");
        if (parsed.Words.Count > 1) return Unknown($"'{verb}' takes one file, got '{parsed.Words[0]}' and '{parsed.Words[1]}'");
        var values = parsed.Values;
        var flags = parsed.Flags;
        var file = parsed.Words.Count == 1 ? parsed.Words[0] : null;
        var programArgs = parsed.Rest ?? [];

        // The package here or above, or the file named — a module of its package, or a package of
        // its own. '-C <dir>': where the search for the package starts, and what a relative file is
        // relative to — the process's own directory stays as it is.
        var baseDirectory = Path.GetFullPath(values.GetValueOrDefault("-C", Directory.GetCurrentDirectory()));
        if (!Directory.Exists(baseDirectory))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{baseDirectory}'");
            return 2;
        }
        if (file is not null) file = Path.GetFullPath(file, baseDirectory);
        // '--offline': what is read from git comes from the user's cache, nothing is fetched.
        var offline = flags.Contains("--offline");
        // '--locked': the lock as it is — what it does not hold, or holds besides, refused.
        var locked = flags.Contains("--locked");

        if (flags.Contains("--shared") && !flags.Contains("--lib"))
            return Unknown("'--shared' says which library '--lib' builds; without '--lib' there is none");
        if (flags.Contains("--lib")) return BuildLibrary(values, flags, baseDirectory, file, lints, offline, locked, clock);

        // The programs this command is about: a package's every one, or the file's.
        List<Project> programs;
        LibraryException? library = null;
        try
        {
            if (file is null)
            {
                if (Project.ProgramsOf(baseDirectory, offline, locked) is not { } package)
                {
                    Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above", $"lyric5 {verb} <file.lyr> builds one file alone");
                    return 2;
                }
                programs = [.. package];
            }
            else
            {
                if (values.ContainsKey("--bin")) return Unknown("'--bin' names a program of the package; a file is a program by itself");
                if (!File.Exists(file))
                {
                    Say.Error(Console.Error, "LYR-CLI0001", $"no such file '{file}'");
                    return 2;
                }
                programs = [Project.ForFile(file, offline, locked)];
            }
        }
        catch (ManifestException manifestRefused)
        {
            Say.Manifest(Console.Error, manifestRefused);
            return manifestRefused.Exit;
        }
        catch (LibraryException libraryRefused)
        {
            // 'build' and 'check' check a library — under its profile, below —; there is nothing to
            // run, and nothing to print.
            if (run || expand || values.ContainsKey("--emit"))
            {
                Say.Error(Console.Error, "LYR-CLI0005", $"{libraryRefused.Message}");
                return 2;
            }
            library = libraryRefused;
            programs = [];
        }

        // The toolchain the packages ask for: one below it refuses them.
        try { ToolchainCheck.Check(library?.Graph ?? programs[0].Graph, DisplayVersion()); }
        catch (ManifestException manifestRefused)
        {
            Say.Manifest(Console.Error, manifestRefused);
            return manifestRefused.Exit;
        }

        // Which program: '--bin' names one; 'run' and '--emit' take the package's src/main.lyr — or
        // its only program —; 'build' and 'check' take every one.
        if (library is null && file is null)
        {
            if (values.TryGetValue("--bin", out var bin))
            {
                var named = programs.FirstOrDefault(p => p.BinaryName == bin);
                if (named is null)
                    return Unknown($"no program '{bin}' in package '{programs[0].Name}': {string.Join(", ", programs.Select(p => p.BinaryName))}");
                programs = [named];
            }
            else if (run || expand || values.ContainsKey("--emit"))
            {
                var main = programs.FirstOrDefault(p => p.Binary is null) ?? (programs.Count == 1 ? programs[0] : null);
                if (main is null)
                {
                    Say.Error(Console.Error, "LYR-CLI0007",
                        $"package '{programs[0].Name}' has several programs and no src/main.lyr: "
                        + string.Join(", ", programs.Select(p => p.BinaryName)),
                        $"--bin <name> names the one to {(run ? "run" : expand ? "expand" : "print")}");
                    return 2;
                }
                programs = [main];
            }
        }

        if (ProfileOf(values, flags, library?.Manifest ?? programs[0].Manifest) is not { } profile) return 2;

        if (library is not null) return Pipeline.CheckLibrary(library, Console.Error, profile, lints);
        programs = [.. programs.Select(p => p with { LintArgs = lints })];

        Target target;
        try { target = values.TryGetValue("--target", out var triple) ? Target.Parse(triple) : Target.Host; }
        catch (ArgumentException)
        {
            return Unknown($"unknown target '{values["--target"]}': {string.Join(", ", Target.Tier1.Select(t => t.Triple))}");
        }

        // the emission is the target's too: 'comptime if (target.os == …)' chose for it
        if (values.TryGetValue("--emit", out var emit))
        {
            if (emit is not ("ir" or "c")) return Unknown($"unknown emission '{emit}': ir, c");
            return Pipeline.Emit(programs[0], emit, Console.Out, Console.Error, profile, target);
        }
        if (expand) return Pipeline.Expand(programs[0], Console.Out, Console.Error, profile, target);
        if (check)
        {
            // every program said, the first that fails deciding the exit
            var exit = 0;
            foreach (var program in programs)
                exit = Math.Max(exit, Pipeline.CheckProgram(program, profile, target, DisplayVersion(), Console.Error));
            if (exit == 0) Finished(profile, clock);
            return exit;
        }

        if (CompilerFor(profile) is not { } compiler) return 2;

        string? executable = null;
        foreach (var program in programs)
        {
            var (exit, built) = Pipeline.Build(new BuildRequest(program, profile, target, compiler, DisplayVersion()), Console.Error);
            if (exit != 0 || built is null) return exit;
            executable = built;
            Lyric.Core.Rendering.RenderStyle.Current.Events?.Artifact("binary", program.BinaryName, built);
        }
        Finished(profile, clock);
        if (!run) return 0;
        if (!target.IsHost)
        {
            Say.Error(Console.Error, "LYR-CLI0003", $"'{target.Triple}' is not this machine; the binary is at {executable}");
            return 2;
        }
        // the build's count before the program, whose output follows on the same stream
        Lyric.Core.Rendering.RenderStyle.Current.Events?.Summary();
        BuildStatus.Say("Running", Pipeline.Shown(executable!));
        return ProcessRunner.RunInherited(executable!, programArgs);
    }

    /// <summary>The last status line of a build that succeeded: the profile, what it compiles with,
    /// and how long the command took.</summary>
    private static void Finished(BuildProfile profile, System.Diagnostics.Stopwatch clock) =>
        BuildStatus.Say("Finished", $"{profile.Name} [opt {profile.Opt}{(profile.DebugInfo ? ", debug info" : "")}] in "
            + clock.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s");

    /// <summary>
    /// <c>build --lib</c>: the package's <c>src/lib.lyr</c> and what it imports as a library C links
    /// — <c>lib&lt;name&gt;.a</c>, or with <c>--shared</c> the system's shared library — and its
    /// header <c>&lt;name&gt;.h</c>, under <c>out/&lt;profile&gt;/&lt;target&gt;/</c>.
    /// </summary>
    private static int BuildLibrary(Dictionary<string, string> values, HashSet<string> flags, string baseDirectory, string? file,
        LintArgs lints, bool offline, bool locked, System.Diagnostics.Stopwatch clock)
    {
        if (file is not null) return Unknown("'--lib' builds the package's library, src/lib.lyr; a file is a program by itself");
        if (values.ContainsKey("--bin")) return Unknown("'--bin' names a program of the package; '--lib' builds its library");
        if (values.ContainsKey("--emit")) return Unknown("'--emit' prints a program's IR or C; '--lib' builds the library");
        Project? project;
        try { project = Project.LibraryOf(baseDirectory, flags.Contains("--shared") ? LibraryKind.Shared : LibraryKind.Static, offline, locked); }
        catch (ManifestException manifestRefused)
        {
            Say.Manifest(Console.Error, manifestRefused);
            return manifestRefused.Exit;
        }
        if (project is null)
        {
            Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above", "'--lib' builds a package's src/lib.lyr");
            return 2;
        }
        if (!File.Exists(project.Source))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such file '{project.Source}'", "a library C links starts from the package's src/lib.lyr");
            return 2;
        }
        try { ToolchainCheck.Check(project.Graph!, DisplayVersion()); }
        catch (ManifestException manifestRefused)
        {
            Say.Manifest(Console.Error, manifestRefused);
            return manifestRefused.Exit;
        }
        if (ProfileOf(values, flags, project.Manifest) is not { } profile) return 2;
        Target target;
        try { target = values.TryGetValue("--target", out var triple) ? Target.Parse(triple) : Target.Host; }
        catch (ArgumentException)
        {
            return Unknown($"unknown target '{values["--target"]}': {string.Join(", ", Target.Tier1.Select(t => t.Triple))}");
        }
        if (CompilerFor(profile) is not { } compiler) return 2;
        var (exit, built, linkArgs) = Pipeline.BuildLibrary(
            new BuildRequest(project with { LintArgs = lints }, profile, target, compiler, DisplayVersion()), Console.Error);
        if (exit != 0 || built is null) return exit;
        Finished(profile, clock);
        // what a C program links beside the archive, the target's (spec 13 §4 rule 9)
        var shared = project.Library == LibraryKind.Shared;
        if (Lyric.Core.Rendering.RenderStyle.Current.Events is { } events)
            events.Artifact("library", project.Name, built, shared ? null : linkArgs);
        else if (!shared && linkArgs.Count > 0)
            Console.Error.WriteLine($"note: link {built} with {string.Join(' ', linkArgs)}");
        return 0;
    }

    /// <summary>The profile: '--profile', else LYRIC_PROFILE, else debug — built in, or
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
        if (values.TryGetValue("--gc", out var gcName))
        {
            if (Collectors.Parse(gcName) is not { } gc)
            {
                Unknown($"'--gc' is immix or boehm, not '{gcName}'");
                return null;
            }
            profile = profile with { Gc = gc };
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

    /// <summary>The C compiler a profile builds with; <c>null</c> after reporting that
    /// there is none.</summary>
    private static CCompiler? CompilerFor(BuildProfile profile)
    {
        var compiler = profile.RequiresClang ? CCompiler.Locate(CCompilerKind.Clang) : CCompiler.Locate();
        if (compiler is null)
            Console.Error.WriteLine(profile.RequiresClang
                ? $"error[LYR-CLI0002]: the {profile.Name} profile needs clang, and none was found"
                : "error[LYR-CLI0002]: no C compiler found — 'lyric5 toolchain install zig' installs the one this toolchain is tested with; or put zig, clang or gcc on the PATH, or set LYRIC_CC");
        return compiler;
    }
}
