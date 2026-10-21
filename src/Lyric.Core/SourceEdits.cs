namespace Lyric.Core;

/// <summary>
/// The replacements the fixes are made of, read off the source so that what is taken out leaves no
/// gap behind: a keyword with the blank after it, a statement with its line where it stands alone.
/// </summary>
public static class SourceEdits
{
    /// <summary>A word taken out with the spaces after it: <c>try f()</c> becomes <c>f()</c>.</summary>
    public static Replacement DeleteWord(SourceManager sources, Span word, Applicability applicability = Applicability.Safe)
    {
        var text = sources.GetText(word.File);
        var end = word.End;
        while (end < text.Length && text[end] is ' ' or '\t') end++;
        return new Replacement(new Span(word.File, word.Start, end), "", applicability);
    }

    /// <summary>
    /// A statement taken out: its <c>;</c> with it, and its line where nothing else stands on it —
    /// from the line's first column to the next line's.
    /// </summary>
    public static Replacement DeleteStatement(SourceManager sources, Span statement, Applicability applicability = Applicability.Safe)
    {
        var text = sources.GetText(statement.File);
        var end = statement.End;
        var after = end;
        while (after < text.Length && text[after] is ' ' or '\t') after++;
        if (after < text.Length && text[after] == ';') end = after + 1;

        var lineStart = statement.Start;
        while (lineStart > 0 && text[lineStart - 1] is ' ' or '\t') lineStart--;
        var lineEnd = end;
        while (lineEnd < text.Length && text[lineEnd] is ' ' or '\t' or '\r') lineEnd++;
        var alone = (lineStart == 0 || text[lineStart - 1] == '\n') && (lineEnd == text.Length || text[lineEnd] == '\n');
        return alone
            ? new Replacement(new Span(statement.File, lineStart, Math.Min(lineEnd + 1, text.Length)), "", applicability)
            : new Replacement(new Span(statement.File, statement.Start, end), "", applicability);
    }

    /// <summary>The text with the replacements made, which must not overlap.</summary>
    public static string Apply(string text, IEnumerable<Replacement> fixes)
    {
        var result = text;
        foreach (var fix in fixes.OrderByDescending(f => f.Span.Start))
            result = result[..fix.Span.Start] + fix.Text + result[fix.Span.End..];
        return result;
    }
}
