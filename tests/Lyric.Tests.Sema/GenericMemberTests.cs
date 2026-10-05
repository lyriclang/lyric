using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A generic member of an interface, D9 whole (design/v5/spec/04 D9, the review's A8): with or
/// without a body it makes the interface a constraint and no type of a value
/// (<c>LYR-SEM0126</c>), and a conformer may write its own in place of a default's — checked
/// against the default's signature (<c>LYR-SEM0042</c>). The 4.x rule — a generic member with a
/// body is not overridable (<c>LYR-SEM0082</c>), and its interface still a value — holds in the
/// 4.x path alone, until that leaves <c>main</c>.
/// </summary>
public class GenericMemberTests
{
    private const string Shower = """
        interface Shower {
            fn name(): string;
            fn tag<T>(x: T): string { return this.name(); }
        }
        struct A :: [Shower] {
            fn name(): string { return "a"; }
        }

        """;

    private static Diagnostic[] Errors(string declarations, bool lyric5)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Shower + declarations);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de) { Lyric5Modules = lyric5 };
        comp.AddModule(new Parser(sm, id, de).ParseModule(), lyric5 ? "test" : null);
        Semantics.Analyze(comp, comp.Resolve(), de, singleProgram: false);
        return de.Diagnostics.Where(d => d.Severity == Severity.Error).ToArray();
    }

    private static void Silent(string declarations, bool lyric5)
    {
        var errors = Errors(declarations, lyric5);
        Assert.True(errors.Length == 0, string.Join("\n", errors.Select(e => e.Code + ": " + e.Message)));
    }

    private static Diagnostic One(string declarations, string code, bool lyric5)
    {
        var errors = Errors(declarations, lyric5);
        Assert.True(errors.Length == 1, errors.Length + " errors\n" + string.Join("\n", errors.Select(e => e.Code + ": " + e.Message)));
        Assert.Equal(code, errors[0].Code);
        return errors[0];
    }

    private const string Override = "struct B :: [Shower] {\n    fn name(): string { return \"b\"; }\n    fn tag<T>(x: T): string { return \"B\"; }\n}\n";
    private const string InABlock = "struct B {\n    n: int,\n}\nextend B :: [Shower] {\n    fn name(): string { return \"b\"; }\n    fn tag<T>(x: T): string { return \"B\"; }\n}\n";
    private const string AsAValue = "fn f(a: A): string {\n    let s: Shower = a;\n    return s.name();\n}\n";

    [Theory]
    [InlineData(Override)]
    [InlineData(InABlock)]
    public void A_conformer_writes_its_own_in_place_of_a_generic_default(string declarations) =>
        Silent(declarations, lyric5: true);

    /// <summary>Its own answers the default: the same type parameters, the same signature.</summary>
    [Theory]
    [InlineData("struct B :: [Shower] {\n    fn name(): string { return \"b\"; }\n    fn tag<T>(x: T): int { return 1; }\n}\n")]
    [InlineData("struct B :: [Shower] {\n    fn name(): string { return \"b\"; }\n    fn tag<T, U>(x: T): string { return \"B\"; }\n}\n")]
    public void Its_own_has_the_defaults_signature(string declarations)
    {
        var error = One(declarations, "LYR-SEM0042", lyric5: true);
        Assert.Contains("'B.tag' does not match interface 'Shower'", error.Message);
    }

    [Fact]
    public void An_interface_with_a_generic_member_is_no_type_of_a_value()
    {
        var error = One(AsAValue, "LYR-SEM0126", lyric5: true);
        Assert.Contains("its member 'tag' is generic", error.Message);
    }

    /// <summary>Control: an abstract generic member made its interface a constraint already.</summary>
    [Fact]
    public void An_abstract_generic_member_made_it_one_before()
    {
        var error = One("interface Folder {\n    fn fold<T>(x: T): T;\n}\nstruct F :: [Folder] {\n    fn fold<T>(x: T): T { return x; }\n}\nfn f(v: F): void {\n    let s: Folder = v;\n}\n",
            "LYR-SEM0126", lyric5: true);
        Assert.Contains("its member 'fold' is generic", error.Message);
    }

    /// <summary>The 4.x path keeps its rule until it leaves main (M16): no override, and a value.</summary>
    [Fact]
    public void The_4x_path_keeps_its_rule()
    {
        Assert.Contains("overrides a generic member", One(Override, "LYR-SEM0082", lyric5: false).Message);
        Silent(AsAValue, lyric5: false);
    }

    private const string Source = "interface Source<T> {\n    fn get(): T;\n    fn pick<U>(u: U): U { return u; }\n}\n";

    /// <summary>A generic INTERFACE's defaults still run for its value, where a conformer's own
    /// member would not be found from inside another default: writing one stays refused there,
    /// and the sentence says which interfaces that is. Its value is gone all the same.</summary>
    [Fact]
    public void A_generic_interfaces_generic_default_is_not_replaced_yet()
    {
        var error = One(Source + "struct G :: [Source<int>] {\n    fn get(): int { return 1; }\n    fn pick<U>(u: U): U { return u; }\n}\n",
            "LYR-SEM0082", lyric5: true);
        Assert.Contains("the generic interface 'Source'", error.Message);
    }

    /// <summary>A generic member a type leaves to a field (05 §5) would need a forwarder per
    /// instantiation, which is not built: refused where the type delegates. A call of it crashed
    /// the compiler ("function has no body"). In both paths — neither can forward it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_generic_member_is_not_forwarded_to_a_field_yet(bool lyric5)
    {
        var error = One("interface Tagger {\n    fn tag<U>(u: U): string;\n}\nstruct Inner :: [Tagger] {\n    n: int,\n    fn tag<U>(u: U): string { return \"inner\"; }\n}\n"
                        + "struct Outer :: [Tagger by inner] {\n    inner: Inner,\n}\n", "LYR-SEM0020", lyric5);
        Assert.Contains("'Outer' does not implement the generic member 'tag' of interface 'Tagger'", error.Message);
        Assert.Contains("not forwarded to a field yet", error.Message);
    }

    /// <summary>Control: written on the type, it conforms.</summary>
    [Fact]
    public void A_generic_member_written_beside_a_delegation_conforms() =>
        Silent("interface Tagger {\n    fn tag<U>(u: U): string;\n    fn name(): string;\n}\nstruct Inner :: [Tagger] {\n    n: int,\n    fn tag<U>(u: U): string { return \"inner\"; }\n    fn name(): string { return \"i\"; }\n}\n"
               + "struct Outer :: [Tagger by inner] {\n    inner: Inner,\n    fn tag<U>(u: U): string { return this.inner.tag(u); }\n}\n", lyric5: true);

    [Fact]
    public void A_generic_interface_with_a_generic_default_is_no_type_of_a_value()
    {
        var error = One(Source + "struct G :: [Source<int>] {\n    fn get(): int { return 1; }\n}\nfn f(g: G): void {\n    let s: Source<int> = g;\n}\n",
            "LYR-SEM0126", lyric5: true);
        Assert.Contains("its member 'pick' is generic", error.Message);
    }
}
