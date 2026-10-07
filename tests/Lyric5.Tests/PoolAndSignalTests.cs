using System.Diagnostics;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Two faults of <c>std.task</c> (design/v5/spec/06; the review's M6-27, M6-28).
///
/// <para>A task given to a pool while the pool closes could land on a worker that had already
/// ended, and never run: <c>spawn</c> looked at "closed", picked a worker and raised its count
/// with nothing in between held, and <c>close()</c> set "closed" and ended the workers meanwhile.
/// Whoever awaited that task waited for ever. Now the check, the pick and the hand-over stand
/// under the pool's guard, as the close does.</para>
///
/// <para>A signal channel closed ended its subscription with the NEXT signal — which the watcher
/// swallowed, finding the channel closed. Now the hub is told at the close, and the signal is
/// the system's again at once.</para>
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class PoolAndSignalTests
{
    private static string Built(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        return Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
    }

    /// <summary>Two hundred rounds of a thread that gives tasks to a pool in bursts while
    /// the main thread closes the pool: every task given is run or refused; none is lost. A lost
    /// task would leave its receiver waiting for ever — the runner's timeout, or, with every
    /// other thread gone, <c>LYR-RT0018</c>. A stress test, not a control: the window of the
    /// fault (the check of "closed", the pick, the count — tens of nanoseconds, and the worker
    /// looking at that instant) was not hit in two hundred rounds before the fix either; what
    /// pins the fix is the guard in the code, and this pins that the guard costs nothing it
    /// should not — no refusal of a task that came before the close, no task lost after.</summary>
    [Fact]
    public void A_task_given_while_the_pool_closes_runs_or_is_refused()
    {
        var exe = Built("""
            import std.io { println };
            import std.task { sleep, yieldNow, Channel, TaskStatus };
            import std.thread { Thread, Pool };
            import std.time { Duration };

            fn main(): void throws Error {
                var refused = 0;
                for (round in 0..200) {
                    let pool = Pool.new(1);
                    let giver = Thread.spawn(() => {
                        let results = Channel<int>.new(64);
                        var given = 0;
                        while (given < 100000) {
                            // a burst of tasks, then their values: a task that was lost never
                            // sends, and the receive below never ends
                            for (_ in 0..40) {
                                pool.spawn(() => {
                                    try results.send(1);
                                });
                            }
                            var got = 0;
                            while (got < 40) {
                                let v = try results.recv();
                                got += v ?? 0;
                            }
                            given += got;
                        }
                        return given;
                    });
                    try sleep(Duration.ofMicros((round % 7) * 50));
                    try pool.close();
                    while (!giver.isDone()) {
                        try yieldNow();
                    }
                    refused += match (giver.status()) {
                        .Panicked(_) => 1,
                        _ => 0,
                    };
                }
                println(f"no task was lost; refused in {refused} rounds");
            }
            """);
        var ran = Lyric5.Toolchain.ProcessRunner.Run(exe, [], TimeSpan.FromSeconds(60));
        Assert.True(ran.ExitCode == 0, $"exit {ran.ExitCode}\n{ran.Stderr}");
        Assert.StartsWith("no task was lost", ran.Stdout);
    }

    /// <summary>POSIX only, as <c>SignalTests</c>: the program subscribes to User1, closes the
    /// channel, and then sleeps; a User1 sent after the close ends the program by the signal's
    /// default action. Before the fix the watcher still caught it, found the channel closed, and
    /// the program slept on.</summary>
    [Fact]
    public void Closing_the_signal_channel_unsubscribes_at_once()
    {
        if (OperatingSystem.IsWindows()) return;
        var exe = Built("""
            import std.io { println };
            import std.task { sleep };
            import std.os { Signal, signals };
            import std.time { Duration };

            fn main(): void throws Error {
                let user = signals(Signal.User1);
                println("ready");
                user.close();
                println("closed");
                try sleep(Duration.ofSecs(3));
                println("still here");
            }
            """);
        using var program = Process.Start(new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        try
        {
            Assert.Equal("ready", Line(program));
            Assert.Equal("closed", Line(program));
            using (var kill = Process.Start("kill", $"-USR1 {program.Id}")!)
            {
                kill.WaitForExit();
                Assert.Equal(0, kill.ExitCode);
            }
            Assert.True(program.WaitForExit(10_000), "the program did not end");
            // ended by the signal: no "still here", and the exit is the signal's, not 0
            Assert.NotEqual(0, program.ExitCode);
            Assert.DoesNotContain("still here", program.StandardOutput.ReadToEnd());
        }
        finally
        {
            if (!program.HasExited) program.Kill();
        }
    }

    private static string? Line(Process program)
    {
        var read = program.StandardOutput.ReadLineAsync();
        Assert.True(read.Wait(TimeSpan.FromSeconds(30)), "no line within 30 s");
        return read.Result;
    }
}
