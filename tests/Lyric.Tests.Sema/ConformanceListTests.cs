using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Xunit;

namespace Lyric.Tests.Sema;

/// <summary>
/// What may stand in a <c>::</c> list, and what <c>@Deprecated</c> means on an interface member.
///
/// <para>Both were gaps of the same kind: something written in the source that the compiler
/// looked past. A non-interface in a conformance list was SKIPPED — the declaration claimed a
/// conformance nobody checked and nobody reported, which is the quietest way for a mistake to
/// survive a compiler. An attribute on an interface member was refused outright, because the
/// question "do implementations inherit the clock?" had no answer.</para>
/// </summary>
public class ConformanceListTests
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

    // ------------------------------------------------------------------ the conformance list

    [Theory]
    [InlineData("pub struct S :: [Vec2] { n: int }")]
    [InlineData("pub class C :: [Vec2] { n: int = 0 }")]
    [InlineData("pub enum E :: [Vec2] { One, Two }")]
    [InlineData("pub extend Vec2 :: [Vec2] { pub fn f(): int { return 1; } }")]
    public void A_conformance_list_takes_interfaces_only(string declaration)
    {
        var de = Check("pub struct Vec2 { x: float, y: float }\n\n" + declaration
                       + "\n\nfn main(): int {\n    return 0;\n}\n");

        var error = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0078");
        Assert.Equal(Severity.Error, error.Severity);
        Assert.Contains("Vec2", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_name_stays_the_resolvers_error_alone()
    {
        // Two reports about one word would be two problems where there is one, and the resolver
        // already named it.
        var de = Check("pub struct S :: [NoSuchThing] { n: int }\n\n"
                       + "fn main(): int {\n    return 0;\n}\n");

        Assert.True(de.HasErrors);
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0078");
    }

    [Fact]
    public void An_interface_list_that_is_right_stays_silent()
    {
        var de = Check("""
            import std.core { Display };

            pub struct Point :: [Display] {
                x: int,
                pub fn show(): string {
                    return "p";
                }
            }

            fn main(): int {
                return 0;
            }
            """);

        Assert.False(de.HasErrors);
    }

    // ------------------------------------------------------------------ @Deprecated on a member

    private const string Interface = """
        import std.core { Deprecated };

        pub interface Shape {
            @Deprecated { message = "use area2" }
            fn area(): float;

            fn area2(): float;
        }

        pub class Circle :: [Shape] {
            r: float = 1.0,
            pub fn area(): float {
                return 3.0;
            }
            pub fn area2(): float {
                return 3.0;
            }
        }

        """;

    [Fact]
    public void A_use_through_the_interface_warns()
    {
        var de = Check(Interface + """
            fn viaInterface(s: Shape): float {
                return s.area();
            }

            fn main(): int {
                return if (viaInterface(Circle { }) > 0.0) 0 else 1;
            }
            """);

        Assert.False(de.HasErrors);
        var warning = Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0076");
        Assert.Contains("use area2", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Implementing_it_does_not_warn_and_neither_does_calling_the_concrete_method()
    {
        // The answer to the question that kept this refused. An implementation is not a use, and
        // a conforming type MUST implement what the interface requires — a warning there would be
        // one nobody can act on without breaking conformance. The concrete method is its own
        // declaration, and its author may deprecate it separately or not at all.
        var de = Check(Interface + """
            fn main(): int {
                let c = Circle { };
                return if (c.area() > 0.0) 0 else 1;
            }
            """);

        Assert.False(de.HasErrors);
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0076");
    }

    [Fact]
    public void Only_Deprecated_may_sit_on_an_interface_member()
    {
        // The member rule is unchanged: the module format has no member rows, so an attribute
        // that would need one has nowhere to land. Opening the parser did not open that.
        var de = Check("""
            import std.core { OnFunction };

            pub struct Marker :: [OnFunction] { n: int = 0 }

            pub interface Shape {
                @Marker
                fn area(): float;
            }

            fn main(): int {
                return 0;
            }
            """);

        Assert.True(de.HasErrors);
        Assert.Single(de.Diagnostics, d => d.Code == "LYR-SEM0065");
    }

    [Fact]
    public void A_deprecated_member_calling_another_one_does_not_warn()
    {
        // Inside anything itself deprecated nothing warns — the one place allowed not to care,
        // and it has to hold for an interface member like it holds for a function.
        var de = Check("""
            import std.core { Deprecated };

            pub interface Shape {
                @Deprecated { message = "use area2" }
                fn area(): float;

                @Deprecated { message = "use area2" }
                fn old(): float {
                    return this.area();
                }

                fn area2(): float;
            }

            fn main(): int {
                return 0;
            }
            """);

        Assert.False(de.HasErrors);
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0076");
    }
}
