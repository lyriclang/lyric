using System.Text;
using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.ToolchainFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric toolchain install zig</c> (spec 14 §13 rule 12) against zig's download page served on
/// this machine (<c>zig/make.py</c>): the archive from a community mirror with the tool named, from
/// the page itself last; a mirror whose bytes do not hold passed over for the next; zig's signature,
/// the file its trusted comment names and the index's hash each refusing an archive; then the zig
/// installed found before the PATH.
/// </summary>
[Collection("console")]
public class ZigInstallTests
{
    private const string Version = "9.9.0";

    private static readonly string Download = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "tests", "Lyric5.Tests", "zig", "download");

    private static string File(string version = Version) =>
        $"zig-{ZigFetch.HostKey(Target.Host)}-{version}.{(OperatingSystem.IsWindows() ? "zip" : "tar.xz")}";

    private static MinisignKey Key(string name = "key.pub") => MinisignKey.Parse(System.IO.File.ReadAllText(Path.Combine(Download, "..", name)));

    /// <summary>A fetcher of the page <paramref name="server"/> serves, its mirrors listed as given.</summary>
    private static ZigFetch Fetch(MirrorServer server, params string[] mirrors)
    {
        if (mirrors.Length > 0) server.Served["/community-mirrors.txt"] = Encoding.ASCII.GetBytes(string.Join("", mirrors.Select(m => $"{server.Address}{m}\n")));
        return new ZigFetch(server.Address, Key(), "lyric/test", progress: null, new Random(7));
    }

    private static ToolchainHome FreshHome() => new(TestDirectories.Fresh("lyric5-zig-home-"));

    private static void LeftNothing(ToolchainHome home)
    {
        Assert.False(Directory.Exists(home.ZigDirectoryOf(Version)), "a zig was put in place");
        Assert.Empty(Directory.Exists(home.Toolchains) ? Directory.GetDirectories(home.Toolchains) : []);
    }

    [Fact]
    public void Install_takes_the_archive_from_a_mirror_names_the_tool_and_puts_zig_in_place()
    {
        using var server = new MirrorServer(Download);
        var home = FreshHome();
        Assert.True(Fetch(server, $"/{Version}").Install(home, Version));

        var dir = home.ZigDirectoryOf(Version);
        Assert.True(home.IsZigInstalled(Version));
        Assert.True(System.IO.File.Exists(Path.Combine(dir, "lib", "std", "std.zig")));
        Assert.Contains($"/{Version}/{File()}?source=lyric", server.Asked);
        Assert.Contains($"/{Version}/{File()}.minisig?source=lyric", server.Asked);
        if (!OperatingSystem.IsWindows())
            Assert.True((System.IO.File.GetUnixFileMode(LyricHome.ZigBinary(home.Root, Version)) & UnixFileMode.UserExecute) != 0, "zig is not executable");
        Assert.Empty(Directory.GetDirectories(home.Toolchains));

        // installed already: said, nothing fetched
        var asked = server.Asked.Count;
        Assert.False(Fetch(server).Install(home, Version));
        Assert.Equal(asked, server.Asked.Count);
    }

    [Fact]
    public void A_mirror_whose_archive_does_not_hold_is_passed_over_and_the_page_itself_comes_last()
    {
        using var server = new MirrorServer(Download);
        var archive = System.IO.File.ReadAllBytes(Path.Combine(Download, Version, File()));
        // another archive's bytes; the right bytes under another file's signature; nothing at all
        server.Served[$"/changed/{File()}"] = [.. archive.Take(archive.Length - 1), (byte)(archive[^1] ^ 1)];
        server.Served[$"/renamed/{File()}"] = archive;
        server.Served[$"/renamed/{File()}.minisig"] = System.IO.File.ReadAllBytes(Path.Combine(Download, "wrong-file", File() + ".minisig"));
        server.Served[$"/foreign/{File()}"] = archive;
        server.Served[$"/foreign/{File()}.minisig"] = System.IO.File.ReadAllBytes(Path.Combine(Download, "other-key", File() + ".minisig"));
        string[] bad = ["/changed", "/renamed", "/foreign", "/gone"];

        var home = FreshHome();
        Assert.True(Fetch(server, bad).Install(home, Version));
        Assert.True(home.IsZigInstalled(Version));
        foreach (var mirror in bad) Assert.Contains($"{mirror}/{File()}?source=lyric", server.Asked);
        Assert.Equal($"/{Version}/{File()}.minisig", server.Asked[^1]);

        // with the page itself gone too, the refusals are what is said, and nothing is left
        server.Missing.Add($"/{Version}/{File()}");
        var refusedHome = FreshHome();
        var refused = Assert.Throws<ToolchainException>(() => Fetch(server, bad).Install(refusedHome, Version));
        Assert.Equal("LYR-CLI0024", refused.Code);
        Assert.Contains("has the SHA-256", refused.Message);
        Assert.Contains($"its signature is the one of zig-{ZigFetch.HostKey(Target.Host)}-9.8.0", refused.Message);
        Assert.Contains("signed with the key", refused.Message);
        LeftNothing(refusedHome);
    }

    [Fact]
    public void The_signature_the_file_it_names_and_the_index_s_hash_each_refuse_the_page_s_own_archive()
    {
        var signature = $"/{Version}/{File()}.minisig";
        foreach (var (variant, says) in new[] { ("other-key", "signed with the key"), ("wrong-file", "its signature is the one of") })
        {
            using var server = new MirrorServer(Download);
            server.Served[signature] = System.IO.File.ReadAllBytes(Path.Combine(Download, variant, File() + ".minisig"));
            var home = FreshHome();
            var refused = Assert.Throws<ToolchainException>(() => Fetch(server).Install(home, Version));
            Assert.Equal("LYR-CLI0024", refused.Code);
            Assert.Contains(says, refused.Message);
            LeftNothing(home);
        }

        using (var server = new MirrorServer(Download))
        {
            var index = System.IO.File.ReadAllText(Path.Combine(Download, "index.json"));
            var sha = System.Text.Json.JsonDocument.Parse(index).RootElement.GetProperty(Version).GetProperty(ZigFetch.HostKey(Target.Host)).GetProperty("shasum").GetString()!;
            server.Served["/index.json"] = Encoding.UTF8.GetBytes(index.Replace(sha, new string('0', 64)));
            var home = FreshHome();
            var refused = Assert.Throws<ToolchainException>(() => Fetch(server).Install(home, Version));
            Assert.Equal("LYR-CLI0024", refused.Code);
            Assert.Contains("and its index gives 0000", refused.Message);
            LeftNothing(home);
        }
    }

    [Fact]
    public void A_version_the_index_lacks_an_index_out_of_reach_and_an_archive_without_lib_are_refused()
    {
        using var server = new MirrorServer(Download);
        var home = FreshHome();
        var missing = Assert.Throws<ToolchainException>(() => Fetch(server).Install(home, "9.8.0"));
        Assert.Equal(("LYR-CLI0023", "zig's index has no version 9.8.0"), (missing.Code, missing.Message));

        var gone = Assert.Throws<ToolchainException>(() => new ZigFetch(MirrorServer.Unreachable(), Key(), "lyric/test", null).Install(home, Version));
        Assert.Equal("LYR-CLI0023", gone.Code);
        Assert.StartsWith("zig's index cannot be fetched", gone.Message);

        var layout = Assert.Throws<ToolchainException>(() => Fetch(server).Install(home, "9.9.1"));
        Assert.Equal("LYR-CLI0024", layout.Code);
        Assert.Contains("does not hold one directory with zig", layout.Message);
        LeftNothing(home);
        Assert.False(Directory.Exists(home.ZigDirectoryOf("9.9.1")));
    }

    [Fact]
    public void The_command_installs_the_pinned_zig_and_says_why_it_cannot()
    {
        using var server = new MirrorServer(Download);
        var home = Home(server);
        server.OnDispose(Variable("LYRIC_ZIG_MIRROR", server.Address));
        server.OnDispose(Variable("LYRIC_ZIG_MIRROR_KEY", System.IO.File.ReadAllText(Path.Combine(Download, "..", "key.pub")).Split('\n')[1]));

        // the index serves 9.9.0 and 9.9.1; the toolchain pins tooling/zig-version
        var (exit, _, error) = RunToolchain("install", "zig");
        Assert.True(exit == 2, error);
        Assert.Contains($"error[LYR-CLI0023]: zig's index has no version {LyricHome.ZigVersion}", error);

        using (Variable("LYRIC_ZIG_MIRROR", null))
        {
            var (unpaired, _, says) = RunToolchain("install", "zig");
            Assert.Equal(2, unpaired);
            Assert.Contains("error[LYR-CLI0023]: LYRIC_ZIG_MIRROR_KEY is set and LYRIC_ZIG_MIRROR is not", says);
        }

        // what is installed is listed, zig among it — a file in its place is enough for the list
        Directory.CreateDirectory(home.ZigDirectoryOf(LyricHome.ZigVersion));
        System.IO.File.WriteAllText(LyricHome.ZigBinary(home.Root, LyricHome.ZigVersion), "");
        var (listed, output, _) = RunToolchain("list");
        Assert.Equal(0, listed);
        Assert.Contains($"zig {LyricHome.ZigVersion} (the C compiler)", output);
        var (again, already, _) = RunToolchain("install", "zig");
        Assert.Equal(0, again);
        Assert.Contains($"zig {LyricHome.ZigVersion} is installed already", already);
    }

    [Fact]
    public void The_search_takes_LYRIC_CC_then_the_installed_zig_then_the_PATH()
    {
        var home = FreshHome();
        var onPath = CCompiler.Locate(null, home.Root);
        Assert.False(onPath?.Path.StartsWith(home.Root, StringComparison.Ordinal) ?? false, onPath?.Path);

        // a stand-in zig in the pinned version's place: says a version, as zig does
        var source = Path.Combine(TestDirectories.Fresh("lyric5-zig-stand-in-"), "zig.c");
        System.IO.File.WriteAllText(source, "#include <stdio.h>\n\nint main(void) {\n    puts(\"0.0.0-stand-in\");\n    return 0;\n}\n");
        var compiler = onPath ?? throw new InvalidOperationException("no C compiler");
        var binary = LyricHome.ZigBinary(home.Root, LyricHome.ZigVersion);
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        string[] compile = [source, "-o", binary];
        var built = ProcessRunner.Run(compiler.Path, compiler.Kind == CCompilerKind.Zig ? ["cc", .. compile] : compile, TimeSpan.FromMinutes(10));
        Assert.True(built.ExitCode == 0, built.Stderr);

        var installed = CCompiler.Locate(null, home.Root);
        Assert.Equal((CCompilerKind.Zig, binary, "0.0.0-stand-in"), (installed?.Kind, installed?.Path, installed?.Version));
        Assert.Equal(compiler.Path, CCompiler.Locate(compiler.Path, home.Root)?.Path);
        // another version's zig is not the pinned one
        Directory.Move(LyricHome.ZigDirectory(home.Root, LyricHome.ZigVersion), LyricHome.ZigDirectory(home.Root, "0.0.1"));
        Assert.Equal(onPath?.Path, CCompiler.Locate(null, home.Root)?.Path);
    }
}
