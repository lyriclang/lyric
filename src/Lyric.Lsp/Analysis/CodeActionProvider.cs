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
        AnalysisSnapshot? snapshot, string? toolVersion) =>
        At(path, uri, range, sources, findings, snapshot, toolVersion, index: null);

    public static IReadOnlyList<CodeAction> At(
        string path, string uri, Protocol.Range range, SourceManager sources, IReadOnlyList<Diagnostic> findings,
        AnalysisSnapshot? snapshot, string? toolVersion, WorkspaceSymbolIndex? index)
    {
        var actions = new List<CodeAction>();
        var safeEditsInFile = new List<TextEdit>();

        foreach (var finding in findings)
        {
            var at = SpanMapper.ToVisibleRange(sources, finding.Span);
            if (at.End.Line < range.Start.Line || at.Start.Line > range.End.Line) continue;
            var shown = DiagnosticMapper.Map(sources, finding, toolVersion);

            foreach (var note in finding.Notes ?? [])
            {
                if (note.Fix is not { } fix || !fix.Span.File.IsValid) continue;
                var fixPath = sources.GetPath(fix.Span.File);
                var isSameFile = DocumentUri.PathComparer.Equals(fixPath, path);
                var editRange = SpanMapper.ToRange(sources, fix.Span);
                if (isSameFile && fix.Applicability == Applicability.Safe)
                    safeEditsInFile.Add(new TextEdit { Range = editRange, NewText = fix.Text });

                actions.Add(new CodeAction
                {
                    Title = note.Message,
                    Kind = "quickfix",
                    Diagnostics = [shown],
                    IsPreferred = fix.Applicability == Applicability.Safe,
                    Edit = Edit(isSameFile ? uri : DocumentUri.FromFilePath(fixPath),
                        editRange, fix.Text),
                });
            }

            if (index is not null
                && finding.Code is "LYR-SEM0002" or "LYR-RES0002"
                && finding.Span.File.IsValid)
            {
                var unresolved = sources.Slice(finding.Span).ToString().Trim();
                if (unresolved.Length > 0)
                {
                    foreach (var exp in index.FindExportsByName(unresolved))
                    {
                        var insertPos = ImportInsertPosition(sources, finding.Span.File, snapshot?.Root);
                        var importLine = $"import {exp.ModuleName} {{ {exp.Name} }};\n";
                        actions.Add(new CodeAction
                        {
                            Title = $"Import '{exp.Name}' from {exp.ModuleName}",
                            Kind = "quickfix",
                            Diagnostics = [shown],
                            IsPreferred = false,
                            Edit = Edit(uri, new Protocol.Range { Start = insertPos, End = insertPos }, importLine),
                        });
                    }
                }
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

        if (snapshot is not null && OrganizeImportsAction(uri, snapshot) is { } organize)
            actions.Add(organize);

        if (safeEditsInFile.Count >= 2 && NonOverlapping(safeEditsInFile) is { } batch)
        {
            actions.Add(new CodeAction
            {
                Title = "Apply all safe quick fixes in file",
                Kind = "source.fixAll",
                IsPreferred = false,
                Edit = new WorkspaceEdit { Changes = new Dictionary<string, List<TextEdit>> { [uri] = batch } },
            });
        }

        return actions;
    }

    private static Position ImportInsertPosition(SourceManager sources, FileId file, Module? root)
    {
        if (root is not null)
        {
            var lastImportEnd = -1;
            foreach (var decl in root.Declarations)
            {
                if (decl is ImportDecl && decl.Span.File == file)
                    lastImportEnd = Math.Max(lastImportEnd, SpanMapper.ToRange(sources, decl.Span).End.Line + 1);
            }
            if (lastImportEnd >= 0) return new Position { Line = lastImportEnd, Character = 0 };
        }
        return new Position { Line = 0, Character = 0 };
    }

    private static CodeAction? OrganizeImportsAction(string uri, AnalysisSnapshot snapshot)
    {
        var imports = snapshot.Root.Declarations.OfType<ImportDecl>()
            .Where(i => i.Span.File == snapshot.File)
            .ToList();
        if (imports.Count < 2) return null;

        var firstSpan = imports[0].Span;
        var lastSpan = imports[^1].Span;
        for (var i = 1; i < imports.Count; i++)
        {
            // Only rewrite a contiguous top-level import block cleanly.
            var prevEnd = SpanMapper.ToRange(snapshot.Sources, imports[i - 1].Span).End.Line;
            var curStart = SpanMapper.ToRange(snapshot.Sources, imports[i].Span).Start.Line;
            if (curStart - prevEnd > 2) return null;
        }

        var lines = new List<string>(imports.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var imp in imports)
        {
            var raw = snapshot.Sources.Slice(imp.Span).ToString().Trim();
            if (seen.Add(raw)) lines.Add(raw);
        }

        var sorted = lines.OrderBy(l => l.Contains("std.", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(l => l, StringComparer.Ordinal)
            .ToList();
        if (sorted.Count == lines.Count && sorted.SequenceEqual(lines, StringComparer.Ordinal))
            return null;

        var blockRange = new Protocol.Range
        {
            Start = SpanMapper.ToRange(snapshot.Sources, firstSpan).Start,
            End = SpanMapper.ToRange(snapshot.Sources, lastSpan).End,
        };
        return new CodeAction
        {
            Title = "Organize imports",
            Kind = "source.organizeImports",
            IsPreferred = false,
            Edit = Edit(uri, blockRange, string.Join('\n', sorted)),
        };
    }

    private static List<TextEdit>? NonOverlapping(List<TextEdit> edits)
    {
        var sorted = edits.OrderBy(e => e.Range.Start.Line).ThenBy(e => e.Range.Start.Character).ToList();
        var kept = new List<TextEdit>(sorted.Count);
        foreach (var e in sorted)
        {
            if (kept.Count > 0)
            {
                var prev = kept[^1].Range.End;
                if (e.Range.Start.Line < prev.Line
                    || (e.Range.Start.Line == prev.Line && e.Range.Start.Character < prev.Character))
                    continue;
            }
            kept.Add(e);
        }
        return kept.Count >= 2 ? kept : null;
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
