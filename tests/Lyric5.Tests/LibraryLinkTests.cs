using System.Text.Json;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// What <c>build --lib</c> makes of the packages' native parts (spec 13 §4 rule 9): their archives
/// inside <c>lib&lt;name&gt;.a</c>, their shared libraries beside it, and the rest of the link — the
/// libraries by name, the runtime's — said as a note and, under <c>--json</c>, as the artifact's
/// <c>linkArgs</c>. A C program built with that line alone runs. Through <c>Main</c>, so in the
/// console collection.
/// </summary>
[Collection("console")]
public class LibraryLinkTests
{
    private const string CalcManifest = "[package]\nname = \"calc\"\nversion = \"0.1.0\"\n";

    private const string CalcLyric = """
        extern "C" fn add(a: int, b: int): int;
        extern "C" fn greet_answer(): int;

        @Export("calc_total")
        pub fn total(a: int, b: int): int {
            return add(a, b) + greet_answer();
        }

        """;

    private const string HostC = """
        #include <stdio.h>
        #include "calc.h"

        int main(void) {
            if (lyr_init(NULL) != LYR_OK) return 10;
            printf("total %lld\n", (long long)calc_total(2, 3));
            lyr_shutdown();
            return 0;
        }

        """;

    /// <summary>A library package whose native part brings an archive (<c>add</c>), a shared library
    /// (<c>greet_answer</c>) and, on Linux, a system library by name.</summary>
    private static string Calc()
    {
        var host = Target.Host;
        var dir = Package(("src/lib.lyr", CalcLyric), ("host.c", HostC));
        var manifest = CalcManifest + $"\n[native.{host.Triple}]\narchives = [\"native/lib/libadd.a\"]\n"
            + (host.Os == TargetOs.Linux ? "libs = [\"rt\"]\n" : "")
            + "\n" + NativeLinkTests.SharedLibrary(dir);
        File.WriteAllText(Path.Combine(dir, "lyric.toml"), manifest);
        NativeLinkTests.Archive(dir, "native/lib/libadd.a", host, NativeLinkTests.AddC);
        return dir;
    }

    /// <summary>The artifact <c>build --lib --json</c> says, and its standard error.</summary>
    private static (JsonElement Artifact, string Error) Library(string dir, params string[] more)
    {
        var (exit, output, error) = RunRaw(["build", "-C", dir, "--lib", "--json", .. more]);
        Assert.True(exit == 0, output + error);
        var artifact = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .Single(e => e.GetProperty("type").GetString() == "artifact");
        return (artifact, error);
    }

    internal static string[] LinkArgsOf(JsonElement artifact) =>
        [.. artifact.GetProperty("linkArgs").EnumerateArray().Select(a => a.GetString()!)];

    /// <summary>The C program links the archive and the line it was given — not the package's
    /// archive, which is inside — and finds the shared library where it lies, beside the archive.</summary>
    [Fact]
    public void A_C_program_links_the_archive_with_the_line_it_names()
    {
        var dir = Calc();
        var (artifact, error) = Library(dir);
        Assert.Equal("", error);
        Assert.Equal("library", artifact.GetProperty("kind").GetString());
        var o = ExportTests.OutOf(dir);
        Assert.Equal(Path.Combine(o, "libcalc.a"), artifact.GetProperty("path").GetString());
        var args = LinkArgsOf(artifact);
        var shared = Target.Host.Os switch
        {
            TargetOs.Windows => "libgreet.dll.a",
            TargetOs.MacOs => "libgreet.dylib",
            _ => "libgreet.so",
        };
        // the shared library by its copy beside the archive, the system's libraries after the package's
        Assert.Equal(Path.Combine(o, shared), args[0]);
        Assert.True(File.Exists(args[0]), $"no {shared} beside the archive");
        var beside = Target.Host.Os switch { TargetOs.Windows => "greet.dll", _ => shared };
        Assert.True(File.Exists(Path.Combine(o, beside)), $"no {beside} beside the archive");
        if (Target.Host.Os == TargetOs.Linux)
            Assert.Equal(["-lrt", "-lpthread", "-lm", "-ldl", "-lunwind"], args[1..]);
        Assert.DoesNotContain(args, a => a.Contains("libadd"));
        // where the program finds the shared library is the program's to say: here, its own directory
        string[] runPath = Target.Host.Os switch
        {
            TargetOs.Linux => ["-Wl,-rpath,$ORIGIN"],
            TargetOs.MacOs => ["-Wl,-rpath,@executable_path"],
            _ => [],
        };
        var ran = ExportTests.BuildHost(dir, [Path.Combine(o, "libcalc.a"), .. args, .. runPath]);
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        Assert.Equal("total 47\n", ran.Stdout.Replace("\r\n", "\n"));
    }

