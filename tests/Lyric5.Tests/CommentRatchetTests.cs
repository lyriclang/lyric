using System.Text;
using System.Text.RegularExpressions;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// Comments describe what the code does and how, never the history of its design. A comment line
/// that cites a design document, a decision label, a milestone or one of its slices, or a design
/// round is counted per file against <c>tooling/comment-ratchet.toml</c>: a file may lose such lines
/// and never gain one, and a file that lost some must have its count lowered with it, so the
/// numbers only ever go down. <c>LYRIC_UPDATE_SNAPSHOTS=1</c> lowers them; it never raises one.
/// </summary>
public partial class CommentRatchetTests
{
    private static readonly string Root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);
    private static readonly string BaselinePath = Path.Combine(Root, "tooling", "comment-ratchet.toml");

    /// <summary>One counted part of the tree. <see cref="Doc"/> splits a language's comments by
    /// whether they are documentation (<c>///</c>); <c>null</c> takes them all.</summary>
    private sealed record Area(string Name, string[] Dirs, string[] Extensions, bool? Doc);

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
            new("stdlib5-doc", ["stdlib5"], [".lyr"], true),
            new("stdlib5", ["stdlib5"], [".lyr"], false),
            new("runtime", ["runtime/src", "runtime/include"], [".c", ".h"], null),
            new("compiler", compiler, [".cs"], null),
            new("frontend", ["src/Lyric.Frontend"], [".cs"], null),
            new("tests", ["tests/Lyric5.Tests"], [".cs"], null),
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
                    foreach (var (line, text, doc) in Comments(File.ReadAllLines(file)))
                    {
                        if (area.Doc is { } wanted && doc != wanted) continue;
                        if (Cites(text)) hits.Add(new Hit(area.Name, relative, line, text.Trim()));
                    }
                }
            }
        }
        return hits;
    }

    private static readonly Regex Section = new(@"^\[""(?<area>[^""]+)""\]\s*$");
    private static readonly Regex Entry = new(@"^""(?<file>[^""]+)""\s*=\s*(?<count>\d+)\s*$");

    private static Dictionary<(string Area, string File), int> ReadBaseline()
    {
        var counts = new Dictionary<(string, string), int>();
        if (!File.Exists(BaselinePath)) return counts;
        var area = "";
        foreach (var raw in File.ReadLines(BaselinePath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (Section.Match(line) is { Success: true } s) area = s.Groups["area"].Value;
            else if (Entry.Match(line) is { Success: true } e)
                counts[(area, e.Groups["file"].Value)] = int.Parse(e.Groups["count"].Value);
            else throw new InvalidDataException($"{BaselinePath}: cannot read '{raw}'");
        }
        return counts;
    }

    private static void WriteBaseline(Dictionary<(string Area, string File), int> counts)
    {
        var text = new StringBuilder();
        text.AppendLine("# Comment lines that cite the design, per file: what CommentRatchetTests allows.");
        text.AppendLine("# A count may only fall. LYRIC_UPDATE_SNAPSHOTS=1 lowers the counts to what the tree holds.");
        foreach (var area in Areas())
        {
            var entries = counts.Where(kv => kv.Key.Area == area.Name && kv.Value > 0)
                .OrderBy(kv => kv.Key.File, StringComparer.Ordinal)
                .ToList();
            text.AppendLine();
            text.AppendLine($"# {area.Name}: {entries.Sum(kv => kv.Value)}");
            text.AppendLine($"[\"{area.Name}\"]");
            foreach (var (key, count) in entries) text.AppendLine($"\"{key.File}\" = {count}");
        }
        File.WriteAllText(BaselinePath, text.ToString());
    }

    [Fact]
    public void No_file_gains_comments_that_cite_the_design()
    {
        var hits = Scan();
        var current = hits.GroupBy(h => (h.Area, h.File)).ToDictionary(g => g.Key, g => g.Count());
        var update = Environment.GetEnvironmentVariable("LYRIC_UPDATE_SNAPSHOTS") == "1";
        var exists = File.Exists(BaselinePath);
        var baseline = ReadBaseline();

        var rose = current.Where(kv => kv.Value > baseline.GetValueOrDefault(kv.Key)).Select(kv => kv.Key).ToList();
        var fell = baseline.Where(kv => kv.Value > current.GetValueOrDefault(kv.Key)).Select(kv => kv.Key).ToList();

        if (update)
        {
            var lowered = exists
                ? baseline.Keys.ToDictionary(k => k, k => Math.Min(baseline[k], current.GetValueOrDefault(k)))
                : current;
            WriteBaseline(lowered);
            if (!exists) return;
        }
        else Assert.True(exists, $"no baseline at {BaselinePath}; set LYRIC_UPDATE_SNAPSHOTS=1 to write it");

        if (rose.Count > 0)
        {
            var message = new StringBuilder("comments that cite the design grew; say what the code does instead:\n");
            foreach (var key in rose.OrderBy(k => k.File, StringComparer.Ordinal))
            {
                message.AppendLine($"  {key.File} ({key.Area}): {baseline.GetValueOrDefault(key)} → {current[key]}");
                foreach (var hit in hits.Where(h => (h.Area, h.File) == key))
                    message.AppendLine($"    {hit.File}:{hit.Line}: {hit.Text}");
            }
            Assert.Fail(message.ToString());
        }
        if (!update && fell.Count > 0)
        {
            var files = string.Join(", ", fell.Select(k => $"{k.File} ({baseline[k]} → {current.GetValueOrDefault(k)})"));
            Assert.Fail($"the count fell in {files}; set LYRIC_UPDATE_SNAPSHOTS=1 to lower the baseline with it");
        }
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
