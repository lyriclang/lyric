using System.Diagnostics;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// Signals as a channel (design/v5/spec/06 K5; 10 Q9, §12) and the plan's Ctrl+C shutdown (M6): the
/// program <c>shutdown</c> runs until Interrupt while the test sends it signals, as a terminal and a
/// process manager do — each once the line before it came, since two signals that come before the
/// first went out may arrive as one. POSIX only: a console control event reaches no child without a
/// console of its own, so Windows' side is compiled (the archive for every target) but not run.
/// </summary>
public class SignalTests
{
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void Ctrl_c_shuts_the_program_down(Profile profile)
    {
        if (OperatingSystem.IsWindows()) return;
        var exe = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC("shutdown"), "shutdown-" + profile.Name(), profile);
        using var program = Process.Start(new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        try
        {
            Assert.Equal("ready", Line(program));
            Send(program, "USR1");
            Assert.Equal("got User1", Line(program));
            Send(program, "USR1");
            Assert.Equal("got User1", Line(program));
            Send(program, "INT");
            Assert.Equal("interrupted: shutting down", Line(program));
            Assert.Equal("worker stopped", Line(program));
            Assert.Equal("done", Line(program));
            Assert.True(program.WaitForExit(30_000), "the program did not end");
            Assert.Equal(0, program.ExitCode);
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

    private static void Send(Process program, string signal)
    {
        using var kill = Process.Start("kill", $"-{signal} {program.Id}")!;
        kill.WaitForExit();
        Assert.Equal(0, kill.ExitCode);
    }
}
