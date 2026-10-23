using System.Text;
using Lyric.Core;
using Lyric.Lexing;
using Lyric.Sema;

namespace Lyric5.Doc;

/// <summary>
/// Lyric source as HTML with its tokens marked: <c>kw</c> for keywords, <c>ty</c> for type names
/// (a builtin type or a name that starts with a capital), <c>str</c> for strings and characters,
/// <c>num</c> for numbers and <c>com</c> for comments. The stylesheet gives the classes their
/// colours.
///
/// <para>The compiler's own lexer reads the code, so the page marks a word as the compiler reads
/// it. Text the lexer does not take without a complaint — a snippet that is not Lyric, or not
/// finished — comes out escaped and unmarked rather than half-marked.</para>
/// </summary>
public static class Highlighter
{
    /// <summary>The source as HTML, escaped, with the tokens in their spans.</summary>
    public static string Highlight(string source)
    {
        var sources = new SourceManager { Lyric5 = true };
        var file = sources.AddVirtual("snippet.lyr", source);
        var diagnostics = new DiagnosticEngine(sources);
        var lexer = new Lexer(sources, file, diagnostics, collectTrivia: true);

        var marks = new List<(int Start, int End, string Class)>();
        while (true)
        {
            var token = lexer.Next();
            if (token.TokenKind == TokenKind.Eof) break;
            if (ClassOf(token.TokenKind, source, token.Span) is { } cls)
                marks.Add((token.Span.Start, token.Span.End, cls));
        }
        if (diagnostics.Count > 0) return Html.Text(source);

        foreach (var trivia in lexer.CollectedTrivia)
            marks.Add((trivia.Span.Start, trivia.Span.End, "com"));
        marks.Sort((a, b) => a.Start.CompareTo(b.Start));

        var sb = new StringBuilder(source.Length * 2);
        var at = 0;
        foreach (var (start, end, cls) in marks)
        {
            if (start < at || end > source.Length) continue;
            Html.AppendText(sb, source.AsSpan(at, start - at));
            sb.Append("<span class=\"").Append(cls).Append("\">");
            Html.AppendText(sb, source.AsSpan(start, end - start));
            sb.Append("</span>");
            at = end;
        }
        Html.AppendText(sb, source.AsSpan(at));
        return sb.ToString();
    }

    private static string? ClassOf(TokenKind kind, string source, Span span) => kind switch
    {
        TokenKind.IntLiteral or TokenKind.FloatLiteral => "num",
        TokenKind.StringLiteral or TokenKind.ByteStringLiteral or TokenKind.CharLiteral
            or TokenKind.FStringStart or TokenKind.FStringChunk or TokenKind.FStringFormatSpec
            or TokenKind.FStringEnd => "str",
        TokenKind.DocComment => "com",
        TokenKind.Identifier => ClassOfName(source.Substring(span.Start, span.End - span.Start)),
        _ => Lexer.KeywordSpelling(kind) is not null ? "kw" : null,
    };

    /// <summary>The words the parser takes as keywords in their place but the lexer reads as
    /// names, marked where they can hardly be anything else. <c>type</c> and <c>with</c> are
    /// left out: they are common names of fields and functions.</summary>
    private static readonly HashSet<string> ContextualKeywords = ["throws", "sealed", "opaque", "extern"];

    private static string? ClassOfName(string name) =>
        ContextualKeywords.Contains(name) ? "kw" : IsTypeName(name) ? "ty" : null;

    private static bool IsTypeName(string name) =>
        name.Length > 0 && (char.IsUpper(name[0]) || TypeFacts.FromBuiltinName(name) is not null);
}
