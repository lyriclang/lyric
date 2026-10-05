using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A lambda's place parameter at run time (design/v5/spec/03 T12; the review's M6-24): what the
/// body writes is written in the caller's variable, field or element. And the locks that hand
/// their body the place of what they hold (06 G4): <c>m.lock { &amp;n =&gt; n += 1; }</c>.
/// Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class LambdaPlaceRunTests
{
    private static string Output(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Fact]
    public void A_lambda_writes_through_the_place_it_takes()
    {
        Assert.Equal("42 10 9 11\n", Output("""
            import std.io { println };

            struct Point { var x: int }

            fn visit(&x: int, f: fn(&int) -> void): void { f(&x); }

            fn main(): void {
                var n = 1;
                visit(&n) { &v => v += 41; };

                var p = Point { x = 1 };
                visit(&p.x, (&v) => { v = v * 10; });

                var xs = [1, 2, 3];
                visit(&xs[1]) { &v => v = 9; };

                let add = (&total: int, by: int) => { total += by; };
                var t = 0;
                add(&t, 5);
                add(&t, 6);

                println(f"{n} {p.x} {xs[1]} {t}");
            }
            """));
    }

    /// <summary>A lambda that captures and takes a place: the environment and the place are two
    /// parameters of the function it is lowered to.</summary>
    [Fact]
    public void A_capturing_lambda_takes_a_place_too()
    {
        Assert.Equal("15 3\n", Output("""
            import std.io { println };

            fn visit(&x: int, f: fn(&int) -> void): void { f(&x); }

            fn main(): void {
                var calls = 0;
                let step = 5;
                var n = 0;
                let bump = (&v: int) => { v += step; calls += 1; };
                visit(&n, bump);
                visit(&n, bump);
                bump(&n);
                println(f"{n} {calls}");
            }
            """));
    }

    [Fact]
    public void A_lock_hands_its_body_the_place_of_what_it_holds()
    {
        Assert.Equal("counted 4000\nwritten 9 10\nlog a1 a2 b1 b2\n", Output("""
            import std.io { println };
            import std.task { spawn, sleep, yieldNow, Thread, Mutex, RwLock };
            import std.time { Duration };

            fn add(m: Mutex<int>): void throws Error {
                for (_ in 0..1000) {
                    try m.lock { &n => n += 1; };
                }
            }

            fn main(): void throws Error {
                let m = Mutex<int>.new(0);
                let t1 = Thread.spawn(() => try add(m));
                let t2 = Thread.spawn(() => try add(m));
                let t3 = Thread.spawn(() => try add(m));
                let t4 = Thread.spawn(() => try add(m));
                try t1.await();
                try t2.await();
                try t3.await();
                try t4.await();
                println(f"counted {try m.lock { &n => n }}");

                let rw = RwLock<int>.new(1);
                try rw.write { &v => v = 9; };
                let next = try rw.write { &v => v + 1 };
                println(f"written {try rw.read { it }} {next}");

                let order = Mutex<string>.new("");
                let a = spawn(() => {
                    try order.lock { &log =>
                        log = log + "a1 ";
                        try sleep(Duration.ofMillis(5));
                        log = log + "a2 ";
                    };
                });
                let b = spawn(() => {
                    try order.lock { &log =>
                        log = log + "b1 ";
                        try yieldNow();
                        log = log + "b2";
                    };
                });
                try a.await();
                try b.await();
                println(f"log {try order.lock { &log => log }}");
            }
            """));
    }
}
