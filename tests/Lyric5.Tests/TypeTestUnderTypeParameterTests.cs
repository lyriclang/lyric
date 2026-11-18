using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A type test whose type takes a type parameter of the code around it — <c>c: Column&lt;T&gt;</c>,
/// <c>e is Column&lt;T&gt;</c>, <c>_ in [Column&lt;T&gt;, Cell&lt;T&gt;]</c> — in a generic function and
/// in a method of a generic class: in each instance it tests the instance's type. The checker
/// accepted it and the lowering stopped with an internal error, "this type argument". Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class TypeTestUnderTypeParameterTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    private const string Types = """
        import std.io { println };

        interface Erased { }

        class Column<T> :: [Erased] {
            values: T[],
        }

        struct Cell<T> :: [Erased] {
            v: T,
        }

        """;

    [Fact]
    public void A_type_pattern_in_a_generic_function_tests_the_instances_type()
    {
        Assert.Equal("true false true false\n", Output(Types + """
            fn pick<T>(e: Erased): ?Column<T> {
                return match (e) {
                    c: Column<T> => c,
                    _ => null,
                };
            }

            fn main(): void {
                let ints: Erased = Column<int> { values = [1, 2] };
                let words: Erased = Column<string> { values = ["x"] };
                println(f"{pick<int>(ints) != null} {pick<int>(words) != null} {pick<string>(words) != null} {pick<string>(ints) != null}");
            }
            """));
    }

    [Fact]
    public void A_type_pattern_in_a_method_of_a_generic_class_tests_the_instances_type()
    {
        Assert.Equal("true false 1\n", Output(Types + """
            class Store<T> {
                fn pick(e: Erased): ?Column<T> {
                    return match (e) {
                        c: Column<T> => c,
                        _ => null,
                    };
                }
            }

            fn main(): void {
                let words: Erased = Column<string> { values = ["x"] };
                let found = Store<string> { }.pick(words);
                let count = if (found != null) found.values.length() else 0;
                println(f"{found != null} {Store<int> { }.pick(words) != null} {count}");
            }
            """));
    }

    [Fact]
    public void An_is_test_and_a_type_set_take_the_instances_type_too()
    {
        Assert.Equal("true false one of mine other\n", Output(Types + """
            fn holds<T>(e: Erased): bool {
                return e is Column<T>;
            }

            fn either<T>(e: Erased): string {
                return match (e) {
                    _ in [Column<T>, Cell<T>] => "one of mine",
                    _ => "other",
                };
            }

            fn main(): void {
                let ints: Erased = Column<int> { values = [1] };
                let cell: Erased = Cell<int> { v = 3 };
                println(f"{holds<int>(ints)} {holds<string>(ints)} {either<int>(cell)} {either<string>(cell)}");
            }
            """));
    }
}
