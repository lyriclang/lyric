using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Attribute macros: a
/// <c>macro</c> whose first parameter is a declaration of <c>std.syntax</c> sits on such a
/// declaration as an attribute, takes it as a value without that attribute, and gives the
/// declaration or several back in its place.
/// </summary>
[Collection("console")]
public class AttributeMacroTests
{
    private const string Macros = """
        import std.io { println };
        import std.collections { List };
        import std.syntax { Expr, Stmt, Decl, StructDecl, FnDecl, FieldDecl, Ident, Type, Span, Visibility, Literal, Block, PostfixOp, FieldInit, WithField, Param };

        /// `@Builder struct P { ... }`: P stays, and PBuilder is added - a `?T` field per field with
        /// null, a setter per field, `build()`.
        pub macro Builder(target: StructDecl): Decl[] {
            let builderName = target.name.name + "Builder";
            let builderType = Type.Named([builderName], [], [], null, Span.none());
            let members = List<Decl>.new();
            let inits = List<FieldInit>.new();
            for (m in target.members) {
                match (m) {
                    .Field(f) => {
                        members.push(Decl.Field(FieldDecl {
                            visibility = Visibility.None, isVar = false, name = f.name,
                            type = Type.Optional(Box<Type> { value = f.type }, Span.none()),
                            default = Expr.Literal(Literal.Null, Span.none()), attributes = [], span = Span.none(),
                        }));
                        let v = Ident.of("v");
                        let setterBody = Block { stmts = [Stmt.Return(Expr.With(Box<Expr> { value = Expr.This(Span.none()) },
                            [WithField { path = [f.name.name], value = Expr.Name(v) }], Span.none()), Span.none())], span = Span.none() };
                        members.push(Decl.Fn(FnDecl {
                            visibility = target.visibility, isStatic = false, isMut = false, isInline = false,
                            name = Ident.of("set_" + f.name.name), generics = [],
                            params = [Param { name = v, type = f.type, default = null, isParams = false, isEllipsis = false, isPlace = false, attributes = [], span = Span.none() }],
                            ret = builderType, retGrouped = false, throws = null, body = setterBody,
                            externAbi = null, externSymbol = null, attributes = [], span = Span.none(),
                        }));
                        inits.push(FieldInit { name = f.name, value = Expr.Postfix(Box<Expr> { value = Expr.Member(Box<Expr> { value = Expr.This(Span.none()) }, f.name, false, Span.none()) }, PostfixOp.Unwrap, Span.none()), isShorthand = false });
                    },
                    _ => { },
                }
            }
            let buildBody = Block { stmts = [Stmt.Return(Expr.StructInit([target.name.name], [], inits.toArray(), false, Span.none()), Span.none())], span = Span.none() };
            members.push(Decl.Fn(FnDecl {
                visibility = target.visibility, isStatic = false, isMut = false, isInline = false, name = Ident.of("build"), generics = [], params = [],
                ret = Type.Named([target.name.name], [], [], null, Span.none()), retGrouped = false, throws = null, body = buildBody,
                externAbi = null, externSymbol = null, attributes = [], span = Span.none(),
            }));
            let builder = StructDecl {
                visibility = target.visibility, name = Ident.of(builderName), generics = [], interfaces = [], delegates = [],
                members = members.toArray(), attributes = [], span = Span.none(),
            };
            return [Decl.Struct(target), Decl.Struct(builder)];
        }

        /// `@Retry(n) fn f() { ... }` runs the body up to n times.
        pub macro Retry(target: FnDecl, n: Expr): Decl {
            let body: Stmt[] = quote {
                var attempts = 0;
                loop {
                    attempts += 1;
                    try {
                        #{target.body!}
                    } catch (e) {
                        if (attempts >= #{n}) { throw e; }
                    }
                }
            };
            return Decl.Fn(target with { body = Block { stmts = body, span = target.span } });
        }

        /// `@Logged fn f() { ... }` prints the name on entry; `println` is this module's.
        pub macro Logged(target: FnDecl): Decl {
            let name = Expr.Literal(Literal.Str("enter " + target.name.name), Span.none());
            let pre: Stmt[] = quote { println(#{name}); };
            let all = List<Stmt>.new();
            all.pushAll(pre);
            all.pushAll(target.body!.stmts);
            return Decl.Fn(target with { body = Block { stmts = all.toArray(), span = target.span } });
        }

        /// `@Twice fn f()` gives the function twice: a name clash the caller sees at the site.
        pub macro Twice(target: FnDecl): Decl[] {
            return [Decl.Fn(target), Decl.Fn(target)];
        }

        pub macro twice(e: Expr): Expr {
            return quote { #{e} + #{e} };
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

    /// <summary>The example macros: <c>@Builder struct P</c> adds <c>PBuilder</c>, built
    /// with the constructors; <c>@Retry(3) fn fetch()</c> wraps the body through a quote with the
    /// body spliced; <c>@Logged</c> prepends a statement whose <c>println</c> is the macro
    /// module's. The other declarations stay as written.</summary>
    [Fact]
    public void An_attribute_macro_rewrites_the_declaration_it_sits_on()
    {
        var dir = Build("""
            import std.io { println };
            import app.macros { Builder, Retry, Logged };

