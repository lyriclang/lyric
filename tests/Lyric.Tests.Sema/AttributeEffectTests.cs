using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The attributes the compiler acts on (3.3, M24 slice 4).
///
/// <para>Before this there was exactly one: <c>@Deprecated</c>. Everything else was metadata for a
/// host or a tool, which is the Java-annotation shape — a decoration whose meaning depends on
/// whether something out there happens to read it, and which you cannot tell apart from a
/// decoration that means nothing. Drawing the line while there are three is cheaper than drawing
/// it at twenty.</para>
/// </summary>
public class AttributeEffectTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static IReadOnlyList<Diagnostic> Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de.Diagnostics;
    }

    private static Diagnostic Reports(string code, string source)
    {
        var found = Check(source).Where(d => d.Code == code).ToList();
        Assert.NotEmpty(found);
        return found[0];
    }

    private static void AssertSilent(string code, string source) =>
        Assert.DoesNotContain(Check(source), d => d.Code == code);

    // ------------------------------------------------------------------ @MustUse

    private const string Marked = """
        import std.core { MustUse };

        @MustUse
        fn save(): bool { return true; }
        """;

    [Fact]
    public void A_dropped_result_is_reported()
    {
        var d = Reports("LYR-SEM0093", Marked + """

            fn main(): int {
                save();
                return 0;
            }
            """);

        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Contains("the result of 'save' is dropped", d.Message);
    }

    [Fact]
    public void A_bound_result_is_not_a_drop() =>
        AssertSilent("LYR-SEM0093", Marked + """

            fn main(): int {
                let ok = save();
                return if (ok) 0 else 1;
            }
            """);

    [Fact]
    public void Binding_to_underscore_says_the_drop_is_meant() =>
        // The escape hatch, and the reason there is no second one: this already reads as "I know",
        // and it greps.
        AssertSilent("LYR-SEM0093", Marked + """

            fn main(): int {
                let _ = save();
                return 0;
            }
            """);

    [Fact]
    public void A_result_used_in_an_expression_is_not_a_drop() =>
        AssertSilent("LYR-SEM0093", Marked + """

            fn main(): int {
                if (save()) { return 0; }
                return 1;
            }
            """);

    [Fact]
    public void MustUse_on_a_void_function_is_refused()
    {
        // It could never fire there, so it is a claim about the code that is not true of it.
        var d = Reports("LYR-SEM0093", """
            import std.core { MustUse };

            @MustUse
            fn shout(): void { }

            fn main(): int { return 0; }
            """);

        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("needs a result to insist on", d.Message);
    }

    [Fact]
    public void A_home_made_MustUse_is_an_ordinary_attribute() =>
        // Identity, not name — the same nominal rule the operators and '@Deprecated' follow.
        AssertSilent("LYR-SEM0093", """
            import std.core { OnFunction };

            struct MustUse :: [OnFunction] { }

            @MustUse
            fn save(): bool { return true; }

            fn main(): int {
                save();
                return 0;
            }
            """);

    [Fact]
    public void The_standard_library_marks_what_reports_through_its_result()
    {
        // Against the real std rather than a stand-in: 'writeText' answers bool because disks fill
        // up, and a call that ignores it has decided writing cannot fail.
        var d = Reports("LYR-SEM0093", """
            import std.io.file { writeText };

            fn main(): int {
                writeText("out.txt", "hello");
                return 0;
            }
            """);

        Assert.Contains("'writeText'", d.Message);
    }

    // ------------------------------------------------------------------ @Test

    [Fact]
    public void A_test_that_returns_a_value_is_refused()
    {
        // The case that earned this check. 'fn checks(): bool { return false; }' marked '@Test'
        // reported PASS: the runner calls through CallVoid and the answer went nowhere.
        var d = Reports("LYR-SEM0092", """
            import std.test { Test };

            @Test
            fn checks(): bool { return false; }

            fn main(): int { return 0; }
            """);

        Assert.Equal(Severity.Error, d.Severity);
        Assert.Contains("a test returns nothing", d.Message);
    }

    [Fact]
    public void A_test_with_parameters_is_refused() =>
        Assert.Contains("takes no parameters", Reports("LYR-SEM0092", """
            import std.test { Test };

            @Test
            fn checks(n: int): void { }

            fn main(): int { return 0; }
            """).Message);

    [Fact]
    public void An_ordinary_test_checks_clean()
    {
        var diagnostics = Check("""
            import std.test { Test, assertEq };

            @Test
            fn two_and_two(): void { assertEq(2 + 2, 4); }

            fn main(): int { return 0; }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Severity == Severity.Error);
    }

    [Fact]
    public void A_test_without_a_return_type_checks_clean() =>
        // 'fn f() { }' with no return type at all is void; the check reads the resolved type
        // rather than the presence of the annotation.
        AssertSilent("LYR-SEM0092", """
            import std.test { Test };

            @Test
            fn nothing_happens() { }

            fn main(): int { return 0; }
            """);
}
