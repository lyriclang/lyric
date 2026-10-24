using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// <c>ProcessRunner.Run</c> as the toolchain and the tests start every tool and program: the child
/// reads an empty input whatever the caller's is. The shell command is POSIX; Windows does not run it.
/// </summary>
public class ProcessRunnerTests
{
    [Fact]
    public void A_child_that_reads_its_input_finds_it_empty()
    {
        if (OperatingSystem.IsWindows()) return;
        // A cat on an open input would wait for ever: the limit, not a stopwatch, says it did not.
        var result = ProcessRunner.Run("/bin/sh", ["-c", "cat; echo done"], TimeSpan.FromMinutes(2));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("done\n", result.Stdout);
    }
}
