using System.Net;
using System.Net.Sockets;
using System.Text;
using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric toolchain</c> (spec 14 §13) against a mirror on this machine: the fixtures of
/// <c>toolchain/make.py</c> — channels signed with a key whose secret half is gone, an archive per
/// Tier 1 target holding a stand-in toolchain — served as a mirror serves them. An install puts the
/// toolchain in place only after the signature, the size, the hash and its own manifest hold; a
/// refusal leaves nothing behind.
/// </summary>
[Collection("console")]
public class ToolchainCommandTests
{
    private static readonly string Fixtures = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tests", "Lyric5.Tests", "toolchain");
    private static readonly string Mirror = Path.Combine(Fixtures, "mirror");

    private const string Old = "9.0.0-dev.20261101+aaaaaaa";
    private const string New = "9.0.0-dev.20261102+bbbbbbb";
    private const string Stray = "9.0.0-dev.20261031+ccccccc";
    private const string Stable = "8.0.0";

    private static string KeyLine(string name) => File.ReadAllText(Path.Combine(Fixtures, name)).Split('\n')[1];

    /// <summary>The archive of <paramref name="version"/> for this machine, as the mirror lays it.</summary>
    private static string ArchivePath(string version)
    {
        var rid = Target.Host.Triple switch
        {
            "x86_64-linux-gnu" => "linux-x64",
            "aarch64-linux-gnu" => "linux-arm64",
            "x86_64-windows-gnu" => "win-x64",
            "aarch64-macos" => "osx-arm64",
            _ => "osx-x64",
        };
        var channel = ToolchainHome.ChannelOf(version);
        return $"/{channel}/lyric-{ToolchainHome.WithoutBuild(version)}-{rid}.{(OperatingSystem.IsWindows() ? "zip" : "tar.gz")}";
    }

