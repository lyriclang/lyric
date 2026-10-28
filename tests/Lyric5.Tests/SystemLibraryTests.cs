using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// What a package takes from the machine (spec 15 §5 rules 9–11): libraries of the system through
/// <c>pkg-config</c> — here a pkg-config of the test's own (<c>PKG_CONFIG</c>), which answers for a
/// library the test built, so nothing hangs on what the machine has installed — and macOS
/// frameworks; each the host's, so a build for another system that needs one is refused. Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class SystemLibraryTests
{
    private const string UseC = """
        #include <stdint.h>
        #include <pkgdemo.h>
        int64_t value_of(int64_t n) { return pkgdemo_value() * n + FROM_PKG; }
        """;

    private const string CallMain = """
        import std.io { println };

        extern "C" fn value_of(n: int): int;

        fn main(): void {
            println(f"{value_of(2)}");
        }
        """;

    /// <summary>A pkg-config that knows <c>pkgdemo</c>: its header's directory, a definition, and the
    /// archive the test built, by its directory and name; another name it does not know.</summary>
    private static string FakePkgConfig(string dir)
    {
        var include = Path.Combine(dir, "sys", "include");
        var lib = Path.Combine(dir, "sys", "lib");
        Directory.CreateDirectory(include);
        File.WriteAllText(Path.Combine(include, "pkgdemo.h"), "#include <stdint.h>\nint64_t pkgdemo_value(void);\n");
        NativeLinkTests.Archive(dir, "sys/lib/libpkgdemo.a", Target.Host, "#include <stdint.h>\nint64_t pkgdemo_value(void) { return 20 + BIAS; }\n");
        var script = Path.Combine(dir, "sys", "pkg-config");
        File.WriteAllText(script, $"""
            #!/bin/sh
            for name in "$@"; do
                case "$name" in
                    --*) ;;
                    pkgdemo) ;;
                    *) echo "Package '$name' not found" >&2; exit 1 ;;
                esac
            done
            echo "-I{include} -DFROM_PKG=2 -L{lib} -lpkgdemo"
            """.Replace("\r\n", "\n") + "\n");
        File.SetUnixFileMode(script, (UnixFileMode)0b111_101_101);
        return script;
    }

    private static (int Exit, string Out, string Err) WithPkgConfig(string program, params string[] args)
    {
        var saved = Environment.GetEnvironmentVariable("PKG_CONFIG");
        Environment.SetEnvironmentVariable("PKG_CONFIG", program);
        try { return Run(args); }
        finally { Environment.SetEnvironmentVariable("PKG_CONFIG", saved); }
    }

    private static string Package(string native) =>
        PackageFixture.Package(("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n" + native),
            ("native/use.c", UseC), ("src/main.lyr", CallMain));

    [Fact]
    public void A_library_of_pkg_config_gives_its_headers_definitions_and_link()
    {
        if (OperatingSystem.IsWindows()) return;   // the test's pkg-config is a shell script
        var dir = Package("pkgConfig = [\"pkgdemo\"]\n");
        var (exit, _, error) = WithPkgConfig(FakePkgConfig(dir), "build", "-C", dir);
        Assert.True(exit == 0, error);
        var exe = Path.Combine(dir, "out", "debug", Target.Host.Triple, "app" + Target.Host.ExecutableSuffix);
        Assert.Equal("42\n", ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1)).Stdout.Replace("\r\n", "\n"));
    }

    /// <summary>A build script asks pkg-config as the manifest does.</summary>
    [Fact]
    public void A_script_asks_pkg_config_as_the_manifest_does()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Package("");
        File.WriteAllText(Path.Combine(dir, "build.lyr"), "import std.build;\n\nfn main(): void {\n    build.pkgConfig(\"pkgdemo\");\n}\n");
        var (exit, _, error) = WithPkgConfig(FakePkgConfig(dir), "build", "-C", dir);
        Assert.True(exit == 0, error);
    }

    [Fact]
    public void A_name_pkg_config_does_not_know_stops_the_build()
    {
        if (OperatingSystem.IsWindows()) return;
        var dir = Package("pkgConfig = [\"pkgdemo\", \"nope\"]\n");
        var (exit, _, error) = WithPkgConfig(FakePkgConfig(dir), "build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-BLD0004]: app's pkgConfig: Package 'nope' not found", error);
    }

    [Fact]
    public void Without_pkg_config_the_build_stops()
    {
        var dir = Package("pkgConfig = [\"pkgdemo\"]\n");
        var (exit, _, error) = WithPkgConfig(Path.Combine(dir, "no-such-pkg-config"), "build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-BLD0004]: app's pkgConfig: pkg-config is not on the PATH", error);
    }

    /// <summary>pkg-config knows the host's libraries: a build for another target is refused before
    /// anything compiles.</summary>
    [CrossTargetFact]
    public void A_library_of_pkg_config_is_the_hosts()
    {
        var dir = Package("pkgConfig = [\"pkgdemo\"]\n");
        var other = Target.Tier1.First(t => t.Os != Target.Host.Os);
        var (exit, _, error) = Run("build", "-C", dir, "--target", other.Triple);
        Assert.Equal(2, exit);
        Assert.Contains($"error[LYR-BLD0004]: app links pkgdemo through pkg-config, which knows the libraries of this host, and the build is for {other.Triple}", error);
    }

    /// <summary>A framework comes from the macOS SDK: a build for macOS from another host is refused.</summary>
    [CrossTargetFact]
    public void A_framework_needs_a_macOS_host()
    {
        if (OperatingSystem.IsMacOS()) return;
        var dir = PackageFixture.Package(("lyric.toml", AppManifest + "\n[native.macos]\nframeworks = [\"CoreFoundation\"]\n"),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--target", "aarch64-macos");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-BLD0004]: app links the framework CoreFoundation, from the macOS SDK, and this host is not macOS", error);
        // a build for the host does not need it
        Assert.Equal(0, Run("build", "-C", dir).Exit);
    }

    /// <summary>On a macOS host a framework links, from the manifest and from a script.</summary>
    [Fact]
    public void A_framework_links_on_a_macOS_host()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var dir = PackageFixture.Package(("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n\n[native.macos]\nframeworks = [\"CoreFoundation\"]\n"),
            ("build.lyr", "import std.build;\n\nfn main(): void {\n    build.linkFramework(\"Foundation\");\n}\n"),
            ("native/cf.c", "#include <stdint.h>\n#include <CoreFoundation/CoreFoundation.h>\nint64_t value_of(int64_t n) { return (int64_t)CFStringGetLength(CFSTR(\"hello\")) * n; }\n"),
            ("src/main.lyr", CallMain));
        Assert.Equal("10\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Theory]
    [InlineData("[native]\nframeworks = [\"Cocoa\"]\n", "LYR-PKG0003", "[native] has no key 'frameworks': a framework is macOS's")]
    [InlineData("[native.linux]\nframeworks = [\"Cocoa\"]\n", "LYR-PKG0003", "[native.linux] has no key 'frameworks'")]
    [InlineData("[native.macos]\nframeworks = [\"-framework Cocoa\"]\n", "LYR-PKG0002", "'-framework Cocoa' in 'frameworks' of [native.macos] is no framework name")]
    [InlineData("[native]\npkgConfig = [\"sdl2 --static\"]\n", "LYR-PKG0002", "'sdl2 --static' in 'pkgConfig' of [native] is no name pkg-config knows a library by")]
    public void Frameworks_and_pkgConfig_are_written_so(string native, string code, string message)
    {
        var dir = PackageFixture.Package(("lyric.toml", AppManifest + "\n" + native), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[{code}]: {message}", error);
    }
}
