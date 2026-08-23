using System.Runtime.CompilerServices;
using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The wall around an opaque alias, and who has the door (3.3, M24 slice 2).
///
/// <para>What this closes: <c>docs/Grammar.md</c> has said since v1.15 that an opaque alias is how
/// a handle crosses the host boundary "while scripts cannot forge one", and that was not true —
/// <c>3 as Ticket</c> compiled in any module that could name <c>Ticket</c>. The wall stopped
/// implicit conversion, arithmetic, ordering and constraints; it did not stop the deliberate
/// reach, which is the only one an attacker makes.</para>
///
/// <para>Every test here needs TWO modules, because the rule is about the line between them. One
/// module is the SDK that declares the handle, the other is the script that holds it.</para>
/// </summary>
public class OpaqueWallTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    /// <summary>Checks a program of several modules. <paramref name="sdk"/> is reachable as
    /// <c>import sdk;</c>, and the standard library behind it.</summary>
    private static IReadOnlyList<Diagnostic> Check(string sdk, string main)
    {
        var sm = new SourceManager();
        var de = new DiagnosticEngine(sm);
        var stdlib = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de);

        var comp = new Compilation(sm, de)
        {
            // The SDK is not native: a bodiless function there would be an error, as in any
            // ordinary project module.
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

    private static Diagnostic Wall(string sdk, string main)
    {
        var found = Check(sdk, main).Where(d => d.Code == "LYR-SEM0090").ToList();
        Assert.NotEmpty(found);
        return found[0];
    }

    private static void AssertNoWall(string sdk, string main)
    {
        var diagnostics = Check(sdk, main);
        Assert.DoesNotContain(diagnostics, d => d.Code == "LYR-SEM0090");
        Assert.DoesNotContain(diagnostics, d => d.Severity == Severity.Error);
    }

    private const string Sdk = """
        module sdk;

        pub opaque type Ticket = int;

        pub fn issue(raw: int): Ticket { return raw as Ticket; }
        pub fn number(t: Ticket): int { return t as int; }
        """;

    // ------------------------------------------------------------------ what the wall refuses

    [Fact]
    public void Making_a_handle_outside_the_declaring_module_is_reported()
    {
        // The one the promise was about. Before 3.3 this compiled and ran.
        var d = Wall(Sdk, """
            module main;
            import sdk { Ticket };

            fn main(): int {
                let forged = 3 as Ticket;
                return 0;
            }
            """);

        Assert.Contains("making one belongs to 'sdk'", d.Message);
        Assert.Contains("'@Open'", d.Message);
    }

    [Fact]
    public void Reading_the_value_inside_a_handle_outside_the_declaring_module_is_reported()
    {
        // Unwrapping cannot invent a handle, and is confined anyway: an alias whose underlying
        // type every caller reads is an alias whose underlying type can never change.
        var d = Wall(Sdk, """
            module main;
            import sdk { Ticket, issue };

            fn main(): int {
                return issue(7) as int;
            }
            """);

        Assert.Contains("reading the value inside it belongs to 'sdk'", d.Message);
    }

    [Fact]
    public void The_report_is_a_warning_while_the_toolchain_is_a_three()
    {
        // Breaking, and the promise ran the other way: code doing this relied on a bug, but it
        // relied on it in builds that passed. One release line of noise before the error.
        Assert.Equal(Severity.Warning, Wall(Sdk, """
            module main;
            import sdk { Ticket };

            fn main(): int {
                let forged = 3 as Ticket;
                return 0;
            }
            """).Severity);
    }

    // ------------------------------------------------------------------ what it lets through

    [Fact]
    public void The_declaring_module_crosses_freely()
    {
        // 'issue' and 'number' in the SDK do both directions; that is where handles come from.
        AssertNoWall(Sdk, """
            module main;
            import sdk { Ticket, issue, number };

            fn main(): int {
                return number(issue(7));
            }
            """);
    }

    [Fact]
    public void An_alias_marked_Open_crosses_from_anywhere()
    {
        AssertNoWall("""
            module sdk;
            import std.core { Open };

            @Open
            pub opaque type Ticket = int;
            """, """
            module main;
            import sdk { Ticket };

            fn main(): int {
                return (3 as Ticket) as int;
            }
            """);
    }

    [Fact]
    public void Holding_and_passing_a_handle_never_touches_the_wall()
    {
        // The shape every SDK actually has, and the reason the sealed default costs so little:
        // a script takes handles from the module that issues them and hands them back.
        AssertNoWall(Sdk, """
            module main;
            import sdk { Ticket, issue, number };

            fn twice(t: Ticket): int { return number(t) * 2; }

            fn main(): int {
                let t = issue(21);
                let all: Ticket[] = [t, t];
                return twice(all[0]);
            }
            """);
    }

    [Fact]
    public void A_transparent_alias_is_not_walled()
    {
        // 'type' is a NAME for a type; there is nothing to forge, and the cast is the ordinary
        // numeric one. The rule applies to the identity, not to the syntax that declares it.
        AssertNoWall("""
            module sdk;

            pub type Meters = int;
            """, """
            module main;
            import sdk { Meters };

            fn main(): int {
                let m: Meters = 5;
                return m as int;
            }
            """);
    }

    [Fact]
    public void An_opaque_alias_used_as_a_type_needs_no_door()
    {
        // Naming the type in a signature, a field or an array is not a crossing. The SDK pattern
        // in the guide spans files this way, and it must stay silent.
        AssertNoWall(Sdk, """
            module main;
            import sdk { Ticket, issue };

            struct Holder { ticket: Ticket }

            fn main(): int {
                let h = Holder { ticket = issue(1) };
                return 0;
            }
            """);
    }
}
