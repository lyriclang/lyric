using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The C programs under <c>runtime/tests</c>, built against <c>liblyr.a</c> and run on the host in
/// the debug and the release profile. Each program states its expectation in its header comment;
/// the expected output and exit code here are the same statement, checked.
/// </summary>
public class RuntimeProgramTests
{
    public static TheoryData<string, Profile, int, string, string[]> Programs()
    {
        var data = new TheoryData<string, Profile, int, string, string[]>();
        foreach (var profile in new[] { Profile.Debug, Profile.Release })
        {
            data.Add("alloc", profile, 0, "alloc ok\n", []);
            data.Add("strings", profile, 0, "strings ok\n", []);
            data.Add("numeric", profile, 0, "numeric ok\n", []);
            data.Add("arrays", profile, 0, "arrays ok\n", []);
            data.Add("config", profile, 0, "config ok\n", []);
            data.Add("hello", profile, 7, "Hello, Lyric!\nargs: 2\n", ["one", "two"]);
            data.Add("roots", profile, 0, "roots ok\n", []);
            data.Add("weak", profile, 0, "weak ok\n", []);
            data.Add("threads", profile, 0, "threads ok\n", []);
            data.Add("coro_basic", profile, 0, "coro ok\n", []);
            data.Add("coro_gc", profile, 0, "coro gc ok\n", []);
            data.Add("coro_pace", profile, 0, "coro pace ok\n", []);
            data.Add("coro_park", profile, 0, "coro park ok\n", []);
            data.Add("poll_basic", profile, 0, "poll ok\n", []);
            data.Add("task_main", profile, 7, "task ok\n", []);
            data.Add("coro_panic", profile, 0, "coro panic ok\n", []);
            data.Add("coro_threads", profile, 0, "coro threads ok\n", []);
            data.Add("coro_storm", profile, 0, "coro storm ok\n", []);
            data.Add("random", profile, 0, "random ok\n", []);
            data.Add("clock", profile, 0, "clock ok\n", []);
        }
        return data;
    }

    /// <summary>A coroutine resumed while it runs or after it ended, and a yield on a thread's own
    /// stack (design/v5/spec/06 A8): a panic, RT0014.</summary>
    [Theory]
    [InlineData("self", Profile.Debug)]
    [InlineData("done", Profile.Debug)]
    [InlineData("outside", Profile.Debug)]
    [InlineData("self", Profile.Release)]
    public void A_coroutine_misused_panics(string which, Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("coro_basic", profile, args: [which]);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.StartsWith("panic [LYR-RT0014]: ", result.Stderr);
        Assert.Equal("", result.Stdout);
    }

    /// <summary>Parking misused (06 N3): with no coroutine running, or closing a parked one — RT0014.</summary>
    [Theory]
    [InlineData("outside", Profile.Debug, "a park with no coroutine running")]
    [InlineData("closed", Profile.Debug, "a coroutine closed while it is parked")]
    [InlineData("outside", Profile.Release, "a park with no coroutine running")]
    public void Parking_misused_panics(string which, Profile profile, string message)
    {
        var result = RuntimeBuildTests.RunTest("coro_park", profile, args: [which]);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.StartsWith("panic [LYR-RT0014]: " + message, result.Stderr);
        Assert.Equal("", result.Stdout);
    }

    /// <summary>A yield in a task, outside every generator (10 §1.13, 06 N3): there is nothing to
    /// yield to — RT0014.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void A_yield_in_a_task_panics(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("task_main", profile, args: ["yield"]);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.StartsWith("panic [LYR-RT0014]: a yield of 'int' in a task, where no generator runs", result.Stderr);
        Assert.Equal("", result.Stdout);
    }

    /// <summary>A panic with nowhere to leave to (05 E8): from a coroutine on the thread's own stack
    /// it ends the process with the first report, the frames where it happened; with a foreign
    /// frame on the coroutine's stack it ends the process at once, even under the quiet resume.</summary>
    [Theory]
    [InlineData("main", Profile.Debug, "panic [LYR-RT0008]: boom 7")]
    [InlineData("main", Profile.Release, "panic [LYR-RT0008]: boom 7")]
    [InlineData("foreign", Profile.Debug, "panic [LYR-RT0008]: through C")]
    public void A_panic_with_nowhere_to_go_ends_the_process(string which, Profile profile, string first)
    {
        var result = RuntimeBuildTests.RunTest("coro_panic", profile, args: [which]);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        var lines = result.Stderr.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(first, lines[0]);
        if (which == "main") Assert.StartsWith("    at explode", lines[1]);
        Assert.Equal("", result.Stdout);
    }

    /// <summary>A poller the system refuses (06 N6 S2): with no descriptors left, a panic — RT0015.
    /// Not on Windows, where a process cannot be denied the event a poller is.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void A_refused_poller_panics(Profile profile)
    {
        if (OperatingSystem.IsWindows()) return;
        var result = RuntimeBuildTests.RunTest("poll_basic", profile, args: ["nofds"]);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.StartsWith("panic [LYR-RT0015]: the system refused a poller: ", result.Stderr);
        Assert.Equal("", result.Stdout);
    }

    /// <summary>A coroutine's stack overflow (01 S3, K1): its guard page makes it a panic with the
    /// coroutine's frames. On Windows too (the review's R6e): the coroutine's stack is laid out
    /// as a thread's, and the kernel raises the overflow with room for the filter — before, the
    /// dispatch had no stack to run on below the guard page, and the process ended without a
    /// line.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void A_coroutine_that_overflows_its_stack_panics(Profile profile)
    {
        var result = RuntimeBuildTests.RunTest("coro_overflow", profile);
        Assert.True(result.ExitCode == 101, $"exit {result.ExitCode}\nstderr:\n{result.Stderr}");
        Assert.StartsWith("panic [LYR-RT0006]: stack overflow", result.Stderr);
        Assert.Equal("", result.Stdout);
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void A_runtime_program_does_what_its_header_says(string name, Profile profile, int exit, string stdout, string[] args)
    {
        var result = RuntimeBuildTests.RunTest(name, profile, args: args);
        Assert.True(result.ExitCode == exit, $"exit {result.ExitCode}, expected {exit}\nstderr:\n{result.Stderr}");
        Assert.Equal(stdout, result.Stdout.Replace("\r\n", "\n"));
    }
}
