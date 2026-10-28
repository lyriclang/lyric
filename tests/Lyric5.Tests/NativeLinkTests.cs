using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// What a package's native part links (spec 15 §5): prebuilt archives and shared
/// libraries by their paths, the tables per triple on top of the system's, the sources'
/// definitions, <c>lyric.h</c> and a dependency's headers, and a link held by the bytes of what it
/// read. The prebuilt files are built here, with the C compiler the build uses. Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class NativeLinkTests
{
    internal const string AddC ="#include <stdint.h>\nint64_t add(int64_t a, int64_t b) { return a + b + BIAS; }\n";

    private static string Main(string declarations, string call) =>
        $"import std.io {{ println }};\n\n{declarations}\n\nfn main(): void {{\n    println(f\"{call}\");\n}}\n";

    private const string AddMain = "extern \"C\" fn add(a: int, b: int): int;";

    /// <summary>A static library built from <paramref name="c"/> for <paramref name="target"/>, written
    /// to <paramref name="path"/> under <paramref name="dir"/> — what a package brings prebuilt.</summary>
    internal static void Archive(string dir, string path, Target target, string c, int bias = 0)
    {
        var compiler = (target.IsHost ? CCompiler.Locate() : CCompiler.Locate(CCompilerKind.Zig))
                       ?? throw new InvalidOperationException("no C compiler");
        var work = TestDirectories.Fresh("lyric5-archive-");
        var source = Path.Combine(work, "lib.c");
        File.WriteAllText(source, c);
        var build = new CBuild(compiler, target, BuildProfile.Of(Profile.Release), work);
        var objects = build.Compile([new CUnit(source, [$"BIAS={bias}"], [])]);
        var output = Path.Combine(dir, path);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        // the archiver names its archive by its objects, beside the path it is given
        File.Copy(build.Archive(objects, Path.Combine(work, "lib.a")), output, overwrite: true);
    }

    /// <summary>A shared library <c>greet</c> for the host, built by the C compiler the build uses into
    /// the package, and the manifest table that names it — named as a library meant to lie beside a
    /// program names itself, unless <paramref name="named"/> is false.</summary>
    private static string SharedLibrary(string dir, bool named = true)
    {
        var compiler = CCompiler.Locate() ?? throw new InvalidOperationException("no C compiler");
        var os = Target.Host.Os;
        var lib = Path.Combine(dir, "native", "shared");
        Directory.CreateDirectory(lib);
        File.WriteAllText(Path.Combine(lib, "greet.c"), "#include <stdint.h>\nint64_t greet_answer(void) { return 42; }\n");
        var file = os switch { TargetOs.Windows => "greet.dll", TargetOs.MacOs => "libgreet.dylib", _ => "libgreet.so" };
        List<string> arguments = compiler.Kind == CCompilerKind.Zig ? ["cc"] : [];
        arguments.AddRange(["-shared", "-o", Path.Combine(lib, file), Path.Combine(lib, "greet.c")]);
        if (os == TargetOs.Linux) arguments.Add("-fPIC");
        if (os == TargetOs.Linux && named) arguments.Add("-Wl,-soname,libgreet.so");
        if (os == TargetOs.MacOs && named) arguments.Add("-Wl,-install_name,@rpath/libgreet.dylib");
        if (os == TargetOs.Windows) arguments.Add("-Wl,--out-implib," + Path.Combine(lib, "libgreet.dll.a"));
        var built = ProcessRunner.Run(compiler.Path, arguments, TimeSpan.FromMinutes(2), lib);
        Assert.True(built.ExitCode == 0, built.Stderr + built.Stdout);
        return os switch
        {
            TargetOs.Windows => "[native.windows]\ndylibs = [{ dll = \"native/shared/greet.dll\", lib = \"native/shared/libgreet.dll.a\" }]\n",
            TargetOs.MacOs => "[native.macos]\ndylibs = [\"native/shared/libgreet.dylib\"]\n",
            _ => "[native.linux]\ndylibs = [\"native/shared/libgreet.so\"]\n",
        };
    }

    private static string Executable(string dir, Target target) =>
        Path.Combine(dir, "out", "debug", target.Triple, "app" + target.ExecutableSuffix);

    [Fact]
    public void A_prebuilt_archive_is_linked_by_its_path()
    {
        var host = Target.Host;
        var dir = Package(("lyric.toml", AppManifest + $"\n[native.{host.Triple}]\narchives = [\"native/lib/{host.Triple}/libadd.a\"]\n"),
            ("src/main.lyr", Main(AddMain, "{add(2, 3)}")));
        Archive(dir, $"native/lib/{host.Triple}/libadd.a", host, AddC);
        Assert.Equal("5\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>An archive is one target's: each triple's table names its own, and the program links
    /// for every Tier 1 target from this host.</summary>
    [CrossTargetFact]
    public void An_archive_of_each_triple_links_for_every_tier_1_target()
    {
        var manifest = AppManifest;
        foreach (var target in Target.Tier1)
            manifest += $"\n[native.{target.Triple}]\narchives = [\"native/lib/{target.Triple}/libadd.a\"]\n";
        var dir = Package(("lyric.toml", manifest), ("src/main.lyr", Main(AddMain, "{add(2, 3)}")));
        foreach (var target in Target.Tier1)
        {
            Archive(dir, $"native/lib/{target.Triple}/libadd.a", target, AddC);
            var (exit, _, error) = Run("build", "-C", dir, "--target", target.Triple);
            Assert.True(exit == 0, $"{target}: {error}");
            Assert.True(File.Exists(Executable(dir, target)), $"no binary for {target}");
        }
    }

    /// <summary>A shared library of the package is linked by its path and lies beside the binary,
    /// which finds it there — and names neither the package's directory nor the library's.</summary>
    [Fact]
    public void A_shared_library_of_the_package_lies_beside_the_binary()
    {
        var dir = Package(("src/main.lyr", Main("extern \"C\" fn greet_answer(): int;", "{greet_answer()}")));
        File.WriteAllText(Path.Combine(dir, "lyric.toml"), AppManifest + "\n" + SharedLibrary(dir));
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var exe = Executable(dir, Target.Host);
        var beside = Target.Host.Os switch { TargetOs.Windows => "greet.dll", TargetOs.MacOs => "libgreet.dylib", _ => "libgreet.so" };
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, beside)), $"no {beside} beside the binary");
        var bytes = File.ReadAllBytes(exe);
        Assert.True(bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(Path.Combine(dir, "native"))) < 0, "the binary names the package's directory");
        // and the test program as well
        var (exit, _, error) = Run("test", "-C", dir);
        Assert.True(exit == 0, error);
    }

    /// <summary>An ELF library without a soname would be recorded by the path it was linked from.</summary>
    [Fact]
    public void A_shared_library_without_its_name_is_refused()
    {
        if (Target.Host.Os == TargetOs.Windows) return; // a DLL is named by its import library
        var dir = Package(("src/main.lyr", Main("extern \"C\" fn greet_answer(): int;", "{greet_answer()}")));
        File.WriteAllText(Path.Combine(dir, "lyric.toml"), AppManifest + "\n" + SharedLibrary(dir, named: false));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-BLD0006]", error);
        Assert.Contains(Target.Host.Os == TargetOs.MacOs ? "@rpath/libgreet.dylib" : "-Wl,-soname,libgreet.so", error);
    }

    /// <summary>A shared library and a file a build script puts beside the binary under one name.</summary>
    [Fact]
    public void A_shared_library_and_a_scripts_file_of_one_name_stop_the_build()
    {
        var dir = Package(("src/main.lyr", Main("extern \"C\" fn greet_answer(): int;", "{greet_answer()}")));
        var name = Target.Host.Os switch { TargetOs.Windows => "greet.dll", TargetOs.MacOs => "libgreet.dylib", _ => "libgreet.so" };
        File.WriteAllText(Path.Combine(dir, "lyric.toml"), AppManifest + "\n" + SharedLibrary(dir));
        Directory.CreateDirectory(Path.Combine(dir, "data"));
        File.WriteAllText(Path.Combine(dir, "data", name), "not a library");
        File.WriteAllText(Path.Combine(dir, "build.lyr"), $"import std.build;\n\nfn main(): void {{\n    build.besideBinary(\"data/{name}\");\n}}\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-BLD0006]: two files named '{name}' would lie beside the binary", error);
    }

    /// <summary>A Mach-O library that names itself by where it was built is refused, from any host.</summary>
    [CrossTargetFact]
    public void A_Mach_O_library_not_named_by_its_run_path_is_refused()
    {
        var zig = CCompiler.Locate(CCompilerKind.Zig) ?? throw new InvalidOperationException("zig is required");
        var dir = Package(("lyric.toml", AppManifest + "\n[native.macos]\ndylibs = [\"native/macos/libgreet.dylib\"]\n"),
            ("native/macos/greet.c", "#include <stdint.h>\nint64_t greet_answer(void) { return 42; }\n"),
            ("src/main.lyr", Main("extern \"C\" fn greet_answer(): int;", "{greet_answer()}")));
        var lib = Path.Combine(dir, "native", "macos");
        var built = ProcessRunner.Run(zig.Path, ["cc", "-target", "aarch64-macos", "-shared", "-o", Path.Combine(lib, "libgreet.dylib"),
            "-Wl,-install_name,/usr/local/lib/libgreet.dylib", Path.Combine(lib, "greet.c")], TimeSpan.FromMinutes(2), lib);
        Assert.True(built.ExitCode == 0, built.Stderr + built.Stdout);
        var (exit, _, error) = Run("build", "-C", dir, "--target", "aarch64-macos");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-BLD0006]: app's 'native/macos/libgreet.dylib' names itself '/usr/local/lib/libgreet.dylib'", error);
    }

    /// <summary>A package's sources get its definitions, see <c>lyric.h</c> and the headers of a
    /// package it declares — not those of a package that one depends on.</summary>
    [Fact]
    public void Native_sources_get_their_definitions_lyric_h_and_a_dependencys_headers()
    {
        const string geo = "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n\n[dependencies]\nbase = { path = \"../base\" }\n\n[native]\ninclude = [\"native/include\"]\n";
        const string @base = "[package]\nname = \"base\"\nversion = \"0.1.0\"\n\n[native]\ninclude = [\"native/include\"]\n";
        var files = new List<(string, string)>
        {
            ("lyric.toml", AppManifest + "\n[dependencies]\ngeo = { path = \"deps/geo\" }\n\n[native]\nsources = [\"native/*.c\"]\ndefines = [\"TWICE=2\", \"LOUD\"]\n"),
            ("src/main.lyr", Main("extern \"C\" fn app_value(): int;", "{app_value()}")),
            ("deps/geo/lyric.toml", geo), ("deps/geo/src/lib.lyr", "pub fn none(): void {\n}\n"),
            ("deps/geo/native/include/geo.h", "#define GEO_BASE 21\n"),
            ("deps/base/lyric.toml", @base), ("deps/base/src/lib.lyr", "pub fn none(): void {\n}\n"),
            ("deps/base/native/include/base.h", "#define BASE_ONE 1\n"),
        };
        var dir = Package([.. files, ("native/app.c",
            "#include <stdint.h>\n#include <lyric.h>\n#include <geo.h>\n#ifndef LOUD\n#error LOUD is not defined\n#endif\n"
            + "int64_t app_value(void) { return GEO_BASE * TWICE; }\n")]);
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));

        File.WriteAllText(Path.Combine(dir, "native", "app.c"), "#include <stdint.h>\n#include <base.h>\nint64_t app_value(void) { return BASE_ONE; }\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-BLD0001]", error);
        Assert.Contains("base.h", error);
    }

    /// <summary><c>[native]</c>, then the system's table, then the triple's: each list grows.</summary>
    [Fact]
    public void A_triples_table_comes_on_top_of_its_systems()
    {
        var host = Target.Host;
        var os = host.Os switch { TargetOs.Windows => "windows", TargetOs.MacOs => "macos", _ => "linux" };
        var dir = Package(
            ("lyric.toml", AppManifest + $"\n[native]\nsources = [\"native/common.c\"]\ndefines = [\"K=1\"]\n\n[native.{os}]\ndefines = [\"OS_K=2\"]\n\n"
                           + $"[native.{host.Triple}]\nsources = [\"native/triple.c\"]\ndefines = [\"TRIPLE_K=3\"]\n"),
            ("native/common.c", "#include <stdint.h>\nint64_t common_value(void) { return K * 100 + OS_K * 10 + TRIPLE_K; }\n"),
            ("native/triple.c", "#include <stdint.h>\nint64_t triple_value(void) { return 7; }\n"),
            ("src/main.lyr", Main("extern \"C\" fn common_value(): int;\nextern \"C\" fn triple_value(): int;", "{common_value()} {triple_value()}")));
        Assert.Equal("123 7\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>An archive replaced by other bytes under the same time relinks the program: the link
    /// is held by what it read, not by when.</summary>
    [Fact]
    public void An_archive_replaced_under_the_same_time_relinks()
    {
        var host = Target.Host;
        var path = $"native/lib/libadd.a";
        var dir = Package(("lyric.toml", AppManifest + $"\n[native]\narchives = [\"{path}\"]\n"), ("src/main.lyr", Main(AddMain, "{add(2, 3)}")));
        Archive(dir, path, host, AddC);
        Assert.Equal("5\n", BuildAndRun(dir, "app", "build", "-C", dir));
        var archive = Path.Combine(dir, path);
        var time = File.GetLastWriteTimeUtc(archive);
        Archive(dir, path, host, AddC, bias: 10);
        File.SetLastWriteTimeUtc(archive, time);
        Assert.Equal("15\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A prebuilt file must be the package's — what a package from git is — and be there.</summary>
    [Fact]
    public void A_prebuilt_file_outside_the_package_or_missing_is_refused()
    {
        var dir = Package(("lyric.toml", AppManifest + "\n[native]\narchives = [\"lib/libadd.a\"]\n"), ("src/main.lyr", Main(AddMain, "{add(2, 3)}")));
        Archive(dir, "lib/libadd.a", Target.Host, AddC);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0002]: app's 'lib/libadd.a' in 'archives' is not part of the package's content", error);

        File.WriteAllText(Path.Combine(dir, "lyric.toml"), AppManifest + "\n[native]\narchives = [\"native/libgone.a\"]\n");
        (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0002]: app's manifest names 'native/libgone.a' in 'archives', and there is no such file", error);
    }

    /// <summary>The manifest's forms: what each key takes, and where.</summary>
    [Theory]
    [InlineData("[native]\ndylibs = [\"native/libx.so\"]\n", "LYR-PKG0003", "[native] has no key 'dylibs'")]
    [InlineData("[native.x86_64-plan9]\nsources = [\"native/*.c\"]\n", "LYR-PKG0003", "[native.x86_64-plan9] names no operating system and no target")]
    [InlineData("[native]\ndefines = [\"2FAST\"]\n", "LYR-PKG0002", "'2FAST' in 'defines' of [native] is no definition")]
    [InlineData("[native]\narchives = [\"native/libx.so\"]\n", "LYR-PKG0002", "is a shared library: name it in 'dylibs'")]
    [InlineData("[native]\narchives = [\"../libx.a\"]\n", "LYR-PKG0002", "'../libx.a' in 'archives' of [native] is no path of the package")]
    [InlineData("[native.windows]\ndylibs = [\"native/x.dll\"]\n", "LYR-PKG0002", "an entry of 'dylibs' in [native.windows] is not one of tables")]
    [InlineData("[native.linux]\ndylibs = [\"native/libx.dylib\"]\n", "LYR-PKG0002", "an entry of 'dylibs' in [native.linux] is not one of paths of the package's .so files")]
    [InlineData("[native.aarch64-macos]\nframework = [\"Cocoa\"]\n", "LYR-PKG0003", "[native.aarch64-macos] has no key 'framework'")]
    public void A_native_part_of_another_form_is_refused(string native, string code, string why)
    {
        var dir = Package(("lyric.toml", AppManifest + "\n" + native), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("check", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[{code}]", error);
        Assert.Contains(why, error);
    }
}
