using Lyric.Lsp.Protocol;
using Lyric5.Build;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// The edits that turn a buffer into its formatted text, a hunk of changed lines each — not one
/// edit over the whole document: the editor keeps the cursor, the selection and the marks of every
/// line the form leaves as it is.
/// </summary>
public static class FormattingEdits
{
    public static IReadOnlyList<TextEdit> Of(string before, string after)
    {
        // lines with their ends, so a hunk's text is its lines put together and nothing is lost at
        // the end of a file without a final newline
        var a = Lines(before);
        var b = Lines(after);
        var script = LineDiff.Script(a, b);

        var edits = new List<TextEdit>();
        var k = 0;
        while (k < script.Count)
        {
            if (script[k].Op == ' ') { k++; continue; }
            var from = script[k].A;
            var put = new System.Text.StringBuilder();
            var taken = 0;
            for (; k < script.Count && script[k].Op != ' '; k++)
            {
                if (script[k].Op == '-') taken++;
                else put.Append(script[k].Text);
            }
            edits.Add(new TextEdit { Range = new Protocol.Range { Start = At(a, from), End = At(a, from + taken) }, NewText = put.ToString() });
        }
        return edits;
    }

    private static List<string> Lines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
            if (text[i] == '\n')
            {
                lines.Add(text[start..(i + 1)]);
                start = i + 1;
            }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    /// <summary>The start of line <paramref name="line"/>; past the last, the end of the text.</summary>
    private static Position At(List<string> lines, int line)
    {
        if (line < lines.Count) return new Position { Line = line, Character = 0 };
        if (lines.Count == 0) return new Position { Line = 0, Character = 0 };
        var last = lines[^1];
        return last.EndsWith('\n')
            ? new Position { Line = lines.Count, Character = 0 }
            : new Position { Line = lines.Count - 1, Character = last.Length };
    }
}