            @Builder
            struct P {
                x: int,
                y: int,
            }

            class Flaky :: [Error] {
                n: int,

                fn message(): string { return "flaky"; }
            }

            var calls = 0;

            @Retry(3)
            fn fetch(): int throws Flaky {
                calls += 1;
                if (calls < 3) { throw Flaky { n = calls }; }
                return calls * 10;
            }

            @Logged
            fn hello(): void {
                println("hello");
            }

            fn main(): void throws Flaky {
                let p = PBuilder { }.set_x(1).set_y(2).build();
                println(f"{p.x} {p.y}");
                println(try fetch());
                hello();
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("1 2\n30\nenter hello\nhello\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void The_declaration_has_to_be_what_the_macro_takes()
    {
        var lines = Errors("""
            import app.macros { Builder, Retry, twice };

            @Builder
            fn wrong(): void { }

            @Retry { n = 3 }
            fn fields(): void { }

            @twice
            struct S {
                x: int,
            }

            fn main(): void { }
            """);
        Assert.Contains(lines, l => l.Contains("LYR-SEM0191") && l.Contains("'@Builder' takes a struct first, and sits on a function"));
        Assert.Contains(lines, l => l.Contains("LYR-SEM0191") && l.Contains("'@Retry' is a macro, and its arguments stand in parentheses"));
        Assert.Contains(lines, l => l.Contains("LYR-SEM0191") && l.Contains("'@twice' takes an expression first, and sits on a struct"));
    }

    [Fact]
    public void An_attribute_macro_gives_declarations_back()
    {
        var lines = Errors("import app.macros { Wrong };\n\n@Wrong\nfn f(): void { }\n\nfn main(): void { }\n", macros: """
            import std.syntax { Expr, FnDecl };

            pub macro Wrong(target: FnDecl): Expr {
                return quote { 1 };
            }
            """);
        Assert.Contains(lines, l => l.Contains("LYR-SEM0186") && l.Contains("an attribute macro gives declarations back"));
    }

    [Fact]
    public void The_result_stands_in_the_declarations_place()
    {
        // two functions of one name: the result was spliced, and the checker sees both
        var lines = Errors("import app.macros { Twice };\n\n@Twice\nfn f(): void { }\n\nfn main(): void { }\n");
        Assert.Contains(lines, l => l.Contains("LYR-SEM") && l.Contains("'f'"));
    }

    [Fact]
    public void A_macro_of_the_own_module_is_refused_as_an_attribute_too()
    {
        var lines = Errors("""
            import std.syntax { Decl, FnDecl };

            macro Own(target: FnDecl): Decl {
                return Decl.Fn(target);
            }

            @Own
            fn f(): void { }

            fn main(): void { }
            """);
        Assert.Contains(lines, l => l.Contains("LYR-SEM0189") && l.Contains("'@Own' is written in the module that declares the macro"));
    }
}
