using System.Globalization;
using System.Text;
using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// <c>char</c> over the Unicode Character Database (design/v5/spec/10 B9 S4, C2; 13 M8c S1): every
/// scalar value's category, simple case mappings and White_Space as a Lyric program reports them,
/// against this test's own reading of the checked-in files — a reader apart from
/// <c>tooling/unicode/gen.py</c>, so a fault in the generator or in the lookups shows.
/// </summary>
public class UnicodeTests
{
    private static readonly string Ucd = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "runtime", "third_party", "ucd");

    // UAX #44's short names and the variants of std.core's UnicodeCategory, in the same order.
    private static readonly string[] Short = ["Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me", "Nd", "Nl", "No",
        "Pc", "Pd", "Ps", "Pe", "Pi", "Pf", "Po", "Sm", "Sc", "Sk", "So", "Zs", "Zl", "Zp", "Cc", "Cf", "Cs", "Co", "Cn"];
    private static readonly string[] Long = ["UppercaseLetter", "LowercaseLetter", "TitlecaseLetter", "ModifierLetter",
        "OtherLetter", "NonspacingMark", "SpacingMark", "EnclosingMark", "DecimalNumber", "LetterNumber", "OtherNumber",
        "ConnectorPunctuation", "DashPunctuation", "OpenPunctuation", "ClosePunctuation", "InitialPunctuation",
        "FinalPunctuation", "OtherPunctuation", "MathSymbol", "CurrencySymbol", "ModifierSymbol", "OtherSymbol",
        "SpaceSeparator", "LineSeparator", "ParagraphSeparator", "Control", "Format", "Surrogate", "PrivateUse", "Unassigned"];

    /// <summary>The lines <c>unicode_dump</c> must print, from UnicodeData.txt and PropList.txt.</summary>
    private static string Expected()
    {
        var category = Enumerable.Repeat("Cn", 0x110000).ToArray();
        var upper = new Dictionary<int, int>();
        var lower = new Dictionary<int, int>();
        int? first = null;
        foreach (var line in File.ReadLines(Path.Combine(Ucd, "UnicodeData.txt")))
        {
            var f = line.Split(';');
            var cp = int.Parse(f[0], NumberStyles.HexNumber);
            if (f[1].EndsWith(", First>")) { first = cp; continue; }
            if (f[1].EndsWith(", Last>"))
            {
                for (var c = first!.Value; c <= cp; c++) category[c] = f[2];
                continue;
            }
            category[cp] = f[2];
            if (f[12] != "") upper[cp] = int.Parse(f[12], NumberStyles.HexNumber);
            if (f[13] != "") lower[cp] = int.Parse(f[13], NumberStyles.HexNumber);
        }
        var white = new HashSet<int>();
        foreach (var line in File.ReadLines(Path.Combine(Ucd, "PropList.txt")))
        {
            var body = line.Split('#')[0].Trim();
            if (body == "") continue;
            var parts = body.Split(';');
            if (parts[1].Trim() != "White_Space") continue;
            var span = parts[0].Trim().Split("..");
            var lo = int.Parse(span[0], NumberStyles.HexNumber);
            var hi = span.Length > 1 ? int.Parse(span[1], NumberStyles.HexNumber) : lo;
            for (var c = lo; c <= hi; c++) white.Add(c);
        }

        var text = new StringBuilder();
        string? last = null;
        for (var cp = 0; cp < 0x110000; cp++)
        {
            if (cp is >= 0xD800 and <= 0xDFFF) continue;
            var name = Long[Array.IndexOf(Short, category[cp])];
            // An enum's Debug names its type: `UnicodeCategory.Control`.
            if (name != last) { text.Append($"c {cp:x} UnicodeCategory.{name}\n"); last = name; }
            if (upper.TryGetValue(cp, out var u) && u != cp) text.Append($"u {cp:x} {u:x}\n");
            if (lower.TryGetValue(cp, out var l) && l != cp) text.Append($"l {cp:x} {l:x}\n");
            if (white.Contains(cp)) text.Append($"w {cp:x}\n");
        }
        return text.ToString();
    }

    [Fact]
    public void Every_scalar_value_answers_as_the_database_says()
    {
        var program = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC("unicode_dump"), "unicode_dump", Profile.Debug);
        var result = ProcessRunner.Run(program, [], TimeSpan.FromMinutes(2));
        Assert.Equal("", result.Stderr);
        Assert.Equal(0, result.ExitCode);
        var expected = Expected().Split('\n');
        var actual = result.Stdout.Replace("\r\n", "\n").Split('\n');
        // The first line that differs, not two texts of 9000 lines.
        var at = Enumerable.Range(0, Math.Min(expected.Length, actual.Length)).FirstOrDefault(i => expected[i] != actual[i], -1);
        Assert.True(at < 0 && expected.Length == actual.Length,
            at < 0 ? $"{actual.Length} lines, {expected.Length} expected" : $"line {at + 1}: '{actual[at]}', expected '{expected[at]}'");
    }
}
