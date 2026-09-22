using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// An f-string hole renders a non-scalar value through <c>Display</c> (§6.6): <c>{p}</c> means
/// <c>{p.show()}</c> when the type conforms, exactly the question <c>println(p)</c> asks. What does
/// not conform — an optional, an array, a tuple, a struct without the conformance — is refused in
/// the sema with the conformance named, rather than accepted and failed in the lowering.
/// </summary>
public class InterpolationDisplayTests
{
    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static DiagnosticEngine Check(string source)
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
        return de;
    }

    private const string Point =
        """
        import std.core { Display };
        struct P :: [Display] {
            x: int,
            fn show(): string { return f"P{this.x}"; }
        }

        """;

    [Fact]
    public void A_conforming_struct_renders_in_a_hole()
    {
        var de = Check(Point +
            """
            fn main(): int {
                let s = f"{P { x = 1 }}!";
                return 0;
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }

    [Fact]
    public void A_constrained_type_parameter_renders_through_its_constraint()
    {
        var de = Check(Point +
            """
            fn tag<T :: [Display]>(v: T): string { return f"<{v}>"; }
            fn main(): int { return 0; }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }

    [Fact]
    public void An_interface_value_renders_through_its_vtable()
    {
        var de = Check(Point +
            """
            fn main(): int {
                let d: Display = P { x = 1 };
                let s = f"{d}";
                return 0;
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }

    [Fact]
    public void A_struct_without_the_conformance_is_refused_by_name()
    {
        var de = Check(
            """
            struct Q { x: int }
            fn main(): int {
                let s = f"{Q { x = 1 }}";
                return 0;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0006");
        Assert.Contains("Display", error.Message);
    }

    [Theory]
    [InlineData("let v: ?int = 1;")]
    [InlineData("let v = [1, 2];")]
    [InlineData("let v = (1, 2);")]
    public void An_optional_array_or_tuple_is_refused_in_the_sema(string binding)
    {
        var de = Check("fn main(): int { " + binding + " let s = f\"{v}\"; return 0; }");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0006");
    }

    [Fact]
    public void A_format_specifier_on_a_display_value_is_refused()
    {
        var de = Check(Point +
            """
            fn main(): int {
                let s = f"{P { x = 1 }:N2}";
                return 0;
            }
            """);
        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0006");
        Assert.Contains("specifier", error.Message);
    }

    [Fact]
    public void Scalars_keep_their_converters()
    {
        var de = Check(
            """
            fn main(): int {
                let s = f"{1} {2.5} {true} {'c'} {"s"} {1:N2}";
                return 0;
            }
            """);
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }
}
