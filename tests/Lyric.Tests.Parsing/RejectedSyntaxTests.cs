using Lyric.Core;
using Lyric.Parsing;

namespace Lyric.Tests.Parsing;

/// <summary>
/// Two forms Lyric does not have, which therefore have to SAY that they do not exist.
///
/// <para>Both used to report something else. An attribute on a parameter was read as a parameter name;
/// the body was then missing and the compiler spoke of native declarations — to someone who wanted to
/// write an attribute. <c>interface B :: [A]</c> ran into a message about parameter parentheses.</para>
///
/// <para>That is no blemish: a diagnostic pointing at the wrong cause costs more time than none at all.
/// Whoever reads it searches at the place it names.</para>
/// </summary>
public class RejectedSyntaxTests
{
    private static IReadOnlyList<Diagnostic> Parse(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        new Parser(sm, id, de).ParseModule();
        return de.Diagnostics;
    }

    // ------------------------------------------------------------------ Attribute

    /// <summary>On a declaration an attribute PARSES since the attribute milestone; what it names
    /// is the sema's question. This test pins the parser half of that split.</summary>
    [Fact]
    public void An_attribute_on_a_declaration_parses() =>
        Assert.Empty(Parse("@test\nfn f(): int { return 1; }"));

    /// <summary>
    /// On a parameter too, since Lyric 5's <c>@callerExpr</c> (design/v5/spec/09 A11): the parser
    /// reads it, and which attribute may sit there is the checker's question. It used to say
    /// <c>LYR-PAR0038</c>, and before that the parser lost the body.
    /// </summary>
    [Fact]
    public void An_attribute_on_a_parameter_parses_and_keeps_the_body() =>
        Assert.Empty(Parse("fn nimm(@noCapture f: fn() -> int): int { return f(); }"));

    /// <summary>Several attributes on one parameter parse as a list, and the loop terminates.</summary>
    [Fact]
    public void Several_attributes_on_one_parameter_parse() =>
        Assert.Empty(Parse("fn f(@a @b x: int): int { return x; }"));

    // ------------------------------------------------------------------ places (03 T12)

    /// <summary>The place mark at a parameter, in a function type and at an argument — the three
    /// spellings of design/v5/spec/03 T12 — parses without a word.</summary>
    [Fact]
    public void The_place_mark_parses_where_it_stands() =>
        Assert.Empty(Parse("fn f(&n: int, g: fn(&int, string) -> void): void { g(&n, \"x\"); f(&n, g); }"));

    /// <summary>
    /// Anywhere else it is <c>LYR-PAR0054</c>, once, and the expression it stood before still
    /// parses: there is no address as a value (design/v5/spec/08 Y4). Between operands it stays
    /// the bitwise and.
    /// </summary>
    [Fact]
    public void The_place_mark_stands_at_an_argument_only()
    {
        var diagnostics = Parse("fn f(): void { var x = 1; let y = &x; let z = x & &x; }");

        Assert.Equal(2, diagnostics.Count(d => d.Code == "LYR-PAR0054"));
        Assert.Equal(2, diagnostics.Count);
    }

    // ------------------------------------------------------------------ interface inheritance

    /// <summary>
    /// v1.13 turned LYR-PAR0039 from an error into the feature: the parent list parses like the
    /// one on structs, and everything it may not be — several parents, a non-interface, a cycle —
    /// is the sema's message, not the parser's.
    /// </summary>
    [Fact]
    public void An_interface_parent_list_parses_since_v1_13() =>
        Assert.Empty(Parse("interface A { fn a(): int; }\ninterface B :: [A] { fn b(): int; }"));

    /// <summary>
    /// The counter-check. A <c>::</c> on a CLASS is valid and must not be hit by this message, or half
    /// the stdlib would be a syntax error.
    /// </summary>
    [Fact]
    public void A_conformance_list_on_a_class_is_still_fine() =>
        Assert.Empty(Parse("""
            interface A { fn a(): int; }
            class K :: [A] { fn a(): int { return 1; } }
            """));

    /// <summary>And an ordinary interface stays ordinary.</summary>
    [Fact]
    public void A_plain_interface_parses() =>
        Assert.Empty(Parse("interface A<T> { fn a(): T; }"));
}
