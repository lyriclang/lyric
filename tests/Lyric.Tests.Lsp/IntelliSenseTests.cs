using System.Text.Json;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Lsp.Analysis;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// Regression tests for LSP 3.17 IntelliSense upgrades and hover conformance fixes (#767, #768).
/// </summary>
public sealed class IntelliSenseTests
{
    private static IReadOnlyList<CompletionItem> Complete(string programWithCursor, bool snippetSupport = false)
    {
        var offset = programWithCursor.IndexOf('$');
        Assert.True(offset >= 0, "the fixture has no '$' marking the cursor");

        var program = programWithCursor.Remove(offset, 1);
        var path = Path.Combine(AppContext.BaseDirectory, "intellisense.lyr");
        var items = CompletionProvider.At(path, program, offset, Single.Options(path), snippetSupport: snippetSupport);
        Assert.NotNull(items);
        return items;
    }

    private static HoverResult? HoverAt(string programWithCursor)
    {
        var offset = programWithCursor.IndexOf('$');
        Assert.True(offset >= 0);
        var program = programWithCursor.Remove(offset, 1);
        var path = Path.Combine(AppContext.BaseDirectory, "hover-derive.lyr");
        var result = Single.Check(path, program);
        Assert.NotNull(result.Model);
        var file = DiagnosticMapper.FindFile(result.Sources, path);
        return HoverProvider.At(result.Model, result.Model.Entry, file, offset);
    }

    private static DefinitionTarget? DefAt(string programWithCursor)
    {
        var offset = programWithCursor.IndexOf('$');
        Assert.True(offset >= 0);
        var program = programWithCursor.Remove(offset, 1);
        var path = Path.Combine(AppContext.BaseDirectory, "def-derive.lyr");
        var result = Single.Check(path, program);
        Assert.NotNull(result.Model);
        var file = DiagnosticMapper.FindFile(result.Sources, path);
        return DefinitionProvider.At(result.Model, result.Model.Entry, file, offset);
    }

    [Fact]
    public void Derived_struct_class_and_enum_preserve_documentation_767()
    {
        var s = HoverAt(
            "/// Doc for StructWithEq.\npub struct StructWithEq :: Equatable & Debug { pub x: int, }\n"
            + "pub fn use(a: $StructWithEq): int { return a.x; }\n");
        Assert.NotNull(s);
        Assert.Contains("Doc for StructWithEq.", s.Markdown);

        var c = HoverAt(
            "/// Doc for ClassWithEq.\npub class ClassWithEq :: Equatable { pub x: int = 0, }\n"
            + "pub fn use(a: $ClassWithEq): int { return a.x; }\n");
        Assert.NotNull(c);
        Assert.Contains("Doc for ClassWithEq.", c.Markdown);

        var e = HoverAt(
            "/// Doc for EnumWithEq.\npub enum EnumWithEq :: Equatable & Debug { A, B, }\n"
            + "pub fn use(a: $EnumWithEq): int { return 0; }\n");
        Assert.NotNull(e);
        Assert.Contains("Doc for EnumWithEq.", e.Markdown);
    }

    [Fact]
    public void Hover_and_definition_resolve_synthesized_builtin_conformances_768()
    {
        const string srcEq = "pub struct S :: Eq$uatable & Debug { pub x: int, }\n";
        var eqHover = HoverAt(srcEq);
        Assert.NotNull(eqHover);
        Assert.Contains("Equatable", eqHover.Markdown);

        var eqDef = DefAt(srcEq);
        Assert.NotNull(eqDef);

        const string srcDbg = "pub struct S :: Equatable & De$bug { pub x: int, }\n";
        var dbgHover = HoverAt(srcDbg);
        Assert.NotNull(dbgHover);
        Assert.Contains("Debug", dbgHover.Markdown);
    }

    [Fact]
    public async Task Initialize_advertises_expanded_triggers_and_wire_snippet_support()
    {
        await using var harness = new ServerHarness();
        var id = await harness.RequestAsync(LspMethods.Initialize, JsonSerializer.Serialize(new
        {
            capabilities = new
            {
                textDocument = new
                {
                    completion = new { completionItem = new { snippetSupport = true } },
                },
            },
        }));
        var caps = (await harness.ReceiveResponseAsync(id)).GetProperty("result").GetProperty("capabilities");

        var triggers = caps.GetProperty("completionProvider").GetProperty("triggerCharacters")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal([".", ":", "@", "<", "/"], triggers);

        var reTriggers = caps.GetProperty("signatureHelpProvider").GetProperty("retriggerCharacters")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal([","], reTriggers);

        var uri = DocumentUri.FromFilePath(Path.Combine(AppContext.BaseDirectory, "wire-snip.lyr"));
        await harness.NotifyAsync(LspMethods.DidOpen, JsonSerializer.Serialize(new
        {
            textDocument = new
            {
                uri,
                languageId = "lyric",
                version = 1,
                text = "fn add(a: int, b: int): int { return a + b; }\nfn main(): int {\n    return a;\n}\n",
            },
        }));
        await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);

