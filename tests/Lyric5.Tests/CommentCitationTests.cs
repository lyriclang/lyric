using System.Text;
using System.Text.RegularExpressions;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// Comments describe what the code does and how, never the history of its design. No comment line
/// of the compiler, the runtime, the standard library or their tests may cite a design document, a
/// decision label, a milestone or one of its slices, or a design round.
/// </summary>
public partial class CommentCitationTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);

    /// <summary>One read part of the tree: its directories and the extensions read in them.</summary>
    private sealed record Area(string Name, string[] Dirs, string[] Extensions);

    private static Area[] Areas()
    {
        var compiler = Directory.EnumerateDirectories(Path.Combine(Root, "src"))
            .Select(Path.GetFileName)
            .Where(d => d!.StartsWith("Lyric5", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(d => $"src/{d}")
            .ToArray();
        return
        [
            new("stdlib5", ["stdlib5"], [".lyr"]),
            new("runtime", ["runtime/src", "runtime/include"], [".c", ".h"]),
            new("compiler", compiler, [".cs"]),
            new("frontend", ["src/Lyric.Frontend", "src/Lyric.Core"], [".cs"]),
            new("tests", ["tests/Lyric5.Tests"], [".cs"]),
            new("std-tests", ["tests/std"], [".lyr"]),
        ];
    }

    /// <summary>A design-document path; a milestone with its optional slice letter or item number,
    /// but not a time-zone rule such as <c>M3.2.0</c>; a slice; the word for a design round; a
    /// decision label in parentheses or after an area number; the numbered points of a round.</summary>
    [GeneratedRegex(
        @"design/v5" +
        @"|\bM\d{1,2}[a-c]?(?:-\d+)?\b(?!\.\d)" +
        @"|\bS\d{1,2}[a-d]?\b" +
        @"|\b[Rr]eview\b" +
        @"|\(\s*(?:\d{2}\s+)?[A-Z]{1,2}\d{1,2}[a-z]?\b" +
        @"|\b(?:0\d|1[0-4]) [A-Z]{1,2}\d{1,2}[a-z]?\b" +
        @"|\bopen point\b")]
    private static partial Regex Citation();

    internal static bool Cites(string comment) => Citation().IsMatch(comment);

    /// <summary>The comment text of each line that has one, with its line number and whether it is
    /// documentation. Strings, character literals, verbatim and raw strings are skipped, so a
    /// <c>//</c> inside one is not a comment; a block comment is followed across lines.</summary>
    internal static IEnumerable<(int Line, string Text, bool Doc)> Comments(IReadOnlyList<string> lines)
    {
        var block = false;
        var rawQuotes = 0;
        var verbatim = false;
        for (var n = 0; n < lines.Count; n++)
        {
            var line = lines[n];
            var text = new StringBuilder();
            var doc = false;
            var i = 0;
            while (i < line.Length)
            {
                if (block)
                {
                    var end = line.IndexOf("*/", i, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        text.Append(line, i, line.Length - i);
                        break;
                    }
                    text.Append(line, i, end + 2 - i);
                    i = end + 2;
                    block = false;
                    continue;
                }
                if (rawQuotes > 0)
                {
                    var close = line.IndexOf(new string('"', rawQuotes), i, StringComparison.Ordinal);
                    if (close < 0) break;
                    i = close + rawQuotes;
                    rawQuotes = 0;
                    continue;
                }
                if (verbatim)
                {
                    var quote = line.IndexOf('"', i);
                    if (quote < 0) break;
                    if (quote + 1 < line.Length && line[quote + 1] == '"')
                    {
                        i = quote + 2;
                        continue;
                    }
                    i = quote + 1;
                    verbatim = false;
                    continue;
                }

                var c = line[i];
                if (c == '"')
                {
                    var run = 0;
                    while (i + run < line.Length && line[i + run] == '"') run++;
                    if (run >= 3)
                    {
                        rawQuotes = run;
                        i += run;
                        continue;
                    }
                    if (IsVerbatimStart(line, i))
                    {
                        verbatim = true;
                        i++;
                        continue;
                    }
                    i = SkipString(line, i);
                    continue;
                }
                if (c == '\'')
                {
                    i = SkipCharLiteral(line, i);
                    continue;
                }
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    doc = i + 2 < line.Length && line[i + 2] == '/';
                    text.Append(line, i, line.Length - i);
                    break;
                }
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    block = true;
                    text.Append("/*");
                    i += 2;
                    continue;
                }
                i++;
            }
            if (text.Length > 0) yield return (n + 1, text.ToString(), doc);
        }
    }

    private static bool IsVerbatimStart(string line, int quote) =>
        quote > 0 && (line[quote - 1] == '@' || (line[quote - 1] == '$' && quote > 1 && line[quote - 2] == '@'));

    /// <summary>The index after the string that opens at <paramref name="quote"/>; a backslash
    /// escapes the next character. An unterminated string runs to the end of the line.</summary>
    private static int SkipString(string line, int quote)
    {
        var j = quote + 1;
        while (j < line.Length && line[j] != '"') j += line[j] == '\\' ? 2 : 1;
        return Math.Min(j + 1, line.Length);
    }

    /// <summary>The index after a character literal, or after the lone quote when none closes
    /// within the longest literal the languages write (a braced Unicode escape).</summary>
    private static int SkipCharLiteral(string line, int quote)
    {
        var j = quote + 1;
        while (j < line.Length && j < quote + 13 && line[j] != '\'') j += line[j] == '\\' ? 2 : 1;
        return j < line.Length && line[j] == '\'' ? j + 1 : quote + 1;
    }

    private sealed record Hit(string Area, string File, int Line, string Text);

    private static List<Hit> Scan()
    {
        var hits = new List<Hit>();
        foreach (var area in Areas())
        {
            foreach (var dir in area.Dirs)
            {
                var full = Path.Combine(Root, dir);
                if (!Directory.Exists(full)) continue;
                foreach (var file in Directory.EnumerateFiles(full, "*.*", SearchOption.AllDirectories))
                {
                    if (!area.Extensions.Contains(Path.GetExtension(file))) continue;
                    var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
                    if (relative.Split('/').Any(s => s is "obj" or "bin" or "third_party")) continue;
                    foreach (var (line, text, _) in Comments(File.ReadAllLines(file)))
                        if (Cites(text)) hits.Add(new Hit(area.Name, relative, line, text.Trim()));
                }
            }
        }
        return hits;
    }

    [Fact]
    public void No_comment_cites_the_design()
    {
        var hits = Scan();
        if (hits.Count == 0) return;

        var message = new StringBuilder("comments that cite the design; say what the code does instead:\n");
        foreach (var hit in hits.OrderBy(h => h.File, StringComparer.Ordinal).ThenBy(h => h.Line))
            message.AppendLine($"  {hit.File}:{hit.Line} ({hit.Area}): {hit.Text}");
        Assert.Fail(message.ToString());
    }

    [Fact]
    public void A_citation_counts_in_a_comment_and_nowhere_else()
    {
        string[] source =
        [
            "let a = \"(10 C3) in a string\"; // plain words",
            "/// Appends a value (10 C3).",
            "// built by M8b S5",
            "/* a review */ let b = 1;",
            "// M3.2.0,M11.1.0 is a zone rule; RT0002 a panic code; table D3-7",
            "let c = '\"'; // a quote in a character",
            "var s = \"\"\"",
            "    // (10 C3) inside a raw string",
            "    \"\"\";",
            "var p = @\"C:\\dir\\\"; // M6-28",
        ];

        var hits = Comments(source).Where(c => Cites(c.Text)).ToList();

        Assert.Equal([2, 3, 4, 10], hits.Select(h => h.Line));
        Assert.True(hits[0].Doc);
        Assert.False(hits[1].Doc);
    }
}
