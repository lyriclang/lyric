using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;
using static Lyric5.Tests.ToolchainFixture;

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
    private const string Old = "9.0.0-dev.20261101+aaaaaaa";
    private const string New = "9.0.0-dev.20261102+bbbbbbb";
    private const string Stray = "9.0.0-dev.20261031+ccccccc";
    private const string Stable = "8.0.0";

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
        var (exit, output, error) = RunToolchain("install", "dev");
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

        var (again, said, _) = RunToolchain("install", "dev");
        Assert.Equal(0, again);
        Assert.Equal($"lyric {New} is installed already\n", said);
        Assert.Equal(1, server.Asked.Count(p => p.StartsWith("/dev/", StringComparison.Ordinal)));
    }

    [Fact]
    public void List_default_update_and_remove()
    {
        using var server = new MirrorServer();
        var home = Home(server);
        Assert.Equal(0, RunToolchain("install", ToolchainHome.WithoutBuild(Old)).Exit);
        var (stable, output, _) = RunToolchain("install", "stable");
        Assert.Equal(0, stable);
        Assert.DoesNotContain("is the default", output);
        Assert.Equal($"{Old} (default)\n{Stable}\n", RunToolchain("list").Out);
        Assert.Equal($"{Old}\n", RunToolchain("default").Out);

        var (refused, _, error) = RunToolchain("remove", Old);
        Assert.Equal(2, refused);
        Assert.Contains($"error[LYR-CLI0025]: lyric {Old} is the default", error);

        var (updated, said, _) = RunToolchain("update");
        Assert.Equal(0, updated);
        Assert.Contains($"installed lyric {New}", said);
        Assert.Contains($"lyric {New} is the default", said);
        Assert.Contains($"lyric {Old} stays until 'lyric5 toolchain remove {Old}'", said);
        Assert.Equal($"lyric {New} is the newest of the channel 'dev'\n", RunToolchain("update").Out);

        Assert.Equal($"removed lyric {Old}\n", RunToolchain("remove", Old).Out);
        Assert.False(Directory.Exists(home.DirectoryOf(Old)));
        Assert.Equal($"{New} (default)\n{Stable}\n", RunToolchain("list").Out);

        Assert.Equal(0, RunToolchain("default", Stable).Exit);
        Assert.Contains($"lyric {Stable}", File.ReadAllText(Path.Combine(home.Bin, ToolchainHome.BinaryName + Target.Host.ExecutableSuffix)));
        var (missing, _, notInstalled) = RunToolchain("remove", Old);
        Assert.Equal(2, missing);
        Assert.Contains($"error[LYR-CLI0025]: lyric {Old} is not installed", notInstalled);

        var (listed, available, _) = RunToolchain("list", "--available");
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
        Assert.Equal(0, RunToolchain("install", "stable").Exit);
        Assert.Equal(0, RunToolchain("install", "dev").Exit);
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
            var (exit, _, error) = RunToolchain(args.Length == 0 ? new[] { "install", "dev" } : args);
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
        Assert.Equal(2, RunToolchain().Exit);
        Assert.Equal(2, RunToolchain("frob").Exit);
        Assert.Equal(2, RunToolchain("install").Exit);
        Assert.Equal(2, RunToolchain("install", "dev", "stable").Exit);
        Assert.Equal(2, RunToolchain("install", "dev", "--available").Exit);
        Assert.Equal(2, RunToolchain("list", "dev").Exit);
        Assert.Equal(2, RunToolchain("install", "not-a-version").Exit);
        Assert.Contains("no toolchain is installed", RunToolchain("list").Out);
        Assert.Contains("no default", RunToolchain("default").Out);
        var (exit, _, error) = RunToolchain("update");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0025]: there is no default to update", error);
        Assert.Empty(server.Asked);
    }
}
