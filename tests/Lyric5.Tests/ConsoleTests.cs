using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// The console's output (design/v5/spec/10 O9; 13 M8b P3, S8a): print, println, eprint and eprintln
/// over <c>Display</c>, each stream buffered in the runtime — a line at a time at a terminal, a block
/// otherwise — and flushed at every end of the program. The order of the two streams in ONE pipe
/// is what tells the modes apart: the harness captures them apart, a shell or a pty joins them.
/// </summary>
public class ConsoleTests
{
    private const string Out = "a12.5\ntrue\nc\nP(3)\n42 left\n";
    private const string Err = "to err\ne\n";

    [Fact]
    public void Both_streams_say_what_was_written()
    {
        var result = RuntimeBuildTests.RunEmitted(CEmitterTests.EmitC("console"), "console", Profile.Debug);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Out, result.Stdout.Replace("\r\n", "\n"));
        Assert.Equal(Err, result.Stderr.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Through_one_pipe_each_stream_comes_a_block_at_a_time_the_output_first()
    {
        if (OperatingSystem.IsWindows()) return;
        var program = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC("console"), "console-pipe", Profile.Debug);
        var joined = ProcessRunner.Run("/bin/sh", ["-c", $"'{program}' 2>&1"], TimeSpan.FromSeconds(30));
        Assert.Equal(0, joined.ExitCode);
        Assert.Equal(Out + Err, joined.Stdout);
    }

    [Fact]
    public void At_a_terminal_each_stream_comes_a_line_at_a_time()
    {
        // a pty from util-linux's script(1); where there is none, the mode cannot be seen here
        if (!OperatingSystem.IsLinux() || CCompiler.FindOnPath("script") is not { } script) return;
        var program = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC("console"), "console-pty", Profile.Debug);
        var joined = ProcessRunner.Run(script, ["-qec", program, "/dev/null"], TimeSpan.FromSeconds(30));
        Assert.Equal(0, joined.ExitCode);
        Assert.Equal("a12.5\ntrue\nc\nP(3)\nto err\ne\n42 left\n", joined.Stdout.Replace("\r", ""));
    }

    [Theory]
    [InlineData("console_panic", 101, "before panic\n", "panic [LYR-RT0008]: boom")]
    [InlineData("console_error", 1, "before error\n", "error: gone")]
    public void What_was_written_comes_out_ahead_of_the_end(string name, int exit, string stdout, string report)
    {
        var result = RuntimeBuildTests.RunEmitted(CEmitterTests.EmitC(name), name, Profile.Debug);
        Assert.Equal(exit, result.ExitCode);
        Assert.Equal(stdout, result.Stdout.Replace("\r\n", "\n"));
        Assert.StartsWith(report, result.Stderr);
    }
}
