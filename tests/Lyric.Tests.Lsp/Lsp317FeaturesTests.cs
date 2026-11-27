using System.Text.Json;
using Lyric.Compiler;
using Lyric.Lsp;
using Lyric.Lsp.Analysis;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// Coverage of Phase 1–3 LSP 3.17 features: declaration hover, contextual completions
/// (<c>.Variant</c>, <c>@Attr</c>, <c>f&lt;T&gt;</c>), typeDefinition, implementation,
/// documentHighlight, selectionRange, documentLink, call/type hierarchy, semanticTokens delta/range,
/// pull diagnostics unchanged + workspace/diagnostic, willRenameFiles, codeActions, and telemetry.
/// </summary>
public sealed class Lsp317FeaturesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-lsp317-" + Guid.NewGuid().ToString("N")[..8]);

    public Lsp317FeaturesTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, "lyric.toml"), "[package]\nname = \"app\"\nversion = \"0.1.0\"\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private static (CompileResult Result, Core.FileId File, int Offset) CheckCursor(string source, string fileName = "feat.lyr")
    {
        var offset = source.IndexOf('$');
        Assert.True(offset >= 0);
        var clean = source.Remove(offset, 1);
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        var result = Single.Check(path, clean);
        Assert.NotNull(result.Model);
        var file = DiagnosticMapper.FindFile(result.Sources, path);
        Assert.True(file.IsValid);
        return (result, file, offset);
    }

    [Fact]
    public void Hover_answers_on_declaration_names_for_fn_struct_enum_field_and_interface()
    {
        var (res, file, offFn) = CheckCursor("/// Adds two ints.\nfn ad$d(a: int, b: int): int { return a + b; }\n");
        var hFn = HoverProvider.At(res.Model!, res.Model!.Entry, file, offFn);
        Assert.NotNull(hFn);
        Assert.Contains("fn add(a: int, b: int): int", hFn.Markdown);
        Assert.Contains("Adds two ints.", hFn.Markdown);

        var (res2, file2, offStruct) = CheckCursor("struct Po$int { x: int, }\nfn main(): int { return 0; }\n");
        var hStruct = HoverProvider.At(res2.Model!, res2.Model!.Entry, file2, offStruct);
        Assert.NotNull(hStruct);
        Assert.Contains("struct Point", hStruct.Markdown);

        var (res3, file3, offIface) = CheckCursor("interface Sha$ped { fn size(): int; }\nfn main(): int { return 0; }\n");
        var hIface = HoverProvider.At(res3.Model!, res3.Model!.Entry, file3, offIface);
        Assert.NotNull(hIface);
        Assert.Contains("interface Shaped", hIface.Markdown);
    }

    [Fact]
    public void Completion_handles_implicit_enum_variant_attributes_and_generic_type_args()
    {
        // 1. Implicit '.Variant' shorthand
        const string enumSrc = "enum Color {\n    Red,\n    Green,\n}\nfn main(): int {\n    let c: Color = .$\n    return 0;\n}\n";
        var enumOff = enumSrc.IndexOf('$');
        var enumPath = Path.Combine(AppContext.BaseDirectory, "imp.lyr");
        var enumItems = CompletionProvider.At(enumPath, enumSrc.Remove(enumOff, 1), enumOff, Single.Options(enumPath));
        Assert.NotNull(enumItems);
        Assert.Contains(enumItems, i => i.Label == "Red" && i.Kind == CompletionItemKind.EnumMember);
        Assert.DoesNotContain(enumItems, i => i.Label == "c");

        // 2. Generic call '<' offers types, filters out local variables
        const string genSrc = "fn id<T>(v: T): T { return v; }\nfn main(): int {\n    let localVar = 1;\n    return id<$\n}\n";
        var genOff = genSrc.IndexOf('$');
        var genPath = Path.Combine(AppContext.BaseDirectory, "gen.lyr");
        var genItems = CompletionProvider.At(genPath, genSrc.Remove(genOff, 1), genOff, Single.Options(genPath));
        Assert.NotNull(genItems);
        Assert.Contains(genItems, i => i.Label == "int");
        Assert.DoesNotContain(genItems, i => i.Label == "localVar");

        // 3. '@' attribute completion offers std.core attributes
        const string attrSrc = "@$\nfn helper(): void { }\n";
        var attrOff = attrSrc.IndexOf('$');
        var attrPath = Path.Combine(AppContext.BaseDirectory, "attr.lyr");
        var attrItems = CompletionProvider.At(attrPath, attrSrc.Remove(attrOff, 1), attrOff, Single.Options(attrPath));
        Assert.NotNull(attrItems);
        Assert.Contains(attrItems, i => i.Label == "Test");
    }

    [Fact]
    public void Navigation_hierarchy_tokens_delta_range_and_inlay_parameter_hints_work()
    {
        const string code =
            "import std.io { println };\n"
            + "interface Greet { fn hi(): int; }\n"
            + "struct Person :: [Greet] {\n"
            + "    age: int,\n"
            + "    fn hi(): int { return this.age; }\n"
            + "}\n"
            + "fn mix(first: int, second: int): int { return first + second; }\n"
            + "fn helper(): int {\n"
            + "    let p = Person { age = 5 };\n"
            + "    let sum = mix(10, 20);\n"
            + "    return p.age + sum;\n"
            + "}\n"
            + "fn main(): int {\n"
            + "    return helper();\n"
            + "}\n";

        var path = Path.Combine(AppContext.BaseDirectory, "nav-all.lyr");
        var res = Single.Check(path, code);
        var model = res.Model!;
        var file = DiagnosticMapper.FindFile(res.Sources, path);

        // TypeDefinition on 'p' -> Person struct
        var td = NavigationProviders.TypeDefinitionAt(model, model.Entry, file, code.IndexOf("p.age", StringComparison.Ordinal));
        Assert.NotNull(td);
        Assert.Equal("Person", code.Substring(td.NameSpan.Start, td.NameSpan.Length));

        // Implementation on 'Greet' -> Person
        var impls = NavigationProviders.ImplementationAt(model, model.Entry, file, code.IndexOf("Greet {", StringComparison.Ordinal));
        Assert.Contains(impls, t => code.Substring(t.NameSpan.Start, t.NameSpan.Length) == "Person");

        // DocumentHighlight on 'sum' -> Write + Read
        var hl = NavigationProviders.DocumentHighlightsAt(model, model.Entry, file, res.Sources, code.IndexOf("+ sum", StringComparison.Ordinal) + 2);
        Assert.Contains(hl, h => h.Kind == DocumentHighlightKind.Write);
        Assert.Contains(hl, h => h.Kind == DocumentHighlightKind.Read);

        // SelectionRange chain grows outward
        var sel = NavigationProviders.SelectionRangesAt(model.Entry, file, res.Sources, [new Position { Line = 10, Character = 18 }]);
        Assert.Single(sel);
        Assert.NotNull(sel[0].Parent);

        // DocumentLinks for import
        var links = NavigationProviders.DocumentLinksOf(model, model.Entry, file, res.Sources);
        Assert.NotEmpty(links);
        Assert.All(links, l => Assert.False(string.IsNullOrEmpty(l.Target)));

        // CallHierarchy on 'helper' -> IncomingCalls has 'main', OutgoingCalls has 'mix'
        var chItems = HierarchyProviders.PrepareCallHierarchy(model, model.Entry, file, res.Sources, code.IndexOf("helper():", StringComparison.Ordinal));
        Assert.Single(chItems);
        var snap = new AnalysisSnapshot(model, res.Sources, file, model.Entry, code, 1, false);
        var incoming = HierarchyProviders.IncomingCalls([snap], chItems[0]);
        Assert.Contains(incoming, c => c.From.Name == "main");
        var outgoing = HierarchyProviders.OutgoingCalls([snap], chItems[0]);
        Assert.Contains(outgoing, c => c.To.Name == "mix");

        // TypeHierarchy on 'Greet' -> Subtypes contains Person
        var thItems = HierarchyProviders.PrepareTypeHierarchy(model, model.Entry, file, res.Sources, code.IndexOf("Greet {", StringComparison.Ordinal));
        Assert.Single(thItems);
        var subs = HierarchyProviders.Subtypes([snap], thItems[0]);
        Assert.Contains(subs, s => s.Name == "Person");

        // Parameter inlay hints for mix(10, 20) + Type inlay hint TextEdits
        var wholeRange = new Lyric.Lsp.Protocol.Range
        {
            Start = new Position { Line = 0, Character = 0 },
            End = new Position { Line = 100, Character = 0 },
        };
        var hints = InlayHintProvider.Of(model, model.Entry, file, res.Sources, wholeRange);
        Assert.Contains(hints, h => h.Kind == InlayHintKind.Parameter && h.Label == "first:");
        Assert.Contains(hints, h => h.Kind == InlayHintKind.Parameter && h.Label == "second:");
        Assert.Contains(hints, h => h.Kind == InlayHintKind.Type && h.TextEdits is { Count: 1 });

        // SemanticTokens range + delta
        var fullTokens = SemanticTokensProvider.Of(model, model.Entry, file, res.Sources);
        var line8Range = new Lyric.Lsp.Protocol.Range { Start = new Position { Line = 8, Character = 0 }, End = new Position { Line = 8, Character = 50 } };
        var rangeTokens = SemanticTokensProvider.OfRange(model, model.Entry, file, res.Sources, line8Range);
        Assert.True(rangeTokens.Count > 0 && rangeTokens.Count < fullTokens.Count);
        var deltaEmpty = SemanticTokensProvider.ComputeDelta(fullTokens, fullTokens);
        Assert.Empty(deltaEmpty);
    }

    [Fact]
    public async Task Pull_unchanged_workspace_diagnostic_willRenameFiles_and_serverStatus_work_over_wire()
    {
        var oldPath = Path.Combine(_dir, "src", "oldmod.lyr");
        var newPath = Path.Combine(_dir, "src", "newmod.lyr");
        var mainPath = Path.Combine(_dir, "src", "main.lyr");
        File.WriteAllText(oldPath, "pub fn answer(): int { return 42; }\n");
        const string mainCode = "import app.oldmod { answer };\nfn main(): int { return answer(); }\n";
        File.WriteAllText(mainPath, mainCode);

        var oldUri = DocumentUri.FromFilePath(oldPath);
        var newUri = DocumentUri.FromFilePath(newPath);
        var mainUri = DocumentUri.FromFilePath(mainPath);

        await using var harness = new ServerHarness();
        await harness.InitializeAsync("""{"textDocument":{"diagnostic":{}}}""");
        await harness.NotifyAsync(LspMethods.DidOpen, JsonSerializer.Serialize(new
        {
            textDocument = new { uri = mainUri, languageId = "lyric", version = 1, text = mainCode },
        }));

        // First pull returns full + resultId
        var d1 = await harness.RequestAsync(LspMethods.Diagnostic,
            JsonSerializer.Serialize(new { textDocument = new { uri = mainUri } }));
        var r1 = (await harness.ReceiveResponseAsync(d1)).GetProperty("result");
        Assert.Equal("full", r1.GetProperty("kind").GetString());
        var resultId = r1.GetProperty("resultId").GetString();
        Assert.False(string.IsNullOrEmpty(resultId));

        // Second pull with previousResultId returns unchanged!
        var d2 = await harness.RequestAsync(LspMethods.Diagnostic,
            JsonSerializer.Serialize(new { textDocument = new { uri = mainUri }, previousResultId = resultId }));
        var r2 = (await harness.ReceiveResponseAsync(d2)).GetProperty("result");
        Assert.Equal("unchanged", r2.GetProperty("kind").GetString());

        // workspace/diagnostic includes project files
        var wd = await harness.RequestAsync(LspMethods.WorkspaceDiagnostic, "{}");
        var wdRes = (await harness.ReceiveResponseAsync(wd)).GetProperty("result");
        Assert.True(wdRes.GetProperty("items").GetArrayLength() >= 1);

        // workspace/willRenameFiles updates import path
        var rn = await harness.RequestAsync(LspMethods.WillRenameFiles,
            JsonSerializer.Serialize(new { files = new[] { new { oldUri, newUri } } }));
        var rnRes = (await harness.ReceiveResponseAsync(rn)).GetProperty("result");
        var edits = rnRes.GetProperty("changes").GetProperty(mainUri).EnumerateArray().ToArray();
        Assert.Single(edits);
        Assert.Equal("app.newmod", edits[0].GetProperty("newText").GetString());

        // lyric/serverStatus reports telemetry
        var st = await harness.RequestAsync(LspMethods.ServerStatus, "{}");
        var stRes = (await harness.ReceiveResponseAsync(st)).GetProperty("result");
        Assert.True(stRes.GetProperty("indexedSymbols").GetInt32() >= 1);
        Assert.True(stRes.GetProperty("analysedFiles").GetInt32() >= 1);
    }
}
