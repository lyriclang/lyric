using System.Net;
using System.Net.Sockets;
using System.Text;
using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

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
    /// The fixtures' mirror over HTTP on the loopback: a request's path is a file under
    /// <c>mirror/</c>, unless <see cref="Served"/> gives other bytes for it or <see cref="Missing"/>
    /// names it. Each path asked for is kept in <see cref="Asked"/>.
    /// </summary>
    public sealed class MirrorServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<IDisposable> _undo = [];
        private readonly Thread _thread;

        public Dictionary<string, byte[]> Served { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);
        public List<string> Asked { get; } = [];
        public int Port { get; }
        public string Address => $"http://127.0.0.1:{Port}";

        public MirrorServer()
        {
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
                using (client)
                {
                    try { Answer(client.GetStream()); }
                    catch (IOException) { }
                }
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
            var path = head.ToString().Split(' ')[1];
            lock (Asked) Asked.Add(path);
            var file = Path.Combine(Mirror, path.TrimStart('/'));
            var body = Missing.Contains(path) ? null
                : Served.TryGetValue(path, out var given) ? given
                : File.Exists(file) ? File.ReadAllBytes(file) : null;
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
