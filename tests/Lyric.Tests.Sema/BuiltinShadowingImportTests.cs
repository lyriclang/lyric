using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A module imported as a namespace does not take the builtin type of the same name with it.
///
/// <para><c>import std.string;</c> binds <c>string</c> as a namespace — legal by the scoping
/// rules, module members shadow the builtin root scope like any parent — and from then on the
/// builtin type <c>string</c> was unnameable for the rest of the file: <c>fn f(): string</c>
/// answered <c>LYR-RES0002: unresolved type 'string'</c>. Importing the module named after a type
/// removed the type, and <c>std.string</c> is not an exotic import.</para>
///
/// <para>The shadowing is not the error and still warns (<c>LYR-SEM0077</c>). What was wrong is
/// giving up in a position where the namespace cannot have been meant: a type annotation admits
/// types, and a module has never been one.</para>
///
/// <para>THE WARNING WAS SILENT TOO, and its silence is worth keeping in mind: warnings run only
/// over a program without errors, deliberately, because a warning computed from half a table is a
/// guess with a confident tone. So the one diagnostic that explained the situation was withheld
/// exactly when the situation bit. Fixing the resolution is what lets it be heard — which is why
/// these tests assert the warning as well as the absence of the error.</para>
/// </summary>
public class BuiltinShadowingImportTests
{
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

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string Errors(DiagnosticEngine de) =>
        string.Join("; ", de.Diagnostics.Where(d => d.Severity == Severity.Error)
            .Select(d => $"{d.Code}: {d.Message}"));

    [Fact]
    public void The_builtin_type_is_still_nameable()
    {
        var de = Check("""
            import std.string;

            fn f(): string { return "x"; }
            fn main(): int { return 0; }
            """);

        Assert.False(de.HasErrors, Errors(de));
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0077");
    }

    /// <summary>
    /// The case that found it: a class whose <c>message()</c> returns <c>string</c> stopped
    /// conforming to <c>Throwable</c>, reported as "returns '&lt;error&gt;'" at the conformance
    /// rather than at the import.
    /// </summary>
    [Fact]
    public void A_conformance_naming_the_builtin_still_holds()
    {
        var de = Check("""
            import std.string;

            pub class Boom :: [Throwable] {
                fn message(): string { return "boom"; }
            }

            fn main(): int { return 0; }
            """);

        Assert.False(de.HasErrors, Errors(de));
    }

    /// <summary>
    /// The other half: the MODULE is still reachable under that name. A fix that simply let the
    /// builtin win everywhere would pass the tests above and break every call through the
    /// namespace.
    /// </summary>
    [Fact]
    public void The_module_is_still_reachable_under_the_same_name()
    {
        var de = Check("""
            import std.string;

            fn f(n: int): string { return string.fromInt(n); }
            fn main(): int { return 0; }
            """);

        Assert.False(de.HasErrors, Errors(de));
    }

    /// <summary>An alias shadows nothing and warns about nothing.</summary>
    [Fact]
    public void An_aliased_import_does_not_shadow()
    {
        var de = Check("""
            import std.string as text;

            fn f(n: int): string { return text.fromInt(n); }
            fn main(): int { return 0; }
            """);

        Assert.False(de.HasErrors, Errors(de));
        Assert.DoesNotContain(de.Diagnostics, d => d.Code == "LYR-SEM0077");
    }

    /// <summary>
    /// The control that matters: a name that is neither a type nor in scope is STILL unresolved.
    /// Falling back to the builtins must not turn every typo into silence.
    /// </summary>
    [Fact]
    public void An_unknown_type_is_still_unresolved()
    {
        var de = Check("""
            fn f(): strng { return "x"; }
            fn main(): int { return 0; }
            """);

        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-RES0002");
    }
}