    [Fact]
    public void Install_dev_puts_the_newest_in_place_and_makes_it_the_default()
    {
        using var server = new MirrorServer();
        var home = Home(server);
        var (exit, output, error) = Toolchain("install", "dev");
        Assert.True(exit == 0, error);
        Assert.Contains($"installed lyric {New} in {home.DirectoryOf(New)}", output);
        Assert.Contains($"lyric {New} is the default", output);
        Assert.Equal(new[] { "/dev.json", "/dev.json.minisig", ArchivePath(New) }, server.Asked);

        var binary = home.BinaryOf(New);
        Assert.Contains($"lyric {New}", File.ReadAllText(binary));
        if (!OperatingSystem.IsWindows()) Assert.True(File.GetUnixFileMode(binary).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal($"default = \"{New}\"\n", File.ReadAllText(home.Config));
        Assert.Equal(File.ReadAllText(binary), File.ReadAllText(Path.Combine(home.Bin, ToolchainHome.BinaryName + Target.Host.ExecutableSuffix)));
        Assert.Equal(home.DirectoryOf(New), ToolchainHome.Running(home.Bin));
        Assert.Equal(new[] { New }, home.Installed());
        Assert.Equal(new[] { New }, Directory.GetDirectories(home.Toolchains).Select(d => Path.GetFileName(d)).ToArray());
        if (OperatingSystem.IsMacOS())
        {
            // fetched by the toolchain itself, the binary carries no quarantine for Gatekeeper
            var quarantine = ProcessRunner.Run("xattr", ["-p", "com.apple.quarantine", binary], TimeSpan.FromSeconds(30));
            Assert.NotEqual(0, quarantine.ExitCode);
        }

        var (again, said, _) = Toolchain("install", "dev");
        Assert.Equal(0, again);
        Assert.Equal($"lyric {New} is installed already\n", said);
        Assert.Equal(1, server.Asked.Count(p => p.StartsWith("/dev/", StringComparison.Ordinal)));
    }

    [Fact]
    public void List_default_update_and_remove()
    {
        using var server = new MirrorServer();
        var home = Home(server);
        Assert.Equal(0, Toolchain("install", ToolchainHome.WithoutBuild(Old)).Exit);
        var (stable, output, _) = Toolchain("install", "stable");
        Assert.Equal(0, stable);
        Assert.DoesNotContain("is the default", output);
        Assert.Equal($"{Old} (default)\n{Stable}\n", Toolchain("list").Out);
        Assert.Equal($"{Old}\n", Toolchain("default").Out);

        var (refused, _, error) = Toolchain("remove", Old);
        Assert.Equal(2, refused);
        Assert.Contains($"error[LYR-CLI0025]: lyric {Old} is the default", error);

        var (updated, said, _) = Toolchain("update");
        Assert.Equal(0, updated);
        Assert.Contains($"installed lyric {New}", said);
        Assert.Contains($"lyric {New} is the default", said);
        Assert.Contains($"lyric {Old} stays until 'lyric5 toolchain remove {Old}'", said);
        Assert.Equal($"lyric {New} is the newest of the channel 'dev'\n", Toolchain("update").Out);

        Assert.Equal($"removed lyric {Old}\n", Toolchain("remove", Old).Out);
        Assert.False(Directory.Exists(home.DirectoryOf(Old)));
        Assert.Equal($"{New} (default)\n{Stable}\n", Toolchain("list").Out);

        Assert.Equal(0, Toolchain("default", Stable).Exit);
        Assert.Contains($"lyric {Stable}", File.ReadAllText(Path.Combine(home.Bin, ToolchainHome.BinaryName + Target.Host.ExecutableSuffix)));
        var (missing, _, notInstalled) = Toolchain("remove", Old);
        Assert.Equal(2, missing);
        Assert.Contains($"error[LYR-CLI0025]: lyric {Old} is not installed", notInstalled);

        var (listed, available, _) = Toolchain("list", "--available");
        Assert.Equal(0, listed);
        Assert.Equal(
            $"{Stable,-34} stable 2026-11-05  installed\n"
            + $"{New,-34} dev    2026-11-02  installed\n{Old,-34} dev    2026-11-01\n{Stray,-34} dev    2026-11-31\n",
            available);
    }

    [Fact]
    public void The_running_toolchain_and_a_stray_bin_are_not_touched()
    {
        using var server = new MirrorServer();
        var home = Home(server);
        Assert.Equal(0, Toolchain("install", "stable").Exit);
        Assert.Equal(0, Toolchain("install", "dev").Exit);
        var refused = Assert.Throws<ToolchainException>(() => home.Remove(New, running: home.DirectoryOf(New)));
        Assert.Equal("LYR-CLI0025", refused.Code);
        Assert.Contains("is the toolchain that runs this command", refused.Message);
        Assert.True(home.IsInstalled(New));

        // a bin the toolchain did not make is the person's
        var other = new ToolchainHome(TestDirectories.Fresh("lyric5-home-"));
        Directory.CreateDirectory(Path.Combine(other.Toolchains, Stable));
        File.WriteAllText(Path.Combine(other.Toolchains, Stable, ToolchainManifest.FileName), "{}");
        Directory.CreateDirectory(other.Bin);
        var kept = Assert.Throws<ToolchainException>(() => other.SetDefault(Stable));
        Assert.Contains("is no link the toolchain made", kept.Message);
        Assert.True(Directory.Exists(other.Bin));
    }

    [Fact]
    public void What_does_not_hold_is_refused_and_leaves_nothing()
    {
        using var server = new MirrorServer();
        var home = Home(server);
        var archive = ArchivePath(New);
        var bytes = File.ReadAllBytes(Path.Combine(Mirror, archive.TrimStart('/')));

        void Refused(string code, string message, params string[] args)
        {
            var (exit, _, error) = Toolchain(args.Length == 0 ? new[] { "install", "dev" } : args);
            Assert.True(exit == 2, error);
            Assert.Contains($"error[{code}]: {message}", error);
            Assert.Empty(home.Installed());
            Assert.True(!Directory.Exists(home.Toolchains) || Directory.GetFileSystemEntries(home.Toolchains).Length == 0,
                string.Join(", ", Directory.GetFileSystemEntries(home.Toolchains)));
            Assert.False(File.Exists(home.Config));
            server.Served.Clear();
        }

        server.Served["/dev.json"] = File.ReadAllBytes(Path.Combine(Fixtures, "badhash.json"));
        server.Served["/dev.json.minisig"] = File.ReadAllBytes(Path.Combine(Fixtures, "badhash.json.minisig"));
        Refused("LYR-CLI0024", $"{Path.GetFileName(archive)} has the SHA-256 ");

        var forged = File.ReadAllBytes(Path.Combine(Mirror, "dev.json"));
        forged[forged.Length / 2] ^= 1;
        server.Served["/dev.json"] = forged;
        Refused("LYR-CLI0024", "the index of the channel 'dev' from http://127.0.0.1:");

        server.Served[archive] = bytes[..^7];
        Refused("LYR-CLI0024", $"{Path.GetFileName(archive)} ended after {bytes.Length - 7} of the {bytes.Length} bytes");
        server.Served[archive] = [.. bytes, 0];
        Refused("LYR-CLI0024", $"{Path.GetFileName(archive)} is larger than the {bytes.Length} bytes");

        Refused("LYR-CLI0024", $"the archive of lyric {Stray} does not hold what its toolchain.json lists: not in the manifest: stray.txt", "install", Stray);
        Refused("LYR-CLI0023", "the channel 'dev' has no version 9.9.9-dev.1", "install", "9.9.9-dev.1");
        Refused("LYR-CLI0023", "the channel 'stable' has no version 9.9.9", "install", "9.9.9");

        server.Missing.Add(archive);
        Refused("LYR-CLI0023", $"http://127.0.0.1:{server.Port}{archive}: 404");
        server.Missing.Clear();

        using (Variable("LYRIC_MIRROR_KEY", KeyLine("other.pub")))
            Refused("LYR-CLI0024", "the index of the channel 'dev' from http://127.0.0.1:");
        using (Variable("LYRIC_MIRROR", MirrorServer.Unreachable()))
            Refused("LYR-CLI0023", "the index of the channel 'dev' cannot be fetched: http://127.0.0.1:");
        using (Variable("LYRIC_MIRROR", null))
        {
            server.Asked.Clear();
            Refused("LYR-CLI0023", "LYRIC_MIRROR_KEY is set and LYRIC_MIRROR is not");
            using (Variable("LYRIC_MIRROR_KEY", null))
                Refused("LYR-CLI0023", "this toolchain carries no key to check a channel's signature with");
            Assert.Empty(server.Asked);
        }
    }

    [Fact]
    public void The_command_line_is_read_strictly()
    {
        using var server = new MirrorServer();
        Home(server);
        Assert.Equal(2, Toolchain().Exit);
        Assert.Equal(2, Toolchain("frob").Exit);
        Assert.Equal(2, Toolchain("install").Exit);
        Assert.Equal(2, Toolchain("install", "dev", "stable").Exit);
        Assert.Equal(2, Toolchain("install", "dev", "--available").Exit);
        Assert.Equal(2, Toolchain("list", "dev").Exit);
        Assert.Equal(2, Toolchain("install", "not-a-version").Exit);
        Assert.Contains("no toolchain is installed", Toolchain("list").Out);
        Assert.Contains("no default", Toolchain("default").Out);
        var (exit, _, error) = Toolchain("update");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0025]: there is no default to update", error);
        Assert.Empty(server.Asked);
    }

    // --- the fixture ---------------------------------------------------------------------------

    /// <summary>A fresh home and the environment that points the command at it and at the mirror;
    /// the variables are restored when the server goes.</summary>
    private static ToolchainHome Home(MirrorServer server)
    {
        var home = new ToolchainHome(TestDirectories.Fresh("lyric5-home-"));
        server.OnDispose(Variable("LYRIC_HOME", home.Root));
        server.OnDispose(Variable("LYRIC_MIRROR", server.Address));
        server.OnDispose(Variable("LYRIC_MIRROR_KEY", KeyLine("key.pub")));
        return home;
    }

    private static (int Exit, string Out, string Err) Toolchain(params string[] args) => RunRaw(["toolchain", .. args]);

    /// <summary>The variable set to <paramref name="value"/> until the scope is disposed.</summary>
    private static IDisposable Variable(string name, string? value)
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
    private sealed class MirrorServer : IDisposable
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
