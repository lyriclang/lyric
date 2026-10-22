using System.Text.RegularExpressions;

namespace Lyric5.Tests;

/// <summary>
/// The text form folded to one line per finding, for the tests that ask "is this finding there, at
/// this place". In the rustc-like form (16 §1) the head and the place stand on two
/// lines — <c>error[CODE]: message</c>, then <c> --> path:line:column</c> —; the fold puts them back
/// on one, <c>path:line:column: error[CODE]: message</c>, and a note's block on one, <c>  note:
/// message — path:line:column</c>, a <c>= note:</c> line as <c>  note: message</c> (a <c>= help:</c> line stays).
/// Every other line stays as it was. What the drawing looks like is
/// <c>Lyric.Tests.Core.TextRendererTests</c>' to say, and the driver's own in <c>DriverTests</c>.
/// </summary>
internal static partial class DiagnosticHeads
{
    [GeneratedRegex(@"^(error|warning|hint)\[[^\]]+\]: ")]
    private static partial Regex Head();

    [GeneratedRegex(@"^(note|help): (.*)$")]
    private static partial Regex NoteHead();

    [GeneratedRegex(@"^\s*--> (.+)$")]
    private static partial Regex Arrow();

    [GeneratedRegex(@"^\s*= (note): (.*)$")]
    private static partial Regex Said();

    public static string Fold(string text)
    {
        var lines = text.Split('\n');
        var folded = new List<string>(lines.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var arrow = i + 1 < lines.Length ? Arrow().Match(lines[i + 1]) : Match.Empty;
            if (Head().IsMatch(line) && arrow.Success)
            {
                folded.Add($"{arrow.Groups[1].Value}: {line}");
                i++;
            }
            else if (NoteHead().Match(line) is { Success: true } note && arrow.Success)
            {
                folded.Add($"  note: {note.Groups[2].Value} — {arrow.Groups[1].Value}");
                i++;
            }
            else if (Said().Match(line) is { Success: true } said) folded.Add($"  note: {said.Groups[2].Value}");
            else folded.Add(line);
        }
        return string.Join('\n', folded);
    }
}
