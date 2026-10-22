using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary><c>assert</c> with a lambda message, built on failure alone, and a module binding's
/// <c>comptime</c> site evaluated only where the program reaches the binding.</summary>
[Collection("console")]
public class AssertAndBindingSiteTests
{
    private static string App(string main) => Package(("lyric.toml", AppManifest), ("src/main.lyr", main));

    /// <summary>The message lambda runs only when the assertion fails: an assertion that holds
    /// builds nothing; a failing one builds its message and panics with it.</summary>
    [Fact]
    public void An_assertions_message_is_built_on_failure_alone()
    {
        var dir = App("""
            import std.io { println };

            var built = 0;

            fn text(n: int): string {
                built += 1;
                return f"n = {n}";
            }

            fn main(): void {
                let n = 1;
                assert(n < 2) { text(n) };
                assert(n < 2);
                println(f"built {built}");
                assert(n > 2) { text(n) };
                println("not reached");
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var ran = Lyric5.Toolchain.ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("built 0\n", ran.Stdout.Replace("\r\n", "\n"));
        Assert.Contains("panic [LYR-RT0011]: assertion failed: n > 2: n = 1", ran.Stderr);
    }

    /// <summary>The old form, a string message, is a type mismatch: the parameter is a lambda.</summary>
    [Fact]
    public void An_assertion_takes_no_string_message()
    {
        var dir = App("""
            fn main(): void {
                let n = 1;
                assert(n > 2, "n is small");
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0001]: cannot assign 'string' to 'fn() -> string'", error);
    }

    /// <summary>A binding nobody reaches has its site left unevaluated: one that would be refused
    /// — it reaches the world — is no error of a program that never reads the binding; a binding
    /// reached through another binding's initializer is evaluated.</summary>
    [Fact]
    public void An_unreached_bindings_site_is_not_evaluated()
    {
        var dir = App("""
            import std.io { println };
            import std.fs { readText };

            fn squares(n: int): int[] {
                var out = [0] * n;
                var i = 0;
                while (i < n) {
                    out[i] = i * i;
                    i += 1;
                }
                return out;
            }

            fn fromTheWorld(): string {
                return try! readText("nowhere.txt");
            }

            fn sumOf(xs: int[]): int {
                var s = 0;
                for (x in xs) {
                    s += x;
                }
                return s;
            }

            let table = comptime squares(8);
            let unused = comptime fromTheWorld();
            let total = comptime sumOf(table);

            fn main(): void {
                println(f"{total} {table.length()}");
            }
            """);
        Assert.Equal("140 8\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The same binding read by the program is evaluated, and its refusal stands.</summary>
    [Fact]
    public void A_reached_bindings_site_is_evaluated_and_refused_where_it_reaches_the_world()
    {
        var dir = App("""
            import std.io { println };
            import std.fs { readText };

            fn fromTheWorld(): string {
                return try! readText("nowhere.txt");
            }

            let text = comptime fromTheWorld();

            fn main(): void {
                println(text);
            }
            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-CT0003]", error);
    }
}
