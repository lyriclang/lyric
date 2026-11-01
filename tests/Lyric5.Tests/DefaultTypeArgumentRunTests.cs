using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A generic type with a defaulted parameter as the ARGUMENT of a written type: a field of type
/// <c>List&lt;Map&lt;string, int&gt;&gt;</c>, where <c>Map</c> is <c>Map&lt;K, V, H = DefaultHasher&gt;</c>.
/// The default was filled for a field whose type is the map itself, and not for the map inside
/// another type's arguments: the checker accepted the program and the lowering refused it as
/// LYR-ICE0002 ("needs 3 type argument(s), got 2"). Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class DefaultTypeArgumentRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    /// <summary>In a class and in a struct, as a list's element and as a map's value, and the same
    /// instance as the one written with its hasher.</summary>
    [Fact]
    public void A_defaulted_type_inside_a_fields_type_takes_its_default()
    {
        Assert.Equal("1 2 2 1 true\n", Output("""
            import std.collections { List, Map, Set };
            import std.hash { DefaultHasher };
            import std.io { println };

            class Scopes {
                scopes: List<Map<string, int>>,
            }

            struct Seen {
                by: Map<string, Set<int>>,
                plain: Map<string, int, DefaultHasher>,
            }

            fn main(): void {
                let c = Scopes { scopes = List<Map<string, int>>.new() };
                let first = Map<string, int>.new();
                first.insert("a", 1);
                c.scopes.push(first);
                let written: Map<string, int, DefaultHasher> = c.scopes[0];
                written.insert("b", 2);
                let s = Seen { by = Map<string, Set<int>>.new(), plain = written };
                let odd = Set<int>.new();
                odd.insert(3);
                s.by.insert("odd", odd);
                println(f"{c.scopes.length()} {c.scopes[0].length()} {s.plain.length()} {s.by.length()} {s.by.get("odd")!.contains(3)}");
            }
            """));
    }
}
