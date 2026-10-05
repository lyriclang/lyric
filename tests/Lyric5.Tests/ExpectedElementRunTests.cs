using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A tuple literal's elements and a field default at run time (design/v5/spec/03 T8; the
/// review's R4a): the element is BUILT as the type the tuple asks for — a <c>?int</c> that is
/// <c>null</c>, a <c>?string</c> that holds one —, and a default of a generic type is evaluated
/// for the instance that is built, two instances in one function included. Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class ExpectedElementRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_tuples_elements_are_built_as_the_types_it_asks_for()
    {
        Assert.Equal("0 true | 1 x | true 0 | 7 none | 5\n", Output("""
            import std.io { println };

            fn pair(): (int, ?int) { return (0, null); }

            fn first(p: (?int, ?string)): string {
                let word = p.1 ?? "none";
                return f"{p.0 ?? 7} {word}";
            }

            fn main(): void {
                let (a, b) = pair();
                let t: (int, ?string) = (1, "x");
                let u: (?int, int[]) = (null, []);
                var w: (int, ?int) = (1, 2);
                w = (5, null);
                let held = t.1 ?? "?";
                println(f"{a} {b == null} | {t.0} {held} | {u.0 == null} {u.1.length()} | {first((null, null))} | {w.0 + (w.1 ?? 0)}");
            }
            """));
    }

    [Fact]
    public void A_field_default_is_evaluated_for_the_instance_that_is_built()
    {
        Assert.Equal("1 2 true 4 x\n", Output("""
            import std.io { println };

            class Bag<T> {
                items: List<T> = List<T>.new(),
                spare: List<T> = List.new(),
                label: ?T = null,
                pick: fn(T) -> T = (x: T) => x,

                fn add(item: T): int {
                    this.items.push(item);
                    return this.items.length() + this.spare.length();
                }
            }

            fn main(): void {
                let ints = Bag<int> { };
                let words = Bag<string> { };
                let one = ints.add(4);
                words.add("a");
                let two = words.add("b");
                println(f"{one} {two} {ints.label == null} {ints.pick(4)} {words.pick("x")}");
            }
            """));
    }
}
