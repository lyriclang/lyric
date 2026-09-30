using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The lambda forms of Lyric 5 that the 4.x front end did not have (design/v5/spec/08 Y11):
/// a trailing block with its own parameters (F2), a method bound to its object and a static
/// function as values (F10). The three older forms and the implicit <c>it</c> have their pins
/// in <see cref="LambdaTests"/>.
/// </summary>
public class LambdaFormTests
{
    private const string Prelude = """
        fn fold(xs: int[], seed: int, f: fn(int, int) -> int): int { return f(seed, xs[0]); }
        fn eachPair(ps: (int, int)[], f: fn((int, int)) -> void): void { f(ps[0]); }
        fn apply(f: fn(int) -> int, x: int): int { return f(x); }
        class Counter {
            var n: int,
            mut fn bump(): int { this.n += 1; return this.n; }
            fn scaled(k: int): int { return this.n * k; }
            static fn make(): Counter { return Counter { n = 0 }; }
        }

        """;

    private static DiagnosticEngine Check(string body)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Prelude + body);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Allowed(string body)
    {
        var de = Check(body);
        Assert.False(de.HasErrors,
            string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static string Rejected(string body, string code)
    {
        var errors = Check(body).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0].Message;
    }

    [Fact]
    public void A_trailing_block_names_its_parameters() =>
        Allowed("""
            fn f(xs: int[]): int {
                let a = fold(xs, 0) { acc, x => acc + x };
                let b = fold(xs, 1) { acc, x =>
                    let y = acc * x;
                    y + 1
                };
                return a + b;
            }
            """);

    [Fact]
    public void A_trailing_block_takes_a_pattern_parameter() =>
        Allowed("""
            fn f(): int {
                var dot = 0;
                eachPair([(1, 2)]) { (a, b) => dot += a * b; }
                return dot;
            }
            """);

    [Fact]
    public void The_parameters_come_from_the_context_and_count()
    {
        // Three parameters against a context of two: no context fits, so every parameter is
        // asked for its type — one message per parameter, as for the parenthesized form.
        var errors = Check("fn f(xs: int[]): int { return fold(xs, 0) { acc, x, extra => acc }; }")
            .Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.NotEmpty(errors);
        Assert.All(errors, d => Assert.Equal("LYR-SEM0045", d.Code));
        Assert.Contains(errors, d => d.Message.Contains("'extra' needs a type annotation"));
    }

    [Fact]
    public void A_method_bound_to_its_object_is_a_function_value() =>
        Allowed("""
            fn f(): int {
                let c = Counter.make();
                let bump: fn() -> int = c.bump;
                let scaled = c.scaled;
                return bump() + apply(scaled, 2);
            }
            """);

    [Fact]
    public void A_static_function_is_a_function_value() =>
        Allowed("fn f(): int { let make: fn() -> Counter = Counter.make; return make().n; }");

    [Fact]
    public void A_bound_method_keeps_its_signature() =>
        Rejected("fn f(): int { let c = Counter.make(); let s: fn() -> int = c.scaled; return s(); }", "LYR-SEM0001");
}
