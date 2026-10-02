using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A package's programs (design/v5/spec/11 W2 P2, C8): its <c>src/main.lyr</c> and each
/// <c>[[bin]]</c>; <c>build</c> builds every one, <c>run</c> and <c>--emit</c> the one <c>--bin</c>
/// names, else <c>src/main.lyr</c>'s, else the only one. And the toolchain a package asks for
/// (P12; 07 V9). Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class ProgramTests
{
    private static string Says(string word) => $"import std.io {{ println }};\n\nfn main(): void {{\n    println(\"{word}\");\n}}\n";

    private static string Bins(params (string Name, string Entry)[] bins) =>
        string.Concat(bins.Select(b => $"\n[[bin]]\nname = \"{b.Name}\"\nentry = \"{b.Entry}\"\n"));

    private static string Binary(string dir, string name, string profile = "debug")
    {
        var host = Target.Host;
        return Path.Combine(dir, "out", profile, host.Triple, name + host.ExecutableSuffix);
    }

    [Fact]
    public void A_manifest_names_its_programs()
    {
        var dir = Package(("lyric.toml", AppManifest + Bins(("tool", "src/tool.lyr"), ("gen", "src/cli/gen.lyr"))));
        var manifest = Manifest.Read(Path.Combine(dir, "lyric.toml"));
        Assert.Equal(
            [new Binary("tool", Path.GetFullPath(Path.Combine(dir, "src", "tool.lyr")), 5),
             new Binary("gen", Path.GetFullPath(Path.Combine(dir, "src", "cli", "gen.lyr")), 9)],
            manifest.Binaries);
    }

    [Theory]
    [InlineData("\n[[bin]]\nname = \"tool\"\n", "LYR-PKG0002", "[[bin]] 'tool' needs an entry")]
    [InlineData("\n[[bin]]\nentry = \"src/tool.lyr\"\n", "LYR-PKG0002", "[[bin]] needs a name")]
    [InlineData("\n[[bin]]\nname = \"Tool\"\nentry = \"src/tool.lyr\"\n", "LYR-PKG0002", "[[bin]] needs a name")]
    [InlineData("\n[[bin]]\nname = \"tool\"\nentry = \"tools/tool.lyr\"\n", "LYR-PKG0002", "which is no module of the package")]
    [InlineData("\n[[bin]]\nname = \"tool\"\nentry = \"src/tool.txt\"\n", "LYR-PKG0002", "which is no module of the package")]
    [InlineData("\n[[bin]]\nname = \"tool\"\nentry = \"src/a.lyr\"\n\n[[bin]]\nname = \"tool\"\nentry = \"src/b.lyr\"\n", "LYR-PKG0002", "two programs are named 'tool'")]
    [InlineData("\n[[bin]]\nname = \"tool\"\nentry = \"src/tool.lyr\"\npath = \"x\"\n", "LYR-PKG0003", "[[bin]] has no key 'path'")]
    [InlineData("\n[bin]\nname = \"tool\"\n", "LYR-PKG0002", "[[bin]] is an array of tables")]
    public void A_program_of_the_manifest_is_checked(string bins, string code, string why)
    {
        var dir = Package(("lyric.toml", AppManifest + bins));
        var e = Assert.Throws<ManifestException>(() => Manifest.Read(Path.Combine(dir, "lyric.toml")));
        Assert.Equal(code, e.Code);
        Assert.Contains(why, e.Message);
    }

    /// <summary>A <c>[[bin]]</c> named like the package while <c>src/main.lyr</c> is the package's.</summary>
    [Fact]
    public void A_program_is_named_once()
    {
        var dir = Package(("lyric.toml", AppManifest + Bins(("app", "src/other.lyr"))), ("src/main.lyr", Says("app")), ("src/other.lyr", Says("other")));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0002]: two programs are named 'app' — src/main.lyr is 'app's", error);
    }

    [Fact]
    public void Build_builds_every_program()
    {
        var dir = Package(("lyric.toml", AppManifest + Bins(("tool", "src/tool.lyr"))), ("src/main.lyr", Says("app")), ("src/tool.lyr", Says("tool")));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal("app\n", ProcessRunner.Run(Binary(dir, "app"), [], TimeSpan.FromMinutes(1)).Stdout.Replace("\r\n", "\n"));
        Assert.Equal("tool\n", ProcessRunner.Run(Binary(dir, "tool"), [], TimeSpan.FromMinutes(1)).Stdout.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Bin_names_the_program()
    {
        var dir = Package(("lyric.toml", AppManifest + Bins(("tool", "src/tool.lyr"))), ("src/main.lyr", Says("app")), ("src/tool.lyr", Says("tool")));
        var (exit, _, error) = Run("build", "-C", dir, "--bin", "tool");
        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Binary(dir, "tool")));
        Assert.False(File.Exists(Binary(dir, "app")));

        var (unknown, _, why) = Run("build", "-C", dir, "--bin", "tol");
        Assert.Equal(2, unknown);
        Assert.Contains("error[LYR-CLI0003]: no program 'tol' in package 'app': app, tool", why);
    }

    /// <summary><c>run</c> and <c>--emit</c> take <c>src/main.lyr</c>'s program unless <c>--bin</c>
    /// names another.</summary>
    [Fact]
    public void Run_and_emit_take_the_package_s_main_program()
    {
        var dir = Package(("lyric.toml", AppManifest + Bins(("tool", "src/tool.lyr"))), ("src/main.lyr", Says("app")), ("src/tool.lyr", Says("tool")));
        var (exit, output, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.Contains("fn app.main.main", output);
        Assert.DoesNotContain("fn app.tool.main", output);
        var (_, tool, _) = Run("build", "-C", dir, "--emit", "ir", "--bin", "tool");
        Assert.Contains("fn app.tool.main", tool);
    }

    /// <summary>Without <c>src/main.lyr</c>, the only program is the one; of several, <c>--bin</c>
    /// names it.</summary>
    [Fact]
    public void Of_several_programs_bin_names_the_one_to_run()
    {
        var one = Package(("lyric.toml", AppManifest + Bins(("tool", "src/tool.lyr"))), ("src/tool.lyr", Says("tool")));
        var (exit, output, error) = Run("build", "-C", one, "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.Contains("fn app.tool.main", output);

        var two = Package(("lyric.toml", AppManifest + Bins(("tool", "src/tool.lyr"), ("gen", "src/gen.lyr"))),
            ("src/tool.lyr", Says("tool")), ("src/gen.lyr", Says("gen")));
        var (refused, _, why) = Run("run", "-C", two);
        Assert.Equal(2, refused);
        Assert.Contains("error[LYR-CLI0007]: package 'app' has several programs and no src/main.lyr: tool, gen", why);
        Assert.Contains("= help: --bin <name> names the one to run", why);
        Assert.Equal(0, Run("build", "-C", two).Exit);
        Assert.True(File.Exists(Binary(two, "tool")) && File.Exists(Binary(two, "gen")));
    }

    [Fact]
    public void A_program_s_entry_is_a_file()
    {
        var dir = Package(("lyric.toml", AppManifest + Bins(("tool", "src/tool.lyr"))), ("src/main.lyr", Says("app")));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0002]: [[bin]] 'tool' enters at 'src/tool.lyr', and there is no such file", error);
    }

    [Fact]
    public void Bin_names_a_program_of_a_package_not_of_a_file()
    {
        var dir = Package(("hello.lyr", Says("hello")));
        var (exit, _, error) = Run("build", Path.Combine(dir, "hello.lyr"), "--bin", "hello");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0003]: '--bin' names a program of the package; a file is a program by itself", error);
    }

    /// <summary>The toolchain a package asks for (P12): this one is a 5.0 — a pin above refuses the
    /// program, for whichever package of it asks; a pin it meets builds.</summary>
    [Fact]
    public void A_package_names_the_least_toolchain_that_builds_it()
    {
        var pinned = AppManifest.Replace("version = \"0.1.0\"\n", "version = \"0.1.0\"\ntoolchain = \">=99.1\"\n");
        var dir = Package(("lyric.toml", pinned), ("src/main.lyr", Says("app")));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-PKG0009]: package 'app' needs lyric 99.1.0 or later, and this is lyric ", error);

        var met = Package(("lyric.toml", AppManifest.Replace("version = \"0.1.0\"\n", "version = \"0.1.0\"\ntoolchain = \"5.0\"\n")), ("src/main.lyr", Says("app")));
        Assert.Equal(0, Run("build", "-C", met).Exit);

        var deep = Package(
            ("lyric.toml", AppManifest + "\n[dependencies]\ngeo = { path = \"deps/geo\" }\n"), ("src/main.lyr", Says("app")),
            ("deps/geo/lyric.toml", "[package]\nname = \"geo\"\nversion = \"0.1.0\"\ntoolchain = \"99.0\"\n"),
            ("deps/geo/src/shapes.lyr", "pub fn one(): int {\n    return 1;\n}\n"));
        var (refused, _, why) = Run("build", "-C", deep);
        Assert.Equal(2, refused);
        Assert.Contains("error[LYR-PKG0009]: package 'geo' needs lyric 99.0.0 or later", why);
    }

    [Theory]
    [InlineData("\"~5.1\"")]
    [InlineData("\"5\"")]
    [InlineData("\"<6.0\"")]
    [InlineData("51")]
    public void A_toolchain_pin_is_a_least_version(string pin)
    {
        var dir = Package(("lyric.toml", AppManifest.Replace("version = \"0.1.0\"\n", $"version = \"0.1.0\"\ntoolchain = {pin}\n")));
        var e = Assert.Throws<ManifestException>(() => Manifest.Read(Path.Combine(dir, "lyric.toml")));
        Assert.Equal("LYR-PKG0002", e.Code);
        Assert.Contains("is no toolchain requirement: \"5.1\" or \">=5.1\"", e.Message);
    }
}
