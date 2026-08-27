using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Embedding;
using Lyric.Vm;

namespace Lyric.Tests.Embedding;

/// <summary>
/// A VM owns what its scripts open, and disposing it closes what they left behind.
///
/// <para>Before 4.3 the descriptor tables were per-THREAD and static. A file a guest opened and
/// did not close stayed open for the life of the thread — measured surviving a budget stop, a
/// panic, an ordinary return, and the VM being collected. That is the half of the sandbox a host
/// cannot work around: stopping an untrusted script left the host holding its handles.</para>
///
/// <para>The tests below are the sweep's own probe turned into pins. A locked file is the
/// cheapest observable resource on Windows, which is why the file case carries most of them;
/// sockets and children ride the same tables.</para>
/// </summary>
public class ResourceOwnershipTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static LangVm Vm() => new(new HostOptions
    {
        StdlibRoot = Path.Combine(RepoRoot(), "stdlib"),
        Capabilities = Capability.FileAccess | Capability.OsAccess | Capability.NetworkAccess,
    });

    private const string Source = """
        import std.io.stream as stream;
        import std.io.net as net;
        import std.task { Wait, spawn, run };

        fn openAndLeave(path: string): Coroutine<Wait> {
            let f = stream.open(path)!;
            let got = stream.readSome(f, 8)!;
        }

        fn openAndPanic(path: string): Coroutine<Wait> {
            let f = stream.open(path)!;
            let got = stream.readSome(f, 0);
        }

        fn drainForever(path: string): Coroutine<Wait> {
            let f = stream.open(path)!;
            while (true) {
                let got = stream.readSome(f, 64)!;
                if (got.length == 0) {
                    break;
                }
            }
            stream.close(f);
        }

        fn listenAndLeave(): Coroutine<Wait> {
            let l = net.listen("127.0.0.1", 0)!;
            port.at = net.localPort(l);
        }

        class Port { at: int }
        let port = Port { at = 0 };

        pub fn leave(path: string): void { spawn(openAndLeave(path)); run(); }
        pub fn boom(path: string): void { spawn(openAndPanic(path)); run(); }
        pub fn drain(path: string): void { spawn(drainForever(path)); run(); }
        pub fn listen(): void { spawn(listenAndLeave()); run(); }
        pub fn listeningPort(): int { return port.at; }

        pub fn compute(): int {
            var n = 0;
            var i = 0;
            while (i < 100) {
                n = n + i;
                i = i + 1;
            }
            return n;
        }
        """;

    private static string Fixture(string tag)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyric-own-{tag}-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[64 * 1024]);
        return path;
    }

    /// <summary>True while something still holds the file open.</summary>
    private static bool Locked(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite,
                FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    private static ScriptInstance Instance(LangVm vm) =>
        vm.Instantiate(vm.Compile(Source, "mod"));

    [Fact]
    public void A_file_a_script_leaves_open_is_closed_by_disposing_the_vm()
    {
        var path = Fixture("leave");
        try
        {
            var vm = Vm();
            Instance(vm).CallVoid("leave", path);

            Assert.True(Locked(path), "the guest still holds it while the VM lives");

            vm.Dispose();

            Assert.False(Locked(path), "disposing the VM releases what the guest left");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_file_held_by_a_script_that_panicked_is_released_too()
    {
        var path = Fixture("panic");
        try
        {
            var vm = Vm();
            var instance = Instance(vm);
            Assert.ThrowsAny<Exception>(() => instance.CallVoid("boom", path));

            Assert.True(Locked(path), "a panic does not close anything by itself");

            vm.Dispose();

            Assert.False(Locked(path), "the VM's dispose does");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_file_held_by_a_script_a_budget_stopped_is_released_too()
    {
        // The case a host actually has to survive: an untrusted script that would not stop.
        var path = Fixture("budget");
        try
        {
            var vm = Vm();
            var instance = Instance(vm);
            var budget = new ExecutionBudget(20_000);
            Assert.Throws<ScriptBudgetException>(() => instance.CallVoid("drain", budget, path));

            Assert.True(Locked(path), "the stopped script never reached its close");

            vm.Dispose();

            Assert.False(Locked(path), "stopping a script must not cost the host a handle");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Two VMs on ONE thread do not share a descriptor table, and disposing one leaves the
    /// other's handles alone.
    ///
    /// <para>This is what the tables being per-thread made impossible to state. It needs no
    /// timing and no second thread, which is exactly why it can be a test at all — the 4.0 sweep
    /// declined the two-thread version as CI-flaky.</para>
    /// </summary>
    [Fact]
    public void Disposing_one_vm_does_not_touch_another_vms_handles()
    {
        var mine = Fixture("mine");
        var theirs = Fixture("theirs");
        try
        {
            var a = Vm();
            var b = Vm();
            Instance(a).CallVoid("leave", mine);
            Instance(b).CallVoid("leave", theirs);

            b.Dispose();

            Assert.True(Locked(mine), "A's file is A's business");
            Assert.False(Locked(theirs), "B's file went with B");

            a.Dispose();
            Assert.False(Locked(mine), "and A's goes with A");
        }
        finally
        {
            File.Delete(mine);
            File.Delete(theirs);
        }
    }

    /// <summary>Two VMs on two THREADS, which the per-thread tables made a timing question and
    /// the per-registry ones make an ordinary one.</summary>
    [Fact]
    public void Two_vms_on_two_threads_keep_their_handles_apart()
    {
        var first = Fixture("t1");
        var second = Fixture("t2");
        try
        {
            LangVm? a = null;
            LangVm? b = null;

            var one = new Thread(() => { a = Vm(); Instance(a).CallVoid("leave", first); });
            var two = new Thread(() => { b = Vm(); Instance(b).CallVoid("leave", second); });
            one.Start();
            one.Join();
            two.Start();
            two.Join();

            Assert.True(Locked(first));
            Assert.True(Locked(second));

            // Disposed from a THIRD thread — this one — which the per-thread tables could not
            // have served at all: the state lived on the thread that opened it.
            a!.Dispose();
            Assert.False(Locked(first), "a VM's handles follow the VM, not the thread");
            Assert.True(Locked(second), "and only that VM's");

            b!.Dispose();
            Assert.False(Locked(second));
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }

    [Fact]
    public void A_listening_socket_goes_with_the_vm()
    {
        var vm = Vm();
        var instance = Instance(vm);
        instance.CallVoid("listen");
        var port = instance.Call<long>("listeningPort");
        Assert.True(port > 0, "the script should have bound a port");

        vm.Dispose();

        // The port is free again: binding it a second time is the observable proof.
        using var probe = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        probe.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, (int)port));
    }

    /// <summary>
    /// A disposed VM keeps interpreting but opens nothing new, and what a script tries to open
    /// after that fails the way any I/O failure does.
    ///
    /// <para>This is the leak the leak-fix nearly introduced: <c>Dispose</c> returns early the
    /// second time, so a handle acquired AFTER the first one was never released. Pure code still
    /// runs — it holds nothing, and stopping it would be a second and stranger failure mode.
    /// </para>
    /// </summary>
    [Fact]
    public void A_disposed_vm_still_computes_but_opens_nothing()
    {
        var path = Fixture("afterwards");
        try
        {
            var vm = Vm();
            var instance = Instance(vm);
            instance.CallVoid("leave", path);
            vm.Dispose();
            Assert.False(Locked(path));

            // Pure interpretation is unaffected.
            Assert.Equal(4950, instance.Call<long>("compute"));

            // Opening is not: the guest sees null, and its own force-unwrap says so.
            Assert.ThrowsAny<Exception>(() => instance.CallVoid("leave", path));

            vm.Dispose();
            Assert.False(Locked(path), "nothing was acquired after the first dispose");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Disposing_twice_disposes_once()
    {
        var path = Fixture("twice");
        try
        {
            var vm = Vm();
            Instance(vm).CallVoid("leave", path);
            vm.Dispose();
            vm.Dispose();
            Assert.False(Locked(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
