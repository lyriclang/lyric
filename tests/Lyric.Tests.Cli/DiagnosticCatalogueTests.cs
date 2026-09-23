using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Lyric.Tests.Cli;

/// <summary>
/// The diagnostic catalogue is a contract, and this checks that the implementation keeps it.
///
/// <para>§12.2: "A code that appears in neither the appendix nor a retirement row does not exist;
/// adding one is a specification change." §12.1 rule 1: "Severity belongs to the code." Both were
/// broken on main and nothing noticed — the spec-mirror job diffs chapters 02 and 13, so the
/// appendix is outside what it can see.</para>
///
/// <para>WHAT IT FOUND WHEN IT WAS WRITTEN. Seven codes existed in the compiler and in no
/// specification, and three of those carried two or three UNRELATED rules under one number: the
/// four feature branches merged in September each allocated from the same free range, none of them
/// touched the appendix, and the merge took all of it. <c>LYR-SEM0100</c> was a warning about an
/// unused loop label in one file and an error about <c>comptime</c> in another.</para>
///
/// <para>Checked over the SOURCE rather than over emitted diagnostics, for the reason the sibling
/// file <c>DiagnosticTextTests</c> gives: there is no way to make a compiler emit every diagnostic
/// it can. A literal is the thing being constrained, so a literal is what is inspected.</para>
/// </summary>
public sealed class DiagnosticCatalogueTests
{
    private static readonly Regex Code = new(@"LYR-(LEX|PAR|RES|SEM|IR|CLI|BC|CAP|VM|EMB)\d{4}",
        RegexOptions.Compiled);

    /// <summary>A report whose code is a literal, with the severity that follows it.</summary>
    private static readonly Regex Reported = new(
        @"""(?<code>LYR-(LEX|PAR|RES|SEM|IR|CLI|BC|CAP|VM|EMB)\d{4})""\s*,\s*Severity\.(?<sev>\w+)",
        RegexOptions.Compiled);

    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    /// <summary>
    /// The specification, when a checkout of it stands beside this one.
    ///
    /// <para>Skipped rather than failed when it does not: a contributor without the spec clone
    /// still gets a green suite, and the CI job that matters checks it out on purpose. The
    /// severity test below needs no spec and always runs.</para>
    /// </summary>
    private static string? AppendixPath()
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(RepoRoot(), "..", "lyric-spec"),
                     Path.Combine(RepoRoot(), "spec-repo"),
                 })
        {
            var appendix = Path.Combine(candidate, "spec", "appendix-a-diagnostics.md");
            if (File.Exists(appendix)) return appendix;
        }
        return null;
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>Every code the implementation names in a string literal, with where it names it.</summary>
    private static Dictionary<string, List<string>> EmittedCodes()
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in SourceFiles())
        {
            foreach (var (line, number) in File.ReadLines(path).Select((l, i) => (l, i)))
            {
                // Only a string LITERAL declares a code. A mention in a comment or in XML
                // documentation is a cross-reference — and those quote codes with the quotes
                // around them too, which is why the line has to be excluded rather than the
                // character before the match: LoweringDiagnostics explains the free IR range as
                // ("LYR-IR0002: lambdas") and means no such code.
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;

                foreach (Match m in Code.Matches(line))
                {
                    if (m.Index == 0 || line[m.Index - 1] != '"') continue;
                    if (!found.TryGetValue(m.Value, out var where)) found[m.Value] = where = [];
                    where.Add($"{Path.GetFileName(path)}:{number + 1}");
                }
            }
        }
        return found;
    }

    [Fact]
    public void Every_code_the_implementation_emits_stands_in_the_appendix()
    {
        if (AppendixPath() is not { } appendix) return;   // no spec checkout; see AppendixPath

        var documented = Code.Matches(File.ReadAllText(appendix))
            .Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

        var undocumented = EmittedCodes()
            .Where(pair => !documented.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"  {pair.Key} — {string.Join(", ", pair.Value.Distinct())}")
            .ToList();

        Assert.True(undocumented.Count == 0,
            "codes emitted by this implementation and absent from the specification's appendix:\n"
            + string.Join("\n", undocumented)
            + "\n\nSpecification §12.2: a code in neither the appendix nor a retirement row does "
            + "not exist. Adding one is a change to lyric-spec, and it goes first.");
    }

    /// <summary>
    /// One code, one severity — §12.1 rule 1.
    ///
    /// <para>Needs no specification checkout, because it is a statement about the implementation
    /// alone: whatever the appendix says, a number that is a warning in one file and an error in
    /// another is two rules wearing one name.</para>
    /// </summary>
    [Fact]
    public void No_code_is_reported_at_two_severities()
    {
        var severities = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var places = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var path in SourceFiles())
        foreach (Match m in Reported.Matches(File.ReadAllText(path)))
        {
            var code = m.Groups["code"].Value;
            if (!severities.TryGetValue(code, out var set)) severities[code] = set = [];
            set.Add(m.Groups["sev"].Value);
            if (!places.TryGetValue(code, out var where)) places[code] = where = [];
            where.Add(Path.GetFileName(path));
        }

        var split = severities
            .Where(pair => pair.Value.Count > 1)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"  {pair.Key} — {string.Join(" and ", pair.Value)}, "
                            + $"in {string.Join(", ", places[pair.Key])}")
            .ToList();

        Assert.True(split.Count == 0,
            "codes reported at more than one severity:\n" + string.Join("\n", split)
            + "\n\nSpecification §12.1 rule 1: severity belongs to the code. Two severities mean "
            + "two rules sharing a number, and a consumer matching on it cannot tell them apart.");
    }
}
