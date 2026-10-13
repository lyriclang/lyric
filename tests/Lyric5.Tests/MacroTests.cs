using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Macros (design/v5/spec/13 M9b P3, P4, P6, P7; 08 Y12): <c>macro</c> declarations over
/// <c>std.syntax</c>, the sites <c>name!(...)</c>, <c>name!(...) { ... }</c> and <c>name! { ... }</c>,
/// the expansion before the module is checked, per module in import order. Through <c>Main</c>
/// with a package of two modules, since a macro expands only in the modules that import it.
/// </summary>
[Collection("console")]
public class MacroTests
{
    private const string Macros = """
        import std.syntax { Expr, Stmt, Block, Ident, Literal, BinaryOp, UnaryOp, error };

        /// `twice!(e)` is `e + e`.
        pub macro twice(e: Expr): Expr {
            return Expr.Binary(Box<Expr> { value = e }, BinaryOp.Add, Box<Expr> { value = e }, e.span());
        }

        /// `unless!(c) { ... }` is `if (!c) { ... }`.
        pub macro unless(c: Expr, body: Block): Stmt[] {
            let not = Expr.Unary(UnaryOp.Not, Box<Expr> { value = c }, c.span());
            return [Stmt.If(not, body, null, false, c.span())];
        }

        /// `positive!(n)` refuses a negative literal at the site.
        pub macro positive(n: Expr): Expr {
            match (n) {
                .Unary(.Neg, _, _) => { error(n.span(), "positive! takes no negative literal"); },
                _ => { },
            }
            return n;
        }

        /// `named!(x)` is the text of the name.
        pub macro named(id: Ident): Expr {
            return Expr.Literal(Literal.Str(id.name), id.span);
        }

        /// `loop!(e)` gives its own site back: an expansion that never ends.
        pub macro loop(e: Expr): Expr {
            return Expr.Macro("loop", [e], null, e.span());
        }

        pub fn plain(n: int): int { return n; }

        """;

    private static string Build(string main, string macros = Macros, string? more = null, string moreName = "more")
    {
        var files = new List<(string, string)> { ("lyric.toml", AppManifest), ("src/macros.lyr", macros), ("src/main.lyr", main) };
        if (more is not null) files.Add(($"src/{moreName}.lyr", more));
        return Package(files.ToArray());
    }

    private static string[] Errors(string main, string macros = Macros, string? more = null)
    {
        var dir = Build(main, macros, more);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        return error.Split('\n');
    }

