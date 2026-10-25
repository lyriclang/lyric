using System.Text.Json;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary><c>lyric.expand</c>: the module of an open document with its macros expanded — what
/// <c>lyric expand</c> prints —, for the client to show beside it.</summary>
public sealed class ExpandCommandTests
{
    private static string DidOpen(string uri, string text) =>
        JsonSerializer.Serialize(new { textDocument = new { uri, languageId = "lyric", version = 1, text } });

    private static async Task<JsonElement> Expand(ServerHarness harness, string uri)
    {
        var id = await harness.RequestAsync(LspMethods.ExecuteCommand,
            JsonSerializer.Serialize(new { command = LspMethods.ExpandCommand, arguments = new[] { uri } }));
        return (await harness.ReceiveResponseAsync(id)).GetProperty("result");
    }

    [Fact]
    public async Task An_open_module_comes_back_expanded_in_the_one_form()
    {
        var uri = DocumentUri.FromFilePath(Path.Combine(AppContext.BaseDirectory, "expand-plain.lyr"));
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, "fn main(): int { return 1; }\n"));

        var result = await Expand(harness, uri);
        Assert.Equal("fn main(): int {\n    return 1;\n}", result.GetProperty("text").GetString()!.TrimEnd());
    }

    [Fact]
    public async Task A_document_that_is_not_open_is_refused_with_a_reason()
    {
        var uri = DocumentUri.FromFilePath(Path.Combine(AppContext.BaseDirectory, "expand-closed.lyr"));
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        var result = await Expand(harness, uri);
        Assert.Equal("the document is not open", result.GetProperty("refused").GetString());
    }

    [Fact]
    public async Task The_server_offers_the_command_and_the_quick_fixes()
    {
        await using var harness = new ServerHarness();
        var id = await harness.RequestAsync(LspMethods.Initialize, "{}");
        var capabilities = (await harness.ReceiveResponseAsync(id)).GetProperty("result").GetProperty("capabilities");
        Assert.Equal(LspMethods.ExpandCommand,
            Assert.Single(capabilities.GetProperty("executeCommandProvider").GetProperty("commands").EnumerateArray()).GetString());
        Assert.Equal("quickfix",
            Assert.Single(capabilities.GetProperty("codeActionProvider").GetProperty("codeActionKinds").EnumerateArray()).GetString());
    }
}
