using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// An initializer settles every field (§3.4, <c>LYR-SEM0106</c>).
///
/// <para>IT WAS THE LOWERING'S JOB, and the lowering has no business refusing a program: the
/// omission answered <c>LYR-IR0001</c> with the note "this compiler version cannot lower it yet"
/// — a promise about a future release, for a program no release will ever run. §12.1 keeps that
/// code for VALID Lyric.</para>
///
/// <para>The practical half is that <c>lyrc check</c> never lowers, so it answered <b>ok</b> to a
/// program <c>lyrc build</c> refuses. A checker that passes what the compiler rejects is worse
/// than no checker, because it is trusted.</para>
///
/// <para>And the message now names EVERY missing field. The lowering threw on the first one it
/// met, so a three-field omission took three compiles to find.</para>
/// </summary>
public class InitializerCompletenessTests
{
    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static string? Omission(DiagnosticEngine de) =>
        de.Diagnostics.Where(d => d.Code == "LYR-SEM0106").Select(d => d.Message).FirstOrDefault();

    [Fact]
    public void A_field_without_a_default_must_be_given_a_value()
    {
        var de = Check("""
            class C { a: int, b: int }
            fn main(): int { let c = C { a = 1 }; return c.a; }
            """);

        Assert.Contains("'b'", Omission(de) ?? "", StringComparison.Ordinal);
    }

    /// <summary>One message, both names — not one compile per field.</summary>
    [Fact]
    public void Every_missing_field_is_named_at_once()
    {
        var de = Check("""
            class C { a: int, b: int, c: string }
            fn main(): int { let c = C { a = 1 }; return c.a; }
            """);

        var message = Omission(de);
        Assert.NotNull(message);
        Assert.Contains("'b'", message!, StringComparison.Ordinal);
        Assert.Contains("'c'", message!, StringComparison.Ordinal);
    }

    /// <summary>The control: the rule is about fields with NO default.</summary>
    [Fact]
    public void A_field_with_a_default_may_be_left_out()
    {
        var de = Check("""
            class C { a: int, b: int = 7 }
            fn main(): int { let c = C { a = 1 }; return c.b; }
            """);

        Assert.False(de.HasErrors);
    }

    [Fact]
    public void A_struct_follows_the_same_rule()
    {
        var de = Check("""
            struct P { x: int, y: int }
            fn main(): int { let p = P { x = 1 }; return p.x; }
            """);

        Assert.Contains("'y'", Omission(de) ?? "", StringComparison.Ordinal);
    }

    /// <summary>An enum's STRUCT variant is an initializer too, and had the same gap.</summary>
    [Fact]
    public void A_struct_variant_follows_the_same_rule()
    {
        var de = Check("""
            enum E { S { a: int, b: int }, U, }
            fn main(): int { let e = E.S { a = 1 }; return 0; }
            """);

        Assert.Contains("'b'", Omission(de) ?? "", StringComparison.Ordinal);
    }

    /// <summary>A complete initializer says nothing, however many fields it has.</summary>
    [Fact]
    public void A_complete_initializer_is_silent()
    {
        var de = Check("""
            class C { a: int, b: int, c: string }
            fn main(): int { let c = C { a = 1, b = 2, c = "x" }; return c.a; }
            """);

        Assert.False(de.HasErrors);
    }

    /// <summary>
    /// A field the type does NOT have is still its own error, and the two do not cancel out: one
    /// name too many does not pay for one name too few.
    /// </summary>
    [Fact]
    public void A_wrong_name_does_not_settle_a_missing_one()
    {
        var de = Check("""
            class C { a: int, b: int }
            fn main(): int { let c = C { a = 1, z = 2 }; return c.a; }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0015");
        Assert.Contains("'b'", Omission(de) ?? "", StringComparison.Ordinal);
    }

    /// <summary>A generic type's initializer is one too, and its instance changes nothing.</summary>
    [Fact]
    public void A_generic_type_follows_the_same_rule()
    {
        var de = Check("""
            class Box<T> { v: T, tag: int }
            fn main(): int { let b = Box<int> { v = 1 }; return b.v; }
            """);

        Assert.Contains("'tag'", Omission(de) ?? "", StringComparison.Ordinal);
    }

    /// <summary>
    /// An EMPTY initializer for a type with no fields stays legal.
    ///
    /// <para>The control for the rule's shape: "omits a field with no default" over an empty list
    /// is vacuously satisfied, and a check written as "did it mention anything?" would not be.</para>
    /// </summary>
    [Fact]
    public void A_type_with_no_fields_takes_an_empty_initializer()
    {
        var de = Check("""
            class Marker { }
            fn main(): int { let m = Marker { }; return 0; }
            """);

        Assert.False(de.HasErrors);
    }
}
