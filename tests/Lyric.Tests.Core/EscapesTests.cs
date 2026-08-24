using Lyric.Core;
using Xunit;

namespace Lyric.Tests.Core;

/// <summary>
/// The contract of <see cref="Escapes.Resolve"/>: valid sequences resolve, invalid ones are
/// skipped without a report — the function is pure and knows no diagnostic engine — and no
/// input may make it throw. The lexer is the place that reports; these tests pin the half
/// that must stay silent and total.
/// </summary>
public class EscapesTests
{
    private const char Backslash = (char)0x5C;

    /// <summary>Builds the escape input without writing backslashes into this file's own
    /// literals, so the test data cannot be mangled by a second level of escaping.</summary>
    private static string Esc(string sequence) => Backslash + sequence;

    [Fact]
    public void Text_without_backslash_is_returned_unchanged()
    {
        var text = "plain text with {braces} and quotes";
        Assert.Same(text, Escapes.Resolve(text));
    }

    [Theory]
    [InlineData("n", 0x0A)]
    [InlineData("r", 0x0D)]
    [InlineData("t", 0x09)]
    [InlineData("0", 0x00)]
    public void Simple_escapes_resolve(string sequence, int expected)
    {
        var resolved = Escapes.Resolve("a" + Esc(sequence) + "b");
        Assert.Equal(3, resolved.Length);
        Assert.Equal(expected, resolved[1]);
    }

    [Fact]
    public void Quote_and_backslash_escapes_resolve()
    {
        Assert.Equal(Backslash.ToString(), Escapes.Resolve(Esc(Backslash.ToString())));
        Assert.Equal("'", Escapes.Resolve(Esc("'")));
        var doubleQuote = ((char)0x22).ToString();
        Assert.Equal(doubleQuote, Escapes.Resolve(Esc(doubleQuote)));
    }

    [Theory]
    [InlineData("x41", 0x41)]
    [InlineData("x0a", 0x0A)]
    [InlineData("x7F", 0x7F)]
    public void Hex_escapes_resolve(string sequence, int expected)
    {
        var resolved = Escapes.Resolve(Esc(sequence));
        Assert.Equal(1, resolved.Length);
        Assert.Equal(expected, resolved[0]);
    }

    [Theory]
    [InlineData("u{41}", 0x41)]
    [InlineData("u{0}", 0x00)]
    [InlineData("u{D7FF}", 0xD7FF)]
    [InlineData("u{E000}", 0xE000)]
    public void Unicode_escapes_resolve(string sequence, int expected)
    {
        var resolved = Escapes.Resolve(Esc(sequence));
        Assert.Equal(1, resolved.Length);
        Assert.Equal(expected, resolved[0]);
    }

    [Theory]
    [InlineData("u{1F30D}", 0x1F30D)]
    [InlineData("u{10FFFF}", 0x10FFFF)]
    public void Astral_unicode_escapes_resolve_to_a_surrogate_pair(string sequence, int expected)
    {
        var resolved = Escapes.Resolve(Esc(sequence));
        Assert.Equal(2, resolved.Length);
        Assert.Equal(expected, char.ConvertToUtf32(resolved, 0));
    }

    [Fact]
    public void Unknown_escape_keeps_the_character()
    {
        Assert.Equal("q", Escapes.Resolve(Esc("q")));
    }

    [Theory]
    [InlineData("u{D800}")]      // surrogate: not a scalar value
    [InlineData("u{DFFF}")]
    [InlineData("u{110000}")]    // beyond the last scalar
    [InlineData("u{80000000}")]  // eight digits, high bit set — wrapped negative before
    [InlineData("u{FFFFFFFF}")]
    [InlineData("u{123456789}")] // more digits than any scalar needs
    public void Invalid_unicode_escape_is_skipped_not_thrown(string sequence)
    {
        // The invalid sequence vanishes; the surrounding text survives. Reporting is the
        // lexer's job, staying total is this function's.
        Assert.Equal("ab", Escapes.Resolve("a" + Esc(sequence) + "b"));
    }

    [Theory]
    [InlineData("x 9")]     // whitespace is not a hex digit
    [InlineData("u{ 41}")]
    public void Whitespace_inside_an_escape_does_not_parse(string sequence)
    {
        // NumberStyles.HexNumber would accept leading blanks; the resolution must not.
        var resolved = Escapes.Resolve("a" + Esc(sequence) + "b");
        Assert.DoesNotContain((char)0x41, resolved);
        Assert.DoesNotContain((char)0x09, resolved);
    }

    [Theory]
    [InlineData("")]        // trailing backslash at end of input
    [InlineData("x")]       // hex escape cut off
    [InlineData("x4")]
    [InlineData("u")]       // unicode escape cut off
    [InlineData("u{")]
    [InlineData("u{41")]    // no closing brace
    public void Truncated_escape_does_not_throw(string sequence)
    {
        _ = Escapes.Resolve(Esc(sequence));
    }
}
