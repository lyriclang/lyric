using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// What follows <c>::</c> (spec 02 §1 rule 6): one interface, or several joined by
/// <c>&amp;</c>; one in brackets is the set of one. Several as a list — in brackets or not — are
/// <c>LYR-PAR0066</c>, read as joined so that the checker still sees each.
/// </summary>
public class JoinedBoundsTests
{
    private static (Module module, DiagnosticEngine diag) ParseModule(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, id, de).ParseModule(), de);
    }

    private static string[] Names(TypeNode[] types) => types.Select(t => string.Join(".", Assert.IsType<NamedType>(t).Path)).ToArray();

    private static TypeNode[] Bounds(Decl decl) => decl switch
    {
        StructDecl d => d.Interfaces,
        ClassDecl d => d.Interfaces,
        EnumDecl d => d.Interfaces,
        InterfaceDecl d => d.Interfaces,
        ExtendDecl d => d.Interfaces,
        FunctionDecl d => d.Generics[0].Constraints,
        _ => throw new InvalidOperationException(decl.GetType().Name),
    };

    [Theory]
    [InlineData("struct S :: I & J & K { }")]
    [InlineData("class S :: I & J & K { }")]
    [InlineData("enum S :: I & J & K { A }")]
    [InlineData("interface S :: I & J & K { }")]
    [InlineData("extend int :: I & J & K { }")]
    [InlineData("fn f<T :: I & J & K>(): void { }")]
    [InlineData("struct S :: [I] & J & K { }")]
    public void Several_are_joined_with_an_ampersand(string source)
    {
        var (module, de) = ParseModule(source);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => d.Message)));
        Assert.Equal(["I", "J", "K"], Names(Bounds(module.Declarations[0])));
    }

    [Fact]
    public void A_constraint_ends_at_the_comma_that_begins_the_next_parameter()
    {
        var (module, de) = ParseModule("fn f<T :: I & J, U :: K>(): void { }");
        Assert.False(de.HasErrors);
        var fn = Assert.IsType<FunctionDecl>(module.Declarations[0]);
        Assert.Equal(["I", "J"], Names(fn.Generics[0].Constraints));
        Assert.Equal(["K"], Names(fn.Generics[1].Constraints));
    }

    [Fact]
    public void An_associated_types_bound_joins_them_too()
    {
        var (module, de) = ParseModule("interface C { type Iter :: I & J; }");
        Assert.False(de.HasErrors);
        var assoc = Assert.IsType<AssociatedTypeDecl>(Assert.IsType<InterfaceDecl>(module.Declarations[0]).Types[0]);
        Assert.Equal(["I", "J"], Names(assoc.Bounds));
    }

    /// <summary>One in brackets stays as written: the set of one is the interface.</summary>
    [Fact]
    public void One_in_brackets_is_the_interface()
    {
        var (module, de) = ParseModule("struct S :: [I] { } struct R :: I { } struct Q :: I & J { }");
        Assert.False(de.HasErrors);
        Assert.True(Assert.IsType<StructDecl>(module.Declarations[0]).InterfacesBracketed);
        Assert.False(Assert.IsType<StructDecl>(module.Declarations[1]).InterfacesBracketed);
        Assert.False(Assert.IsType<StructDecl>(module.Declarations[2]).InterfacesBracketed);
    }

    [Fact]
    public void Each_entry_may_delegate()
    {
        var (module, de) = ParseModule("class D :: Walker by legs & Named & Runner by feet { }");
        Assert.False(de.HasErrors);
        var d = Assert.IsType<ClassDecl>(module.Declarations[0]);
        Assert.Equal(["Walker", "Named", "Runner"], Names(d.Interfaces));
        Assert.Equal(new string?[] { "legs", null, "feet" }, d.Delegates!);
    }

    /// <summary>A list of several is refused, each entry still read.</summary>
    [Theory]
    [InlineData("struct S :: [I, J] { }")]
    [InlineData("struct S :: I, J { }")]
    [InlineData("interface S :: [I, J,] { }")]
    [InlineData("extend int :: [I, J] { }")]
    [InlineData("fn f<T :: [I, J]>(): void { }")]
    public void A_list_of_several_is_refused(string source)
    {
        var (module, de) = ParseModule(source);
        var refused = Assert.Single(de.Diagnostics);
        Assert.Equal("LYR-PAR0066", refused.Code);
        Assert.Contains("are joined with '&'", refused.Message);
        Assert.Equal(["I", "J"], Names(Bounds(module.Declarations[0])));
    }

    [Theory]
    [InlineData("struct S :: [I,] { }")]
    [InlineData("struct S :: [I by f] { }")]
    public void One_with_a_trailing_comma_or_a_delegate_stays_one(string source)
    {
        var (module, de) = ParseModule(source);
        Assert.False(de.HasErrors);
        Assert.Equal(["I"], Names(Bounds(module.Declarations[0])));
    }

    [Fact]
    public void A_constraint_delegates_nothing() =>
        Assert.Contains(ParseModule("fn f<T :: I by x>(): void { }").diag.Diagnostics, d => d.Code == "LYR-PAR0047");
}
