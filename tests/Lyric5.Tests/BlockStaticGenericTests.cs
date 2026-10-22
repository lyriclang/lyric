using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A generic static of a generic <c>extend</c> block, called with its own type arguments
/// as in <c>Pack&lt;int&gt;.rd&lt;StringBuilder&gt;(&amp;sb)</c> from a
/// block <c>extend&lt;T&gt; Pack&lt;T&gt; :: [Rd]</c>. It went the generic type's way, where only
/// the type's parameters are bound, and its body kept the block's <c>T</c> — an internal error
/// of the IR verifier.
/// </summary>
[Collection("console")]
public class BlockStaticGenericTests
{
    [Fact]
    public void A_generic_static_of_a_generic_block_binds_the_blocks_parameters()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.io { println };
            import std.string { StringBuilder };

            interface Sink {
                mut fn sink(s: string): void;
            }

            extend StringBuilder :: [Sink] {
                mut fn sink(s: string): void {
                    this.appendStr(s);
                }
            }

            interface Rd {
                static fn rd<S :: [Sink]>(&s: S): Self;
            }

            struct Pack<T> :: [Display] {
                n: int,

                fn show(): string {
                    return f"Pack({this.n})";
                }
            }

            extend<T :: [Display]> Pack<T> :: [Rd] {
                static fn rd<S :: [Sink]>(&s: S): Pack<T> {
                    s.sink("made ");
                    return Pack<T> { n = 4 };
                }
            }

            extend<T :: [Display]> T[] {
                fn packed<S :: [Sink]>(&s: S): Pack<T> {
                    return Pack<T>.rd(&s);
                }
            }

            fn main(): void {
                let xs = [1, 2, 3];
                var sb = StringBuilder.new();
                let direct = Pack<int>.rd(&sb);
                let through = xs.packed(&sb);
                let other = Pack<string>.rd(&sb);
                println(f"{sb.toString()}{direct} {through} {other} {xs.length()}");
            }
            """));
        Assert.Equal("made made made Pack(4) Pack(4) Pack(4) 3\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
