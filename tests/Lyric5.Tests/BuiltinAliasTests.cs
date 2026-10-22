using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The 64-bit names are aliases of the word-sized types: one symbol under
/// two names. They were two builtin symbols, and an <c>extend uint64</c> did not reach a
/// <c>uint</c>.
/// </summary>
[Collection("console")]
public class BuiltinAliasTests
{
    [Fact]
    public void An_extend_of_the_alias_reaches_the_type()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.io { println };

            interface Tag {
                fn tag(): string;
            }

            extend uint64 :: [Tag] {
                fn tag(): string {
                    return f"u:{this}";
                }
            }

            extend int :: [Tag] {
                fn tag(): string {
                    return f"i:{this}";
                }
            }

            fn tagged<T :: [Tag]>(v: T): string {
                return v.tag();
            }

            fn main(): void {
                let a: uint = 5;
                let b: uint64 = 6;
                let c: int64 = -7;
                println(f"{a.tag()} {b.tag()} {tagged(a)} {tagged(b)} {c.tag()}");
            }
            """));
        Assert.Equal("u:5 u:6 u:5 u:6 i:-7\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
