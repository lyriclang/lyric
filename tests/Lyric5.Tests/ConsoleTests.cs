using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// The console's output (design/v5/spec/10 O9; 13 M8b P3, S8a): print, println, eprint and eprintln
/// over <c>Display</c>, each stream buffered in the runtime — the output a line at a time at a
/// terminal and a block otherwise, the error always a line (10 Review 2026-10-08, S8c) — and
/// flushed at every end of the program. The order of the two streams in ONE pipe is what tells the
/// modes apart: the harness captures them apart, a shell or a pty joins them.
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
    public void Through_one_pipe_the_output_comes_a_block_at_a_time_and_the_error_a_line()
    {
        if (OperatingSystem.IsWindows()) return;
        var program = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC("console"), "console-pipe", Profile.Debug);
        var joined = ProcessRunner.Run("/bin/sh", ["-c", $"'{program}' 2>&1"], TimeSpan.FromSeconds(30));
        Assert.Equal(0, joined.ExitCode);
        Assert.Equal(Err + Out, joined.Stdout);
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

    /// <summary>
    /// The standard input (S8b): a line at a time, raw bytes from the same buffer between, the
    /// rest by lines, null at its end, and one stdin() for the program; the output and the error
    /// as Writers over print's buffers. Fed through a pipe by a shell, so POSIX only here: Windows'
    /// pipe was run by hand (a Windows process through WSL interop), and a console's ReadConsoleW
    /// needs a person typing at it.
    /// </summary>
    [Fact]
    public void The_standard_input_is_read_a_line_at_a_time()
    {
        if (OperatingSystem.IsWindows()) return;
        var program = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC("console_in"), "console_in", Profile.Debug);
        var result = ProcessRunner.Run("/bin/sh", ["-c", $"printf 'first\\nsecond\\r\\nthird\\nfourth' | '{program}'"],
            TimeSpan.FromSeconds(30));
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1 first sec ond\n2 [third][fourth]\n3 <end> same\n4 written and printed\n", result.Stdout);
        Assert.Equal("5 to err\n", result.Stderr);
    }

    /// <summary>
    /// A pipe nobody reads is BrokenPipe (10 Q9: SIGPIPE ignored; O9): the program writes after its
    /// reader is gone and hears of it, instead of dying of the signal. The test host ignores
    /// SIGPIPE itself (.NET does) and a child inherits that across exec, so the program is started
    /// with the signal's default, as a shell gives it — GNU env's --default-signal; Linux only.
    /// </summary>
    [Fact]
    public void A_pipe_nobody_reads_is_a_broken_pipe()
    {
        if (!OperatingSystem.IsLinux()) return;
        var program = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC("console_pipe"), "console_pipe", Profile.Debug);
        var result = ProcessRunner.Run("/bin/sh", ["-c", $"env --default-signal=PIPE '{program}' | true"], TimeSpan.FromSeconds(30));
        Assert.Equal("broken pipe (written to the standard output)\n", result.Stderr);
    }

    /// <summary>
    /// print into a pipe nobody reads ends the program as SIGPIPE would (10 Review 2026-10-08, Go's
    /// way; S8c): `prog | head -1` stops when head does, while a Writer of stdout() still hears
    /// BrokenPipe (above). A print's own flush, or the end's, which writes what print left. The
    /// shell reports the program's status itself: 128 + 13. Linux only, as above; Windows has no
    /// such signal and goes on, as Go does there.
    /// </summary>
    [Theory]
    [InlineData("console_gone")]
    [InlineData("console_gone_at_end")]
    public void Print_into_a_pipe_nobody_reads_ends_the_program_as_the_signal_would(string name)
    {
        if (!OperatingSystem.IsLinux()) return;
        var program = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC(name), name, Profile.Debug);
        var result = ProcessRunner.Run("/bin/sh", ["-c", $"{{ env --default-signal=PIPE '{program}'; echo $? >&2; }} | true"],
            TimeSpan.FromSeconds(30));
        Assert.Equal("141\n", result.Stderr);
    }
}
