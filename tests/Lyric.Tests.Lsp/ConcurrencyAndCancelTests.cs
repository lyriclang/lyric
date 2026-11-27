using System.Text.Json;
using Lyric.Core;
using Lyric.Lsp;
using Lyric.Lsp.Analysis;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;
using Lyric.Parsing;

namespace Lyric.Tests.Lsp;

/// <summary>
/// Regression tests for LSP Phase 1 concurrency, cancellation, TOCTOU protection,
/// syntax recovery (<c>p.\nreturn</c>), and fast syntax outlines before sema finishes.
/// </summary>
public sealed class ConcurrencyAndCancelTests
{
    private static string DidOpen(string uri, string text, int version = 1) =>
        JsonSerializer.Serialize(new { textDocument = new { uri, languageId = "lyric", version, text } });

    [Fact]
    public async Task Outline_and_folding_answer_immediately_from_syntax_cache_before_first_analysis()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fast-outline.lyr");
        var uri = DocumentUri.FromFilePath(path);
        const string text =
            "struct Point {\n"
            + "    x: int,\n"
            + "    y: int,\n"
            + "}\n"
            + "\n"
            + "fn main(): int {\n"
            + "    return 0;\n"
            + "}\n";

        await using var harness = new ServerHarness(new LspServerOptions { Debounce = TimeSpan.FromMinutes(5) });
        await harness.InitializeAsync("""{"textDocument":{"documentSymbol":{"hierarchicalDocumentSymbolSupport":true}}}""");
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, text));

        var symId = await harness.RequestAsync(LspMethods.DocumentSymbol,
            JsonSerializer.Serialize(new { textDocument = new { uri } }));
        var symResp = await harness.ReceiveResponseAsync(symId);
        var syms = symResp.GetProperty("result").EnumerateArray().ToArray();
        Assert.Equal(2, syms.Length);
        Assert.Equal("Point", syms[0].GetProperty("name").GetString());
        Assert.Equal("main", syms[1].GetProperty("name").GetString());

        var foldId = await harness.RequestAsync(LspMethods.FoldingRange,
            JsonSerializer.Serialize(new { textDocument = new { uri } }));
        var foldResp = await harness.ReceiveResponseAsync(foldId);
        Assert.True(foldResp.GetProperty("result").GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Cancellation_stops_in_flight_requests_without_blocking_other_responses()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "cancel-inflight.lyr");
        var uri = DocumentUri.FromFilePath(path);
        const string text = "fn main(): int {\n    let count = 1;\n    return count;\n}\n";

        await using var harness = new ServerHarness(new LspServerOptions { Debounce = TimeSpan.FromMinutes(5) });
        await harness.InitializeAsync("""{"textDocument":{"documentSymbol":{"hierarchicalDocumentSymbolSupport":true}}}""");
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, text));

        // Send a request that waits on first analysis, then ask for documentSymbol, then cancel the waiting one.
        var refId = await harness.RequestAsync(LspMethods.References, JsonSerializer.Serialize(new
        {
            textDocument = new { uri },
            position = new { line = 2, character = 12 },
            context = new { includeDeclaration = true },
        }));

        // Read-loop is non-blocking: documentSymbol answers immediately while References is held!
        var symId = await harness.RequestAsync(LspMethods.DocumentSymbol,
            JsonSerializer.Serialize(new { textDocument = new { uri } }));
        var symResp = await harness.ReceiveResponseAsync(symId);
        Assert.True(symResp.TryGetProperty("result", out _));

        await harness.NotifyAsync(LspMethods.CancelRequest, $$"""{"id":{{refId}}}""");
        var refResp = await harness.ReceiveResponseAsync(refId);
        Assert.Equal(JsonRpcErrorCodes.RequestCancelled, refResp.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Dot_before_return_keyword_does_not_swallow_return_during_recovery()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "dot-before-return.lyr");
        const string source =
            "struct Point { x: int, }\n"
            + "fn main(): int {\n"
            + "    let p = Point { x = 1 };\n"
            + "    p.\n"
            + "    return 0;\n"
            + "}\n";

        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var fid = sm.AddVirtual(path, source);
        var parsed = ParsedModule.Parse(sm, fid, de);

        // Expect 'main' to still contain 3 statements (let p, expr p., return 0) rather than swallowing 'return'.
        var mainFn = Assert.Single(parsed.Ast.Declarations.OfType<Lyric.AST.FunctionDecl>(), f => f.Name == "main");
        Assert.NotNull(mainFn.Body);
        Assert.True(mainFn.Body.Statements.Length >= 3,
            $"Expected >=3 statements, got {mainFn.Body.Statements.Length}");
        Assert.IsType<Lyric.AST.ReturnStmt>(mainFn.Body.Statements[^1]);
    }

    [Fact]
    public async Task Rapid_didChange_burst_delivers_diagnostics_for_latest_version_only()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "rapid-burst.lyr");
        var uri = DocumentUri.FromFilePath(path);
        const string v1 = "fn main(): int {\n    return \"bad\";\n}\n";
        const string v5 = "fn main(): int {\n    return 0;\n}\n";

        await using var harness = new ServerHarness(new LspServerOptions { Debounce = TimeSpan.FromMilliseconds(35) });
        await harness.InitializeAsync();
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, v1, 1));
        for (var v = 2; v <= 4; v++)
        {
            await harness.NotifyAsync(LspMethods.DidChange, JsonSerializer.Serialize(new
            {
                textDocument = new { uri, version = v },
                contentChanges = new[] { new { text = v1.Replace("bad", $"bad{v}") } },
            }));
        }
        await harness.NotifyAsync(LspMethods.DidChange, JsonSerializer.Serialize(new
        {
            textDocument = new { uri, version = 5 },
            contentChanges = new[] { new { text = v5 } },
        }));

        while (true)
        {
            var note = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);
            var p = note.GetProperty("params");
            if (p.GetProperty("uri").GetString() != uri) continue;
            if (p.TryGetProperty("version", out var ver) && ver.GetInt32() == 5)
            {
                Assert.Empty(p.GetProperty("diagnostics").EnumerateArray());
                break;
            }
        }
    }
}