    /// <summary>The artifact (13 M9b S2): <c>twice!(x)</c> as an <c>Expr</c> result, nested
    /// (the outer first, the inner in the round after), beside a <c>comptime</c> site, the
    /// statement form with a trailing block, an <c>Ident</c> parameter, and the imports of the
    /// macros counted as used.</summary>
    [Fact]
    public void A_site_is_replaced_by_what_the_macro_gives_back()
    {
        var dir = Build("""
            import std.io { println };
            import app.macros { twice, unless, named };

            fn main(): void {
                let x = 3;
                println(twice!(x));
                println(twice!(twice!(2)));
                println(twice!(x) + comptime 10);
                unless!(x > 5) {
                    println("small");
                }
                println(named!(x));
                println(twice!("ab"));
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("LYR-SEM0072", error);
        Assert.Equal("6\n8\n16\nsmall\nx\nabab\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A macro module that uses a macro of a third module: its sites go first, then the
    /// modules that import it (P6, import order).</summary>
    [Fact]
    public void A_macro_module_with_sites_of_its_own_expands_first()
    {
        var dir = Build("""
            import std.io { println };
            import app.macros { twice };
            import app.more { quad };

            fn main(): void {
                println(quad!(2));
            }
            """,
            more: """
                import std.syntax { Expr };
                import app.macros { twice };

                /// `quad!(e)` is `twice!(twice!(e))`, written with the imported macro inside.
                pub macro quad(e: Expr): Expr {
                    return Expr.Macro("twice", [Expr.Macro("twice", [e], null, e.span())], null, e.span());
                }

                pub fn fourOf(n: int): int {
                    return twice!(twice!(n));
                }
                """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("8\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_macro_of_the_own_module_is_refused()
    {
        var lines = Errors("""
            import std.io { println };
            import std.syntax { Expr, Span, BinaryOp };

            macro own(e: Expr): Expr {
                return Expr.Binary(Box<Expr> { value = e }, BinaryOp.Mul, Box<Expr> { value = e }, Span.none());
            }

            fn main(): void {
                println(own!(3));
            }
            """);
        Assert.Contains(lines, l => l.Contains("LYR-SEM0189") && l.Contains("'own!' is written in the module that declares the macro"));
    }

    [Fact]
    public void A_macro_is_never_called_as_a_function()
    {
        var lines = Errors("import std.io { println };\nimport app.macros { twice };\n\nfn main(): void {\n    println(twice(2));\n}\n");
        Assert.Contains(lines, l => l.Contains("LYR-SEM0187") && l.Contains("'twice' is a macro"));
    }

    [Fact]
    public void A_name_that_is_no_macro_takes_no_bang()
    {
        var lines = Errors("import std.io { println };\nimport app.macros { plain };\n\nfn main(): void {\n    println(plain!(2));\n    println(nothing!(2));\n}\n");
        Assert.Contains(lines, l => l.Contains("LYR-SEM0190") && l.Contains("'plain' is no macro"));
        Assert.Contains(lines, l => l.Contains("LYR-SEM0190") && l.Contains("no macro named 'nothing'"));
    }

    [Fact]
    public void A_macro_takes_and_gives_syntax_only()
    {
        // the module is checked when the program reaches it: imported, with no site
        var lines = Errors("import app.macros { bad, worse, generic };\n\nfn main(): void { }\n", macros: """
            import std.syntax { Expr, Ident };

            pub macro bad(n: int): Expr {
                return Expr.Name(Ident.of("x"));
            }

            pub macro worse(e: Expr): int {
                return 1;
            }

            pub macro generic<T>(e: Expr): Expr {
                return e;
            }
            """);
        Assert.Equal(3, lines.Count(l => l.Contains("LYR-SEM0186")));
        Assert.Contains(lines, l => l.Contains("'n' needs a type of std.syntax"));
        Assert.Contains(lines, l => l.Contains("'worse' needs a return type of std.syntax"));
        Assert.Contains(lines, l => l.Contains("'generic' sees syntax, which carries no types"));
    }

    /// <summary><c>error(node, msg)</c> (P7): the main span at the node, the note at the site's <c>!</c>.</summary>
    [Fact]
    public void An_error_of_the_macro_points_at_the_node_and_the_site()
    {
        var lines = Errors("import std.io { println };\nimport app.macros { positive };\n\nfn main(): void {\n    println(positive!(-1));\n}\n");
        var at = Array.FindIndex(lines, l => l.Contains("LYR-MAC0001") && l.Contains("positive! takes no negative literal"));
        Assert.True(at >= 0, string.Join("\n", lines));
        Assert.EndsWith("main.lyr:5:23: error[LYR-MAC0001]: positive! takes no negative literal", lines[at]);
        Assert.Contains(lines.Skip(at), l => l.Contains("note: in the expansion of 'positive!'") && l.EndsWith("main.lyr:5:21"));
    }

    [Fact]
    public void A_result_of_statements_needs_a_statement_site()
    {
        var lines = Errors("import std.io { println };\nimport app.macros { unless };\n\nfn main(): void {\n    let v = unless!(true) { println(\"x\"); };\n    println(v);\n}\n");
        Assert.Contains(lines, l => l.Contains("LYR-SEM0188") && l.Contains("'unless!' gives statements back"));
    }

    [Fact]
    public void The_arguments_are_counted_by_the_parameters()
    {
        var lines = Errors("import std.io { println };\nimport app.macros { twice, named };\n\nfn main(): void {\n    println(twice!(1, 2));\n    println(twice! { 1 });\n    println(named!(1 + 1));\n}\n");
        Assert.Contains(lines, l => l.Contains("LYR-SEM0191") && l.Contains("'twice!' takes 1 argument, more are written"));
        Assert.Contains(lines, l => l.Contains("LYR-SEM0191") && l.Contains("'twice!' takes 1 argument, 0 are written"));
        Assert.Contains(lines, l => l.Contains("LYR-SEM0191") && l.Contains("'named!' takes a name here"));
    }

    [Fact]
    public void An_expansion_that_never_ends_is_stopped()
    {
        var lines = Errors("import std.io { println };\nimport app.macros { loop };\n\nfn main(): void {\n    println(loop!(1));\n}\n");
        Assert.Contains(lines, l => l.Contains("LYR-CT0007") && l.Contains("did not end after 32 rounds"));
    }

    /// <summary>A macro's body runs under the interpreter's rules (09 Q9): a panic is
    /// <c>LYR-CT0001</c> at the site, with the chain.</summary>
    [Fact]
    public void A_panic_in_the_macro_is_reported_at_the_site()
    {
        var lines = Errors("import std.io { println };\nimport app.macros { first };\n\nfn main(): void {\n    println(first!(1));\n}\n", macros: """
            import std.syntax { Expr };

            pub macro first(e: Expr): Expr {
                let none: Expr[] = [];
                return none[0];
            }
            """);
        Assert.Contains(lines, l => l.Contains("LYR-CT0001") && l.Contains("main.lyr:5:13"));
    }
}
