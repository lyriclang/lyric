using System.Net;
using System.Net.Sockets;
using System.Text;
using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.ToolchainFixture;

namespace Lyric5.Tests;

/// <summary>
/// What an install does with more than one source and more than one key: a source that cannot be
/// reached, or that serves something refused, is followed by the next one and said; only what holds
/// is accepted, wherever it came from; an index older than one this machine has seen is refused; and
/// the keys in the binary are the ones in <c>tooling/release-keys</c>.
/// </summary>
public class ToolchainSourcesTests
{
    private const string Newest = "9.0.0-dev.20261102+bbbbbbb";

    private static string Fixture(string name) => Path.Combine(Fixtures, name);

    private static byte[] Forged()
    {
        var forged = File.ReadAllBytes(Path.Combine(Mirror, "dev.json"));
        forged[forged.Length / 2] ^= 1;
        return forged;
    }

    /// <summary>A fetch over mirrors in order, with the fixtures' key; what it warns of lands in <paramref name="warnings"/>.</summary>
    private static ToolchainFetch Fetch(List<string> warnings, string? seen, params string[] addresses) =>
        new([.. addresses.Select(ToolchainSource.OfMirror)], _ => [MinisignKey.Parse(KeyLine("key.pub"))], "lyric/test", null, warnings.Add, seen);

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
        return $"/{ToolchainHome.ChannelOf(version)}/lyric-{ToolchainHome.WithoutBuild(version)}-{rid}.{(OperatingSystem.IsWindows() ? "zip" : "tar.gz")}";
    }

    [Fact]
    public void A_mirror_that_cannot_be_reached_is_followed_by_the_next_source_in_silence()
    {
        using var second = new MirrorServer();
        var warnings = new List<string>();
        var index = Fetch(warnings, null, MirrorServer.Unreachable(), second.Address).Index(ChannelIndex.Dev);
        Assert.Equal(Newest, index.Newest!.Version);
        Assert.Empty(warnings);
    }

    [Fact]
    public void A_mirror_that_serves_a_forged_index_is_said_and_the_next_source_asked()
    {
        using var first = new MirrorServer();
        using var second = new MirrorServer();
        first.Served["/dev.json"] = Forged();
        var warnings = new List<string>();
        var index = Fetch(warnings, null, first.Address, second.Address).Index(ChannelIndex.Dev);
        Assert.Equal(Newest, index.Newest!.Version);
        var warning = Assert.Single(warnings);
        Assert.Contains($"the index of the channel 'dev' from {first.Address} is refused", warning);
    }

    [Fact]
    public void A_connection_that_sends_nothing_does_not_hold_the_mirrors_answers()
    {
        using var server = new MirrorServer();
        using var silent = new TcpClient();
        silent.Connect(IPAddress.Loopback, server.Port);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var index = Fetch([], null, server.Address).Index(ChannelIndex.Dev);
        clock.Stop();
        Assert.Equal(Newest, index.Newest!.Version);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"the index took {clock.Elapsed}");
    }

    [Fact]
    public void A_forged_index_at_every_source_installs_nothing()
    {
        using var first = new MirrorServer();
        using var second = new MirrorServer();
        first.Served["/dev.json"] = Forged();
        second.Served["/dev.json"] = Forged();
        var refused = Assert.Throws<ToolchainException>(() => Fetch([], null, first.Address, second.Address).Index(ChannelIndex.Dev));
        Assert.Equal("LYR-CLI0024", refused.Code);
        Assert.Contains(first.Address, refused.Message);
        Assert.Contains(second.Address, refused.Message);
    }

    [Fact]
    public void A_refusal_outweighs_a_source_that_cannot_be_reached()
    {
        using var first = new MirrorServer();
        first.Served["/dev.json"] = Forged();
        var refused = Assert.Throws<ToolchainException>(() => Fetch([], null, first.Address, MirrorServer.Unreachable()).Index(ChannelIndex.Dev));
        Assert.Equal("LYR-CLI0024", refused.Code);
    }

    [Fact]
    public void A_publish_between_the_two_requests_is_healed_by_fetching_the_pair_again()
    {
        using var server = new MirrorServer();
        // the index from before the publish, once; the signature and every later request from after it
        server.Once["/dev.json"] = Forged();
        var warnings = new List<string>();
        var index = Fetch(warnings, null, server.Address).Index(ChannelIndex.Dev);
        Assert.Equal(Newest, index.Newest!.Version);
        Assert.Empty(warnings);
        Assert.Equal(2, server.Asked.Count(path => path == "/dev.json"));
    }

    [Fact]
    public void An_archive_a_mirror_lacks_or_has_broken_comes_from_the_next_source()
    {
        using var first = new MirrorServer();
        using var second = new MirrorServer();
        var archive = ArchivePath(Newest);

        void Installs(params string[] expected)
        {
            var home = new ToolchainHome(TestDirectories.Fresh("lyric5-home-"));
            var warnings = new List<string>();
            var fetch = Fetch(warnings, null, first.Address, second.Address);
            var (release, channel) = fetch.Release(ChannelIndex.Dev);
            Assert.True(fetch.Install(home, release, channel));
            Assert.Equal(new[] { Newest }, home.Installed());
            var warning = Assert.Single(warnings);
            foreach (var part in expected) Assert.Contains(part, warning);
        }

        first.Missing.Add(archive);
        Installs($"{first.Address}{archive}: 404");
        first.Missing.Clear();

        var bytes = File.ReadAllBytes(Path.Combine(Mirror, archive.TrimStart('/')));
        var broken = bytes.ToArray();
        broken[broken.Length / 2] ^= 1;
        first.Served[archive] = broken;
        Installs("has the SHA-256");
    }

    [Fact]
    public void An_archive_broken_at_every_source_says_so_rather_than_that_one_was_missing()
    {
        using var first = new MirrorServer();
        using var second = new MirrorServer();
        var archive = ArchivePath(Newest);
        first.Missing.Add(archive);
        var broken = File.ReadAllBytes(Path.Combine(Mirror, archive.TrimStart('/')));
        broken[broken.Length / 2] ^= 1;
        second.Served[archive] = broken;

        var home = new ToolchainHome(TestDirectories.Fresh("lyric5-home-"));
        var fetch = Fetch([], null, first.Address, second.Address);
        var (release, channel) = fetch.Release(ChannelIndex.Dev);
        var refused = Assert.Throws<ToolchainException>(() => fetch.Install(home, release, channel));
        Assert.Equal("LYR-CLI0024", refused.Code);
        Assert.Empty(home.Installed());
    }

    [Fact]
    public void A_download_that_stops_sending_counts_as_a_source_that_is_gone()
    {
        using var second = new MirrorServer();
        var archive = ArchivePath(Newest);
        // the index is served as usual; the archive's first bytes come, then nothing more
        using var stalling = new TcpListener(IPAddress.Loopback, 0);
        stalling.Start();
        var port = ((IPEndPoint)stalling.LocalEndpoint).Port;
        using var hold = new ManualResetEventSlim();
        var serving = new Thread(() =>
        {
            try
            {
                using var client = stalling.AcceptTcpClient();
                var stream = client.GetStream();
                var buffer = new byte[4096];
                _ = stream.Read(buffer);
                stream.Write(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 100000\r\nConnection: close\r\n\r\nabc"));
                hold.Wait(TimeSpan.FromSeconds(120));
            }
            catch (Exception e) when (e is SocketException or IOException or ObjectDisposedException) { }
        }) { IsBackground = true };
        serving.Start();

        var home = new ToolchainHome(TestDirectories.Fresh("lyric5-home-"));
        var warnings = new List<string>();
        IReadOnlyList<ToolchainSource> sources =
        [
            new("stalling", channel => second.Address + "/" + ChannelIndex.FileName(channel), (_, _, _) => $"http://127.0.0.1:{port}{archive}"),
            ToolchainSource.OfMirror(second.Address),
        ];
        var fetch = new ToolchainFetch(sources, _ => [MinisignKey.Parse(KeyLine("key.pub"))], "lyric/test", null, warnings.Add, null, TimeSpan.FromMilliseconds(300));
        var (release, channel) = fetch.Release(ChannelIndex.Dev);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.True(fetch.Install(home, release, channel));
        clock.Stop();
        hold.Set();
        Assert.Equal(new[] { Newest }, home.Installed());
        Assert.Contains($"http://127.0.0.1:{port}{archive}: nothing came for 0.3 s", Assert.Single(warnings));
        // given up on by the stall's limit, not by the server closing in the end
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"it took {clock.Elapsed}");
    }

    [Fact]
    public void An_index_older_than_the_one_seen_here_is_refused_and_the_next_source_asked()
    {
        using var mirror = new MirrorServer();
        var seen = TestDirectories.Fresh("lyric5-seen-");
        var key = MinisignKey.Parse(KeyLine("key.pub"));

        Assert.Equal(1000, Fetch([], seen, mirror.Address).Index(ChannelIndex.Dev).Sequence);
        Assert.Equal("1000", File.ReadAllText(Path.Combine(seen, $"dev-{key.IdText}")).Trim());

        using var rolledBack = new MirrorServer();
        rolledBack.Served["/dev.json"] = File.ReadAllBytes(Fixture("older-dev.json"));
        rolledBack.Served["/dev.json.minisig"] = File.ReadAllBytes(Fixture("older-dev.json.minisig"));

        var alone = Assert.Throws<ToolchainException>(() => Fetch([], seen, rolledBack.Address).Index(ChannelIndex.Dev));
        Assert.Equal("LYR-CLI0024", alone.Code);
        Assert.Contains("it is number 999, and this machine has seen number 1000", alone.Message);

        var warnings = new List<string>();
        Assert.Equal(1000, Fetch(warnings, seen, rolledBack.Address, mirror.Address).Index(ChannelIndex.Dev).Sequence);
        Assert.Contains("it is number 999", Assert.Single(warnings));

        // the control: the same older index on a machine that has seen no newer one
        Assert.Equal(999, Fetch([], TestDirectories.Fresh("lyric5-seen-"), rolledBack.Address).Index(ChannelIndex.Dev).Sequence);
        // and the same number again is no rollback
        Assert.Equal(1000, Fetch([], seen, mirror.Address).Index(ChannelIndex.Dev).Sequence);
    }

    [Fact]
    public void The_numbers_of_one_signer_say_nothing_about_another_s()
    {
        using var rolledBack = new MirrorServer();
        rolledBack.Served["/dev.json"] = File.ReadAllBytes(Fixture("older-dev.json"));
        rolledBack.Served["/dev.json.minisig"] = File.ReadAllBytes(Fixture("older-dev.json.minisig"));
        var seen = TestDirectories.Fresh("lyric5-seen-");
        File.WriteAllText(Path.Combine(seen, "dev-0000000000000000"), "5000\n");
        Assert.Equal(999, Fetch([], seen, rolledBack.Address).Index(ChannelIndex.Dev).Sequence);
    }

    [Fact]
    public void An_install_remembers_the_number_under_the_home()
    {
        using var server = new MirrorServer();
        var home = Home(server);
        Assert.Equal(0, RunToolchain("install", "dev").Exit);
        var key = MinisignKey.Parse(KeyLine("key.pub"));
        Assert.Equal("1000", File.ReadAllText(Path.Combine(home.Root, "seen", $"dev-{key.IdText}")).Trim());
    }

    [Fact]
    public void Each_channel_accepts_its_own_key_and_the_reserve_and_not_the_other_channel_s()
    {
        string Ids(string channel) => string.Join(" ", ToolchainFetch.KeysOf(channel).Select(k => k.IdText));
        Assert.Equal("88FC8745E9E5D10C 4AD2F77EA29B9DA1", Ids(ChannelIndex.Dev));
        Assert.Equal("2CE7B166C1F1B873 4AD2F77EA29B9DA1", Ids(ChannelIndex.Stable));
    }

    [Theory]
    [InlineData("dev.pub", ToolchainFetch.DevKey)]
    [InlineData("stable.pub", ToolchainFetch.StableKey)]
    [InlineData("reserve.pub", ToolchainFetch.ReserveKey)]
    public void The_keys_in_the_binary_are_the_ones_in_the_repository(string file, string line)
    {
        var path = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tooling", "release-keys", file);
        Assert.Equal(line, File.ReadAllText(path).Split('\n')[1].Trim());
    }

    [Fact]
    public void An_index_signed_by_any_of_the_keys_is_read_and_says_which()
    {
        var json = File.ReadAllBytes(Path.Combine(Mirror, "dev.json"));
        var signature = File.ReadAllText(Path.Combine(Mirror, "dev.json.minisig"));
        var key = MinisignKey.Parse(KeyLine("key.pub"));
        var other = MinisignKey.Parse(KeyLine("other.pub"));
        Assert.Equal(key.IdText, ChannelIndex.Read(json, signature, [other, key], ChannelIndex.Dev).SignedBy);
        Assert.Equal(key.IdText, ChannelIndex.Read(json, signature, [key, other], ChannelIndex.Dev).SignedBy);
        Assert.Throws<SignatureException>(() => ChannelIndex.Read(json, signature, [other], ChannelIndex.Dev));

        var forged = Forged();
        var refused = Assert.Throws<SignatureException>(() => ChannelIndex.Read(forged, signature, [other, key], ChannelIndex.Dev));
        Assert.Equal("the signature does not match the file", refused.Message);
    }

    private static string Archives(params string[] names)
    {
        var directory = TestDirectories.Fresh("lyric5-archives-");
        foreach (var name in names) File.WriteAllText(Path.Combine(directory, name), name);
        return directory;
    }

    [Fact]
    public void The_next_index_puts_the_new_version_first_and_keeps_as_many_as_asked()
    {
        var directory = Archives("lyric-5.0.0-dev.3-abc-linux-x64.tar.gz", "lyric-5.0.0-dev.3-abc-win-x64.zip", "lyric-5.0.0-dev.3-abc-win-x64.zip.minisig", "SHA256SUMS");
        ChannelIndex? index = null;
        // three builds of one day: they compare equal, and the newest is the one the file gives first
        foreach (var (build, number) in new[] { ("aaaaaaa", 10L), ("bbbbbbb", 20L), ("ccccccc", 30L) })
            index = ChannelIndex.Next(index, ChannelIndex.Dev, ChannelIndex.ReleaseFrom(directory, $"5.0.0-dev.20261105+{build}", "2026-11-05"), number, keep: 2);

        var read = ChannelIndex.Parse(index!.ToJson());
        Assert.Equal(30, read.Sequence);
        Assert.Equal(["5.0.0-dev.20261105+ccccccc", "5.0.0-dev.20261105+bbbbbbb"], read.Releases.Select(r => r.Version));
        Assert.Equal("5.0.0-dev.20261105+ccccccc", read.Newest!.Version);

        var archives = read.Newest.Archives;
        Assert.Equal(["x86_64-linux-gnu", "x86_64-windows-gnu"], archives.Keys.Order(StringComparer.Ordinal));
        var linux = archives["x86_64-linux-gnu"];
        Assert.Equal("lyric-5.0.0-dev.3-abc-linux-x64.tar.gz", linux.File);
        Assert.Equal(linux.File.Length, linux.Size);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(linux.File))), linux.Sha256);
        // the version's '+' stands as itself, for the eye and for the signature's reader
        Assert.Contains("\"5.0.0-dev.20261105+ccccccc\"", Encoding.UTF8.GetString(index.ToJson()));
    }

    [Fact]
    public void The_next_index_replaces_a_version_built_again_rather_than_listing_it_twice()
    {
        var directory = Archives("lyric-5.0.0-linux-x64.tar.gz");
        var release = ChannelIndex.ReleaseFrom(directory, "5.0.0", "2026-11-05");
        var again = ChannelIndex.Next(ChannelIndex.Next(null, ChannelIndex.Stable, release, 1, 10), ChannelIndex.Stable, release, 2, 10);
        Assert.Single(ChannelIndex.Parse(again.ToJson()).Releases);
    }

    [Fact]
    public void A_number_that_does_not_grow_or_an_index_of_another_channel_makes_no_next_index()
    {
        var release = ChannelIndex.ReleaseFrom(Archives("lyric-5.0.0-linux-x64.tar.gz"), "5.0.0", "2026-11-05");
        var first = ChannelIndex.Next(null, ChannelIndex.Stable, release, 5, 10);
        Assert.Contains("does not grow", Assert.Throws<InvalidDataException>(() => ChannelIndex.Next(first, ChannelIndex.Stable, release, 5, 10)).Message);
        Assert.Contains("stands where", Assert.Throws<InvalidDataException>(() => ChannelIndex.Next(first, ChannelIndex.Dev, release, 6, 10)).Message);
    }

    [Theory]
    [InlineData("lyric-5.0.0-riscv64.tar.gz", "names no target")]
    [InlineData("lyric-5.0.0-linux-x64.rpm", "names no target")]
    public void An_archive_without_a_target_of_a_release_is_refused(string name, string message)
    {
        var refused = Assert.Throws<InvalidDataException>(() => ChannelIndex.ReleaseFrom(Archives(name), "5.0.0", "2026-11-05"));
        Assert.Contains(message, refused.Message);
    }

    [Fact]
    public void Two_archives_for_one_target_or_none_at_all_are_refused()
    {
        Assert.Contains("two archives", Assert.Throws<InvalidDataException>(() => ChannelIndex.ReleaseFrom(Archives("lyric-a-linux-x64.tar.gz", "lyric-b-linux-x64.tar.gz"), "5.0.0", "2026-11-05")).Message);
        Assert.Contains("no archive", Assert.Throws<InvalidDataException>(() => ChannelIndex.ReleaseFrom(Archives("SHA256SUMS"), "5.0.0", "2026-11-05")).Message);
    }
}
