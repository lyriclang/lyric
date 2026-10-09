using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// The network (design/v5/spec/10 O7; 13 M8b P2, S10b): TCP within one program on 127.0.0.1, every
/// wait a socket's on the thread's poller — an echo, 100 connections at once on one thread, the
/// waits a cancel or a close end, a refused connection, both ways of one socket waiting at once,
/// and a thread too busy to be idle that still sees a socket's readiness. POSIX only: Windows' poller and Winsock come with S11 (P5), and until
/// then its calls throw Unsupported, which the last test pins. Each run has a deadline (13 M8b's
/// tests: a hanging test is a red job).
/// </summary>
public class NetTests
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
    public void A_stream_echoes_within_one_program()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("hello over tcp\n127.0.0.1 true\n", Run("net_echo"));
    }

    [Fact]
    public void A_hundred_connections_wait_on_one_thread()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("100 echoed, 4950 summed\n", Run("net_many"));
    }

    [Fact]
    public void A_cancel_a_close_and_a_refusal_end_a_sockets_wait()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("accept: timed out\nread: timed out\nclosed while accepting: closed (accepted)\nrefused: connection refused\n",
            Run("net_ends"));
    }

    [Fact]
    public void Both_ways_of_one_socket_wait_at_once()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("4194304 back, all of them right\n", Run("net_duplex"));
    }

    /// <summary>
    /// One way fires while the other waits on, its waiter then the only one: the poller arms that
    /// way again itself (epoll's one-shot disarmed both). The duplex test cannot see this — there
    /// the reader's next arm re-arms both ways.
    /// </summary>
    [Fact]
    public void One_way_firing_leaves_the_other_ways_wait_armed()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("1 byte read, then 33554432 written\n", Run("net_one_way"));
    }

    [Fact]
    public void A_busy_thread_still_sees_a_sockets_readiness()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("read while busy\n", Run("net_busy"));
    }

    [Fact]
    public void Windows_throws_unsupported_until_its_poller()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal("unsupported: 127.0.0.1:0 (bound)\n", Run("net_unsupported"));
    }
}
