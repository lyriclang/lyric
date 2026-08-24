using System.Text;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// The front end's totality contract: NO input makes the lexer or the parser throw or hang —
/// every failure is a diagnostic. The inputs are pseudo-random with a FIXED seed, so a failure
/// reproduces exactly and a run costs the same every time; this is a smoke layer under the
/// unit tests, not a replacement for them.
/// </summary>
public class TotalityTests
{
    private static void ParseAll(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("<fuzz>", source);
        var de = new DiagnosticEngine(sm);
        var module = new Parser(sm, id, de).ParseModule();
        Assert.NotNull(module);
    }

    /// <summary>Fragments that stress the tokenizer's stateful corners when spliced randomly.</summary>
    private static readonly string[] Fragments =
    [
        "fn", "let", "var", "match", "f\"", "\"", "{", "}", "(", ")", "[", "]",
        "..", "..=", "::", "=>", "->", "?.", "??", "??=", "<<=", ">>»", "0x", "0b", "1_",
        "\\u{", "\\x", "'", ";", ",", "@", "@x", "//", "/*", "*/", "///", "e5", "1.5",
        "9223372036854775808", "\\u{FFFFFFFF}", "\\u{D800}", "🌍", "é", "\t", "\n", " ",
        "resume", "yield", "throws", "type", "opaque", "params", "this", "null",
    ];

    [Fact]
    public void Random_fragment_splices_never_throw()
    {
        var random = new Random(0x1_C0FFEE);
        for (var run = 0; run < 400; run++)
        {
            var sb = new StringBuilder();
            var parts = random.Next(1, 40);
            for (var i = 0; i < parts; i++) sb.Append(Fragments[random.Next(Fragments.Length)]);
            ParseAll(sb.ToString());
        }
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(0xBEEF);
        for (var run = 0; run < 200; run++)
        {
            var length = random.Next(0, 200);
            var sb = new StringBuilder(length);
            for (var i = 0; i < length; i++) sb.Append((char)random.Next(0, 0x300));
            ParseAll(sb.ToString());
        }
    }

    [Fact]
    public void Deep_nesting_does_not_overflow_the_stack()
    {
        // Recursive descent has a stack proportional to the nesting. 200 levels stand for a
        // pathological but plausible file; a true bomb (tens of thousands) is out of contract.
        ParseAll("fn main(): int { return " + new string('(', 200) + "1"
                 + new string(')', 200) + "; }");
        ParseAll("fn main(): int { let x: " + string.Concat(Enumerable.Repeat("?", 1))
                 + new string('(', 200) + "int" + new string(')', 200) + " = 1; return 0; }");
    }
}
