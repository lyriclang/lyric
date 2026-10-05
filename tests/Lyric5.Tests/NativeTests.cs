using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A package's native part (design/v5/spec/07 B5; 11 W2) and the C functions the program declares
/// (<c>extern "C"</c>, 11 W4, stage 1): C sources compiled with the program, include directories,
/// libraries, per operating system more of each; scalars across, everything else refused until
/// M14's type table. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class NativeTests
{
    private const string AddC = "#include <stdint.h>\n#include \"add.h\"\n\nint64_t add(int64_t a, int64_t b) { return a + b; }\n";
    private const string AddH = "#include <stdint.h>\nint64_t add(int64_t a, int64_t b);\n";

    private static string Main(string declarations, string call) =>
        $"import std.io {{ println }};\n\n{declarations}\n\nfn main(): void {{\n    println(f\"{call}\");\n}}\n";

    [Fact]
    public void A_package_calls_its_native_part()
    {
        var dir = Package(
            ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\ninclude = [\"native/include\"]\n"),
            ("native/add.c", AddC), ("native/include/add.h", AddH),
            ("src/main.lyr", Main("extern \"C\" fn add(a: int, b: int): int;", "{add(2, 3)}")));
        Assert.Equal("5\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_symbol_names_the_C_function()
    {
        var dir = Package(
            ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/add.c\"]\ninclude = [\"native/include\"]\n"),
            ("native/add.c", AddC), ("native/include/add.h", AddH),
            ("src/main.lyr", Main("extern \"C\" fn plus(a: int, b: int): int = \"add\";", "{plus(40, 2)}")));
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>Floats and bool cross as they are; a C function may return nothing.</summary>
    [Fact]
    public void Scalars_cross_into_C()
    {
        var c = "#include <stdint.h>\n#include <stdbool.h>\n\ndouble half(double x) { return x / 2; }\n"
                + "bool even(int64_t n) { return n % 2 == 0; }\nfloat third(float x) { return x / 3; }\n"
                + "uint8_t low(uint32_t x) { return (uint8_t)x; }\nstatic int64_t kept;\nvoid keep(int64_t x) { kept = x; }\n"
                + "int64_t kept_value(void) { return kept; }\n";
        var declarations = "extern \"C\" fn half(x: float): float;\nextern \"C\" fn even(n: int): bool;\n"
                           + "extern \"C\" fn third(x: float32): float32;\nextern \"C\" fn low(x: uint32): uint8;\n"
                           + "extern \"C\" fn keep(x: int): void;\nextern \"C\" fn kept_value(): int;";
        var main = "import std.io { println };\n\n" + declarations + "\n\nfn main(): void {\n    keep(7);\n"
                   + "    println(f\"{half(5.0)} {even(4)} {even(3)} {third(3.0f32)} {low(258)} {kept_value()}\");\n}\n";
        var dir = Package(("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n"), ("native/scalars.c", c), ("src/main.lyr", main));
        Assert.Equal("2.5 true false 1.0 2 7\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The operating system's table comes on top of the common one.</summary>
    [Fact]
    public void An_operating_system_gets_its_own_native_part()
    {
        var manifest = AppManifest + "\n[native]\nsources = [\"native/common.c\"]\n\n[native.linux]\nsources = [\"native/linux.c\"]\n\n"
                       + "[native.windows]\nsources = [\"native/windows.c\"]\n\n[native.macos]\nsources = [\"native/macos.c\"]\n";
        var dir = Package(("lyric.toml", manifest),
            ("native/common.c", "#include <stdint.h>\nint64_t base(void) { return 10; }\n"),
            ("native/linux.c", "#include <stdint.h>\nint64_t os_id(void) { return 1; }\n"),
            ("native/windows.c", "#include <stdint.h>\nint64_t os_id(void) { return 2; }\n"),
            ("native/macos.c", "#include <stdint.h>\nint64_t os_id(void) { return 3; }\n"),
            ("src/main.lyr", Main("extern \"C\" fn base(): int;\nextern \"C\" fn os_id(): int;", "{base() + os_id()}")));
        var expected = Target.Host.Os switch { TargetOs.Linux => 11, TargetOs.Windows => 12, _ => 13 };
        Assert.Equal($"{expected}\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A dependency's native part is the program's too.</summary>
    [Fact]
    public void A_dependency_brings_its_native_part()
    {
        var dir = Package(
            ("lyric.toml", AppManifest + "\n[dependencies]\ngeo = { path = \"deps/geo\" }\n"),
            ("src/main.lyr", "import std.io { println };\nimport geo.sums { sum };\n\nfn main(): void {\n    println(f\"{sum(3, 4)}\");\n}\n"),
            ("deps/geo/lyric.toml", "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n\n[native]\nsources = [\"native/*.c\"]\ninclude = [\"native/include\"]\n"),
            ("deps/geo/native/add.c", AddC), ("deps/geo/native/include/add.h", AddH),
            ("deps/geo/src/sums.lyr", "extern \"C\" fn add(a: int, b: int): int;\n\npub fn sum(a: int, b: int): int {\n    return add(a, b);\n}\n"));
        Assert.Equal("7\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_library_of_the_native_part_is_linked()
    {
        var dir = Package(("lyric.toml", AppManifest + "\n[native]\nlibs = [\"lyric_no_such_library\"]\n"),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-BLD0001]", error);
        Assert.Contains("-llyric_no_such_library", error);
    }

    [Theory]
    [InlineData("extern \"C\" fn f(s: string): int;", "parameter 's' of extern 'f' has type 'string', which does not cross into C")]
    [InlineData("extern \"C\" fn f(c: char): int;", "parameter 'c' of extern 'f' has type 'char'")]
    [InlineData("extern \"C\" fn f(xs: int[]): int;", "parameter 'xs' of extern 'f'")]
    [InlineData("extern \"C\" fn f(): string;", "extern 'f' returns 'string', which does not come back from C")]
    // a place is an address: it was taken, and a call of it stopped the toolchain (LYR-ICE0001)
    [InlineData("extern \"C\" fn f(&x: int): void;", "parameter 'x' of extern 'f' takes a place ('&')")]
    [InlineData("extern \"C\" fn f(): ?int;", "extern 'f' returns '?int'")]
    [InlineData("extern \"dotnet\" fn f(): int = \"System.Math::Abs\";", "unknown ABI \"dotnet\" — this compiler binds \"C\"")]
    [InlineData("extern \"C\" fn f(): int = \"no-name\";", "'no-name' is no C function's name")]
    [InlineData("extern \"C\" fn f<T>(x: int): int;", "'f' is extern and cannot have type parameters")]
    [InlineData("extern \"C\" fn f(): int throws Exception;", "'f' is a C function, and a C function throws nothing")]
    public void What_does_not_cross_into_C_is_refused(string declaration, string why)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", declaration + "\n\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0099]: " + why, error);
    }

    [Theory]
    [InlineData("[native]\nsources = \"native/a.c\"\n", "LYR-PKG0002", "'sources' of [native] is an array of patterns")]
    [InlineData("[native]\ninclude = [\"native/*\"]\n", "LYR-PKG0002", "'native/*' in 'include' of [native] is no directory of the package")]
    [InlineData("[native]\nlibs = [\"bad name\"]\n", "LYR-PKG0002", "'bad name' in 'libs' of [native] is no library name")]
    [InlineData("[native]\nflags = [\"-O3\"]\n", "LYR-PKG0003", "[native] has no key 'flags'")]
    [InlineData("[native.beos]\nlibs = [\"z\"]\n", "LYR-PKG0003", "[native] has no key 'beos'")]
    [InlineData("[native.linux]\nlinux = []\n", "LYR-PKG0003", "[native.linux] has no key 'linux'")]
    public void A_native_part_is_checked(string native, string code, string why)
    {
        var dir = Package(("lyric.toml", AppManifest + "\n" + native));
        var e = Assert.Throws<ManifestException>(() => Manifest.Read(Path.Combine(dir, "lyric.toml")));
        Assert.Equal(code, e.Code);
        Assert.Contains(why, e.Message);
    }
}
