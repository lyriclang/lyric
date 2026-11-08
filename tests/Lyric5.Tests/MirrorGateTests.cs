using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// The server's half of a publish, which no toolchain runs: the gate behind the deploy key against
/// what it must refuse and keep (<c>tooling/mirror/test_gate.py</c>, in Python as the gate is), and the
/// pinned identity of the server against the address the toolchain carries.
/// </summary>
public class MirrorGateTests
{
    private static readonly string Mirror = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tooling", "mirror");

    [Fact]
    public void The_gate_lets_through_uploads_and_publishes_and_nothing_else()
    {
        if (OperatingSystem.IsWindows()) return; // the gate runs on the server; links and modes are Unix's
        var ran = ProcessRunner.Run("python3", ["-I", Path.Combine(Mirror, "test_gate.py")], TimeSpan.FromMinutes(2));
        Assert.True(ran.ExitCode == 0, ran.Stdout + ran.Stderr);
        Assert.Contains("failures: 0", ran.Stdout);
    }

    [Fact]
    public void The_pinned_host_is_the_mirror_the_toolchain_asks()
    {
        var line = Assert.Single(File.ReadAllLines(Path.Combine(Mirror, "known_hosts")));
        var parts = line.Split(' ');
        Assert.Equal(new Uri(ToolchainFetch.Mirror).Host, parts[0]);
        Assert.Equal("ssh-ed25519", parts[1]);
    }
}
