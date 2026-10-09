using Lyric5.Toolchain;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// The network (design/v5/spec/10 O7; 13 M8b P2, S10b): TCP within one program on 127.0.0.1, every
/// wait a socket's on the thread's poller — an echo, 100 connections at once on one thread, the
/// waits a cancel or a close end, a refused connection, both ways of one socket waiting at once,
/// and a thread too busy to be idle that still sees a socket's readiness; UDP, the resolver, a
/// close across threads (S10c). On every platform since S11 — Windows' poller over AFD, Winsock —
/// but the echo server's, whose Interrupt a POSIX kill sends. Each run has a deadline (13 M8b's
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
        Assert.Equal("hello over tcp\n127.0.0.1 true\n", Run("net_echo"));
    }

    [Fact]
    public void A_hundred_connections_wait_on_one_thread()
    {
        Assert.Equal("100 echoed, 4950 summed\n", Run("net_many"));
    }

    [Fact]
    public void A_cancel_a_close_and_a_refusal_end_a_sockets_wait()
    {
        Assert.Equal("accept: timed out\nread: timed out\nclosed while accepting: closed (accepted)\nrefused: connection refused\n",
            Run("net_ends"));
    }

    [Fact]
    public void Both_ways_of_one_socket_wait_at_once()
    {
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
        Assert.Equal("1 byte read, then 33554432 written\n", Run("net_one_way"));
    }

    [Fact]
    public void A_busy_thread_still_sees_a_sockets_readiness()
    {
        Assert.Equal("read while busy\n", Run("net_busy"));
    }

    [Fact]
    public void A_datagram_goes_there_and_back_and_a_receive_ends_at_a_cancel()
    {
        Assert.Equal("ping from the client's port\npong from the server's port\nreceive: timed out\n", Run("net_udp"));
    }

    [Fact]
    public void The_resolver_knows_localhost_and_an_addresss_own_text()
    {
        Assert.Equal("localhost: a loopback\n127.0.0.1: [127.0.0.1]\n", Run("net_resolve"));
    }

    /// <summary>The M8b open point 1t, S10c: a close on the main thread ends a read that waits on
    /// another thread's poller.</summary>
    [Fact]
    public void A_close_on_one_thread_ends_a_wait_on_another()
    {
        Assert.Equal("closed (read)\n", Run("net_threads"));
    }

    /// <summary>
    /// The echo server, M8b S10's artifact: three clients of the test's own — .NET's sockets —
    /// each get their lines back; one of them stays connected and idle while Interrupt comes, and
    /// the server cancels its task, waits for it, and ends with 0. POSIX: a console control event
    /// reaches no child without a console of its own (as SignalTests).
    /// </summary>
    [Fact]
    public void The_echo_server_echoes_until_interrupted()
    {
        if (OperatingSystem.IsWindows()) return;
        var exe = RuntimeBuildTests.BuildEmitted(CEmitterTests.EmitC("echo_server"), "echo_server", Profile.Debug);
        using var server = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        try
        {
            var first = Line(server);
            Assert.StartsWith("listening on 127.0.0.1:", first);
            var port = int.Parse(first!["listening on 127.0.0.1:".Length..]);
            var idle = new System.Net.Sockets.TcpClient("127.0.0.1", port);
            for (var i = 0; i < 2; i++)
            {
                using var client = new System.Net.Sockets.TcpClient("127.0.0.1", port);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream);
                using var writer = new StreamWriter(stream) { AutoFlush = true, NewLine = "\n" };
                writer.WriteLine($"hello {i}");
                writer.WriteLine("again");
                Assert.Equal($"hello {i}", reader.ReadLine());
                Assert.Equal("again", reader.ReadLine());
            }
            using var kill = System.Diagnostics.Process.Start("kill", $"-INT {server.Id}")!;
            kill.WaitForExit();
            Assert.Equal("stopping", Line(server));
            Assert.Equal("served 3 connections", Line(server));
            Assert.True(server.WaitForExit(30_000), "the server did not end");
            Assert.Equal(0, server.ExitCode);
            idle.Dispose();
        }
        finally
        {
            if (!server.HasExited) server.Kill();
        }
    }

    private static string? Line(System.Diagnostics.Process program)
    {
        var read = program.StandardOutput.ReadLineAsync();
        Assert.True(read.Wait(TimeSpan.FromSeconds(30)), "no line within 30 s");
        return read.Result;
    }
}
