using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A panic in the body of a lock (design/v5/spec/06 G4; the review's M6-21). A panic unwinds
/// nothing (10 §5 rule 1), so the <c>defer</c> that releases the lock never ran: the lock
/// stayed held, and every task that came for it waited for ever — since R6b a deadlock panic,
/// which names the wrong thing. Now the scheduler RELEASES what the panicked task held, and
/// POISONS it: every <c>lock</c>, <c>read</c>, <c>write</c> and <c>run</c> from then on panics
/// with <c>LYR-RT0019</c>, naming the first panic. A <c>Once</c> whose body panicked is poisoned
/// the same way.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class PoisonTests
{
    private const string Head = "import std.io { println };\nimport std.task { spawn, sleep, yieldNow, Mutex, RwLock, Once, TaskStatus };\nimport std.time { Duration };\n\n"
        + "fn said(s: TaskStatus<void>): string {\n    return match (s) {\n        .Panicked(info) => f\"panicked {info.code}\",\n        .Done(_) => \"done\",\n        .Failed(_) => \"failed\",\n        .Cancelled => \"cancelled\",\n        .Running => \"running\",\n    };\n}\n\n";

    /// <summary>What a program that builds does within ten seconds.</summary>
    private static Lyric5.Toolchain.ProcessRunner.Result Ran(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Head + main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        return Lyric5.Toolchain.ProcessRunner.Run(
            Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix), [], TimeSpan.FromSeconds(10));
    }

    private static string Printed(Lyric5.Toolchain.ProcessRunner.Result ran) => ran.Stdout.Replace("\r\n", "\n");

    private static string FirstLine(Lyric5.Toolchain.ProcessRunner.Result ran) => ran.Stderr.Replace("\r\n", "\n").Split('\n')[0];

    private const string Poisoned = "panic [LYR-RT0019]: lock poisoned by a panic: LYR-RT0008: inside";

    // ------------------------------------------------------------------ released and poisoned

    [Fact]
    public void A_mutex_a_panicked_task_held_is_released_and_poisoned()
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let m = Mutex<int>.new(0);
                let t = spawn(() => {
                    try m.lock { &n =>
                        n = 1;
                        panic("inside");
                    };
                });
                while (!t.isDone()) {
                    try yieldNow();
                }
                println(said(t.status()));
                let v = try m.lock { &n => n };
                println(f"not reached {v}");
            }
            """);
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("panicked LYR-RT0008\n", Printed(ran));
        Assert.Equal(Poisoned, FirstLine(ran));
    }

    /// <summary>A task already parked for the lock when its holder panics: the release wakes it,
    /// and it panics itself — not a deadlock, the poison.</summary>
    [Fact]
    public void A_task_that_waited_for_it_wakes_and_panics()
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let m = Mutex<int>.new(0);
                let holder = spawn(() => {
                    try m.lock { &n =>
                        try yieldNow();
                        panic("inside");
                    };
                });
                try yieldNow();
                println("waiting");
                let v = try m.lock { &n => n };
                println(f"not reached {v}");
            }
            """);
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("waiting\n", Printed(ran));
        Assert.Equal(Poisoned, FirstLine(ran));
    }

    [Theory]
    [InlineData("rw.read { v => panic(\"inside\"); }", "try rw.write { &v => v = 2; };")]
    [InlineData("rw.write { &v => panic(\"inside\"); }", "let w = try rw.read { it };")]
    public void A_reader_writer_lock_the_same(string panics, string next)
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let rw = RwLock<int>.new(1);
                let t = spawn(() => {
                    try PANICS;
                });
                while (!t.isDone()) {
                    try yieldNow();
                }
                println(said(t.status()));
                NEXT
                println("not reached");
            }
            """.Replace("PANICS", panics).Replace("NEXT", next));
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("panicked LYR-RT0008\n", Printed(ran));
        Assert.Equal(Poisoned, FirstLine(ran));
    }

    [Fact]
    public void A_once_whose_body_panicked_is_poisoned()
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let once = Once.new();
                let t = spawn(() => {
                    try once.run {
                        panic("inside");
                    };
                });
                while (!t.isDone()) {
                    try yieldNow();
                }
                println(said(t.status()));
                try once.run {
                    println("ran again");
                };
                println("not reached");
            }
            """);
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("panicked LYR-RT0008\n", Printed(ran));
        Assert.Equal(Poisoned, FirstLine(ran));
    }

    /// <summary>Two locks held, inside one another: both are released and poisoned.</summary>
    [Fact]
    public void Nested_locks_are_both_released_and_poisoned()
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let outer = Mutex<int>.new(0);
                let inner = Mutex<int>.new(0);
                let t = spawn(() => {
                    try outer.lock { &a =>
                        try inner.lock { &b =>
                            panic("inside");
                        };
                    };
                });
                while (!t.isDone()) {
                    try yieldNow();
                }
                println(said(t.status()));
                let second = spawn(() => {
                    try inner.lock { &b => b = 2; };
                });
                while (!second.isDone()) {
                    try yieldNow();
                }
                println(said(second.status()));
                let v = try outer.lock { &a => a };
                println(f"not reached {v}");
            }
            """);
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("panicked LYR-RT0008\npanicked LYR-RT0019\n", Printed(ran));
        Assert.Equal(Poisoned, FirstLine(ran));
    }

    // ------------------------------------------------------------------ what stays

    /// <summary>An error — <c>Cancelled</c> among them — unwinds: the body's lock is released by
    /// the <c>defer</c>, and nothing is poisoned.</summary>
    [Fact]
    public void A_cancelled_body_releases_its_lock_and_poisons_nothing()
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let m = Mutex<int>.new(0);
                let t = spawn(() => {
                    try m.lock { &n =>
                        n = 1;
                        try sleep(Duration.ofSecs(30));
                        n = 2;
                    };
                });
                try yieldNow();
                t.cancel();
                while (!t.isDone()) {
                    try yieldNow();
                }
                println(said(t.status()));
                let v = try m.lock { &n => n };
                println(f"held {v}");
            }
            """);
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("cancelled\nheld 1\n", Printed(ran));
    }
}
