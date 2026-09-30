using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// What may be written, in Lyric 5 (design/v5/spec/02 M2, M3, M4, M9): a field is written only
/// when it is declared <c>var</c>, and only through a root that may be written. A <c>let</c>
/// freezes a VALUE to the bottom and a parameter is a <c>let</c>; a REFERENCE starts the chain
/// anew, so behind a class value or an array the object is the place, whatever holds the
/// reference. <c>this</c> is a place only in a <c>mut fn</c>, for a class as for a struct, and
/// a <c>mut fn</c> is called only on a place.
///
/// <para>The rules of Lyric 4 these replace — every field writable, a struct field writable
/// through <c>let</c> and through a parameter, <c>mut</c> unenforced on a class — were pinned
/// here and went with them. What stayed is the first lesson of this file: a rule about
/// mutability that no test holds does not hold.</para>
/// </summary>
public class MutabilityTests
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

    private static void Allowed(string source)
    {
        var de = Check(source);
        Assert.False(de.HasErrors,
            string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string source)
    {
        var errors = Check(source).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == "LYR-SEM0019",
            "expected exactly one LYR-SEM0019, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    // ------------------------------------------------------------------ M2, M9: the field says it

    [Fact]
    public void A_var_field_of_a_class_is_written_through_any_reference() =>
        Allowed("""
            class P { var hp: int }
            fn main(): int { let p = P { hp = 1 }; p.hp = 9; return p.hp; }
            """);

    [Fact]
    public void A_field_without_var_is_fixed_on_a_class_too()
    {
        // M9: a class 'let' field is really fixed. Lyric 4 wrote every class field.
        var message = Rejected("""
            class P { hp: int }
            fn main(): int { var p = P { hp = 1 }; p.hp = 9; return p.hp; }
            """);
        Assert.Contains("field 'hp' of 'P' is not declared 'var'", message);
    }

    [Fact]
    public void A_field_without_var_is_fixed_on_a_struct() =>
        Rejected("""
            struct V { x: int, }
            fn main(): int { var v = V { x = 1 }; v.x = 9; return v.x; }
            """);

    [Fact]
    public void A_var_field_of_a_var_struct_is_written() =>
        Allowed("""
            struct V { var x: int, }
            fn main(): int { var v = V { x = 1 }; v.x = 9; v.x += 1; v.x++; return v.x; }
            """);

    // ------------------------------------------------------------------ M3: the root decides

    [Fact]
    public void A_let_freezes_a_struct_to_its_fields()
    {
        // Lyric 4 let this through: 'let' pinned the name and not the value.
        var message = Rejected("""
            struct V { var x: int, }
            fn main(): int { let v = V { x = 1 }; v.x = 9; return v.x; }
            """);
        Assert.Contains("'v' is bound with 'let'", message);
    }

    [Fact]
    public void A_parameter_is_a_let()
    {
        var message = Rejected("""
            struct V { var x: int, }
            fn f(v: V): int { v.x = 9; return v.x; }
            fn main(): int { return f(V { x = 1 }); }
            """);
        Assert.Contains("'v' is a parameter", message);
        Rejected("fn f(n: int): int { n = 2; return n; }\nfn main(): int { return f(1); }");
    }

    [Fact]
    public void The_freeze_reaches_through_nested_structs()
    {
        const string types = "struct In { var n: int, }\nstruct Out { var a: In, b: In, }\n";
        Allowed(types + "fn main(): int { var o = Out { a = In { n = 1 }, b = In { n = 2 } }; o.a.n = 9; return o.a.n; }");
        // The inner field is 'var', the way to it is not.
        var message = Rejected(types + "fn main(): int { var o = Out { a = In { n = 1 }, b = In { n = 2 } }; o.b.n = 9; return o.b.n; }");
        Assert.Contains("field 'b' of 'Out' is not declared 'var'", message);
        Rejected(types + "fn main(): int { let o = Out { a = In { n = 1 }, b = In { n = 2 } }; o.a.n = 9; return o.a.n; }");
    }

    [Fact]
    public void A_reference_starts_the_chain_anew()
    {
        // Behind a class value the object is the place — through a 'let', through a 'let' field
        // of a frozen struct, through a parameter.
        Allowed("""
            class C { var n: int }
            struct S { c: C, }
            fn touch(c: C): void { c.n = 7; }
            fn main(): int {
                let s = S { c = C { n = 1 } };
                s.c.n = 9;
                touch(s.c);
                return s.c.n;
            }
            """);
    }

    [Fact]
    public void An_array_is_a_reference_and_its_elements_are_places()
    {
        Allowed("fn main(): int { let xs = [1, 2]; xs[0] = 9; return xs[0]; }");
        // A struct element is a place in the array's block: written through a 'let' array.
        Allowed("""
            struct V { var x: int, }
            fn main(): int { let vs = [V { x = 1 }]; vs[0].x = 9; return vs[0].x; }
            """);
        Allowed("""
            class P { var hp: int }
            fn main(): int { let ps = [P { hp = 1 }]; ps[0].hp = 9; return ps[0].hp; }
            """);
        // The field rule holds behind the element as anywhere.
        Rejected("""
            struct V { x: int, }
            fn main(): int { let vs = [V { x = 1 }]; vs[0].x = 9; return vs[0].x; }
            """);
    }

    [Fact]
    public void A_let_binding_cannot_be_rebound()
    {
        // 'let' pins the name. Without this test the rule would be green even if 'let' meant
        // nothing at all any more.
        Rejected("fn main(): int { let x = 1; x = 2; return x; }");
        Rejected("fn main(): int { let xs = [1, 2]; xs = [3]; return xs[0]; }");
    }

    [Fact]
    public void A_temporary_is_not_written()
    {
        // M4: writing into the result of a call would change a copy nobody reads. Refused,
        // never copied in silence. A class result is a reference, and that is a place.
        var message = Rejected("""
            struct V { var x: int, }
            fn make(): V { return V { x = 1 }; }
            fn main(): int { make().x = 9; return 0; }
            """);
        Assert.Contains("temporary", message);
        Allowed("""
            class C { var n: int }
            fn make(): C { return C { n = 1 }; }
            fn main(): int { make().n = 9; return 0; }
            """);
    }

    // ------------------------------------------------------------------ M4: 'mut fn'

    [Fact]
    public void A_method_that_writes_this_is_a_mut_fn_on_a_struct_and_on_a_class()
    {
        var message = Rejected("""
            struct V { var x: int, fn poke(): int { this.x = 9; return this.x; } }
            fn main(): int { return 0; }
            """);
        Assert.Contains("not declared 'mut fn'", message);
        // Lyric 4 enforced the word on structs only.
        Rejected("""
            class C { var n: int, fn poke(): void { this.n = 9; } }
            fn main(): int { return 0; }
            """);
        Rejected("""
            class C { var n: int, fn poke(): void { this.n++; } }
            fn main(): int { return 0; }
            """);
        Allowed("""
            struct V { var x: int, mut fn poke(): void { this.x = 9; } }
            class C { var n: int, mut fn poke(): void { this.n += 1; } }
            fn main(): int { return 0; }
            """);
    }

    [Fact]
    public void Mut_is_transitive()
    {
        // A method that is not 'mut' may not call a 'mut fn' on 'this', or the word would
        // promise nothing about the methods that call it.
        var message = Rejected("""
            class C {
                var n: int,
                mut fn bump(): void { this.n += 1; }
                fn twice(): void { this.bump(); }
            }
            fn main(): int { return 0; }
            """);
        Assert.Contains("cannot call 'mut fn bump'", message);
        Allowed("""
            class C {
                var n: int,
                mut fn bump(): void { this.n += 1; }
                mut fn twice(): void { this.bump(); this.bump(); }
                fn read(): int { return this.n; }
            }
            fn main(): int { return 0; }
            """);
    }

    [Fact]
    public void A_mut_fn_is_called_on_a_place()
    {
        const string v = "struct V { var x: int, mut fn bump(): void { this.x += 1; } }\n";
        Allowed(v + "fn main(): int { var a = V { x = 1 }; a.bump(); return a.x; }");
        // Lyric 4 ran this and changed 'a'.
        var message = Rejected(v + "fn main(): int { let a = V { x = 1 }; a.bump(); return a.x; }");
        Assert.Contains("cannot call 'mut fn bump' on this receiver", message);
        Assert.Contains("'a' is bound with 'let'", message);
        Rejected(v + "fn f(a: V): int { a.bump(); return a.x; }\nfn main(): int { return f(V { x = 1 }); }");
        Rejected(v + "fn make(): V { return V { x = 1 }; }\nfn main(): int { make().bump(); return 0; }");
        // Through an array element, a place; through a field, by the field's own rule.
        Allowed(v + "fn main(): int { let vs = [V { x = 1 }]; vs[0].bump(); return vs[0].x; }");
        Allowed(v + "struct W { var v: V, }\nfn main(): int { var w = W { v = V { x = 1 } }; w.v.bump(); return w.v.x; }");
        Rejected(v + "struct W { v: V, }\nfn main(): int { var w = W { v = V { x = 1 } }; w.v.bump(); return w.v.x; }");
    }

    [Fact]
    public void A_mut_fn_of_a_class_is_called_through_any_reference()
    {
        Allowed("""
            class C { var n: int, mut fn bump(): void { this.n += 1; } }
            struct S { c: C, }
            fn make(): C { return C { n = 0 }; }
            fn poke(c: C): void { c.bump(); }
            fn main(): int {
                let c = C { n = 0 };
                c.bump();
                let s = S { c = c };
                s.c.bump();
                make().bump();
                poke(c);
                return c.n;
            }
            """);
    }

    [Fact]
    public void A_mut_fn_of_a_value_needs_something_to_write()
    {
        // M4: on a struct without a 'var' field the word promises a write that cannot happen,
        // and costs every caller a 'var' root for it.
        var errors = Check("""
            struct V { x: int, mut fn poke(): void { } }
            fn main(): int { return 0; }
            """).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        var error = Assert.Single(errors);
        Assert.Equal("LYR-SEM0023", error.Code);
        Assert.Contains("'mut fn poke' has nothing to write", error.Message);

        // Three ways to have something: a 'var' field, 'this' as a whole, a 'mut fn' on 'this'.
        Allowed("""
            struct A { var x: int, mut fn poke(): void { } }
            struct B { x: int, mut fn reset(): void { this = B { x = 0 }; } mut fn again(): void { this.reset(); } }
            fn main(): int { return 0; }
            """);
        // And one to be asked for it: an interface declares the method 'mut'.
        Allowed("""
            interface Source { mut fn next(): int; }
            struct Empty :: [Source] { mut fn next(): int { return 0; } }
            fn main(): int { return 0; }
            """);
        // A class is not asked: it changes through a 'let' reference to an object that does.
        Allowed("""
            class Inner { var n: int, mut fn bump(): void { this.n += 1; } }
            class Outer { inner: Inner, mut fn bump(): void { this.inner.bump(); } }
            fn main(): int { return 0; }
            """);
    }

    [Fact]
    public void A_static_let_is_a_constant()
    {
        var message = Rejected("""
            class C { n: int, static let LIMIT: int = 3; }
            fn main(): int { C.LIMIT = 4; return 0; }
            """);
        Assert.Contains("'static let'", message);
    }

    // ------------------------------------------------- the three ways past the rule

    /// <summary>
    /// <c>++</c> and <c>--</c> write as much as they read, and the walker only ever looked at
    /// assignments: <c>let x = 1; x++;</c> compiled without a word and answered 2.
    /// </summary>
    [Theory]
    [InlineData("x++;")]
    [InlineData("x--;")]
    [InlineData("let y = ++x; return y;")]   // prefix only in expression position: '++x;' as a
    [InlineData("let y = --x; return y;")]   // statement is LYR-SEM0022, which is a rule of its own
    public void Increment_and_decrement_obey_let(string form) =>
        Rejected($$"""
            fn main(): int { let x = 1; {{form}} return x; }
            """);

    [Theory]
    [InlineData("x++;")]
    [InlineData("let y = ++x; return y;")]
    public void Increment_and_decrement_still_work_on_var(string form) =>
        Allowed($$"""
            fn main(): int { var x = 1; {{form}} return x; }
            """);

    /// <summary>
    /// A lambda body is a body. It was reached through the child-expression list, which carries
    /// expressions and cannot carry a block, so the rules never ran inside one at all — and an
    /// assignment to a captured <c>let</c> travelled to the lowering, which has no diagnostic for
    /// it and threw.
    /// </summary>
    [Fact]
    public void A_lambda_body_obeys_let() =>
        Rejected("""
            fn main(): int { let x = 1; let f = (): void => { x = 5; }; f(); return x; }
            """);

    /// <summary>And the same for an expression-bodied lambda, which the list did carry.</summary>
    [Fact]
    public void An_expression_lambda_obeys_let() =>
        Rejected("""
            fn main(): int { let x = 1; let f = (): int => (x = 5); return f(); }
            """);

    /// <summary>
    /// The BLOCK arm of a match EXPRESSION, for the same reason: the list collected the expression
    /// arms only. The statement form was always walked, so the two spellings of one construct
    /// disagreed — and the block arm quietly overwrote the binding at runtime.
    /// </summary>
    [Fact]
    public void A_block_arm_of_a_match_expression_obeys_let() =>
        Rejected("""
            fn main(): int {
                let x = 1;
                let k = 1;
                let v = match (k) { 1 => { x = 9; return x; }, _ => 5 };
                return v;
            }
            """);
}
