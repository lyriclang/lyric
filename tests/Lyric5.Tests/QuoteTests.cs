using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>quote { ... }</c> and <c>#{...}</c> (design/v5/spec/08 Y12 Q2, Q3; 13 M9b P5): a tree as a
/// value of its content's kind, the splices set in by their positions, every name the quote
/// binds fresh per expansion, every free name the macro module's.
/// </summary>
[Collection("console")]
public class QuoteTests
{
    private const string Macros = """
        import std.syntax { Expr, Block, Ident, Stmt, Decl };

        pub macro twice(e: Expr): Expr {
            return quote { #{e} + #{e} };
        }

        /// `retry!(n) { body }` runs the body up to n times; the counter and the catch are the quote's own.
        pub macro retry(n: Expr, body: Block): Stmt[] {
            return quote {
                var attempts = 0;
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

        pub fn helper(): int { return 41; }

        /// `helped!()` names a function of this module: the name is this module's at the site.
        pub macro helped(): Expr {
            return quote { helper() + 1 };
        }

        /// A declaration with a name splice.
        pub fn getterSource(): string {
            let name = Ident.of("hello");
            let d: Decl = quote { fn #{name}(): int { return 1; } };
            return d.toSource();
        }

        /// Statements spliced from an array; the outer quote's own let renamed.
        pub fn spliced(): string {
            let stmts: Stmt[] = quote { let a = 1; let b = 2; };
            let all: Stmt[] = quote { let start = 0; #{stmts}; start + a + b };
            return all.length().debug() + " " + all[3].toSource();
        }

        /// A block spliced where an expression stands is a block expression.
        pub macro blockValue(body: Block): Expr {
            return quote { #{body} };
        }

        """;

    private const string Main = """
        import std.io { println };
        import app.macros { twice, retry, helped, getterSource, spliced, blockValue };

        class Flaky :: [Error] {
            n: int,

            fn message(): string { return f"flaky {this.n}"; }
        }

        var calls = 0;

        fn flaky(): void throws Flaky {
            calls += 1;
            if (calls < 3) { throw Flaky { n = calls }; }
        }

        fn main(): void throws Flaky {
            let attempts = 7;
            retry!(3) {
                flaky();
            }
            println(f"{calls} {attempts}");
            println(twice!(20));
            println(helped!());
            println(comptime getterSource());
            println(comptime spliced());
            println(blockValue! { let k = 2; k * 21 });
        }
        """;

    private static string Build(string main, string macros = Macros) =>
        Package(("lyric.toml", AppManifest), ("src/macros.lyr", macros), ("src/main.lyr", main));

    private static string[] Errors(string main, string macros = Macros)
    {
        var dir = Build(main, macros);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        return error.Split('\n');
    }

    /// <summary>The artifacts (13 M9b S3): <c>retry!(3) { ... }</c> from a quote with a trailing
    /// block spliced as statements; the quote's <c>attempts</c> and the caller's are two names
    /// (hygiene); <c>helper</c> is the macro module's though the caller never imports it;
    /// <c>fn #{name}()</c> builds a name; <c>#{stmts}</c> splices an array.</summary>
    [Fact]
    public void A_quote_builds_its_tree_at_the_site()
    {
        var dir = Build(Main);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("3 7\n40\n42\nfn hello(): int {\n    return 1;\n}\n4 start__3 + a + b;\n42\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>Without hygiene the quote's <c>var attempts</c> would stand beside the caller's
    /// <c>let attempts</c>: the pin that the names are two.</summary>
    [Fact]
    public void A_name_the_quote_binds_is_not_the_callers()
    {
        var dir = Build("""
            import std.io { println };
            import app.macros { retry };

            fn main(): void throws Error {
                let attempts = 7;
                var e = 0;
                retry!(2) {
                    e += 1;
                }
                println(f"{attempts} {e}");
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("7 1\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_splice_takes_what_its_position_allows()
    {
        var lines = Errors("import std.io { println };\nimport app.macros { badName };\n\nfn main(): void {\n    println(badName!(1));\n}\n", macros: """
            import std.syntax { Expr, Stmt };

            pub macro bad(s: Stmt[]): Expr {
                return quote { #{s} + 1 };
            }

            pub macro badName(e: Expr): Expr {
                let d = quote { fn #{e}(): int { return 1; } };
                return e;
            }
            """);
        Assert.Contains(lines, l => l.Contains("LYR-SEM0193") && l.Contains("a splice in expression position") && l.Contains("not a 'Stmt[]'"));
        Assert.Contains(lines, l => l.Contains("LYR-SEM0193") && l.Contains("a splice in name position takes an Ident"));
    }

    [Fact]
    public void A_quote_takes_the_kind_its_place_expects()
    {
        var lines = Errors("import std.io { println };\nimport app.macros { twice };\n\nfn main(): void {\n    println(twice!(1));\n}\n", macros: """
            import std.syntax { Expr, Stmt };

            pub macro twice(e: Expr): Expr {
                let statements: Stmt[] = quote { #{e} + #{e} };
                return quote { #{e} + #{e} };
            }

            fn wrong(e: Expr): Expr {
                return quote { let x = 1; x };
            }
            """);
        // a lone tail is an Expr unless statements are wanted; two statements are never an Expr
        Assert.Contains(lines, l => l.Contains("LYR-SEM") && l.Contains("Stmt[]") && l.Contains("Expr"));
    }
}
