using System.Text.Json;
using Lyric.Lsp;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// The text an edit sends — a range and what replaces it — and the diagnostics a client asks for
/// itself (<c>textDocument/diagnostic</c>), while the files it has not opened are still pushed.
/// </summary>
public sealed class SyncAndPullTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-lsp-pull-" + Guid.NewGuid().ToString("N")[..8]);

    public SyncAndPullTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, "lyric.toml"), "[package]\nname = \"app\"\nversion = \"0.1.0\"\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private const string Clean = "fn main(): int {\n    return 0;\n}\n";

    private static string DidOpen(string uri, string text) =>
        JsonSerializer.Serialize(new { textDocument = new { uri, languageId = "lyric", version = 1, text } });

    private static async Task<JsonElement> DiagnosticsFor(ServerHarness harness, string uri)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);
            if (published.GetProperty("params").GetProperty("uri").GetString() == uri)
                return published.GetProperty("params").GetProperty("diagnostics");
        }
        throw new InvalidOperationException($"no diagnostics arrived for {uri}");
    }

    [Fact]
    public async Task An_edit_replaces_its_range_and_leaves_the_rest()
    {
        var uri = DocumentUri.FromFilePath(Path.Combine(_dir, "src", "main.lyr"));
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, Clean));
        Assert.Empty((await DiagnosticsFor(harness, uri)).EnumerateArray());

        // '0' on line 1 becomes a string; the next message makes it '1' and then '7' — its second
        // range counts in the text the first left.
        await harness.NotifyAsync(LspMethods.DidChange, JsonSerializer.Serialize(new
        {
            textDocument = new { uri, version = 2 },
            contentChanges = new object[]
            {
                new { range = new { start = new { line = 1, character = 11 }, end = new { line = 1, character = 12 } }, text = "\"no\"" },
            },
        }));
        var finding = Assert.Single((await DiagnosticsFor(harness, uri)).EnumerateArray());
        Assert.Equal("LYR-SEM0001", finding.GetProperty("code").GetString());

        await harness.NotifyAsync(LspMethods.DidChange, JsonSerializer.Serialize(new
        {
            textDocument = new { uri, version = 3 },
            contentChanges = new object[]
            {
                new { range = new { start = new { line = 1, character = 11 }, end = new { line = 1, character = 15 } }, text = "1" },
                new { range = new { start = new { line = 1, character = 11 }, end = new { line = 1, character = 12 } }, text = "7" },
            },
        }));
        Assert.Empty((await DiagnosticsFor(harness, uri)).EnumerateArray());
    }

    [Fact]
    public async Task A_client_that_asks_gets_an_open_files_diagnostics_and_the_closed_ones_pushed()
    {
        File.WriteAllText(Path.Combine(_dir, "src", "util.lyr"), "pub fn value(): int {\n    return \"one\";\n}\n");
        var main = "import app.util { value };\n\nfn main(): int {\n    let x: int = \"two\";\n    return value();\n}\n";
        var uri = DocumentUri.FromFilePath(Path.Combine(_dir, "src", "main.lyr"));
        var util = DocumentUri.FromFilePath(Path.Combine(_dir, "src", "util.lyr"));

        await using var harness = new ServerHarness();
        await harness.InitializeAsync("""{"textDocument":{"diagnostic":{}}}""");
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, main));

        // util is not open: its finding is pushed — and main's is not, which the package's run would
        // have published first (its files go out in the order of their paths)
        var pushed = new List<string>();
        while (true)
        {
            var published = (await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics)).GetProperty("params");
            pushed.Add(published.GetProperty("uri").GetString()!);
            if (pushed[^1] != util) continue;
            Assert.NotEmpty(published.GetProperty("diagnostics").EnumerateArray());
            break;
        }
        Assert.DoesNotContain(uri, pushed);

        // main is open: asked for, its own finding comes back as the report
        var id = await harness.RequestAsync(LspMethods.Diagnostic,
            JsonSerializer.Serialize(new { textDocument = new { uri } }));
        var report = (await harness.ReceiveResponseAsync(id)).GetProperty("result");
        Assert.Equal("full", report.GetProperty("kind").GetString());
        var finding = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal(3, finding.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public async Task A_pull_withdrawn_by_the_open_files_own_analysis_is_asked_again_and_the_server_stays()
    {
        // What an editor sends on opening a file: the open and, at once, the ask. The ask compiles
        // the package; the open's own run starts after the debounce and withdraws it. The answer is
        // then a cancellation the client asks again on — or the report, where the ask finished first.
        var uri = DocumentUri.FromFilePath(Path.Combine(_dir, "src", "main.lyr"));
        await using var harness = new ServerHarness(new LspServerOptions { Debounce = TimeSpan.FromMilliseconds(20) });
        await harness.InitializeAsync("""{"textDocument":{"diagnostic":{}}}""");
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, "fn main(): int {\n    return \"no\";\n}\n"));
        var asked = await harness.RequestAsync(LspMethods.Diagnostic,
            JsonSerializer.Serialize(new { textDocument = new { uri } }));

        var first = await harness.ReceiveResponseAsync(asked);
        if (first.TryGetProperty("error", out var error))
        {
            Assert.Equal(JsonRpcErrorCodes.ServerCancelled, error.GetProperty("code").GetInt32());
            Assert.True(error.GetProperty("data").GetProperty("retriggerRequest").GetBoolean());
        }

        // asked again, as the client does: the server is still there and says what the file holds
        var again = await harness.RequestAsync(LspMethods.Diagnostic,
            JsonSerializer.Serialize(new { textDocument = new { uri } }));
        var report = (await harness.ReceiveResponseAsync(again)).GetProperty("result");
        var finding = Assert.Single(report.GetProperty("items").EnumerateArray());
        Assert.Equal("LYR-SEM0001", finding.GetProperty("code").GetString());

        var shutdown = await harness.RequestAsync(LspMethods.Shutdown);
        await harness.ReceiveResponseAsync(shutdown);
        await harness.NotifyAsync(LspMethods.Exit);
        Assert.Equal(0, await harness.ExitCodeAsync());
    }

    [Fact]
    public async Task A_client_that_does_not_ask_gets_every_file_pushed()
    {
        // The counter-check: without the capability nothing changes for the open file.
        var uri = DocumentUri.FromFilePath(Path.Combine(_dir, "src", "main.lyr"));
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, "fn main(): int {\n    return \"no\";\n}\n"));
        Assert.NotEmpty((await DiagnosticsFor(harness, uri)).EnumerateArray());
    }

    [Fact]
    public async Task The_server_offers_both_the_ranges_and_the_pull()
    {
        await using var harness = new ServerHarness();
        var id = await harness.RequestAsync(LspMethods.Initialize, "{}");
        var capabilities = (await harness.ReceiveResponseAsync(id)).GetProperty("result").GetProperty("capabilities");
        Assert.Equal((int)TextDocumentSyncKind.Incremental, capabilities.GetProperty("textDocumentSync").GetProperty("change").GetInt32());
        var pull = capabilities.GetProperty("diagnosticProvider");
        Assert.True(pull.GetProperty("interFileDependencies").GetBoolean());
        Assert.False(pull.GetProperty("workspaceDiagnostics").GetBoolean());
    }
}
