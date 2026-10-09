using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// Processes (design/v5/spec/10 O8, Q9; 13 M8b S12a): children started, their pipes read and
/// written, their ends waited for — a kill, a cancelled wait, SIGPIPE at its default in the child
/// although the test host and the runtime ignore it, twenty at once, a wait on another thread — and
/// the plan's pipeline example. POSIX: Windows' processes come with S12b. Each run has a deadline.
/// </summary>
public class ProcessTests
{
    private static string Run(string name)
    {
        var program = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC(name), name, Profile.Debug);
        var result = ProcessRunner.Run(program, [], TimeSpan.FromSeconds(60));
        Assert.Equal("", result.Stderr);
        Assert.Equal(0, result.ExitCode);
        return result.Stdout.Replace("\r\n", "\n");
    }

    [Fact]
    public void Children_are_started_read_written_waited_for_and_signalled()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(
            "hello world | exit code 0\nout | err | 3\ncat: piped data\nenv: set | cwd: /\n"
            + "not found: no-such-program-for-lyric (started)\nkilled: signal 9\nwait: timed out\n"
            + "sigpipe: signal 13\n20 children, codes summed 190\nwaited on another thread: exit code 7\n",
            Run("processes"));
    }

    [Fact]
    public void The_pipeline_example_joins_three_children()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("apple\nbanana\ncherry\n3 children: exit code 0, exit code 0, exit code 0\n", Run("pipeline"));
    }
}
