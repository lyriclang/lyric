using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The four MIGRATION WARNINGS (§12.5): places where a program that compiles today means
/// something else in 5.0.
///
/// <para>The maintainer's decision of 2026-09-24 is what these are: the four open rule questions
/// are settled together with the major rather than one release at a time, and until then only a
/// warning is produced. Each of them therefore marks a place WITHOUT presuming which way the
/// question goes — a warning that fired only under one candidate answer would BE that answer,
/// taken quietly.</para>
///
/// <para>THE CONTROLS ARE HALF OF THIS FILE, and they are the part that makes the warnings worth
/// having. A warning nobody can switch off has to be right about a program that is NOT going to
/// change, or it teaches its readers to ignore it. Measured over the corpus before the codes were
/// written: across every file under <c>examples/</c>, <c>stdlib/</c> and <c>stdlib-tests/</c> the
/// four together fired <b>once</b> — on <c>examples/objects.lyr</c>, whose <c>advance()</c> writes
/// its receiver without <c>mut</c>, which is exactly the hole SEM0108 is about.</para>
/// </summary>
public class MigrationWarningTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string source, bool withStdlib = false)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        if (withStdlib)
            comp.ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static Diagnostic? Warning(DiagnosticEngine de, string code) =>
        de.Diagnostics.Where(d => d.Code == code).Select(d => (Diagnostic?)d).FirstOrDefault();

    private static string Codes(DiagnosticEngine de) =>
        string.Join(", ", de.Diagnostics.Select(d => $"{d.Code}({d.Severity})"));

    /// <summary>
    /// Every one of the four is a WARNING, names 5.0, and carries the note that places it in the
    /// family. Asserted once for all of them rather than four times: the property belongs to the
    /// family, not to any member.
    /// </summary>
    [Theory]
    [InlineData("LYR-SEM0107", "fn main(): int { let x = 1; let x = 2; return x; }")]
    [InlineData("LYR-SEM0108",
        "class C { n: int, fn w(): void { this.n = 1; } }\nfn main(): int { let c = C { n = 0 }; c.w(); return c.n; }")]
    [InlineData("LYR-SEM0109",
        "struct P { x: int }\nfn main(): int { let p = P { x = 1 }; p.x = 2; return p.x; }")]
    [InlineData("LYR-SEM0110",
        """
        class B :: [Throwable] { n: int, fn message(): string { return "b"; } }
        fn c(): void throws B { throw B { n = 1 }; }
        fn w(): void throws B { defer c(); }
        fn main(): int { try { w(); } catch (_: B) { return 0; } return 1; }
        """)]
    public void Each_one_is_a_warning_that_names_the_version(string code, string source)
    {
        var de = Check(source);

        var diagnostic = Warning(de, code);
        Assert.True(diagnostic is not null, $"{code} did not fire; diagnostics were {Codes(de)}");
        Assert.Equal(Severity.Warning, diagnostic!.Value.Severity);
        Assert.Contains("5.0", diagnostic.Value.Message, StringComparison.Ordinal);
        Assert.Contains(diagnostic.Value.Notes ?? [],
            n => n.Message.Contains("migration warning", StringComparison.Ordinal));
    }

    /// <summary>None of them refuses anything: the program still compiles.</summary>
    [Fact]
    public void A_migration_warning_refuses_nothing()
    {
        var de = Check("fn main(): int { let x = 1; let x = 2; return x; }");

        Assert.False(de.HasErrors, Codes(de));
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0107");
    }

    // ------------------------------------------------------------------ SEM0107

    /// <summary>The note points at the binding that WINS, which is the one a reader is looking
    /// for.</summary>
    [Fact]
    public void The_rebinding_warning_points_at_the_first_binding()
    {
        var de = Check("fn main(): int { let x = 1; let x = 2; return x; }");

        var diagnostic = Warning(de, "LYR-SEM0107");
        Assert.Contains(diagnostic!.Value.Notes ?? [],
            n => n.Message.Contains("previous binding", StringComparison.Ordinal));
    }

    /// <summary>
    /// It fires even when the second binding changes the TYPE, which is the case where today's
    /// behaviour is hardest to guess: <c>return x</c> goes on checking as an <c>int</c>.
    /// </summary>
    [Fact]
    public void A_rebinding_at_another_type_warns_too()
    {
        var de = Check("""
            fn main(): int {
                let x = 1;
                let x = "two";
                return x;
            }
            """);

        Assert.NotNull(Warning(de, "LYR-SEM0107"));
        Assert.False(de.HasErrors, Codes(de));
    }

    /// <summary>
    /// THE CONTROL THAT MATTERS: shadowing an ENCLOSING scope is a different thing and stays
    /// legal. A check that looked up the name instead of trying to declare it would fire here,
    /// and would fire on most programs.
    /// </summary>
    [Fact]
    public void Shadowing_an_enclosing_scope_does_not_warn()
    {
        var de = Check("""
            fn main(): int {
                let x = 1;
                if (x > 0) {
                    let x = 2;
                    return x;
                }
                return x;
            }
            """);

        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0107");
    }

    /// <summary>
    /// The "never used" warning that used to stand beside it is gone.
    ///
    /// <para>It was true and described the situation backwards: the binding is not unused because
    /// its author forgot it, it is unreachable because an earlier one of the same name wins. Two
    /// warnings for one fact, and the less informative one read like an accusation.</para>
    /// </summary>
    [Fact]
    public void The_rebinding_warning_replaces_the_unused_one()
    {
        var de = Check("fn main(): int { let x = 1; let x = 2; return x; }");

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0107");
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0071");
    }

    // ------------------------------------------------------------------ SEM0108

    [Fact]
    public void A_mut_class_method_does_not_warn()
    {
        var de = Check("""
            class C { n: int, mut fn w(): void { this.n = 1; } }
            fn main(): int { let c = C { n = 0 }; c.w(); return c.n; }
            """);

        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0108");
    }

    /// <summary>A method that only READS its receiver is not a place anything moves.</summary>
    [Fact]
    public void A_method_that_only_reads_does_not_warn()
    {
        var de = Check("""
            class C { n: int, fn get(): int { return this.n; } }
            fn main(): int { let c = C { n = 1 }; return c.get(); }
            """);

        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0108");
    }

    /// <summary>
    /// A STRUCT keeps its error. The warning is about the class half; turning the struct's
    /// <c>LYR-SEM0019</c> into a warning would be a change nobody asked for.
    /// </summary>
    [Fact]
    public void A_non_mut_struct_method_is_still_an_error()
    {
        var de = Check("""
            struct S { n: int, fn w(): void { this.n = 1; } }
            fn main(): int { let s = S { n = 0 }; return s.n; }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0019");
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0108");
    }

    /// <summary>
    /// An increment is a write. <c>this.n++</c> in a non-<c>mut</c> class method is the same hole
    /// written shorter, and a check hung off the assignment alone would miss it.
    /// </summary>
    [Fact]
    public void An_increment_of_this_warns_as_well()
    {
        var de = Check("""
            class C { n: int, fn w(): void { this.n++; } }
            fn main(): int { let c = C { n = 0 }; c.w(); return c.n; }
            """);

        Assert.NotNull(Warning(de, "LYR-SEM0108"));
    }

    /// <summary>And it is reported ONCE, not once per predicate call.</summary>
    [Fact]
    public void An_increment_warns_exactly_once()
    {
        var de = Check("""
            class C { n: int, fn w(): void { this.n++; } }
            fn main(): int { let c = C { n = 0 }; c.w(); return c.n; }
            """);

        Assert.Single(de.Diagnostics.Where(d => d.Code == "LYR-SEM0108"));
    }

    // ------------------------------------------------------------------ SEM0109

    [Fact]
    public void A_var_struct_binding_does_not_warn()
    {
        var de = Check("""
            struct P { x: int }
            fn main(): int { var p = P { x = 1 }; p.x = 2; return p.x; }
            """);

        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0109");
    }

    /// <summary>A parameter is an immutable binding by the same rule, and §3.4a says so.</summary>
    [Fact]
    public void A_struct_parameter_warns_too()
    {
        var de = Check("""
            struct P { x: int }
            fn set(p: P): int { p.x = 9; return p.x; }
            fn main(): int { let p = P { x = 1 }; return set(p); }
            """);

        Assert.NotNull(Warning(de, "LYR-SEM0109"));
    }

    /// <summary>
    /// A CLASS through a <c>let</c> does not warn. That `let` pins the reference and not the
    /// object is the intended reading (§7.1) and no candidate answer changes it — warning here
    /// would fire on most programs that use a class at all.
    /// </summary>
    [Fact]
    public void A_class_through_a_let_does_not_warn()
    {
        var de = Check("""
            class C { n: int }
            fn main(): int { let c = C { n = 1 }; c.n = 2; return c.n; }
            """);

        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0109");
    }

    // ------------------------------------------------------------------ SEM0110

    [Fact]
    public void A_defer_that_cannot_throw_does_not_warn()
    {
        var de = Check("""
            class C { n: int, mut fn bump(): void { this.n = this.n + 1; } }
            fn work(c: C): void { defer c.bump(); }
            fn main(): int { let c = C { n = 0 }; work(c); return c.n; }
            """);

        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0110");
    }

    /// <summary>
    /// A throwing call OUTSIDE a defer does not warn either: the question is about the chain, not
    /// about throwing.
    /// </summary>
    [Fact]
    public void A_throwing_call_outside_a_defer_does_not_warn()
    {
        var de = Check("""
            class B :: [Throwable] { n: int, fn message(): string { return "b"; } }
            fn c(): void throws B { throw B { n = 1 }; }
            fn main(): int { try { c(); } catch (_: B) { return 0; } return 1; }
            """);

        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0110");
    }

    /// <summary>A <c>throw</c> written directly in the defer body is the same case.</summary>
    [Fact]
    public void A_throw_in_the_defer_body_warns()
    {
        var de = Check("""
            class B :: [Throwable] { n: int, fn message(): string { return "b"; } }
            fn w(): void throws B { defer throw B { n = 1 }; }
            fn main(): int { try { w(); } catch (_: B) { return 0; } return 1; }
            """);

        Assert.NotNull(Warning(de, "LYR-SEM0110"));
    }
}
