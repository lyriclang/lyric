using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Xunit;

namespace Lyric.Tests.Resolver;

/// <summary>
/// A type position looks past a value of the name (M9b S1, <c>std.syntax</c>): in
/// <c>enum Expr { Literal(Literal, Span) }</c> the payload names the module's type
/// <c>Literal</c>, not the enum's own variant, which stands nearer in the scope chain.
/// </summary>
public class TypePositionTests
{
    private static (Compilation comp, DiagnosticEngine de, BindingResult binding) Resolve(string source)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        var id = sm.AddVirtual("m.lyr", source);
        comp.AddModule(new Parser(sm, id, de).ParseModule(), "m");
        return (comp, de, comp.Resolve());
    }

    [Fact]
    public void A_variant_of_the_name_does_not_hide_the_type_in_its_payload()
    {
        var (comp, de, binding) = Resolve("struct Literal { v: int, } struct Span { line: int, } enum Expr { Literal(Literal, Span), Block(Block), } struct Block { n: int, }");
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => d.Message)));
        var m = comp.Modules[0];
        var expr = (EnumDecl)((TypeSymbol)m.Members.LookupLocal("Expr")!).Declaration!;
        var literal = (TypeSymbol)m.Members.LookupLocal("Literal")!;
        Assert.Same(literal, binding.Resolve(expr.Variants[0].TupleFields![0]));
        var block = (TypeSymbol)m.Members.LookupLocal("Block")!;
        Assert.Same(block, binding.Resolve(expr.Variants[1].TupleFields![0]));
    }

    [Fact]
    public void A_method_of_the_name_does_not_hide_the_type_either()
    {
        var (_, de, _) = Resolve("struct Point { x: int, } struct Line { fn Point(): int { return 1; } fn at(): Point { return Point { x = 1 }; } }");
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void A_name_that_is_no_type_anywhere_is_still_unresolved()
    {
        var (_, de, _) = Resolve("enum E { Nope(Nope), }");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-RES0002");
    }
}
