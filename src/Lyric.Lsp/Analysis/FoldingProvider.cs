using Lyric.AST;
using Lyric.Core;
using Lyric.Lsp.Protocol;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// What an editor may collapse: every multi-line declaration, block and match.
///
/// <para>Syntax alone, off the last good tree — folding is read while the file is broken, and
/// ranges that empty out on a type error would snap every region open exactly when someone is
/// mid-edit. The same argument as the outline's, and the same walk kind.</para>
///
/// <para>The last line stays visible: a fold that swallows the closing brace leaves the reader
/// with no cue that anything is folded at all.</para>
/// </summary>
public static class FoldingProvider
{
    public static IReadOnlyList<FoldingRange> Of(Module root, FileId file, SourceManager sources)
    {
        var ranges = new List<FoldingRange>();
        var startLines = new HashSet<int>();

        Walk(root);
        AddImportGroupFold();
        AddDocCommentFolds();
        return ranges;

        void AddImportGroupFold()
        {
            var importStart = -1;
            var importEnd = -1;
            var count = 0;
            foreach (var decl in root.Declarations)
            {
                if (decl is ImportDecl && decl.Span.File == file)
                {
                    var r = SpanMapper.ToRange(sources, decl.Span);
                    if (importStart < 0) importStart = r.Start.Line;
                    importEnd = Math.Max(importEnd, r.End.Line);
                    count++;
                }
                else
                {
                    if (count >= 3 && importEnd > importStart && startLines.Add(importStart))
                        ranges.Add(new FoldingRange { StartLine = importStart, EndLine = importEnd });
                    importStart = -1;
                    importEnd = -1;
                    count = 0;
                }
            }
            if (count >= 3 && importEnd > importStart && startLines.Add(importStart))
                ranges.Add(new FoldingRange { StartLine = importStart, EndLine = importEnd });
        }

        void AddDocCommentFolds()
        {
            var text = sources.GetText(file);
            var lines = text.Split('\n');
            var docStart = -1;
            for (var i = 0; i <= lines.Length; i++)
            {
                var isDoc = i < lines.Length && lines[i].TrimStart().StartsWith("///", StringComparison.Ordinal);
                if (isDoc)
                {
                    if (docStart < 0) docStart = i;
                }
                else if (docStart >= 0)
                {
                    var docEnd = i - 1;
                    if (docEnd - docStart >= 2 && startLines.Add(docStart))
                        ranges.Add(new FoldingRange { StartLine = docStart, EndLine = docEnd });
                    docStart = -1;
                }
            }
        }

        void Walk(Node node)
        {
            if (Foldable(node) && node.Span.File == file)
            {
                var range = SpanMapper.ToRange(sources, node.Span);

                // Minus one keeps the closing line visible. A one-line node has nothing to hide.
                var end = range.End.Line - 1;

                // One range per start line: 'fn f() {' is the declaration AND its body block, and
                // two folds on one line render as a doubled control. The outer node walks first
                // and wins.
                if (end > range.Start.Line && startLines.Add(range.Start.Line))
                    ranges.Add(new FoldingRange { StartLine = range.Start.Line, EndLine = end });
            }

            foreach (var child in AstChildren.Of(node)) Walk(child);
        }
    }

    /// <summary>The forms worth collapsing. An expression is not one: folding a subexpression
    /// leaves text that reads as a different program.</summary>
    private static bool Foldable(Node node) => node is
        StructDecl or ClassDecl or EnumDecl or InterfaceDecl or ExtendDecl or FunctionDecl
        or Block or MatchStmt or MatchExpr;
}
