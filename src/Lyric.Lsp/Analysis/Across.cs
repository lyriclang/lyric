using Lyric.AST;
using Lyric.Core;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// The other packages the editor has analysed, seen from a declaration: a package's function is
/// used in each package that depends on it, and those are other compilations — their own symbols,
/// their own files. A declaration is the same in two of them where it stands in the same file, at
/// the same place, in the same text; there each answers for its own uses.
/// </summary>
internal static class Across
{
    /// <summary>The place of a declaration's name in each other compilation that reads its file as
    /// <paramref name="asked"/> does.</summary>
    public static IEnumerable<(AnalysisSnapshot Snapshot, FileId File, Module Root, int Offset)> Elsewhere(
        AnalysisSnapshot asked, Span name, IReadOnlyList<AnalysisSnapshot> compilations)
    {
        if (!name.File.IsValid) yield break;
        var path = asked.Sources.GetPath(name.File);
        var text = asked.Sources.GetText(name.File);
        foreach (var other in compilations)
        {
            if (ReferenceEquals(other.Model, asked.Model)) continue;
            var file = DiagnosticMapper.FindFile(other.Sources, path);
            if (!file.IsValid || other.Sources.GetText(file) != text) continue;
            var root = other.Model.Compilation.Modules.Select(m => other.Model.Compilation.AstOf(m))
                .FirstOrDefault(ast => ast.Span.File == file);
            if (root is not null) yield return (other, file, root, name.Start);
        }
    }

    /// <summary>The name span of the declaration the cursor means, in <paramref name="asked"/>'s
    /// compilation.</summary>
    public static Span? DeclarationName(AnalysisSnapshot asked, int offset) =>
        ReferenceProvider.SymbolNodeAt(asked.Model, asked.Root, asked.File, offset) is { Symbol.Declaration: { } declaration }
            ? declaration is INamedDecl named ? named.NameSpan : declaration.Span
            : null;
}
