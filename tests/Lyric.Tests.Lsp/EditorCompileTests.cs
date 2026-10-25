using Lyric.Compiler;
using Lyric.Lsp.Analysis;
using Lyric.Parsing;

namespace Lyric.Tests.Lsp;

/// <summary>
/// What the editor's compilations do differently from a build: the standard library parsed once
/// and shared (<see cref="StdlibSeed"/>), no lowering, and a token that stops them between their
/// phases — each without changing what the compilation says.
/// </summary>
public sealed class EditorCompileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-lsp-editor-" + Guid.NewGuid().ToString("N")[..8]);

    public EditorCompileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static readonly StdlibSeed Seed = StdlibSeed.Shared(Lyric5.Build.Pipeline.StdlibRoot);

    /// <summary>Programs that say something: clean, an error, a warning, one the lints take back,
    /// one reaching far into std.</summary>
    private const string Hello = "import std.io { println };\n\nfn main(): int {\n    println(\"hi\");\n    return 0;\n}\n";

    public static TheoryData<string> Programs() =>
    [
        Hello,
        "fn main(): int {\n    let x: int = \"not an int\";\n    return 0;\n}\n",
        "fn main(): int {\n    let unused = 1;\n    return 0;\n}\n",
        "@Allow(\"LYR-SEM0071\")\nfn main(): int {\n    let unused = 1;\n    return 0;\n}\n",
        "import std.collections { Map };\nimport std.json;\n\nfn main(): int {\n    var m: Map<string, int> = Map.new();\n    m[\"a\"] = 1;\n    return m.getOr(\"a\", 0) + m.keys().count();\n}\n",
    ];

    private string Program(string text)
    {
        var path = Path.Combine(_dir, $"p{Guid.NewGuid().ToString("N")[..6]}.lyr");
        File.WriteAllText(path, text);
        return path;
    }

    private static string[] Said(CompileResult result) =>
        result.Diagnostics.SortedSnapshot()
            .Select(d => $"{d.Code} {(d.Span.File.IsValid ? Path.GetFileName(result.Sources.GetPath(d.Span.File)) + ":" + result.Sources.LocateStart(d.Span) : "-")} {d.Message}")
            .ToArray();

    private static CompileResult Check(string path, CompilerOptions options) =>
        SourceCompiler.CheckProject([ScriptSource.FromDisk(path, Path.GetFileNameWithoutExtension(path))], options);

    [Theory]
    [MemberData(nameof(Programs))]
    public void The_seed_changes_nothing_a_compilation_says(string text)
    {
        var path = Program(text);
        var options = PackageUnit.SingleFileOptions(path);
        Assert.Equal(Said(Check(path, options)), Said(Check(path, options with { StdlibSeed = Seed })));
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void Without_lowering_a_compilation_says_what_it_says_with_it(string text)
    {
        var path = Program(text);
        var options = PackageUnit.SingleFileOptions(path);
        var unlowered = Check(path, options with { Lower = false });
        Assert.Equal(Said(Check(path, options)), Said(unlowered));
        Assert.Null(unlowered.Ir);
        Assert.NotNull(unlowered.Model);
    }

    [Fact]
    public void Two_compilations_with_the_seed_share_the_library_trees()
    {
        var path = Program(Hello);
        var options = PackageUnit.SingleFileOptions(path) with { StdlibSeed = Seed };

        static object IoTree(CompileResult result) =>
            result.Model!.Compilation.LoadedAstOf(result.Model.Compilation.FindModule(["std", "io"])!);

        Assert.Same(IoTree(Check(path, options)), IoTree(Check(path, options)));
        Assert.NotSame(IoTree(Check(path, options with { StdlibSeed = null })), IoTree(Check(path, options)));
    }

    /// <summary>A copy of the library, whose files a test may change.</summary>
    private string CopyOfStd()
    {
        var root = Path.Combine(_dir, "stdlib5");
        foreach (var file in Directory.EnumerateFiles(Lyric5.Build.Pipeline.StdlibRoot, "*.lyr", SearchOption.AllDirectories))
        {
            var to = Path.Combine(root, Path.GetRelativePath(Lyric5.Build.Pipeline.StdlibRoot, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        return root;
    }

    private const string UsesShout = "import std.io { shout };\n\nfn main(): int {\n    shout(\"hi\");\n    return 0;\n}\n";

    private const string Shout = "\n/// Says it louder.\npub fn shout(text: string): void {\n    println(text);\n}\n";

    [Fact]
    public void A_library_file_changed_since_the_seed_was_parsed_is_read_again()
    {
        var root = CopyOfStd();
        var seed = StdlibSeed.Parse(root);
        var io = Path.Combine(root, "std", "io.lyr");
        File.AppendAllText(io, Shout);
        File.SetLastWriteTimeUtc(io, DateTime.UtcNow.AddMinutes(1));

        var path = Program(UsesShout);
        var options = PackageUnit.SingleFileOptions(path) with { StdlibRoot = root, StdlibSeed = seed };
        Assert.Empty(Said(Check(path, options)));
    }

    [Fact]
    public void An_open_library_file_wins_over_the_seed()
    {
        var io = Path.Combine(Lyric5.Build.Pipeline.StdlibRoot, "std", "io.lyr");
        var path = Program(UsesShout);
        var options = PackageUnit.SingleFileOptions(path) with
        {
            StdlibSeed = Seed,
            SourceOverlay = new Dictionary<string, string> { [Path.GetFullPath(io)] = File.ReadAllText(io) + Shout },
        };
        Assert.Empty(Said(Check(path, options)));
        // the counter-check: the seed's own io knows no 'shout'
        Assert.NotEmpty(Said(Check(path, options with { SourceOverlay = null })));
    }

    [Fact]
    public void A_cancelled_compilation_stops_with_its_token()
    {
        var path = Program(Hello);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            Check(path, PackageUnit.SingleFileOptions(path) with { Cancellation = cancelled.Token }));
    }

    /// <summary>A site of about 7.8 million steps — a round of the loop takes 13 —: within the
    /// default budget, over a tenth of it.</summary>
    private const string Spins = "fn spin(n: int): int {\n    var s = 0;\n    var i = 0;\n    while (i < n) {\n"
        + "        s += i;\n        i += 1;\n    }\n    return s;\n}\n\nfn main(): int {\n    return comptime spin(600000) % 7;\n}\n";

    [Fact]
    public void A_site_over_the_editors_budget_stays_without_a_value_and_without_a_finding()
    {
        var path = Program(Spins);
        var options = PackageUnit.SingleFileOptions(path) with { Lower = false };

        // the program's own site: the one in its file
        static bool Valued(CompileResult result)
        {
            var sites = result.Model!.Types.ComptimeSites;
            var file = result.Model.Entry.Span.File;
            var own = Enumerable.Range(0, sites.Count).Single(i => sites[i].Span.File == file);
            return result.ComptimeValues?.ContainsKey(own) == true;
        }

        var build = Check(path, options);
        Assert.Empty(Said(build));
        Assert.True(Valued(build));

        var editor = Check(path, options with { EditorBudget = AnalysisService.EditorBudget });
        Assert.Empty(Said(editor));
        Assert.False(Valued(editor));
    }

    [Fact]
    public void Under_a_program_budget_below_the_editors_the_editor_says_what_the_build_says()
    {
        // The program's own budget is the smaller: the editor's cap is no cap, and the site's
        // finding is the build's.
        var path = Program(Spins);
        var options = PackageUnit.SingleFileOptions(path) with { Lower = false, ComptimeBudget = 20000 };
        var build = Said(Check(path, options));
        Assert.Contains(build, l => l.StartsWith("LYR-CT0002", StringComparison.Ordinal));
        Assert.Equal(build, Said(Check(path, options with { EditorBudget = AnalysisService.EditorBudget })));
    }
}
