using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A blanket member beside a type's own (design/v5/spec/04 D15, the review's A5): the type's
/// comes first on the type, generic code that reaches the member through the block's
/// constraints calls the block's, and the type's member is warned about. Through <c>Main</c>,
/// so in the console collection.
/// </summary>
[Collection("console")]
public class BlanketMemberTests
{
    private const string Program = """
        import std.io { println };

        interface Named {
            fn name(): string;
        }

        extend<T :: [Named]> T {
            fn greeting(): string { return f"hello {this.name()}"; }
        }

        struct Cat :: [Named] {
            n: int,
            fn name(): string { return "cat"; }
            fn greeting(): string { return "meow"; }
        }

        struct Dog :: [Named] {
            n: int,
            fn name(): string { return "dog"; }
        }

        fn greet<T :: [Named]>(t: T): string { return t.greeting(); }

        fn main(): void {
            let cat = Cat { n = 0 };
            let dog = Dog { n = 0 };
            println(f"{cat.greeting()} | {greet(cat)} | {dog.greeting()} | {greet(dog)}");
        }
        """;

    [Fact]
    public void The_types_own_on_the_type_and_the_blocks_in_generic_code()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Program));
        Assert.Equal("meow | hello cat | hello dog | hello dog\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void The_types_member_is_warned_about_where_it_stands()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Program));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, $"exit {exit}\n{error}");
        Assert.Contains("main.lyr:14:8: warning[LYR-SEM0168]: 'Cat.greeting' hides the blanket member 'greeting'", error);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(error, @"LYR-SEM0168"));
    }
}
