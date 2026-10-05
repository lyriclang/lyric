using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// An instance of a generic function is an instance of ONE declaration (design/v5/spec/01 C3,
/// 04 D4). The instance table keys by the instance's name, and the name did not say everything
/// the program says: a static function of a type's body was named without its type, and two of
/// one name that differ in their count were named alike. So <c>Small.make(7)</c>,
/// <c>Tiny.make(7)</c> and a free <c>make(7)</c> were ONE function — the first one asked for —,
/// and in the library <c>spawn(f)</c> and <c>Thread.spawn(f)</c> were: a thread that was a task,
/// or a task that was a thread, by the order of two lines. No diagnostic; with the IR verifier
/// off, as a release build has it, not even a crash where the counts differed. Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class InstanceOfADeclarationTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private const string TwoTypes = """
        import std.io { println };

        struct Small {
            n: int,
            static fn make<T>(v: T): int { return 1; }
            static fn twice(): int { return make(7) + make(8); }
        }

        struct Tiny {
            n: int,
            static fn make<T>(v: T): int { return 2; }
        }

        fn make<T>(v: T): int { return 3; }


        """;

    /// <summary>In both orders: the first instance asked for was the one every call reached.</summary>
    [Theory]
    [InlineData("println(f\"{Small.make(7)} {Tiny.make(7)} {make(7)}\");", "1 2 3\n")]
    [InlineData("println(f\"{make(7)} {Tiny.make(7)} {Small.make(7)}\");", "3 2 1\n")]
    // named bare inside its type, a static is the type's — not the module's function of the name
    [InlineData("println(f\"{make(7)} {Small.twice()}\");", "3 2\n")]
    // as values
    [InlineData("let a: fn(int) -> int = Small.make<int>;\n    let b: fn(int) -> int = Tiny.make<int>;\n    let c: fn(int) -> int = make<int>;\n    println(f\"{a(7)} {b(7)} {c(7)} {Small.make(7)}\");", "1 2 3 1\n")]
    public void A_types_static_is_its_own_function(string body, string printed) =>
        Assert.Equal(printed, Output(TwoTypes + "fn main(): void {\n    " + body + "\n}\n"));

    [Fact]
    public void Two_of_a_name_that_differ_in_their_count_are_two_functions()
    {
        Assert.Equal("1 2 | 10 20 | 100 200\n", Output("""
            import std.io { println };

            fn of<T>(a: T): int { return 1; }
            fn of<T>(a: T, b: T): int { return 2; }

            struct S {
                n: int,
                static fn mk<T>(a: T): int { return 10; }
                static fn mk<T>(a: T, b: T): int { return 20; }
                fn at<T>(a: T): int { return 100; }
                fn at<T>(a: T, b: T): int { return 200; }
            }

            fn main(): void {
                let s = S { n = 0 };
                println(f"{of(1)} {of(1, 2)} | {S.mk(1)} {S.mk(1, 2)} | {s.at(1)} {s.at(1, 2)}");
            }
            """));
    }

    /// <summary>The same on a generic type, whose members are instances of the type's instance:
    /// two methods of a name, and two statics — through the written instance and through the
    /// bare name (03 §9.2 rule 6).</summary>
    [Fact]
    public void Two_of_a_name_on_a_generic_type_are_two_functions()
    {
        Assert.Equal("1 2 | 10 20 | 10 20\n", Output("""
            import std.io { println };

            struct Box<T> {
                v: T,
                fn add(a: T): int { return 1; }
                fn add(a: T, b: T): int { return 2; }
                static fn from(a: T): int { return 10; }
                static fn from(a: T, b: T): int { return 20; }
            }

            fn main(): void {
                let b = Box<int> { v = 0 };
                println(f"{b.add(1)} {b.add(1, 2)} | {Box<int>.from(1)} {Box<int>.from(1, 2)} | {Box.from(1)} {Box.from(1, 2)}");
            }
            """));
    }

    private static string Spawns(bool threadFirst) => $$"""
        import std.io { println };
        import std.task { spawn, Thread };

        fn one(): int { return 1; }

        fn main(): void throws Error {
            let first = {{(threadFirst ? "Thread.spawn" : "spawn")}}(() => one());
            let second = {{(threadFirst ? "spawn" : "Thread.spawn")}}(() => one());
            let a = try first.await();
            let b = try second.await();
            println(f"{a} {b}");
        }
        """;

    /// <summary>The library's own pair: a task of this thread and a thread, of one name and — here
    /// — one list of type arguments. Each is its own function, in either order, and a thread is
    /// started exactly where the program says <c>Thread.spawn</c>.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_task_and_a_thread_are_spawned_by_two_functions(bool threadFirst)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Spawns(threadFirst)));
        var (exit, ir, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.Contains("= call std.task.spawn<int, never>(", ir);
        Assert.Contains("= call std.task.Thread.spawn<int, never>(", ir);
        Assert.Contains("fn std.task.Thread.spawn<int, never> ", ir);
        // One thread is started: in the body of 'Thread.spawn', and nowhere in the task's.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(ir, @"callimport std\.task\.startThread\("));
        Assert.Equal("1 1\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
