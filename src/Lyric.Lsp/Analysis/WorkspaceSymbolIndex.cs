using System.Collections.Concurrent;
using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;
using Lyric.Resolver;

namespace Lyric.Lsp.Analysis;

/// <summary>One indexed symbol from a workspace module.</summary>
public sealed record IndexedSymbol(
    string Name,
    SymbolKind Kind,
    string Path,
    string Uri,
    Protocol.Range Range,
    string? ContainerName);

/// <summary>One public top-level export eligible for auto-import suggestions/fixes.</summary>
public sealed record ExportedSymbol(
    string Name,
    string ModuleName,
    string FilePath,
    CompletionItemKind CompletionKind,
    string? Detail,
    string? Documentation);

/// <summary>
/// Persistent per-file workspace symbol and public-export index updated incrementally whenever a
/// compilation completes, avoiding full-AST flattening on every <c>workspace/symbol</c> query and
/// enabling auto-import completions and quick fixes across modules.
/// </summary>
public sealed class WorkspaceSymbolIndex
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<IndexedSymbol>> _symbolsByFile =
        new(DocumentUri.PathComparer);

    private readonly ConcurrentDictionary<string, IReadOnlyList<ExportedSymbol>> _exportsByFile =
        new(DocumentUri.PathComparer);

    public int SymbolCount
    {
        get
        {
            var total = 0;
            foreach (var list in _symbolsByFile.Values) total += list.Count;
            return total;
        }
    }

    public void UpdateCompilation(SemanticModel model, SourceManager sources)
    {
        foreach (var module in model.Compilation.Modules)
        {
            if (model.Compilation.IsNative(module)) continue;
            var ast = model.Compilation.AstOf(module);
            if (!ast.Span.File.IsValid || model.Compilation.IsSynthesized(ast.Span.File)) continue;

            var path = Path.GetFullPath(sources.GetPath(ast.Span.File));
            var uri = DocumentUri.FromFilePath(path);

            var outlined = DocumentSymbolProvider.Of(sources, ast);
            var indexed = new List<IndexedSymbol>();
            Flatten(outlined, container: null, path, uri, indexed);
            _symbolsByFile[path] = indexed;

            var exports = new List<ExportedSymbol>();
            foreach (var sym in module.Members.Symbols)
            {
                if (sym is ErrorSymbol or ImportBindingSymbol) continue;
                var isPub = sym switch
                {
                    TypeSymbol ts => ts.Visibility == Visibility.Public,
                    FunctionSymbol fn => fn.Visibility == Visibility.Public,
                    GlobalSymbol { Declaration: GlobalBindingDecl g } => g.Visibility == VisibilityWord.Pub,
                    _ => false,
                };
                if (!isPub) continue;
                var detail = CompletionProvider.SignatureDetail(model, sym);
                var doc = sym.Declaration is { } d ? model.Documentation.Of(d) : null;
                exports.Add(new ExportedSymbol(
                    sym.Name,
                    module.FullName,
                    path,
                    CompletionProvider.KindOf(sym),
                    detail,
                    doc));
            }
            _exportsByFile[path] = exports;
        }
    }

    public void RemoveFile(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            _symbolsByFile.TryRemove(full, out _);
            _exportsByFile.TryRemove(full, out _);
        }
        catch (ArgumentException) { }
    }

    public IReadOnlyList<SymbolInformation> Query(string query, int limit = 512)
    {
        var matches = new List<(int Score, IndexedSymbol Item)>();
        var seen = new HashSet<(string Path, int Line, int Character, string Name)>();

        foreach (var list in _symbolsByFile.Values)
        {
            foreach (var sym in list)
            {
                var score = ScoreMatch(sym.Name, query);
                if (score < 0) continue;
                if (!seen.Add((sym.Path, sym.Range.Start.Line, sym.Range.Start.Character, sym.Name))) continue;
                matches.Add((score, sym));
            }
        }

        matches.Sort((a, b) =>
        {
            var cmp = b.Score.CompareTo(a.Score);
            if (cmp != 0) return cmp;
            cmp = a.Item.Name.Length.CompareTo(b.Item.Name.Length);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Item.Name, b.Item.Name);
        });

        var count = Math.Min(matches.Count, limit);
        var result = new List<SymbolInformation>(count);
        for (var i = 0; i < count; i++)
        {
            var sym = matches[i].Item;
            result.Add(new SymbolInformation
            {
                Name = sym.Name,
                Kind = sym.Kind,
                Location = new Location { Uri = sym.Uri, Range = sym.Range },
                ContainerName = sym.ContainerName,
            });
        }
        return result;
    }

    public IReadOnlyList<ExportedSymbol> FindExportsByName(string name)
    {
        var found = new List<ExportedSymbol>();
        foreach (var list in _exportsByFile.Values)
        {
            foreach (var exp in list)
            {
                if (string.Equals(exp.Name, name, StringComparison.Ordinal))
                    found.Add(exp);
            }
        }
        return found;
    }

    public IEnumerable<ExportedSymbol> AllPublicExports()
    {
        foreach (var list in _exportsByFile.Values)
            foreach (var exp in list)
                yield return exp;
    }

    private static int ScoreMatch(string name, string query)
    {
        if (string.IsNullOrEmpty(query)) return 0;
        if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase)) return 1;
        return -1;
    }

    private static void Flatten(
        IReadOnlyList<DocumentSymbol> symbols, string? container, string path, string uri,
        List<IndexedSymbol> target)
    {
        foreach (var sym in symbols)
        {
            target.Add(new IndexedSymbol(sym.Name, sym.Kind, path, uri, sym.SelectionRange, container));
            if (sym.Children is { } children)
                Flatten(children, sym.Name, path, uri, target);
        }
    }
}
