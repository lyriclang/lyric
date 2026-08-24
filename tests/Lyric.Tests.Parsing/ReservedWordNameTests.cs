using Lyric.Core;
using Lyric.Parsing;

namespace Lyric.Tests.Parsing;

/// <summary>
/// A keyword written where a NAME belongs. The parser recovers as it always did; what these tests
/// pin is the sentence a reader needs, which used to be missing entirely.
///
/// <para><c>keep.resume()</c> produced four errors, and every one of them named <c>Resume</c> as
/// the token that stood where something else was expected — the way a parser talks about a typo.
/// A reader checks the spelling first and only then suspects the word itself, which is two round
/// trips for a fact the compiler knew before it started.</para>
/// </summary>
public class ReservedWordNameTests
{
    private static IReadOnlyList<Diagnostic> Parse(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        new Parser(sm, id, de).ParseModule();
        return de.Diagnostics;
    }

    private static void AssertSaysKeyword(IReadOnlyList<Diagnostic> diagnostics, string code,
        string word)
    {
        var diagnostic = Assert.Single(diagnostics, d => d.Code == code);
        Assert.NotNull(diagnostic.Notes);
        Assert.Contains(diagnostic.Notes!,
            n => n.Message == $"'{word}' is a keyword and cannot be used as a name");
    }

    [Fact]
    public void A_member_name_that_is_a_keyword_says_so() =>
        AssertSaysKeyword(Parse("fn f(): int { keep.resume(); return 0; }"), "LYR-PAR0003",
            "resume");

    [Fact]
    public void A_binding_name_that_is_a_keyword_says_so() =>
        AssertSaysKeyword(Parse("fn f(): int { let resume = 1; return resume; }"), "LYR-PAR0020",
            "resume");

    [Fact]
    public void A_type_path_segment_that_is_a_keyword_says_so() =>
        AssertSaysKeyword(Parse("fn f(): keep.match { return 0; }"), "LYR-PAR0011", "match");

    /// <summary>
    /// The negative, and the reason the note hangs on the identifier expectations alone: a
    /// statement that forgot its semicolon meets the NEXT statement's keyword, and "'return' is a
    /// keyword" would be true, useless and printed on every one of them.
    /// </summary>
    [Fact]
    public void A_keyword_where_a_name_was_not_expected_gets_no_note()
    {
        var diagnostics = Parse("fn f(): int { let x = 1 return x; }");

        Assert.Contains(diagnostics, d => d.Code == "LYR-PAR0016");
        Assert.All(diagnostics, d => Assert.DoesNotContain(d.Notes ?? [],
            n => n.Message.Contains("is a keyword")));
    }

    /// <summary>An ordinary misspelling still reads as one: no note invents a keyword.</summary>
    [Fact]
    public void An_ordinary_missing_name_gets_no_note()
    {
        var diagnostics = Parse("fn f(): int { let = 1; return 0; }");

        Assert.Contains(diagnostics, d => d.Code == "LYR-PAR0020");
        Assert.All(diagnostics, d => Assert.DoesNotContain(d.Notes ?? [],
            n => n.Message.Contains("is a keyword")));
    }
}
