using System.Text.Json;
using Lyric.Lsp;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// The window before a file's first analysis (E20): a hover in it waits for the analysis instead of
/// answering null, which a client reads as "nothing here" and does not ask again; and a client that
/// draws progress is shown that the analysis runs.
/// </summary>
public sealed class NullWindowTests
{
    private const string Program = "fn main(): int {\n    let count = 1;\n    return count;\n}\n";

    private const string WorkDoneProgress = """{"window":{"workDoneProgress":true}}""";

    private static string BufferPath([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Path.Combine(AppContext.BaseDirectory, $"{name.ToLowerInvariant()}.lyr");

    private static string DidOpen(string uri, string text) =>
        JsonSerializer.Serialize(new { textDocument = new { uri, languageId = "lyric", version = 1, text } });

    private static string HoverOnCount(string uri) =>
        JsonSerializer.Serialize(new { textDocument = new { uri }, position = new { line = 2, character = 12 } });

    private static string? Method(JsonElement message) =>
        message.TryGetProperty("method", out var method) ? method.GetString() : null;

    private static bool Answers(JsonElement message, int id) =>
        Method(message) is null && message.TryGetProperty("id", out var value)
        && value.ValueKind == JsonValueKind.Number && value.GetInt32() == id;

    [Fact]
    public async Task A_hover_before_the_first_analysis_waits_for_it()
    {
        // The debounce keeps the analysis back long enough for the hover to arrive before it.
        var uri = DocumentUri.FromFilePath(BufferPath());
        await using var harness = new ServerHarness(new LspServerOptions { Debounce = TimeSpan.FromMilliseconds(300) });
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, Program));
        var id = await harness.RequestAsync(LspMethods.Hover, HoverOnCount(uri));

        var published = false;
        while (true)
        {
            var message = await harness.ReceiveAsync();
            if (Method(message) == LspMethods.PublishDiagnostics) published = true;
            if (!Answers(message, id)) continue;

            Assert.True(published, "the hover was answered before the analysis it needs");
            Assert.Contains("let count: int", message.GetProperty("result").GetProperty("contents")
                .GetProperty("value").GetString());
            return;
        }
    }

