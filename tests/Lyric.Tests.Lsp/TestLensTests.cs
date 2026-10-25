using System.Text.Json;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// The tests in the editor: a lens over each <c>@Test</c> function and each runnable block of the
/// documentation, giving the client the package's directory and the name <c>lyric test -f</c> runs
/// it by — the names <c>lyric test</c> itself finds.
/// </summary>
public sealed class TestLensTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-lsp-lens-" + Guid.NewGuid().ToString("N")[..8]);

    private const string Lib = "/// Adds.\n///\n/// ```lyric\n/// println(add(1, 2));\n/// ```\n///\n/// ```output\n/// 3\n/// ```\npub fn add(a: int, b: int): int {\n    return a + b;\n}\n";

    private const string Check = "import app { add };\n\n@Test\nfn adds(): void {\n    assert(add(1, 2) == 3);\n}\n\nfn helper(): int {\n    return 0;\n}\n";

    public TestLensTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        Directory.CreateDirectory(Path.Combine(_dir, "tests"));
        File.WriteAllText(Path.Combine(_dir, "lyric.toml"), "[package]\nname = \"app\"\nversion = \"0.1.0\"\n");
        File.WriteAllText(Path.Combine(_dir, "src", "lib.lyr"), Lib);
        File.WriteAllText(Path.Combine(_dir, "tests", "check.lyr"), Check);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static string DidOpen(string uri, string text) =>
        JsonSerializer.Serialize(new { textDocument = new { uri, languageId = "lyric", version = 1, text } });

    private static async Task Analysed(ServerHarness harness, string uri)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);
            if (published.GetProperty("params").GetProperty("uri").GetString() == uri) return;
        }
        throw new InvalidOperationException($"no diagnostics arrived for {uri}");
    }

    private async Task<JsonElement[]> Lenses(string file, string text)
    {
        var uri = DocumentUri.FromFilePath(Path.Combine(_dir, file));
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, text));
        await Analysed(harness, uri);
        var id = await harness.RequestAsync(LspMethods.CodeLens, JsonSerializer.Serialize(new { textDocument = new { uri } }));
        return (await harness.ReceiveResponseAsync(id)).GetProperty("result").EnumerateArray().ToArray();
    }

    [Fact]
    public async Task A_test_function_gets_run_and_debug_with_the_name_lyric_test_gives_it()
    {
        var lenses = await Lenses(Path.Combine("tests", "check.lyr"), Check);

        Assert.Equal(["Run test", "Debug test"], lenses.Select(l => l.GetProperty("command").GetProperty("title").GetString()));
        foreach (var lens in lenses)
        {
            Assert.Equal(3, lens.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
            var arguments = lens.GetProperty("command").GetProperty("arguments").EnumerateArray().Select(a => a.GetString()).ToArray();
            Assert.Equal(Path.GetFullPath(_dir), Path.GetFullPath(arguments[0]!));
            Assert.Equal("app.tests.check.adds", arguments[1]);
        }

        // the name lyric test runs it by
        var tests = Lyric5.Build.TestRun.Discover(Lyric5.Build.Project.TestsOf(_dir, offline: true, write: false)!, TextWriter.Null);
        Assert.Contains(tests!, t => t.Name == "app.tests.check.adds");
    }

    [Fact]
    public async Task A_documentation_block_that_runs_gets_its_lenses_on_its_fence()
    {
        var lenses = await Lenses(Path.Combine("src", "lib.lyr"), Lib);
        var lens = lenses.First();
        Assert.Equal(2, lens.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal("doc: app.add (src/lib.lyr:3)",
            lens.GetProperty("command").GetProperty("arguments")[1].GetString());
    }

    [Fact]
    public async Task A_file_outside_a_package_has_no_tests()
    {
        var uri = DocumentUri.FromFilePath(Path.Combine(Path.GetTempPath(), "lyric-lsp-lone-" + Guid.NewGuid().ToString("N")[..6] + ".lyr"));
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, "@Test\nfn adds(): void {\n}\nfn main(): int {\n    return 0;\n}\n"));
        await Analysed(harness, uri);
        var id = await harness.RequestAsync(LspMethods.Tests, JsonSerializer.Serialize(new { textDocument = new { uri } }));
        var result = (await harness.ReceiveResponseAsync(id)).GetProperty("result");
        Assert.Empty(result.GetProperty("tests").EnumerateArray());
    }
}
