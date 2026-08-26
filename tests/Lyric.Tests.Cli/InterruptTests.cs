using System.Diagnostics;

namespace Lyric.Tests.Cli;

/// <summary>
/// The real signal, end to end: a program parks a task on <c>Wait.Interrupt</c>, the test
/// sends SIGINT, and the program shuts down CLEANLY — the parked task wakes, says goodbye,
/// and <c>run()</c> drains to a zero exit instead of the process dying mid-flight.
///
/// <para>Unix only: SIGINT can be aimed at one child there (<c>kill -s INT</c>), while a
/// Windows console event goes to the whole process group, the test runner included. The
/// in-process machinery — flag, event, self-pipe, the wake through the scheduler — is the
/// same on both and is covered for both by the <c>interrupt()</c> tests in
/// <c>stdlib-tests</c>; only the OS delivery differs, and that is what this test pins.</para>
///
/// <para>Every read from the child is BOUNDED. The first version of this test waited on a
/// bare <c>ReadLine()</c> and hung both Linux CI jobs for six hours: a test about not dying
/// must itself be unable to wait forever, and stderr is drained concurrently so a chatty
/// child cannot deadlock the pipes either.</para>
/// </summary>
public sealed class InterruptTests
{
    [Fact]
    public void Sigint_wakes_the_parked_task_and_the_program_drains()
    {
        if (OperatingSystem.IsWindows()) return;

        using var source = Toolchain.Temp(".lyr");
        File.WriteAllText(source.Path, """
            import std.io.console as console;
            import std.task { Wait, spawn, run };

            fn waiter(): Coroutine<Wait> {
                console.println("ready");
                console.flush();
                yield Wait.Interrupt;
                console.println("bye");
            }

            fn main(): int {
                spawn(waiter());
                run();
                return 0;
            }
            """);

        var info = new ProcessStartInfo(Toolchain.LyricPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Toolchain.RepositoryRoot,
        };
        info.ArgumentList.Add("run");
        info.ArgumentList.Add(source.Path);

        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();

        // "ready" is printed right before the park; the pause after it covers the last few
        // steps into the poll that arms the handler.
        var ready = process.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(120)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("the program never said ready within 120s; stderr:\n" + Drain(stderr));
        }
        Assert.Equal("ready", ready.Result);
        Thread.Sleep(500);

        using var kill = Process.Start(new ProcessStartInfo("/bin/kill")
        {
            ArgumentList = { "-s", "INT", process.Id.ToString() },
            UseShellExecute = false,
        })!;
        kill.WaitForExit();

        var rest = process.StandardOutput.ReadToEndAsync();
        var drained = process.WaitForExit(10_000);
        if (!drained) process.Kill(entireProcessTree: true);
        Assert.True(drained,
            "the program did not drain within 10s of SIGINT; stderr:\n" + Drain(stderr));

        Assert.Equal("bye", Drain(rest).Trim());
        Assert.Equal(0, process.ExitCode);
    }

    /// <summary>What a pipe task has produced, without ever waiting forever on it — after a
    /// kill the pipe closes and the task completes; five seconds is generosity, not hope.</summary>
    private static string Drain(Task<string> pipe) =>
        pipe.Wait(TimeSpan.FromSeconds(5)) ? pipe.Result : "<the pipe never closed>";
}
