using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A wait nothing can end, and a thread's end (design/v5/spec/06; the review's M6-12, M6-20b).
///
/// <para>DEADLOCK: a scheduler with no task ready, no sleeper and nothing that could wake one —
/// no other living thread — used to wait on its poller for ever: a program whose <c>recv</c>
/// has no sender hung. Where that is provable it is a panic now, <c>LYR-RT0018</c>, in the
/// stack of a task that waits — the oldest that waits for something other than a task's end —,
/// so the trace names the wait that is the reason.
/// With a second thread alive nothing is provable, and nothing is said; once that thread has
/// ended, it is.</para>
///
/// <para>A THREAD'S END: a thread ended with its first task, and what else lived on it never ran
/// again — a task of another thread that awaited one of those waited for ever. It cancels them
/// now and runs until they have ended: their <c>defer</c>s run, their handles are done. A
/// pool's thread does the same when the pool closes. <c>main</c> stays as it is (06 T6): the
/// program ends with it.</para>
///
/// <para>Through <c>Main</c>, so in the console collection. A program that waits without end
/// fails here by the runner's timeout.</para>
/// </summary>
[Collection("console")]
public class DeadlockTests
{
    private const string Head = "import std.io { println };\nimport std.task { spawn, spawnDetached, sleep, Channel, Cancelled };\nimport std.thread { Thread, Pool };\nimport std.time { Duration };\n\n";

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

    // ------------------------------------------------------------------ a wait nothing can end

    [Fact]
    public void A_receive_nobody_sends_to_is_a_deadlock()
    {
        var ran = Ran("""
            fn main(): void {
                let ch = Channel<int>.new();
                println("before");
                let v = try ch.recv();
                println(f"not reached {v ?? 0}");
            }
            """);
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("before\n", Printed(ran));
        Assert.Equal("panic [LYR-RT0018]: deadlock: every task waits, and nothing can wake one", FirstLine(ran));
        // the trace is the waiting task's own: the call that waits, in the program
        Assert.Contains("main.lyr:9", ran.Stderr);
    }

    [Fact]
    public void Tasks_that_wait_for_each_other_are_a_deadlock()
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let a = Channel<int>.new();
                let b = Channel<int>.new();
                let first = spawn(() => {
                    let v = try a.recv();
                    try b.send(v ?? 0);
                });
                let second = spawn(() => {
                    let v = try b.recv();
                    try a.send(v ?? 0);
                });
                try first.await();
                try second.await();
                println("not reached");
            }
            """);
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("", Printed(ran));
        Assert.StartsWith("panic [LYR-RT0018]: deadlock", FirstLine(ran));
        // The task that panics is the oldest that waits for something other than a task: the
        // first one's receive (line 9), whose panic main's await passes on with its frames —
        // not main, which only waits for it (line 16).
        Assert.Contains("main.lyr:10", ran.Stderr);
        Assert.DoesNotContain("main.lyr:17", ran.Stderr);
    }

    /// <summary>Where something can still wake a task, nothing is said: a sleeper of the thread,
    /// and another thread that lives.</summary>
    [Fact]
    public void A_sleeper_and_another_thread_are_no_deadlock()
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let ch = Channel<int>.new();
                let later = spawn(() => {
                    try sleep(Duration.ofMillis(50));
                    try ch.send(1);
                });
                let one = try ch.recv();
                let far = Thread.spawn(() => {
                    try sleep(Duration.ofMillis(50));
                    try ch.send(2);
                });
                let two = try ch.recv();
                try later.await();
                try far.await();
                println(f"{one ?? 0} {two ?? 0}");
            }
            """);
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("1 2\n", Printed(ran));
    }

    /// <summary>The thread that could have sent ends while the main thread is asleep on its
    /// poller: from then on it is provable, and the thread that ends wakes the other to see
    /// it.</summary>
    [Fact]
    public void After_the_other_thread_has_ended_it_is_one()
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let ch = Channel<int>.new();
                let other = Thread.spawn(() => {
                    try sleep(Duration.ofMillis(100));
                    return 7;
                });
                println("waiting");
                let v = try ch.recv();
                println(f"not reached {v ?? 0} {other.isDone()}");
            }
            """);
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("waiting\n", Printed(ran));
        Assert.StartsWith("panic [LYR-RT0018]: deadlock", FirstLine(ran));
    }

    // ------------------------------------------------------------------ a thread's end

    /// <summary>The thread's first task ends while another task of the thread still lives — not
    /// yet started in the first row, parked in its wait in the second. It is cancelled, its
    /// <c>defer</c> runs, and the task of another thread that awaits it goes on.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("try sleep(Duration.ofMillis(20));")]
    public void A_thread_ends_what_still_lives_on_it(string before)
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let never = Channel<int>.new();
                let t = Thread.spawn(() => {
                    let inner = spawn(() => {
                        defer println("the defer ran");
                        let v = try never.recv();
                        return v ?? 0;
                    });
                    BEFORE
                    return inner;
                });
                let inner = try t.await();
                try {
                    let v = inner.await();
                    println(f"not reached {v}");
                } catch (_: Cancelled) {
                    println("cancelled");
                }
                println(match (inner.status()) {
                    .Cancelled => "status cancelled",
                    _ => "another status",
                });
            }
            """.Replace("BEFORE", before));
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("the defer ran\ncancelled\nstatus cancelled\n", Printed(ran));
    }

    /// <summary>A pool's thread the same, when the pool closes: <c>close()</c> returns once
    /// nothing lives on its threads any more.</summary>
    [Fact]
    public void A_pool_s_thread_ends_what_still_lives_on_it()
    {
        var ran = Ran("""
            fn main(): void throws Error {
                let never = Channel<int>.new();
                let pool = Pool.new(1);
                let t = pool.spawn(() => {
                    return spawn(() => {
                        defer println("the defer ran");
                        let v = try never.recv();
                        return v ?? 0;
                    });
                });
                let inner = try t.await();
                try pool.close();
                println(match (inner.status()) {
                    .Cancelled => "cancelled",
                    .Running => "running",
                    _ => "another status",
                });
            }
            """);
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("the defer ran\ncancelled\n", Printed(ran));
    }

    /// <summary><c>main</c> stays (06 T6): the program ends with it, what is still ready or
    /// asleep then never runs again, and no <c>defer</c> of those runs.</summary>
    [Fact]
    public void The_program_still_ends_with_main()
    {
        var ran = Ran("""
            fn main(): void {
                spawnDetached(() => {
                    defer println("never");
                    try sleep(Duration.ofSecs(30));
                });
                println("main ends");
            }
            """);
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("main ends\n", Printed(ran));
    }
}
