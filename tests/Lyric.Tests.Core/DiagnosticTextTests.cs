using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Lyric.Tests.Core;

/// <summary>
/// What a diagnostic is allowed to say.
///
/// <para>A message points at the code the reader is looking at, never at a document. A citation ages
/// in two ways at once — the file gets renamed and the section gets renumbered — and both had
/// happened: five messages named <c>Sprache.md</c>, which has been <c>docs/Grammar.md</c> for a
/// while, and cited sections §10 and §11 of a document that has seven. Someone following either was
/// sent nowhere twice.</para>
///
/// <para>Checked over the SOURCE rather than over emitted messages, because there is no way to make a
/// compiler emit every diagnostic it can. A string literal is the thing being constrained, so a
/// string literal is what is inspected — the compiler's, and the standard library's and the
/// runtime's, whose panics and errors a program's user reads; comments are left to the comment
/// ratchet.</para>
/// </summary>
public sealed class DiagnosticTextTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    /// <summary>A double-quoted literal on one line. Verbatim and raw literals spanning lines are not
    /// matched, and no diagnostic message is written as one.</summary>
    private static readonly Regex StringLiteral = new("\"(?:[^\"\\\\\\n]|\\\\.)*\"", RegexOptions.Compiled);

    /// <summary>What must not stand inside one: a section sign, the name of a document, a decision's
    /// label — <c>(07 K5)</c>, <c>11 W3</c> — or a milestone: a reader of a message has none of them.</summary>
    private static readonly Regex Citation = new(
        @"§|\.md\b|design/v5|\((?:\d{2}\s+)?[A-Z]{1,2}\d{1,2}[a-z]?\b|\b(?:0\d|1[0-4]) [A-Z]{1,2}\d{1,2}[a-z]?\b|\bM\d{1,2}[a-c]?\b(?!\.\d)",
        RegexOptions.Compiled);

    public static TheoryData<string> ProductionSources()
    {
        var data = new TheoryData<string>();
        (string Dir, string[] Patterns)[] roots =
        [
            ("src", ["*.cs"]),
            ("stdlib5", ["*.lyr"]),
            ("runtime/src", ["*.c", "*.h"]),
            ("runtime/include", ["*.c", "*.h"]),
        ];
        foreach (var (dir, patterns) in roots)
        foreach (var pattern in patterns)
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(RepoRoot(), dir), pattern, SearchOption.AllDirectories))
        {
            var path = file.Replace('\\', '/');
            if (path.Contains("/obj/") || path.Contains("/bin/") || path.Contains("/third_party/")) continue;
            data.Add(file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ProductionSources))]
    public void No_string_literal_cites_a_document(string path)
    {
        var offenders = new List<string>();
        var lines = File.ReadAllLines(path);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimStart();

            // A '//' or '///' line is a comment in full, and so is a C block comment's line.
            if (line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith("/*", StringComparison.Ordinal)
                || line.StartsWith('*')) continue;

            foreach (Match literal in StringLiteral.Matches(lines[i]))
                if (Citation.IsMatch(literal.Value))
                    offenders.Add($"  line {i + 1}: {literal.Value}");
        }

        Assert.True(offenders.Count == 0,
            $"{Path.GetFileName(path)} has string literals citing a document:\n"
            + string.Join('\n', offenders)
            + "\n\nA diagnostic names what is wrong, not where to read about it.");
    }
}