        var cid = await harness.RequestAsync(LspMethods.Completion, JsonSerializer.Serialize(new
        {
            textDocument = new { uri },
            position = new { line = 2, character = 12 },
        }));
        var items = (await harness.ReceiveResponseAsync(cid)).GetProperty("result").EnumerateArray().ToArray();
        var add = Assert.Single(items, el => el.GetProperty("label").GetString() == "add");
        Assert.Equal("add($1)$0", add.GetProperty("insertText").GetString());
        Assert.Equal(2, add.GetProperty("insertTextFormat").GetInt32());
        Assert.Contains("a: int, b: int", add.GetProperty("labelDetails").GetProperty("detail").GetString());
    }

    [Fact]
    public void Struct_initializer_offers_only_unset_fields_with_snippets()
    {
        var items = Complete(
            "struct Point { x: int, y: int, }\n"
            + "fn main(): int {\n    let p = Point { x = 1, $ };\n    return p.x;\n}\n",
            snippetSupport: true);

        Assert.Contains(items, i => i.Label == "y" && i.InsertText == "y = $1" && i.InsertTextFormat == 2);
        Assert.DoesNotContain(items, i => i.Label == "x");
    }

    [Fact]
    public void Type_position_filters_values_and_keeps_types_and_prelude()
    {
        var items = Complete(
            "struct Point { x: int, }\nfn helper(): int { return 1; }\n"
            + "fn main(): int {\n    let num = 42;\n    let p: $ = Point { x = 1 };\n    return num;\n}\n");

        Assert.Contains(items, i => i.Label == "Point" && i.Kind == CompletionItemKind.Struct);
        Assert.Contains(items, i => i.Label == "List");
        Assert.Contains(items, i => i.Label == "int");
        Assert.DoesNotContain(items, i => i.Label == "num");
        Assert.DoesNotContain(items, i => i.Label == "helper");
    }

    [Fact]
    public void Import_completion_suggests_modules_and_exports_without_snippets()
    {
        var modItems = Complete("import std.$;\nfn main(): void {}\n");
        Assert.Contains(modItems, i => i.Label == "os" && i.Kind == CompletionItemKind.Module);

        var selItems = Complete("import std.os { $ };\nfn main(): void {}\n", snippetSupport: true);
        var cpu = Assert.Single(selItems, i => i.Label == "cpuCount");
        Assert.Null(cpu.InsertText);
    }

    [Fact]
    public void Signature_help_advances_parameter_index_after_trailing_comma()
    {
        const string prog = "fn add(a: int, b: int): int { return a + b; }\nfn main(): int {\n    return add(1, $);\n}\n";
        var offset = prog.IndexOf('$');
        var src = prog.Remove(offset, 1);
        var path = Path.Combine(AppContext.BaseDirectory, "sig-comma.lyr");
        var help = SignatureHelpProvider.At(path, src, offset, Single.Options(path));
        Assert.NotNull(help);
        Assert.Equal(1, help.ActiveParameter);
    }

    [Fact]
    public void Completion_never_leaks_marker_across_declarations_and_incomplete_imports()
    {
        var declItems = Complete("fn ma$(): void {}\n");
        Assert.DoesNotContain(declItems, i => i.Label.Contains("__lyric_completion__", StringComparison.Ordinal));
        Assert.Empty(declItems);

        var impItems = Complete("import std.u$\nstruct MyTest { id: int, }\n");
        Assert.DoesNotContain(impItems, i => i.Label.Contains("__lyric_completion__", StringComparison.Ordinal));
        Assert.Contains(impItems, i => i.Label == "uri" && i.Kind == CompletionItemKind.Module);
        Assert.DoesNotContain(impItems, i => i.Label == "uint" || i.Label == "Coroutine");
    }

    [Fact]
    public void Struct_initializer_offers_fields_on_empty_brace_and_unclosed_second_field()
    {
        var emptyBrace = Complete(
            "struct MyTest { pub id: int, pub name: string, }\n"
            + "fn main(): void {\n    let t: MyTest = MyTest { $ };\n}\n");
        Assert.Contains(emptyBrace, i => i.Label == "id" && i.Kind == CompletionItemKind.Field);
        Assert.Contains(emptyBrace, i => i.Label == "name" && i.Kind == CompletionItemKind.Field);

        var unclosedSecond = Complete(
            "struct MyTest { pub id: int, pub name: string, }\n"
            + "fn main(): void {\n    let t: MyTest = MyTest { id = 0, $\n}\n");
        Assert.DoesNotContain(unclosedSecond, i => i.Label == "id");
        Assert.Contains(unclosedSecond, i => i.Label == "name" && i.Kind == CompletionItemKind.Field);
    }
}
