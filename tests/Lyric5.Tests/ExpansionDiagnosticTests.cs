using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The diagnostics of expanded code: what a macro gives back stands
/// in a synthesized file whose origin is the site, so an error in it is said at the site with the
/// line of the expansion and the quote it came from; what the caller wrote keeps its place, so an
/// error in an argument is the caller's and a warning in it stays; a site a result holds chains.
/// </summary>
[Collection("console")]
public class ExpansionDiagnosticTests
{
    private const string Macros = """
        import std.io { println };
        import std.collections { List };
        import std.syntax { Expr, Stmt, Decl, FnDecl, Block, Literal, Span };

        /// `bad!(e)` adds a string to whatever it gets: wrong for an int, in the quote.
        pub macro bad(e: Expr): Expr {
            return quote { #{e} + "s" };
        }

        pub macro twice(e: Expr): Expr {
            return quote { #{e} + #{e} };
        }

        /// `retry!(n) { body }`: the quote's own `dead` is never used, which is nobody's warning.
        pub macro retry(n: Expr, body: Block): Stmt[] {
            return quote {
                var attempts = 0;
                let dead = 1;
                loop {
                    attempts += 1;
                    try {
                        #{body}
                        break;
                    } catch (e) {
                        if (attempts >= #{n}) { throw e; }
                    }
                }
            };
        }

        /// `outer!(e)` is `bad!(e)` written by a quote: a site inside an expansion.
        pub macro outer(e: Expr): Expr {
            return quote { bad!(#{e}) };
        }

        /// `@Logged fn f() { ... }` prepends two statements, the second wrong on purpose.
        pub macro Logged(target: FnDecl): Decl {
            let name = Expr.Literal(Literal.Str("enter " + target.name.name), Span.none());
            let pre: Stmt[] = quote { println(#{name}); let n: int = "wrong"; };
            let all = List<Stmt>.new();
            all.pushAll(pre);
            all.pushAll(target.body!.stmts);
            return Decl.Fn(target with { body = Block { stmts = all.toArray(), span = target.span } });
        }

        """;

    private static string Build(string main) =>
        Package(("lyric.toml", AppManifest), ("src/macros.lyr", Macros), ("src/main.lyr", main));

    private static string[] Lines(string main, int expectedExit)
    {
        var dir = Build(main);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == expectedExit, $"exit {exit}\n{error}");
        return error.Split('\n');
    }

    /// <summary>An error in expanded code shows the site, the line of the
    /// expansion it stood on, and the quote it came from.</summary>
    [Fact]
    public void An_error_in_expanded_code_is_said_at_the_site_with_the_line_and_the_quote()
    {
        var lines = Lines("import std.io { println };\nimport app.macros { bad };\n\nfn main(): void {\n    println(bad!(1));\n}\n", 1);
        var at = Array.FindIndex(lines, l => l.Contains("LYR-SEM0003"));
        Assert.True(at >= 0, string.Join("\n", lines));
        Assert.EndsWith("main.lyr:5:13: error[LYR-SEM0003]: in the expansion of 'bad!': operator '+' is not applicable to 'int' and 'string'", lines[at]);
        Assert.Contains(lines.Skip(at), l => l.Contains("note: synthesized as: 1 + \"s\""));
        Assert.Contains(lines.Skip(at), l => l.Contains("note: from the quote of 'bad' here") && l.EndsWith("macros.lyr:7:20"));
        Assert.Equal(1, lines.Count(l => l.Contains("error[")));
    }

    /// <summary>What the caller wrote keeps its place: the error is at the argument, without the
    /// expansion's prefix, and once though the macro spliced the argument twice.</summary>
    [Fact]
    public void An_error_in_an_argument_is_the_callers_and_said_once()
    {
        var lines = Lines("import std.io { println };\nimport app.macros { twice };\n\nfn main(): void {\n    println(twice!(undefined));\n}\n", 1);
        var errors = lines.Where(l => l.Contains("error[")).ToList();
        Assert.Single(errors);
        Assert.EndsWith("main.lyr:5:20: error[LYR-SEM0002]: unknown identifier 'undefined'", errors[0]);
        Assert.DoesNotContain(lines, l => l.Contains("synthesized as"));
    }

    /// <summary>A warning in the caller's block stays; one in the quote's own code is nobody's.</summary>
    [Fact]
    public void A_warning_in_an_argument_stays_and_one_in_the_quote_falls()
    {
        var lines = Lines("""
            import std.io { println };
            import app.macros { retry };

            fn main(): void {
                retry!(3) {
                    let unused = 1;
                    println("once");
                }
            }
            """, 0);
        var warnings = lines.Where(l => l.Contains("warning[")).ToList();
        Assert.True(warnings.Count == 1, string.Join("\n", lines));
        Assert.EndsWith("main.lyr:6:13: warning[LYR-SEM0071]: 'unused' is never used", warnings[0]);
        Assert.DoesNotContain(lines, l => l.Contains("'dead'"));
    }

    /// <summary>A site an expansion holds: the chain of expansions, innermost first.</summary>
    [Fact]
    public void A_site_inside_an_expansion_chains_the_notes()
    {
        var lines = Lines("import std.io { println };\nimport app.macros { bad, outer };\n\nfn main(): void {\n    println(outer!(1));\n}\n", 1);
        var at = Array.FindIndex(lines, l => l.Contains("LYR-SEM0003"));
        Assert.True(at >= 0, string.Join("\n", lines));
        Assert.EndsWith("main.lyr:5:13: error[LYR-SEM0003]: in the expansion of 'outer!': in the expansion of 'bad!': operator '+' is not applicable to 'int' and 'string'", lines[at]);
        var notes = lines.Skip(at + 1).Where(l => l.Contains("note:")).Select(l => l.Trim()).ToList();
        Assert.Equal(4, notes.Count);
        Assert.StartsWith("note: synthesized as: 1 + \"s\"", notes[0]);
        Assert.StartsWith("note: from the quote of 'bad' here", notes[1]);
        Assert.StartsWith("note: synthesized as: bad!(1)", notes[2]);
        Assert.StartsWith("note: from the quote of 'outer' here", notes[3]);
    }

    /// <summary>An attribute macro's result: the macro's mistake at the attribute with the quote's
    /// line, the caller's mistake in the body at the caller's line.</summary>
    [Fact]
    public void An_attribute_macros_expansion_keeps_the_declarations_own_lines()
    {
        var lines = Lines("""
            import std.io { println };
            import app.macros { Logged };

            @Logged
            fn hello(): void {
                let x: int = "mine";
                println("hello");
            }

            fn main(): void {
                hello();
            }
            """, 1);
        var errors = lines.Where(l => l.Contains("error[")).ToList();
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, l => l.EndsWith("main.lyr:4:1: error[LYR-SEM0001]: in the expansion of '@Logged': cannot assign 'string' to 'int'"));
        Assert.Contains(errors, l => l.EndsWith("main.lyr:6:18: error[LYR-SEM0001]: cannot assign 'string' to 'int'"));
        Assert.Contains(lines, l => l.Contains("note: synthesized as: let n__1: int = \"wrong\";"));
        Assert.Contains(lines, l => l.Contains("note: from the quote of 'Logged' here") && l.EndsWith("macros.lyr:39:31"));
    }
}
