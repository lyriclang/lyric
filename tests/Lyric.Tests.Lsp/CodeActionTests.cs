using System.Text.Json;
using Lyric.Lsp;
using Lyric.Lsp.Analysis;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// The quick fixes: the changes a finding's notes suggest, word for word, the one <c>lyric fix</c>
/// would apply preferred; a lint's <c>@Allow</c> above its declaration; and the catalog's page of
/// each code beside the finding.
/// </summary>
public sealed class CodeActionTests
{
    private static string BufferPath([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Path.Combine(AppContext.BaseDirectory, $"actions_{name.ToLowerInvariant()}.lyr");

    private static string DidOpen(string uri, string text) =>
        JsonSerializer.Serialize(new { textDocument = new { uri, languageId = "lyric", version = 1, text } });

    /// <summary>The program opened and analysed, its diagnostics and the actions over all of it.</summary>
    private static async Task<(JsonElement Diagnostics, JsonElement[] Actions)> ActionsFor(string program,
        [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var uri = DocumentUri.FromFilePath(BufferPath(name));
        await using var harness = new ServerHarness(new LspServerOptions { Debounce = TimeSpan.Zero, Version = "5.0.0" });
        await harness.InitializeAsync();
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, program));
        var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);

        var id = await harness.RequestAsync(LspMethods.CodeAction, JsonSerializer.Serialize(new
        {
            textDocument = new { uri },
            range = new { start = new { line = 0, character = 0 }, end = new { line = 99, character = 0 } },
            context = new { diagnostics = Array.Empty<object>() },
        }));
        var actions = (await harness.ReceiveResponseAsync(id)).GetProperty("result").EnumerateArray().ToArray();
        return (published.GetProperty("params").GetProperty("diagnostics"), actions);
    }

    private static (int Line, int Character, int EndLine, int EndCharacter, string Text) OnlyEdit(JsonElement action)
    {
        var edits = action.GetProperty("edit").GetProperty("changes").EnumerateObject().Single().Value;
        var edit = Assert.Single(edits.EnumerateArray());
        var range = edit.GetProperty("range");
        return (range.GetProperty("start").GetProperty("line").GetInt32(), range.GetProperty("start").GetProperty("character").GetInt32(),
            range.GetProperty("end").GetProperty("line").GetInt32(), range.GetProperty("end").GetProperty("character").GetInt32(),
            edit.GetProperty("newText").GetString()!);
    }

    [Fact]
    public async Task A_safe_change_the_compiler_suggests_is_the_preferred_fix()
    {
        // 'var' never assigned again: the warning suggests 'let', and 'lyric fix' would write it.
        var (_, actions) = await ActionsFor("fn main(): int {\n    var x = 1;\n    return x;\n}\n");
        var fix = Assert.Single(actions, a => a.GetProperty("title").GetString() == "write 'let'");
        Assert.Equal("quickfix", fix.GetProperty("kind").GetString());
        Assert.True(fix.GetProperty("isPreferred").GetBoolean());
        Assert.Equal((1, 4, 1, 7, "let"), OnlyEdit(fix));
    }

    [Fact]
    public async Task A_change_that_needs_a_reader_is_offered_and_not_preferred()
    {
        var (_, actions) = await ActionsFor("fn main(): int {\n    let count = 1;\n    return coun;\n}\n");
        var fix = Assert.Single(actions, a => a.GetProperty("title").GetString() == "did you mean 'count'?");
        Assert.False(fix.GetProperty("isPreferred").GetBoolean());
        Assert.Equal((2, 11, 2, 15, "count"), OnlyEdit(fix));
    }

    [Fact]
    public async Task A_lint_can_be_allowed_above_its_declaration()
    {
        var (_, actions) = await ActionsFor("fn main(): int {\n    let unused = 1;\n    return 0;\n}\n");
        var allow = Assert.Single(actions, a => a.GetProperty("title").GetString()!.StartsWith("allow 'unused-variable'", StringComparison.Ordinal));
        Assert.Equal((0, 0, 0, 0, "@Allow(\"LYR-SEM0071\")\n"), OnlyEdit(allow));
    }

    [Fact]
    public async Task An_error_without_a_suggestion_offers_nothing()
    {
        // The counter-check: no action the compiler did not suggest.
        var (_, actions) = await ActionsFor("fn main(): int {\n    let x: int = \"no\";\n    return x;\n}\n");
        Assert.Empty(actions);
    }

    [Fact]
    public async Task A_finding_links_its_page_in_the_catalog()
    {
        var (diagnostics, _) = await ActionsFor("fn main(): int {\n    let x: int = \"no\";\n    return x;\n}\n");
        var finding = Assert.Single(diagnostics.EnumerateArray());
        Assert.Equal("https://lyric.oil1i.de/5.0/diagnostics/LYR-SEM0001/",
            finding.GetProperty("codeDescription").GetProperty("href").GetString());
    }
}
