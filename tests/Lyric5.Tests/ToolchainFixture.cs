using System.Net;
using System.Net.Sockets;
using System.Text;
using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The tests that fetch from a mirror on the loopback: alone, after the others. A fetch gives up a
/// source that sends nothing for a few seconds, and under the load of the whole suite in parallel a
/// fixture mirror answered that late now and then — the source counted as failed, and a test went
/// red on one platform or another without anything being wrong.
/// </summary>
[CollectionDefinition(nameof(LoopbackMirror), DisableParallelization = true)]
public sealed class LoopbackMirror;

/// <summary>What the tests of <c>lyric toolchain</c> and of the switch share: the fixtures of
/// <c>toolchain/make.py</c>, a mirror serving them on the loopback, a fresh home and the environment
/// pointing at both.</summary>
internal static class ToolchainFixture
{
    public static readonly string Fixtures = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tests", "Lyric5.Tests", "toolchain");
    public static readonly string Mirror = Path.Combine(Fixtures, "mirror");

    public static string KeyLine(string name) => File.ReadAllText(Path.Combine(Fixtures, name)).Split('\n')[1];

    /// <summary>A fresh home and the environment that points the command at it and at the mirror;
    /// the variables are restored when the server goes.</summary>
    public static ToolchainHome Home(MirrorServer server)
    {
        var home = new ToolchainHome(TestDirectories.Fresh("lyric5-home-"));
        server.OnDispose(Variable("LYRIC_HOME", home.Root));
        server.OnDispose(Variable("LYRIC_MIRROR", server.Address));
        server.OnDispose(Variable("LYRIC_MIRROR_KEY", KeyLine("key.pub")));
        return home;
    }

    public static (int Exit, string Out, string Err) RunToolchain(params string[] args) => RunRaw(["toolchain", .. args]);

    /// <summary>The variable set to <paramref name="value"/> until the scope is disposed.</summary>
    public static IDisposable Variable(string name, string? value)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new Restore(() => Environment.SetEnvironmentVariable(name, previous));
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>
    /// The fixtures' mirror over HTTP on the loopback: a request's path, its query left aside, is a
    /// file under <c>mirror/</c> — or the directory given —, unless <see cref="Once"/> or
    /// <see cref="Served"/> gives other bytes for it or <see cref="Missing"/> names it. Each path
    /// asked for, with its query, is kept in <see cref="Asked"/>. Each connection is answered on a
    /// thread of its own and dropped after 10 s of silence: answered one after another, a single
    /// connection that sends nothing held every request behind it until the fetch's 60 s ran out,
    /// and the source counted as unreachable where a test expected it refused.
    /// </summary>
    public sealed class MirrorServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<IDisposable> _undo = [];
        private readonly Thread _thread;
        private readonly string _root;

        public Dictionary<string, byte[]> Served { get; } = new(StringComparer.Ordinal);

        /// <summary>Bytes for a path's next request alone.</summary>
        public Dictionary<string, byte[]> Once { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);
        public List<string> Asked { get; } = [];
        public int Port { get; }
        public string Address => $"http://127.0.0.1:{Port}";

        public MirrorServer(string? root = null)
        {
            _root = root ?? Mirror;
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _thread = new Thread(Serve) { IsBackground = true };
            _thread.Start();
        }

        public void OnDispose(IDisposable undo) => _undo.Add(undo);

        /// <summary>An address where nothing listens.</summary>
        public static string Unreachable()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return $"http://127.0.0.1:{port}";
        }

        private void Serve()
        {
            while (true)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); }
                catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException) { return; }
                new Thread(() =>
                {
                    using (client)
                    {
                        client.ReceiveTimeout = 10_000;
                        try { Answer(client.GetStream()); }
                        catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException) { }
                    }
                }) { IsBackground = true }.Start();
            }
        }

        private void Answer(NetworkStream stream)
        {
            var head = new StringBuilder();
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                var b = stream.ReadByte();
                if (b < 0) return;
                head.Append((char)b);
            }
            var asked = head.ToString().Split(' ')[1];
            lock (Asked) Asked.Add(asked);
            var path = asked.Split('?')[0];
            var file = Path.Combine(_root, path.TrimStart('/'));
            byte[]? body;
            lock (Asked)
            {
                body = Missing.Contains(path) ? null
                    : Once.Remove(path, out var once) ? once
                    : Served.TryGetValue(path, out var given) ? given
                    : File.Exists(file) ? File.ReadAllBytes(file) : null;
            }
            var status = body is null ? "404 Not Found" : "200 OK";
            body ??= "not here\n"u8.ToArray();
            stream.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
            stream.Write(body);
        }

        public void Dispose()
        {
            _listener.Stop();
            foreach (var undo in Enumerable.Reverse(_undo)) undo.Dispose();
        }
    }
}