    [Fact]
    public async Task A_held_hover_the_client_withdraws_is_cancelled()
    {
        // No analysis comes within the test: the hover stays held until the client withdraws it.
        var uri = DocumentUri.FromFilePath(BufferPath());
        await using var harness = new ServerHarness(new LspServerOptions { Debounce = TimeSpan.FromMinutes(5) });
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, Program));
        var id = await harness.RequestAsync(LspMethods.Hover, HoverOnCount(uri));
        await harness.NotifyAsync(LspMethods.CancelRequest, $$"""{"id":{{id}}}""");

        var response = await harness.ReceiveResponseAsync(id);
        Assert.Equal(JsonRpcErrorCodes.RequestCancelled, response.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task The_first_analysis_is_reported_as_progress()
    {
        var uri = DocumentUri.FromFilePath(BufferPath());
        await using var harness = new ServerHarness();
        await harness.InitializeAsync(WorkDoneProgress);

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, Program));

        // The token is asked for first, and used only once the client has made it.
        string? token = null;
        var kinds = new List<string>();
        var said = new List<string>();
        while (kinds.LastOrDefault() != "end")
        {
            var message = await harness.ReceiveAsync();
            if (Method(message) == LspMethods.WorkDoneProgressCreate)
            {
                Assert.Null(token);
                token = message.GetProperty("params").GetProperty("token").GetString();
                await harness.SendAsync($$"""{"jsonrpc":"2.0","id":{{message.GetProperty("id").GetInt32()}},"result":null}""");
            }
            else if (Method(message) == LspMethods.Progress)
            {
                Assert.NotNull(token);
                Assert.Equal(token, message.GetProperty("params").GetProperty("token").GetString());
                var value = message.GetProperty("params").GetProperty("value");
                kinds.Add(value.GetProperty("kind").GetString()!);
                if (value.TryGetProperty("message", out var text)) said.Add(text.GetString()!);
            }
        }

        // Begun, told each phase, ended: the phases in between, in the order the front end runs them.
        Assert.Equal("begin", kinds[0]);
        Assert.Equal("end", kinds[^1]);
        Assert.All(kinds[1..^1], kind => Assert.Equal("report", kind));
        Assert.Contains(said, text => text.StartsWith("parsing", StringComparison.Ordinal));
        Assert.Contains(said, text => text.StartsWith("checking", StringComparison.Ordinal));
        Assert.True(said.FindIndex(text => text.StartsWith("parsing", StringComparison.Ordinal))
                    < said.FindIndex(text => text.StartsWith("checking", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_later_analysis_shorter_than_the_delay_is_not_reported()
    {
        // A bar for each keystroke would be a flicker: the delay is out of reach here.
        var uri = DocumentUri.FromFilePath(BufferPath());
        await using var harness = new ServerHarness(new LspServerOptions { ProgressDelay = TimeSpan.FromMinutes(10) });
        await harness.InitializeAsync(WorkDoneProgress);

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, Program));

        // The first analysis to its end: its token is asked for on a task of its own, and may
        // arrive after its diagnostics.
        while (true)
        {
            var message = await harness.ReceiveAsync();
            if (Method(message) == LspMethods.WorkDoneProgressCreate)
                await harness.SendAsync($$"""{"jsonrpc":"2.0","id":{{message.GetProperty("id").GetInt32()}},"result":null}""");
            else if (Method(message) == LspMethods.Progress
                     && message.GetProperty("params").GetProperty("value").GetProperty("kind").GetString() == "end")
                break;
        }

        await harness.NotifyAsync(LspMethods.DidChange, JsonSerializer.Serialize(new
        {
            textDocument = new { uri, version = 2 },
            contentChanges = new[] { new { text = Program.Replace("1", "2") } },
        }));

        while (await harness.ReceiveAsync() is var message && Method(message) != LspMethods.PublishDiagnostics)
            Assert.NotEqual(LspMethods.WorkDoneProgressCreate, Method(message));
    }

    [Fact]
    public async Task A_later_analysis_longer_than_the_delay_is_reported()
    {
        // In a large package every keystroke's analysis takes seconds; past the delay it is shown.
        var uri = DocumentUri.FromFilePath(BufferPath());
        await using var harness = new ServerHarness(new LspServerOptions { ProgressDelay = TimeSpan.Zero });
        await harness.InitializeAsync(WorkDoneProgress);

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, Program));
        while (true)
        {
            var message = await harness.ReceiveAsync();
            if (Method(message) == LspMethods.WorkDoneProgressCreate)
                await harness.SendAsync($$"""{"jsonrpc":"2.0","id":{{message.GetProperty("id").GetInt32()}},"result":null}""");
            else if (Method(message) == LspMethods.Progress
                     && message.GetProperty("params").GetProperty("value").GetProperty("kind").GetString() == "end")
                break;
        }

        await harness.NotifyAsync(LspMethods.DidChange, JsonSerializer.Serialize(new
        {
            textDocument = new { uri, version = 2 },
            contentChanges = new[] { new { text = Program.Replace("1", "2") } },
        }));

        var asked = false;
        while (true)
        {
            var message = await harness.ReceiveAsync();
            if (Method(message) == LspMethods.WorkDoneProgressCreate)
            {
                asked = true;
                await harness.SendAsync($$"""{"jsonrpc":"2.0","id":{{message.GetProperty("id").GetInt32()}},"result":null}""");
            }
            else if (Method(message) == LspMethods.Progress
                     && message.GetProperty("params").GetProperty("value").GetProperty("kind").GetString() == "end")
                break;
        }
        Assert.True(asked);
    }

    [Fact]
    public async Task A_client_that_draws_no_progress_is_sent_none()
    {
        var uri = DocumentUri.FromFilePath(BufferPath());
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, Program));
        while (await harness.ReceiveAsync() is var message && Method(message) != LspMethods.PublishDiagnostics)
            Assert.DoesNotContain(Method(message), new[] { LspMethods.WorkDoneProgressCreate, LspMethods.Progress });
    }
}
