using Lyric.AST;
using Lyric.Core;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// The quick fixes at a place: each change a finding's notes suggest — the ones <c>lyric fix</c>
/// applies and the ones it leaves to a reader, the first preferred —, and for a lint the
/// declaration's <c>@Allow</c> that takes it back there.
///
/// <para>Nothing is composed here: a change is the compiler's, word for word, and its title is
/// the suggestion's own text. An editor's fix that the command line did not know would be a second
/// answer to what the program should say.</para>
/// </summary>
public static class CodeActionProvider
{
    /// <param name="uri">The client's own URI for the file, used for the edits that land in it.</param>
    /// <param name="snapshot">The analysis the findings came from, where it has a tree: what
    /// <c>@Allow</c> is written above.</param>
    public static IReadOnlyList<CodeAction> At(
        string path, string uri, Protocol.Range range, SourceManager sources, IReadOnlyList<Diagnostic> findings,
        AnalysisSnapshot? snapshot, string? toolVersion)
    {
        var actions = new List<CodeAction>();
        foreach (var finding in findings)
        {
            var at = SpanMapper.ToVisibleRange(sources, finding.Span);
            if (at.End.Line < range.Start.Line || at.Start.Line > range.End.Line) continue;
            var shown = DiagnosticMapper.Map(sources, finding, toolVersion);

            foreach (var note in finding.Notes ?? [])
            {
                if (note.Fix is not { } fix || !fix.Span.File.IsValid) continue;
                var fixPath = sources.GetPath(fix.Span.File);
                actions.Add(new CodeAction
                {
                    Title = note.Message,
                    Kind = "quickfix",
                    Diagnostics = [shown],
                    IsPreferred = fix.Applicability == Applicability.Safe,
                    Edit = Edit(DocumentUri.PathComparer.Equals(fixPath, path) ? uri : DocumentUri.FromFilePath(fixPath),
                        SpanMapper.ToRange(sources, fix.Span), fix.Text),
                });
            }

            if (DiagnosticCatalog.Entries.TryGetValue(finding.Code, out var entry) && entry.Lint is { } lint
                && snapshot is not null && ReferenceEquals(snapshot.Sources, sources)
                && Allowing(snapshot, finding.Span.Start) is { } declaration)
            {
                var text = sources.GetText(snapshot.File);
                var lineStart = text.LastIndexOf('\n', Math.Max(0, declaration.Span.Start - 1)) + 1;
                var indent = text[lineStart..declaration.Span.Start];
                if (indent.Any(c => c is not (' ' or '\t'))) continue; // the declaration does not begin its line
                var where = SpanMapper.ToRange(sources, new Span(snapshot.File, lineStart, lineStart));
                actions.Add(new CodeAction
                {
                    Title = $"allow '{lint}' in this declaration (@Allow(\"{finding.Code}\"))",
                    Kind = "quickfix",
                    Diagnostics = [shown],
                    IsPreferred = false,
                    Edit = Edit(uri, where, $"{indent}@Allow(\"{finding.Code}\")\n"),
                });
            }
        }
        return actions;
    }

    /// <summary>The innermost declaration around an offset that carries attributes: a function, a
    /// type, a field, a binding, a block.</summary>
    private static Decl? Allowing(AnalysisSnapshot snapshot, int offset) =>
        NodeFinder.PathAt(snapshot.Root, snapshot.File, offset)
            .OfType<Decl>()
            .LastOrDefault(d => d is FunctionDecl or StructDecl or ClassDecl or EnumDecl or InterfaceDecl
                or GlobalBindingDecl or StaticBindingDecl or FieldDecl or TypeAliasDecl or ExtendDecl);

    private static WorkspaceEdit Edit(string uri, Protocol.Range range, string text) => new()
    {
        Changes = new Dictionary<string, List<TextEdit>> { [uri] = [new TextEdit { Range = range, NewText = text }] },
    };
}
