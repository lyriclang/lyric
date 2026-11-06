using Lyric4.From4;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The migration's acceptance: 4.6's examples (<c>tests/fix-from-4/examples-4.6/</c>) go through the
/// tool and come out as <c>examples/</c>, and what those print is what 4.6 printed. The oracle is 4.6
/// itself, built from its tag: <c>tests/fix-from-4-acceptance/oracle/</c> holds each program's output
/// and exit code, <c>record-oracle.sh</c> beside it makes them. <c>report.txt</c> is the tool's report
/// over all of them, each note with its place, and what a person did about it.
/// </summary>
[Collection("console")]
public class FixFrom4AcceptanceTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);
    private static readonly string Input = Path.Combine(Root, "tests", "fix-from-4", "examples-4.6");
    private static readonly string Acceptance = Path.Combine(Root, "tests", "fix-from-4-acceptance");

    /// <summary>The programs with no place in <c>examples/</c>: a library of a host and a binding of
    /// .NET have no form in Lyric 5, and the macros have their own package there.</summary>
    private static readonly HashSet<string> Unplaced = new(StringComparer.Ordinal) { "embedded", "ffi/dotnet", "macros/comptime" };

    /// <summary>The programs whose code a person changed after the tool: each for a note in the report,
    /// or for a warning the tool leaves, which an example does not show its reader.</summary>
    private static readonly HashSet<string> ChangedByHand = new(StringComparer.Ordinal)
    {
        "vectors", "patterns/simplifier", "constants", "patterns/binding-conditions",
    };

    /// <summary>The programs that print a map's entries, whose order is the map's own in both languages.</summary>
    private static readonly HashSet<string> AnyOrder = new(StringComparer.Ordinal) { "lambdas/destructuring" };

    private static readonly Dictionary<string, string[]> Arguments = new(StringComparer.Ordinal)
    {
        ["greet"] = ["World", "Lyric"],
        ["wc"] = ["hello.lyr"],
    };

    private static IEnumerable<string> Programs() =>
        Directory.EnumerateFiles(Input, "*.lyr", SearchOption.AllDirectories)
            .Select(f => Path.ChangeExtension(Path.GetRelativePath(Input, f), null).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);

    public static TheoryData<string> Placed() => [.. Programs().Where(p => !Unplaced.Contains(p))];

    public static TheoryData<string> Runnable() => [.. Programs().Where(p => p is not ("embedded" or "ffi/dotnet"))];

    /// <summary>The program in a fresh directory, migrated; the result's text and the notes.</summary>
    private static (string Text, List<string> Notes) Migrate(string program)
    {
        var dir = TestDirectories.Fresh("lyric-from4-accept-");
        var file = Path.Combine(dir, "main.lyr");
        File.Copy(Path.Combine(Input, program + ".lyr"), file);
        var model = From4.CheckFile(file);
        Assert.False(model.HasErrors, string.Join("\n", model.Diagnostics.Diagnostics.Select(d => $"{d.Code} {d.Message}")));
        var migration = Migrator.Run(model);
        migration.Apply();
        return (File.ReadAllText(file).Replace("\r\n", "\n"), migration.Ordered().Select(n => Migration.Line(n, dir)).ToList());
    }

    /// <summary>The code of a text: a whole-line comment is prose, which a person may bring up to date.</summary>
    private static string Code(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Theory]
    [MemberData(nameof(Placed))]
    public void An_example_is_what_the_tool_makes_of_4_6s(string program)
    {
        if (ChangedByHand.Contains(program)) return;
        var (text, _) = Migrate(program);
        var placed = File.ReadAllText(Path.Combine(Root, "examples", program + ".lyr"));
        Assert.Equal(Code(text), Code(placed));
    }

    [Fact]
    public void The_report_holds_every_note_of_the_tool()
    {
        var want = File.ReadAllLines(Path.Combine(Acceptance, "report.txt")).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
        var got = Programs().SelectMany(p => Migrate(p).Notes.Select(n => n.Replace("main.lyr:", p + ".lyr:", StringComparison.Ordinal))).ToList();
        Assert.Equal(want, got);
    }

    [Theory]
    [MemberData(nameof(Runnable))]
    public void A_migrated_program_prints_what_4_6_printed(string program)
    {
        var text = Unplaced.Contains(program) ? Migrate(program).Text : File.ReadAllText(Path.Combine(Root, "examples", program + ".lyr"));
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", text));
        var (built, _, error) = Run("build", "-C", dir);
        Assert.True(built == 0, error);
        var host = Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);

        var arguments = Arguments.GetValueOrDefault(program, []).ToArray();
        var input = Path.Combine(dir, "hello.lyr");
        File.Copy(Path.Combine(Input, "hello.lyr"), input);
        for (var i = 0; i < arguments.Length; i++)
            if (arguments[i] == "hello.lyr") arguments[i] = input;
        var ran = ProcessRunner.Run(exe, arguments, TimeSpan.FromMinutes(2));
        var printed = ran.Stdout.Replace("\r\n", "\n").Replace(input, "hello.lyr", StringComparison.Ordinal);

        var name = program.Replace('/', '_');
        var oracle = Path.Combine(Acceptance, "oracle", name);
        var expected = File.ReadAllText(oracle + ".out").Replace("\r\n", "\n");
        Assert.Equal(int.Parse(File.ReadAllText(oracle + ".code").Trim()), ran.ExitCode);
        if (AnyOrder.Contains(program))
            Assert.Equal(expected.Split('\n').Order(StringComparer.Ordinal), printed.Split('\n').Order(StringComparer.Ordinal));
        else
            Assert.Equal(expected, printed);
    }
}
