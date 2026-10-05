using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// An enum's body (design/v5/spec/08; the review's M8a-10): the variants, a ';', and then the
/// members — methods, associated types, and constants, <c>static let</c>. A member where a
/// variant's name is expected is one error (<c>LYR-PAR0057</c>), and no keyword becomes a
/// variant: <c>static</c> and <c>let</c> were read as two.
/// </summary>
public class EnumBodyTests
{
    private static (EnumDecl Decl, DiagnosticEngine De) Parse(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var module = new Parser(sm, sm.AddVirtual("test.lyr", source), de).ParseModule();
        return (module.Declarations.OfType<EnumDecl>().Single(), de);
    }

    private static string[] Variants(EnumDecl decl) => decl.Variants.Select(v => v.Name).ToArray();

    [Fact]
    public void A_constant_stands_behind_the_variants()
    {
        var (decl, de) = Parse("""
            enum Level {
                Low, High;
                static let fallback: Level = Level.Low;
                pub static let count = 2;
                fn name(): string { return "x"; }
            }
            """);
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Equal(["Low", "High"], Variants(decl));
        Assert.Equal(["fallback", "count"], decl.Statics.Select(s => s.Binding.Name).ToArray());
        Assert.Equal(VisibilityWord.Pub, decl.Statics[1].Visibility);
        Assert.Single(decl.Methods);
    }

    [Theory]
    [InlineData("enum Level { Low, High, static let fallback: Level = Level.Low; }", "static")]
    [InlineData("enum Level { Low, High, fn name(): string { return \"x\"; } }", "fn")]
    [InlineData("enum Level { Low, High, pub static fn make(): Level { return Level.Low; } }", "pub")]
    [InlineData("enum Level { Low, High, mut fn flip(): void { } }", "mut")]
    [InlineData("enum Level { Low, High, type Item = int; }", "type")]
    public void A_member_where_a_variant_is_expected_is_one_error(string source, string at)
    {
        var (decl, de) = Parse(source);
        var error = Assert.Single(de.Diagnostics);
        Assert.Equal("LYR-PAR0057", error.Code);
        Assert.Equal(at, source[error.Span.Start..error.Span.End]);
        Assert.Equal(["Low", "High"], Variants(decl));
        Assert.Equal(1, decl.Statics.Length + decl.Methods.Length + decl.Types.Length);
    }

    /// <summary>A keyword is no name: it was read as one, and the enum had a variant 'let'.</summary>
    [Fact]
    public void A_keyword_becomes_no_variant()
    {
        var (decl, de) = Parse("enum Level { Low, let, High }");
        Assert.Equal("LYR-PAR0026", Assert.Single(de.Diagnostics).Code);
        Assert.Equal(["Low", "High"], Variants(decl));
    }

    /// <summary>The controls: 'type' alone is a name, and a visibility word before a name is a
    /// variant's — refused as that (LYR-PAR0053), not taken for a member.</summary>
    [Fact]
    public void What_only_looks_like_a_member_is_a_variant()
    {
        var (named, de) = Parse("enum Kind { type, other }");
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        Assert.Equal(["type", "other"], Variants(named));

        var (worded, de2) = Parse("enum Level { pub Low, High }");
        Assert.Equal("LYR-PAR0053", Assert.Single(de2.Diagnostics).Code);
        Assert.Equal(["Low", "High"], Variants(worded));
    }

    /// <summary>What an enum holds, a walk over the tree comes to: its constants — and its
    /// associated types, which were not handed on.</summary>
    [Fact]
    public void A_walk_comes_to_an_enums_constants_and_associated_types()
    {
        var (decl, de) = Parse("""
            enum Walk :: [Iterator] {
                A;
                type Item = int;
                static let limit: int = 3;
                fn next(): ?int { return null; }
            }
            """);
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        var children = AstChildren.Of(decl).ToArray();
        Assert.Single(children.OfType<AssociatedTypeDecl>());
        Assert.Single(children.OfType<StaticBindingDecl>());
        Assert.Single(children.OfType<FunctionDecl>());
        Assert.Single(children.OfType<EnumVariant>());
    }
}
