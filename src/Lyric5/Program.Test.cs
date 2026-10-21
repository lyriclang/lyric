using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

public static partial class Program
{
    // --- test and bench ------------------------------------------------------------------------

    /// <summary>
    /// <c>lyric test</c>: the package's tests — every function it marks <c>@Test</c>, under
    /// <c>src/</c> and under <c>tests/</c> — built into one program for this machine and run, one
    /// after another, each in a task of its own; the program's exit is the command's: 0 when every
    /// test held, 1 when one failed or the program was refused.
    /// </summary>
    private static int Test(string[] args, LintArgs lints) =>
        Package(args, "test", "tests", null, lints, (project, profile, target, compiler, values) =>
            TestRun.Run(project, profile, target, compiler, DisplayVersion(), values.GetValueOrDefault("-f"), Console.Out, Console.Error));

    /// <summary>
    /// <c>lyric bench</c>: the package's benchmarks — every function it marks <c>@Bench</c>, under
    /// <c>src/</c> and under <c>tests/</c> — built into one program for this machine, in the
    /// release profile unless <c>--profile</c> names another, and run one after another.
    /// <c>--save</c> names a file the run writes its times to, <c>--compare</c> one it reads to say
    /// how they changed. The program's exit is the command's.
    /// </summary>
    private static int Bench(string[] args, LintArgs lints) =>
        Package(args, "bench", "benchmarks", "release", lints, (project, profile, target, compiler, values) =>
            BenchRun.Run(project, profile, target, compiler, DisplayVersion(), values.GetValueOrDefault("-f"),
                values.TryGetValue("--save", out var save) ? Path.GetFullPath(save) : "",
                values.TryGetValue("--compare", out var compare) ? Path.GetFullPath(compare) : "",
                Console.Out, Console.Error));

    /// <summary>A verb over the package of the nearest <c>lyric.toml</c> — <c>test</c>, <c>bench</c>:
    /// its options read, the package and its graph loaded, the profile chosen — <paramref name="profileDefault"/>
    /// where the command line names none, else as <c>build</c> chooses —, the target — this machine
    /// unless <c>--target</c> names another, as <c>build</c> and <c>run</c> take it —, and the
    /// compiler found; then <paramref name="run"/>.</summary>
    private static int Package(string[] args, string verb, string what, string? profileDefault,
        LintArgs lints, Func<Project, BuildProfile, Target, CCompiler, Dictionary<string, string>, int> run)
    {
        if (Verbs.Read(Verbs.Find(verb)!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Words.Count > 0) return Unknown($"'{verb}' runs a package's {what} and takes no file, got '{parsed.Words[0]}'");
        if (parsed.Rest is not null) return Unknown($"unknown option '--' for '{verb}'");
        var values = parsed.Values;
        var flags = parsed.Flags;
        var baseDirectory = Path.GetFullPath(values.GetValueOrDefault("-C", Directory.GetCurrentDirectory()));
        if (!Directory.Exists(baseDirectory))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{baseDirectory}'");
            return 2;
        }

        Project project;
        try
        {
            if (Project.TestsOf(baseDirectory, flags.Contains("--offline"), flags.Contains("--locked")) is not { } found)
            {
                Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above", $"lyric5 {verb} runs a package's {what}");
                return 2;
            }
            project = found with { LintArgs = lints };
            ToolchainCheck.Check(project.Graph, DisplayVersion());
        }
        catch (ManifestException refused)
        {
            Say.Manifest(Console.Error, refused);
            return refused.Exit;
        }

        if (profileDefault is not null) values.TryAdd("--profile", profileDefault);
        if (ProfileOf(values, flags, project.Manifest) is not { } profile) return 2;
        Target target;
        try { target = values.TryGetValue("--target", out var triple) ? Target.Parse(triple) : Target.Host; }
        catch (ArgumentException)
        {
            return Unknown($"unknown target '{values["--target"]}': {string.Join(", ", Target.Tier1.Select(t => t.Triple))}");
        }
        if (CompilerFor(profile) is not { } compiler) return 2;
        return run(project, profile, target, compiler, values);
    }
}
