using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The enum the position names, unnamed (design/v5/spec/03 T9; 08 Y6, Y9): <c>.Red</c>,
/// <c>.Num(3)</c> and <c>.Rect { w = 1 }</c> are members of the type the position expects —
/// an initializer with a written type, an argument, a return, an assignment, either side of
/// <c>==</c>, a pattern. Where no enum is expected, the form is an error that says to name
/// the enum. In a pattern a bare name is a binding, always; one that spells a variant is
/// refused (LYR-SEM0111), and an arm no value reaches is a warning (LYR-SEM0112).
/// </summary>
public class ImplicitMemberTests
{
    private const string Prelude = """
        enum Color { Red, Green, Blue }
        enum Shape { Num(int), Rect { w: int, h: int }, Empty }

        """;

    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string body, bool withStdlib = false)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Prelude + body);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        if (withStdlib)
            comp.ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Allowed(string body, bool withStdlib = false)
    {
        var de = Check(body, withStdlib);
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

    private static List<Diagnostic> Warnings(string body, string code)
    {
        var de = Check(body);
        Assert.False(de.HasErrors,
            string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return de.Diagnostics.Where(d => d.Code == code && d.Severity == Severity.Warning).ToList();
    }

    // ------------------------------------------------------------------ Y9: the expression forms

    [Fact]
    public void A_written_type_names_the_enum() =>
        Allowed("""
            fn main(): int {
                let c: Color = .Red;
                let s: Shape = .Num(3);
                let r: Shape = .Rect { w = 1, h = 2 };
                let o: ?Color = .Blue;
                return 0;
            }
            """);

    [Fact]
    public void An_argument_a_return_and_an_assignment_name_the_enum() =>
        Allowed("""
            fn take(c: Color): int { return 1; }
            fn give(): Color { return .Green; }
            fn main(): int {
                var v = give();
                v = .Blue;
                return take(.Red) + take(v);
            }
            """);

    [Fact]
    public void A_field_default_an_initializer_field_and_an_arm_name_the_enum() =>
        Allowed("""
            struct Scene { light: Color = .Red, var shape: Shape = .Empty }
            fn pick(flag: bool): Color { return if (flag) .Red else .Green; }
            fn main(): int {
                let scene = Scene { light = .Green, shape = .Rect { w = 1, h = 2 } };
                let s: Shape = match (pick(true)) { .Red => .Num(1), _ => .Empty };
                let all: Color[] = [.Red, .Green];
                return match (scene.shape) { .Rect { w, h } => w + h, _ => 0 } + all.length;
            }
            """);

    /// <summary>Equality itself comes from <c>Equatable</c> (02 M10); what the implicit member
    /// adds is the type, from the OTHER side — on either side of the operator.</summary>
    [Fact]
    public void Either_side_of_an_equality_lends_its_type() =>
        Allowed("""
            import std.core { Equatable };
            enum Light :: [Equatable<Light>] {
                On, Off;
                fn equals(other: Light): bool {
                    return match ((this, other)) { (.On, .On) => true, (.Off, .Off) => true, _ => false };
                }
            }
            fn main(): int {
                let l = Light.On;
                return if (l == .On && .Off != l && l != .Off) 1 else 0;
            }
            """, withStdlib: true);

    [Fact]
    public void Without_an_expected_type_the_form_says_to_name_the_enum()
    {
        var message = Rejected("fn main(): int { let a = .Red; return 0; }", "LYR-SEM0113");
        Assert.Contains("expects no particular type; name the enum", message);
    }

    [Fact]
    public void A_position_that_expects_no_enum_is_refused()
    {
        var message = Rejected("fn main(): int { let n: int = .Red; return n; }", "LYR-SEM0113");
        Assert.Contains("expects 'int', which is not an enum", message);
        message = Rejected("fn main(): int { let n = 3; return if (n == .Red) 1 else 0; }", "LYR-SEM0113");
        Assert.Contains("'int'", message);
    }

    [Fact]
    public void A_name_the_enum_does_not_have_is_the_ordinary_member_error() =>
        Assert.Contains("'Purple'", Rejected("fn main(): int { let c: Color = .Purple; return 0; }", "LYR-SEM0012"));

    [Fact]
    public void A_struct_variant_by_dot_wants_its_fields() =>
        Assert.Contains("'h'", Rejected("fn main(): int { let s: Shape = .Rect { w = 1 }; return 0; }", "LYR-SEM0106"));

    [Fact]
    public void A_dotted_initializer_on_a_unit_variant_is_refused() =>
        Assert.Contains("no payload", Rejected("fn main(): int { let c: Color = .Red { x = 1 }; return 0; }", "LYR-SEM0031"));

    // ------------------------------------------------------------------ Y6: the pattern forms

    [Fact]
    public void A_dotted_pattern_tests_the_scrutinees_variant() =>
        Allowed("""
            fn f(c: Color, s: Shape): int {
                let a = match (c) { .Red => 1, .Green => 2, .Blue => 3 };
                let b = match (s) { .Num(n) => n, .Rect { w = 0, h } => h, .Rect { w, h } => w + h, .Empty => 0 };
                return a + b;
            }
            """);

    [Fact]
    public void The_qualified_form_stays_beside_the_dotted_one() =>
        Allowed("fn f(c: Color): int { return match (c) { Color.Red => 1, .Green => 2, Color.Blue => 3 }; }");

    [Fact]
    public void A_dotted_pattern_on_a_value_that_is_no_enum_is_refused() =>
        Rejected("fn f(n: int): int { return match (n) { .Red => 1, _ => 0 }; }", "LYR-SEM0029");

    [Fact]
    public void A_bare_name_that_spells_a_variant_is_refused()
    {
        var message = Rejected("fn f(c: Color): int { return match (c) { Red => 1, _ => 0 }; }", "LYR-SEM0111");
        Assert.Contains("'.Red' or 'Color.Red'", message);
    }

    [Fact]
    public void A_bare_name_that_spells_no_variant_binds_the_whole_value() =>
        Allowed("fn f(c: Color): Color { return match (c) { whole => whole }; }");

    [Fact]
    public void The_witness_of_a_missing_case_is_dotted()
    {
        var de = Check("fn f(s: Shape): int { return match (s) { .Num(_) => 1, .Empty => 0 }; }");
        var d = Assert.Single(de.Diagnostics, x => x.Code == "LYR-SEM0050");
        Assert.Contains("'.Rect { … }'", d.Message);
    }

    // ------------------------------------------------------------------ Y6: unreachable arms

    [Fact]
    public void An_arm_after_one_that_matches_everything_is_unreachable()
    {
        var w = Warnings("fn f(c: Color): int { return match (c) { _ => 1, .Red => 2 }; }", "LYR-SEM0112");
        Assert.Contains("matches every value", Assert.Single(w).Message);
        w = Warnings("fn f(c: Color): int { return match (c) { whole => 1, .Green => 2 }; }", "LYR-SEM0112");
        Assert.Single(w);
    }

    [Fact]
    public void An_arm_that_repeats_a_tested_case_is_unreachable()
    {
        var w = Warnings("fn f(c: Color): int { return match (c) { .Red => 1, .Red => 2, _ => 3 }; }", "LYR-SEM0112");
        Assert.Contains("'.Red' is matched by an arm above", Assert.Single(w).Message);
        w = Warnings("fn f(c: Color): int { return match (c) { Color.Red => 1, .Red => 2, _ => 3 }; }", "LYR-SEM0112");
        Assert.Single(w);
        w = Warnings("fn f(n: int): int { return match (n) { 1 => 1, 1 => 2, _ => 3 }; }", "LYR-SEM0112");
        Assert.Contains("'1'", Assert.Single(w).Message);
        w = Warnings("fn f(o: ?Color): int { return match (o) { null => 0, null => 1, _ => 2 }; }", "LYR-SEM0112");
        Assert.Contains("'null'", Assert.Single(w).Message);
    }

    /// <summary>A guard keeps the case open, and a payload pattern may test different values of
    /// the same variant: neither is a repetition.</summary>
    [Fact]
    public void A_guarded_arm_and_a_payload_arm_keep_the_case_open()
    {
        Assert.Empty(Warnings("fn f(c: Color, n: int): int { return match (c) { .Red if n > 1 => 1, .Red => 2, _ => 3 }; }", "LYR-SEM0112"));
        Assert.Empty(Warnings("fn f(s: Shape): int { return match (s) { .Num(1) => 1, .Num(n) => n, _ => 3 }; }", "LYR-SEM0112"));
    }
}
