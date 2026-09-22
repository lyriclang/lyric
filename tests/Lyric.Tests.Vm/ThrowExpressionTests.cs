using System.Runtime.CompilerServices;
using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// <c>throw</c> as an expression, end to end: the absent path of <c>??</c> throws, the throwing
/// branch of an <c>if</c> and the throwing arm of a <c>match</c> take nothing and reach no merge,
/// and a user function declared <c>never</c> ends the path like <c>panic</c>. Before this every one of
/// these forms was either a parse error or a compiler crash ("expression produced no value").
/// </summary>
public class ThrowExpressionTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static string Out(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);

        var diagnostics = new StringWriter();
        de.RenderText(diagnostics);
        Assert.False(de.HasErrors, "source did not compile: " + diagnostics);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);

        var output = new StringWriter();
        Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(output, TextWriter.Null));
        return output.ToString().ReplaceLineEndings("\n");
    }

    private const string Prelude =
        """
        import std.io.console { println };

        class NotFound :: [Throwable] {
            key: string,
            fn message(): string { return "not found: " + this.key; }
        }

        """;

    [Fact]
    public void The_absent_path_of_a_coalesce_throws() =>
        Assert.Equal("7\nnot found: b\n", Out(Prelude +
            """
            fn need(o: ?int, key: string): int throws NotFound {
                return o ?? throw NotFound { key = key };
            }
            fn main(): int {
                try {
                    println(f"{need(7, "a")}");
                    println(f"{need(null, "b")}");
                } catch (e: NotFound) {
                    println(e.message());
                }
                return 0;
            }
            """));

    [Fact]
    public void A_throwing_if_branch_takes_nothing() =>
        Assert.Equal("10\nnot found: x\n", Out(Prelude +
            """
            fn pick(b: bool): int throws NotFound {
                let v = if (b) 10 else throw NotFound { key = "x" };
                return v;
            }
            fn main(): int {
                try {
                    println(f"{pick(true)}");
                    println(f"{pick(false)}");
                } catch (e: NotFound) {
                    println(e.message());
                }
                return 0;
            }
            """));

    [Fact]
    public void A_throwing_match_arm_takes_nothing() =>
        Assert.Equal("1\n7\nnot found: dial\n", Out(Prelude +
            """
            enum Cmd { Go, Dial(int) }
            fn code(c: Cmd): int throws NotFound {
                return match (c) {
                    Cmd.Go => 1,
                    Cmd.Dial(n) if n > 0 => n,
                    _ => throw NotFound { key = "dial" },
                };
            }
            fn main(): int {
                try {
                    println(f"{code(Cmd.Go)}");
                    println(f"{code(Cmd.Dial(7))}");
                    println(f"{code(Cmd.Dial(0))}");
                } catch (e: NotFound) {
                    println(e.message());
                }
                return 0;
            }
            """));

    [Fact]
    public void A_panic_arm_in_a_match_expression_lowers() =>
        Assert.Equal("2\n", Out(Prelude +
            """
            fn half(n: int): int {
                return match (n) {
                    4 => 2,
                    _ => panic("odd"),
                };
            }
            fn main(): int { println(f"{half(4)}"); return 0; }
            """));

    [Fact]
    public void A_user_never_function_ends_the_path() =>
        Assert.Equal("42\ncaught: negative\n", Out(Prelude +
            """
            fn fail(msg: string): never throws NotFound { throw NotFound { key = msg }; }
            fn guard(n: int): int throws NotFound {
                if (n < 0) { fail("negative"); }
                return n * 2;
            }
            fn main(): int {
                try {
                    println(f"{guard(21)}");
                    println(f"{guard(-1)}");
                } catch (e: NotFound) {
                    println("caught: " + e.key);
                }
                return 0;
            }
            """));

    [Fact]
    public void Defers_run_while_a_thrown_expression_unwinds() =>
        Assert.Equal("cleanup\nnot found: k\n", Out(Prelude +
            """
            fn f(o: ?int): int throws NotFound {
                defer println("cleanup");
                return o ?? throw NotFound { key = "k" };
            }
            fn main(): int {
                try { println(f"{f(null)}"); } catch (e: NotFound) { println(e.message()); }
                return 0;
            }
            """));
}
