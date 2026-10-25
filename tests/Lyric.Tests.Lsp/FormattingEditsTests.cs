using System.Runtime.CompilerServices;
using Lyric.Lsp.Analysis;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// The formatting as the hunks it changes: applied to the buffer, the edits give the formatted
/// text exactly — over every file of std, each with its lines disturbed —, and a line the form
/// leaves alone is in no edit.
/// </summary>
public sealed class FormattingEditsTests
{
    private static string Root([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    /// <summary>The edits applied to <paramref name="text"/>, the last first, so the earlier
    /// positions hold.</summary>
    private static string Apply(string text, IReadOnlyList<TextEdit> edits)
    {
        foreach (var edit in edits.Reverse())
        {
            var start = TextOffsets.ToOffset(text, edit.Range.Start);
            var end = TextOffsets.ToOffset(text, edit.Range.End);
            // a position past the last character of the last line: the end of the text
            if (edit.Range.End.Line > CountLines(text)) end = text.Length;
            text = text[..start] + edit.NewText + text[end..];
        }
        return text;
    }

    private static int CountLines(string text) => text.Count(c => c == '\n');

    public static TheoryData<string> StdFiles() =>
    [.. Directory.EnumerateFiles(Path.Combine(Root(), "stdlib5", "std"), "*.lyr").Order()
        .Select(f => Path.GetRelativePath(Root(), f))];

    [Theory]
    [MemberData(nameof(StdFiles))]
    public void The_edits_give_the_formatted_text_and_touch_only_what_changed(string file)
    {
        var formatted = File.ReadAllText(Path.Combine(Root(), file)).ReplaceLineEndings("\n");
        // every seventh line out of its indentation
        var lines = formatted.Split('\n');
        var touched = 0;
        for (var i = 0; i < lines.Length; i += 7)
            if (lines[i].StartsWith("    ", StringComparison.Ordinal)) { lines[i] = lines[i][2..]; touched++; }
        var disturbed = string.Join('\n', lines);

        var edits = FormattingEdits.Of(disturbed, formatted);
        Assert.Equal(formatted, Apply(disturbed, edits));
        Assert.Equal(touched, edits.Sum(e => e.Range.End.Line - e.Range.Start.Line));
    }

    [Theory]
    [InlineData("a\nb\n", "a\nb\n")]
    [InlineData("a\nb", "a\nc")]
    [InlineData("", "a\n")]
    [InlineData("a\n", "")]
    [InlineData("x\ny\nz", "x\nz\n")]
    public void The_ends_of_a_text_are_edited_as_well(string before, string after)
    {
        var edits = FormattingEdits.Of(before, after);
        Assert.Equal(after, Apply(before, edits));
        if (before == after) Assert.Empty(edits);
    }
}