    /// <summary>Each Tier 1 target's archive holds the members of that triple's package archive.</summary>
    [CrossTargetFact]
    public void The_archive_of_every_tier_1_target_holds_the_packages_archive()
    {
        var manifest = CalcManifest;
        foreach (var target in Target.Tier1)
            manifest += $"\n[native.{target.Triple}]\narchives = [\"native/lib/{target.Triple}/libadd.a\"]\n";
        var dir = Package(("lyric.toml", manifest), ("src/lib.lyr", "extern \"C\" fn add(a: int, b: int): int;\n\n@Export(\"calc_sum\")\npub fn sum(a: int, b: int): int {\n    return add(a, b);\n}\n"));
        var zig = CCompiler.Locate(CCompilerKind.Zig) ?? throw new InvalidOperationException("zig is required");
        string[] Members(string archive)
        {
            var listed = ProcessRunner.Run(zig.Path, ["ar", "t", archive], TimeSpan.FromMinutes(1));
            Assert.True(listed.ExitCode == 0, listed.Stderr);
            return listed.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        foreach (var target in Target.Tier1)
        {
            NativeLinkTests.Archive(dir, $"native/lib/{target.Triple}/libadd.a", target, NativeLinkTests.AddC);
            var (exit, _, error) = RunRaw("build", "-C", dir, "--lib", "--target", target.Triple);
            Assert.True(exit == 0, $"{target}: {error}");
            var library = Path.Combine(dir, "out", "debug", target.Triple, "libcalc.a");
            Assert.Subset(Members(library).ToHashSet(), Members(Path.Combine(dir, "native", "lib", target.Triple, "libadd.a")).ToHashSet());
        }
    }

    /// <summary>Without <c>--json</c> the line is a note after the build.</summary>
    [Fact]
    public void The_text_form_says_the_line_as_a_note()
    {
        var dir = Calc();
        var (exit, _, said) = RunRaw("build", "-C", dir, "--lib");
        Assert.True(exit == 0, said);
        var error = WithoutStatus(said);
        var o = ExportTests.OutOf(dir);
        Assert.StartsWith($"note: link {Path.Combine(o, "libcalc.a")} with ", error);
        if (Target.Host.Os == TargetOs.Linux) Assert.EndsWith(" -lrt -lpthread -lm -ldl -lunwind\n", error);
    }

    /// <summary>A shared library is linked against the packages' libraries itself: its artifact names
    /// no line, and the text form says none.</summary>
    [Fact]
    public void A_shared_library_names_no_line()
    {
        var dir = Calc();
        var (artifact, _) = Library(dir, "--shared");
        Assert.False(artifact.TryGetProperty("linkArgs", out _), artifact.ToString());
        var (exit, _, error) = RunRaw("build", "-C", dir, "--lib", "--shared");
        Assert.True(exit == 0, error);
        Assert.Equal("", WithoutStatus(error));
    }

    /// <summary>The package's archive changed under the same name and size makes a new library.</summary>
    [Fact]
    public void A_replaced_archive_makes_a_new_library()
    {
        var dir = Calc();
        Library(dir);
        var o = ExportTests.OutOf(dir);
        var before = File.ReadAllBytes(Path.Combine(o, "libcalc.a"));
        NativeLinkTests.Archive(dir, "native/lib/libadd.a", Target.Host, NativeLinkTests.AddC, bias: 1);
        var (artifact, _) = Library(dir);
        Assert.NotEqual(before, File.ReadAllBytes(Path.Combine(o, "libcalc.a")));
        string[] runPath = Target.Host.Os switch
        {
            TargetOs.Linux => ["-Wl,-rpath,$ORIGIN"],
            TargetOs.MacOs => ["-Wl,-rpath,@executable_path"],
            _ => [],
        };
        var ran = ExportTests.BuildHost(dir, [Path.Combine(o, "libcalc.a"), .. LinkArgsOf(artifact), .. runPath]);
        Assert.Equal("total 48\n", ran.Stdout.Replace("\r\n", "\n"));
    }
}
