using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A lambda inside a lambda that names <c>this</c>: it captures the <c>this</c> the outer lambda
/// captured, from that lambda's environment. The lowering read a method's slot the outer lambda
/// has none of and stopped with an internal error ("lambda captures 'this' outside a method").
/// Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class NestedLambdaThisTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_lambda_in_a_lambda_reads_and_writes_the_classs_this()
    {
        Assert.Equal("p 1 2\np 2 2\n9\n", Output("""
            import std.io { println };

            class Panel {
                var count: int = 0,
                label: string,

                fn each(f: fn(int) -> void): void {
                    f(1);
                    f(2);
                }

                mut fn run(): void {
                    this.each((i) => {
                        this.each((j) => {
                            this.count += i * j;
                            if (j == 2) {
                                println(f"{this.label} {i} {j}");
                            }
                        });
                    });
                }
            }

            fn main(): void {
                let p = Panel { label = "p" };
                p.run();
                println(f"{p.count}");
            }
            """));
    }

    [Fact]
    public void A_struct_and_three_levels_take_the_copy_the_outer_lambda_took()
    {
        Assert.Equal("7 7 7\n", Output("""
            import std.io { println };

            fn call(f: fn() -> void): void { f(); }

            struct Box {
                n: int,

                fn show(): void {
                    call(() => {
                        call(() => {
                            call(() => {
                                println(f"{this.n} {this.n} {this.n}");
                            });
                        });
                    });
                }
            }

            fn main(): void {
                let box = Box { n = 7 };
                box.show();
            }
            """));
    }
}
