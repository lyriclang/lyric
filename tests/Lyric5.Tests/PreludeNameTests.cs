using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A prelude name in the two places that ask what a NAME is before they check a call
/// (design/v5/spec/08 Y9; 04 D2 R5): a type in call position — <c>Exception("…")</c> is
/// <c>Exception.new("…")</c> — and an interface before a member — <c>Display.show(x)</c>.
/// Both asked the scope alone, and a prelude name is in no scope: it is what a name means where
/// the scope has nothing. With an import of the same name both worked; nobody met the first,
/// because <c>Exception</c> is the first prelude type without type parameters that has a
/// <c>new</c> (the review's M5-2).
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class PreludeNameTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", "import std.io { println };\n\n" + main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_prelude_type_in_call_position_is_its_factory()
    {
        Assert.Equal("boom | boom <- none\n", Output("""
            fn main(): void {
                let e = Exception("boom");
                let seen: Error = Exception("boom");
                println(f"{e.message()} | {seen.message()} <- {seen.cause()?.message() ?? "none"}");
            }
            """));
    }

    [Fact]
    public void A_prelude_interface_qualifies_a_call()
    {
        Assert.Equal("P1\n", Output("""
            struct P :: [Display] {
                x: int,

                fn show(): string {
                    return f"P{this.x}";
                }
            }

            fn main(): void {
                println(Display.show(P { x = 1 }));
            }
            """));
    }

    /// <summary>The scope stands before the prelude, here as everywhere: a function of the
    /// module named as a prelude type is that function.</summary>
    [Fact]
    public void A_name_of_the_module_stands_before_the_prelude()
    {
        Assert.Equal("mine boom\n", Output("""
            fn Exception(text: string): string {
                return f"mine {text}";
            }

            fn main(): void {
                println(Exception("boom"));
            }
            """));
    }
}
