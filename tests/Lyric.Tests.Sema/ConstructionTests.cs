using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// How a value of a class or a struct comes to be, and what identity it has, in Lyric 5
/// (design/v5/spec/02 M10, M14; 04 D11; 08 Y9): the initializer is the primitive, a factory is
/// an ordinary <c>static fn new</c> that a type name in call position means, an omitted
/// <c>?T</c> field is <c>null</c>, a struct is replaced as a whole through <c>this</c> in a
/// <c>mut fn</c>, and identity is the function <c>same</c> on references — never <c>==</c>,
/// and never asked of a value.
/// </summary>
public class ConstructionTests
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

    private static string Rejected(string source, string code)
    {
        var errors = Check(source).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    // ------------------------------------------------------------------ Y9: Point(1, 2)

    [Fact]
    public void A_type_name_in_call_position_is_its_factory() =>
        Allowed("""
            struct Point {
                x: int, y: int,
                static fn new(x: int, y: int): Point { return Point { x = x, y = y }; }
            }
            class Account {
                owner: string, var balance: int,
                static fn new(owner: string): Account { return Account { owner = owner, balance = 0 }; }
            }
            fn main(): int {
                let p = Point(1, 2);
                let a = Account("alice");
                return p.x + p.y + a.balance;
            }
            """);

    [Fact]
    public void The_factory_is_checked_as_the_call_it_stands_for()
    {
        const string point = "struct Point { x: int, y: int, static fn new(x: int, y: int): Point { return Point { x = x, y = y }; } }\n";
        Rejected(point + "fn main(): int { let p = Point(1); return p.x; }", "LYR-SEM0014");
        Rejected(point + "fn main(): int { let p = Point(1, \"two\"); return p.x; }", "LYR-SEM0001");
        // What 'new' answers is what the call answers: a factory may return anything.
        Allowed("""
            class Pool { n: int, static fn new(): int { return 7; } }
            fn main(): int { return Pool(); }
            """);
    }

    [Fact]
    public void A_type_without_new_names_the_initializer()
    {
        var message = Rejected("class P { hp: int }\nfn main(): int { let p = P(1); return p.hp; }", "LYR-SEM0013");
        Assert.Contains("declares no 'static fn new'", message);
        Assert.Contains("'P { … }'", message);
        // An instance method called 'new' is not the factory.
        Rejected("class P { hp: int, fn new(): int { return 1; } }\nfn main(): int { let p = P(); return 0; }", "LYR-SEM0013");
    }

    [Fact]
    public void A_local_of_the_name_shadows_the_type() =>
        Allowed("""
            struct P { x: int, }
            fn main(): int { let P = (n: int): int => n + 1; return P(1); }
            """);

    // ------------------------------------------------------------------ M14 I1: '?T' is null

    [Fact]
    public void An_omitted_optional_field_is_null()
    {
        Allowed("""
            class Node { value: int, var next: ?Node }
            fn main(): int { let n = Node { value = 1 }; return if (n.next == null) 0 else 1; }
            """);
        // Everything else still needs its value, and the message names all that is missing.
        var message = Rejected("""
            class Node { value: int, label: string, var next: ?Node }
            fn main(): int { let n = Node { }; return 0; }
            """, "LYR-SEM0106");
        Assert.Contains("'value', 'label'", message);
        Assert.DoesNotContain("next", message);
    }

    // ------------------------------------------------------------------ M4: this = value

    [Fact]
    public void A_struct_is_replaced_whole_through_this_in_a_mut_fn()
    {
        Allowed("""
            struct V { x: int, mut fn reset(): void { this = V { x = 0 }; } }
            fn main(): int { var v = V { x = 5 }; v.reset(); return v.x; }
            """);
        var message = Rejected("""
            struct V { x: int, fn reset(): void { this = V { x = 0 }; } }
            fn main(): int { return 0; }
            """, "LYR-SEM0019");
        Assert.Contains("not declared 'mut fn'", message);
        // A class's 'this' is the reference the caller holds.
        Rejected("""
            class C { n: int, mut fn reset(): void { this = C { n = 0 }; } }
            fn main(): int { return 0; }
            """, "LYR-SEM0019");
    }

    // ------------------------------------------------------------------ M10: identity

    [Fact]
    public void Same_answers_identity_for_references() =>
        Allowed("""
            class C { n: int }
            fn main(): int {
                let a = C { n = 1 };
                let b = a;
                let xs = [1, 2];
                let ok = same(a, b) && !same(a, C { n = 1 }) && same(xs, xs);
                return if (ok) 0 else 1;
            }
            """);

    [Fact]
    public void A_value_has_no_identity()
    {
        var message = Rejected("""
            struct V { x: int, }
            fn main(): int { let a = V { x = 1 }; return if (same(a, a)) 0 else 1; }
            """, "LYR-SEM0003");
        Assert.Contains("a value, which has no identity", message);
        Rejected("fn main(): int { return if (same(1, 1)) 0 else 1; }", "LYR-SEM0003");
        Rejected("fn main(): int { return if (same(\"a\", \"a\")) 0 else 1; }", "LYR-SEM0003");
    }

    [Fact]
    public void Same_takes_two_references_of_one_type()
    {
        const string types = "class C { n: int }\nclass D { n: int }\n";
        Rejected(types + "fn main(): int { return if (same(C { n = 1 }, D { n = 1 })) 0 else 1; }", "LYR-SEM0003");
        Rejected(types + "fn main(): int { return if (same(C { n = 1 })) 0 else 1; }", "LYR-SEM0014");
    }

    [Fact]
    public void A_function_of_the_name_shadows_the_builtin() =>
        Allowed("""
            fn same(a: int, b: int): bool { return a == b; }
            fn main(): int { return if (same(1, 1)) 0 else 1; }
            """);

    [Fact]
    public void Equality_is_not_identity()
    {
        // '==' means value equality through 'Equatable' and nothing else: a class without it
        // has no '=='. Never silent identity.
        var de = Check("""
            class C { n: int }
            fn main(): int { let a = C { n = 1 }; return if (a == a) 0 else 1; }
            """);
        Assert.True(de.HasErrors);
    }
}
