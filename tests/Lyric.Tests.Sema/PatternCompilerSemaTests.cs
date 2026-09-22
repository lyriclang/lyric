using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// The sema half of the pattern compiler (4.5): a name nested over a <c>?T</c> binds the whole
/// optional and covers it, a pattern literal adapts to the scrutinee's width, or-alternatives
/// after the first share the first alternative's symbols, and a parenthesized guard is a guard.
/// </summary>
public class PatternCompilerSemaTests
{
    private static (TypeResult types, DiagnosticEngine de, Module module) Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        var module = new Parser(sm, id, de).ParseModule();
        comp.AddModule(module);
        var binding = comp.Resolve();
        var types = new TypeChecker(comp, binding, de).Check();
        return (types, de, module);
    }

    private static void AssertClean(DiagnosticEngine de) =>
        Assert.False(de.HasErrors, string.Join("; ", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

    private static MatchExpr OnlyMatch(Module module) =>
        module.Declarations.OfType<FunctionDecl>().Single().Body!.Statements
            .OfType<ReturnStmt>().Select(r => (MatchExpr)r.Value!).Single();

    [Fact]
    public void A_name_nested_over_an_optional_payload_covers_the_variant()
    {
        var (_, de, _) = Check("enum Opt<T> { Some(T), None } fn f(o: Opt<?int>): int { return match (o) { Some(v) => v ?? 0, None => 0 }; }");
        AssertClean(de);
    }

    [Fact]
    public void A_name_nested_over_an_optional_binds_the_optional_type()
    {
        var (types, de, module) = Check("fn f(t: (?int, int)): int { return match (t) { (a, b) => b }; }");
        AssertClean(de);
        var a = ((TuplePattern)OnlyMatch(module).Arms[0].Pattern).Elements[0];
        var local = Assert.IsType<LocalSymbol>(types.RefOf(a));
        Assert.True(local.Type is Optional { Inner: PrimitiveType { Kind: PrimitiveKind.Int } });
    }

    [Fact]
    public void A_name_at_the_top_of_a_match_still_narrows_and_needs_the_null_arm()
    {
        var (_, de, _) = Check("fn f(o: ?int): int { return match (o) { n => n }; }");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0050" && d.Message.Contains("'null'"));
    }

    [Fact]
    public void A_null_pattern_inside_a_tuple_is_a_presence_test()
    {
        var (_, de, _) = Check("fn f(t: (?int, int)): int { return match (t) { (null, b) => b, (a, b) => b }; }");
        AssertClean(de);
    }

    [Fact]
    public void A_pattern_literal_takes_the_scrutinee_width()
    {
        var (types, de, module) = Check("fn f(b: int8): int { return match (b) { 1 => 10, 2..=5 => 20, _ => 0 }; }");
        AssertClean(de);
        var arms = OnlyMatch(module).Arms;
        Assert.True(types.TypeOf(((LiteralPattern)arms[0].Pattern).Literal) is PrimitiveType { Kind: PrimitiveKind.Int8 });
        Assert.True(types.TypeOf(((RangePattern)arms[1].Pattern).High) is PrimitiveType { Kind: PrimitiveKind.Int8 });
    }

    [Fact]
    public void Or_alternatives_share_the_first_alternatives_symbol_and_warn_nothing()
    {
        var (types, de, module) = Check("enum E { A(int), B(int) } fn f(e: E): int { return match (e) { A(x) | B(x) => x }; }");
        AssertClean(de);
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0071");
        var or = (OrPattern)OnlyMatch(module).Arms[0].Pattern;
        var first = ((VariantPattern)or.Alternatives[0]).TupleElements![0];
        var second = ((VariantPattern)or.Alternatives[1]).TupleElements![0];
        Assert.Same(types.RefOf(first), types.RefOf(second));
    }

    [Fact]
    public void A_parenthesized_guard_is_not_a_lambda()
    {
        var (_, de, _) = Check("fn f(n: int): int { return match (n) { x if (x > 0) => 1, _ => 0 }; }");
        AssertClean(de);
    }

    [Fact]
    public void A_struct_field_pattern_with_a_literal_is_refutable()
    {
        var (_, de, _) = Check("struct P { x: int, y: int } fn f(p: P): int { return match (p) { P { x = 0, y } => y }; }");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0050");
    }
}
