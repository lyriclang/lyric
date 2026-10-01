using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Named arguments and defaults per call (design/v5/spec/04 D5): every parameter is nameable,
/// positional before named, a default is evaluated at the call in the callee's scope and may
/// read the parameters before it; names decide nothing about which function is called — the
/// count does (D4).
/// </summary>
public class NamedArgumentTests
{
    private const string Prelude = """
        fn connect(host: int, port: int = 80, retries: int = 3): int { return host + port + retries; }
        fn span(from: int, to: int = from + 10, step: int = to - from): int { return step; }
        fn sum(base: int, params xs: int[]): int { return base; }
        fn of(hex: int): int { return hex; }
        fn of(r: int, g: int, b: int): int { return r + g + b; }

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
    public void Every_parameter_is_nameable_in_any_order() =>
        Allowed("fn f(): int { return connect(host: 1, port: 0, retries: 0) + connect(retries: 0, host: 2, port: 0); }");

    [Fact]
    public void Positional_arguments_come_first_and_named_ones_fill_the_rest() =>
        Allowed("fn f(): int { return connect(3, retries: 0, port: 0) + connect(3, retries: 0); }");

    [Fact]
    public void A_middle_default_is_skipped_by_name() =>
        Allowed("fn f(): int { return span(5, step: 3); }");

    [Fact]
    public void A_default_may_read_the_parameters_before_it() =>
        Allowed("fn f(): int { return span(5) + span(5, 7); }");

    [Fact]
    public void A_default_may_stand_at_any_position() =>
        // 4.x refused a required parameter after a defaulted one (LYR-SEM0025); with names, the
        // middle default is skippable, so the position is free (D5 F3).
        Allowed("fn tag(prefix: string = \"#\", n: int): string { return prefix; }\nfn f(): string { return tag(n: 1) + tag(\"-\", 2); }");

    [Fact]
    public void A_default_may_not_read_this() =>
        Assert.Contains("'this'", Rejected(
            "struct S { n: int, fn f(x: int = this.n): int { return x; } }\nfn f(): int { return S { n = 2 }.f(); }",
            "LYR-SEM0120"));

    [Fact]
    public void A_default_may_not_read_a_later_parameter() =>
        Rejected("fn later(a: int = b, b: int): int { return a; }\nfn f(): int { return later(b: 1); }", "LYR-SEM0002");

    [Fact]
    public void A_positional_argument_after_a_named_one_is_refused() =>
        Assert.Contains("positional", Rejected("fn f(): int { return connect(host: 1, 2); }", "LYR-SEM0119"));

    [Fact]
    public void An_unknown_name_lists_the_parameters()
    {
        var message = Rejected("fn f(): int { return connect(1, prot: 2); }", "LYR-SEM0119");
        Assert.Contains("'prot'", message);
        Assert.Contains("'port'", message);
    }

    [Fact]
    public void A_name_already_set_positionally_is_refused() =>
        Assert.Contains("position 1", Rejected("fn f(): int { return connect(1, host: 2); }", "LYR-SEM0119"));

    [Fact]
    public void A_name_given_twice_is_refused() =>
        Assert.Contains("twice", Rejected("fn f(): int { return connect(1, port: 2, port: 3); }", "LYR-SEM0119"));

    [Fact]
    public void The_params_parameter_is_not_named() =>
        Assert.Contains("params", Rejected("fn f(): int { return sum(1, xs: [2]); }", "LYR-SEM0119"));

    [Fact]
    public void A_parameter_without_a_default_left_out_by_a_named_call_is_named() =>
        Assert.Contains("'host'", Rejected("fn f(): int { return connect(port: 1); }", "LYR-SEM0014"));

    [Fact]
    public void Names_do_not_choose_the_overload_the_count_does() =>
        Allowed("fn f(): int { return of(hex: 7) + of(r: 1, g: 2, b: 3) + of(1, 2, 3); }");

    [Fact]
    public void A_function_value_takes_no_names() =>
        Rejected("fn f(g: fn(int) -> int): int { return g(x: 1); }", "LYR-SEM0119");
}
