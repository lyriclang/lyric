using System.Runtime.CompilerServices;
using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A field belongs to its module unless it says <c>pub</c> (3.3, M24 slice 3).
///
/// <para>Until now fields were the one member kind with no visibility at all: types, globals,
/// functions and static bindings all took <c>pub</c>, fields took nothing and were readable and
/// WRITABLE from everywhere. So no type could hold an invariant — <c>Random { state = 99 }</c>
/// compiled in any module, and 0 is a fixed point of xorshift that the constructor goes out of its
/// way to avoid.</para>
///
/// <para>The unit is the MODULE. A module is what somebody writes and reviews as one thing, and
/// putting a helper beside a type is the ordinary way to build one.</para>
/// </summary>
public class FieldVisibilityTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static IReadOnlyList<Diagnostic> Check(string sdk, string main)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var stdlib = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de);

        var comp = new Compilation(sm, de)
        {
            ModuleLoader = path => path is ["sdk"]
                ? new LoadedModule(
                    new Parser(sm, sm.AddVirtual("sdk.lyr", sdk), de).ParseModule(),
                    IsNative: false,
                    new DocumentationTable())
                : stdlib(path),
        };

        comp.AddModule(new Parser(sm, sm.AddVirtual("main.lyr", main), de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de.Diagnostics;
    }

    private static Diagnostic Hidden(string sdk, string main)
    {
        var found = Check(sdk, main).Where(d => d.Code == "LYR-SEM0091").ToList();
        Assert.NotEmpty(found);
        return found[0];
    }

    private static void AssertReachable(string sdk, string main)
    {
        var diagnostics = Check(sdk, main);
        Assert.DoesNotContain(diagnostics, d => d.Code == "LYR-SEM0091");
        Assert.DoesNotContain(diagnostics, d => d.Severity == Severity.Error);
    }

    private const string Sdk = """
        module sdk;

        pub struct Point {
            pub x: int,
            y: int,
        }

        pub fn origin(): Point { return Point { x = 0, y = 0 }; }
        pub fn depth(p: Point): int { return p.y; }
        """;

    // ------------------------------------------------------------------ what is out of reach

    [Fact]
    public void Reading_a_field_without_pub_from_another_module_is_reported()
    {
        var d = Hidden(Sdk, """
            module main;
            import sdk { Point, origin };

            fn main(): int { return origin().y; }
            """);

        Assert.Contains("'y' is not public", d.Message);
        Assert.Contains("it belongs to 'sdk'", d.Message);
    }

    [Fact]
    public void Writing_one_in_a_literal_is_reported()
    {
        // The half the wall exists for: this is how a foreign module fabricates an instance the
        // type itself would never have produced.
        var d = Hidden(Sdk, """
            module main;
            import sdk { Point };

            fn main(): int {
                let p = Point { x = 1, y = 2 };
                return 0;
            }
            """);

        Assert.Contains("'y' is not public", d.Message);
    }

    [Fact]
    public void Assigning_to_one_is_reported()
    {
        Assert.Contains("'y' is not public", Hidden(Sdk, """
            module main;
            import sdk { Point, origin };

            fn main(): int {
                var p = origin();
                p.y = 5;
                return 0;
            }
            """).Message);
    }

    [Fact]
    public void The_report_is_a_warning_while_the_toolchain_is_a_three() =>
        Assert.Equal(Severity.Warning, Hidden(Sdk, """
            module main;
            import sdk { Point, origin };

            fn main(): int { return origin().y; }
            """).Severity);

    // ------------------------------------------------------------------ what stays reachable

    [Fact]
    public void A_pub_field_is_reachable_from_anywhere() =>
        AssertReachable(Sdk, """
            module main;
            import sdk { Point, origin };

            fn main(): int { return origin().x; }
            """);

    [Fact]
    public void The_declaring_module_reaches_its_own_fields() =>
        // 'depth' and 'origin' in the SDK touch 'y' freely; the unit is the module, so a helper
        // beside the type needs no permission.
        AssertReachable(Sdk, """
            module main;
            import sdk { Point, origin, depth };

            fn main(): int { return depth(origin()); }
            """);

    [Fact]
    public void A_method_is_governed_by_its_own_pub_as_before() =>
        AssertReachable("""
            module sdk;

            pub struct Point {
                x: int,
                pub fn sum(): int { return this.x; }
            }

            pub fn origin(): Point { return Point { x = 0 }; }
            """, """
            module main;
            import sdk { Point, origin };

            fn main(): int { return origin().sum(); }
            """);

    [Fact]
    public void An_attribute_argument_is_not_a_field_reach()
    {
        // Deliberate: an attribute struct exists to be written from somewhere else. Requiring
        // 'pub' there would put the keyword on every field of every attribute ever written, where
        // it could never mean anything else — and std.core's own '@Deprecated' would need it.
        AssertReachable("""
            module sdk;
            import std.core { OnFunction };

            pub struct Tag :: [OnFunction] { order: int = 0 }
            """, """
            module main;
            import sdk { Tag };

            @Tag { order = 3 }
            fn tagged(): int { return 1; }

            fn main(): int { return tagged(); }
            """);
    }

    [Fact]
    public void An_enum_variant_payload_stays_matchable_across_modules() =>
        // A variant's fields are what 'match' reads. Private ones would make an enum unusable
        // outside its module, so the payload carries no visibility and takes no 'pub'.
        AssertReachable("""
            module sdk;

            pub enum Shape {
                Circle { radius: int },
                Empty,
            }

            pub fn unit(): Shape { return Shape.Circle { radius = 1 }; }
            """, """
            module main;
            import sdk { Shape, unit };

            fn main(): int {
                return match (unit()) {
                    Shape.Circle { radius } => radius,
                    Shape.Empty => 0,
                };
            }
            """);
}
