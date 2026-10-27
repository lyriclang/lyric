using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Functions C calls by a symbol (<c>@Export</c>) and the library <c>lyric build --lib</c> makes of
/// a package: an archive with the runtime inside or a shared library, and the header that
/// declares the runtime's start and stop, the <c>@Layout</c> types the exports name, and the
/// exports. A C program built here links each and calls them. Through <c>Main</c>, so in the
/// console collection.
/// </summary>
[Collection("console")]
public class ExportTests
{
    private const string GeoManifest = "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n";

    /// <summary>Scalars, an enum as its integer, a struct by value both ways, a module binding the
    /// library's start fills, a C string in and a throwing export, one without a result.</summary>
    private const string GeoLyric = """
        import std.io { println };
        import std.ffi { CStr };
        import std.core { Utf8Error };

        @Layout(C)
        pub struct Point {
            x: float,
            y: float,
        }

        @Layout(uint8)
        pub enum Shape { Square, Circle }

        let scale = [1, 2, 3].length();

        @Export("geo_add")
        pub fn add(a: int, b: int): int {
            return a + b;
        }

        @Export("geo_area")
        pub fn area(shape: Shape, side: float): float {
            return match (shape) {
                .Square => side * side,
                .Circle => 3.0 * side * side,
            };
        }

        @Export("geo_mid")
        pub fn mid(a: Point, b: Point): Point {
            return Point { x = (a.x + b.x) / 2.0, y = (a.y + b.y) / 2.0 };
        }

        @Export("geo_scaled")
        pub fn scaled(n: int): int {
            return n * scale;
        }

        @Export("geo_length")
        pub fn length(text: CStr): int throws Utf8Error {
            return (try text.toString()).length();
        }

        @Export("geo_hello")
        pub fn hello(): void {
            println("hello from geo");
        }

        """;

    /// <summary>The C program: its own lines first, flushed, then the library's, which the runtime
    /// writes at its stop.</summary>
    private const string HostC = """
        #include <stdio.h>
        #include "geo.h"

        int main(void) {
            if (lyr_init(NULL) != 0) return 10;
            printf("add %lld\n", (long long)geo_add(40, 2));
            printf("area %.1f %.1f\n", geo_area(geo_Shape_Square, 3.0), geo_area(geo_Shape_Circle, 1.0));
            geo_Point a = { 1.0, 2.0 }, b = { 3.0, 6.0 };
            geo_Point m = geo_mid(a, b);
            printf("mid %.1f %.1f\n", m.x, m.y);
            printf("scaled %lld\n", (long long)geo_scaled(5));
            int64_t n = -1;
            LyrStatus s = geo_length("h\xc3\xa9llo", &n);
            printf("length %d %lld\n", (int)s, (long long)n);
            n = -1;
            s = geo_length("\xff", &n);
            printf("bad %d %lld\n", (int)s, (long long)n);
            fflush(stdout);
            geo_hello();
            lyr_shutdown();
            return 0;
        }

        """;

    private const string Expected = "add 42\narea 9.0 3.0\nmid 2.0 4.0\nscaled 15\nlength 0 6\nbad 1 -1\nhello from geo\n";

    private static string Geo(string host = HostC) =>
        Package(("lyric.toml", GeoManifest), ("src/lib.lyr", GeoLyric), ("host.c", host));

    private static string OutOf(string dir) => Path.Combine(dir, "out", "debug", Target.Host.Triple);

    [Fact]
    public void A_C_program_links_the_library_and_calls_its_exports()
    {
        var dir = Geo();
        var (exit, _, error) = Run("build", "-C", dir, "--lib");
        Assert.True(exit == 0, error);
        var o = OutOf(dir);
        var header = File.ReadAllText(Path.Combine(o, "geo.h"));
        Assert.Contains("LYRIC_API int lyr_init(const LyrConfig *config);", header);
        Assert.Contains("typedef struct geo_Point {\n    double x;\n    double y;\n} geo_Point;", header);
        Assert.Contains("typedef uint8_t geo_Shape;\nenum { geo_Shape_Square = 0, geo_Shape_Circle = 1 };", header);
        Assert.Contains("LYRIC_API geo_Point geo_mid(geo_Point a, geo_Point b);", header);
        Assert.Contains("LYRIC_API LyrStatus geo_length(const char *text, int64_t *lyr_out);", header);
        Assert.Contains("LYRIC_API void geo_hello(void);", header);
        Assert.Equal(Expected, RunHost(dir, [Path.Combine(o, "libgeo.a"), .. SystemLibraries()]));
    }

    [Fact]
    public void A_C_program_loads_the_shared_library_and_calls_its_exports()
    {
        var dir = Geo();
        var (exit, _, error) = Run("build", "-C", dir, "--lib", "--shared");
        Assert.True(exit == 0, error);
        var o = OutOf(dir);
        string[] link = Target.Host.Os switch
        {
            // the import library; the DLL itself lies beside the program, where Windows looks first
            TargetOs.Windows => [Path.Combine(o, "geo.lib")],
            TargetOs.MacOs => [Path.Combine(o, "libgeo.dylib"), "-Wl,-rpath," + o],
            _ => [Path.Combine(o, "libgeo.so"), "-Wl,-rpath," + o],
        };
        Assert.Equal(Expected, RunHost(dir, link));
    }

    /// <summary>An export called before the runtime started stops the process with a report.</summary>
    [Fact]
    public void An_export_before_lyr_init_ends_the_process()
    {
        var dir = Geo("#include <stdio.h>\n#include \"geo.h\"\n\nint main(void) {\n    printf(\"%lld\\n\", (long long)geo_add(1, 2));\n    return 0;\n}\n");
        var (exit, _, error) = Run("build", "-C", dir, "--lib");
        Assert.True(exit == 0, error);
        var ran = BuildHost(dir, [Path.Combine(OutOf(dir), "libgeo.a"), .. SystemLibraries()]);
        Assert.Equal(101, ran.ExitCode);
        Assert.Contains("panic [LYR-RT0022]: C called an export before lyr_init started the runtime", ran.Stderr);
    }

    /// <summary>A program's own native part calls an export: the runtime runs already, and main's
    /// start filled the globals.</summary>
    [Fact]
    public void A_programs_native_part_calls_an_export()
    {
        var dir = Package(
            ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n"),
            ("native/twice.c", "#include <stdint.h>\n\nint64_t app_twice(int64_t x);\n\nint64_t call_twice(int64_t x) { return app_twice(x) + 1; }\n"),
            ("src/main.lyr", """
                import std.io { println };

                extern "C" fn call_twice(x: int): int;

                let factor = [1, 1].length();

                @Export("app_twice")
                pub fn twice(x: int): int {
                    return factor * x;
                }

                fn main(): void {
                    println(f"{call_twice(20)}");
                }

                """));
        Assert.Equal("41\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Theory]
    [InlineData("@Export\nfn f(): int {\n    return 1;\n}", "LYR-SEM0099", "'f' is exported, and an export is 'pub'")]
    [InlineData("@Export\npub fn f<T>(x: T): T {\n    return x;\n}", "LYR-SEM0099", "'f' is generic")]
    [InlineData("@Export\npub fn f(s: string): int {\n    return 1;\n}", "LYR-SEM0099", "parameter 's' of export 'f' is a 'string'")]
    [InlineData("@Export\npub fn f(&x: int): void {\n}", "LYR-SEM0099", "parameter 'x' of export 'f' takes a place")]
    [InlineData("@Export\npub fn f(): string {\n    return \"\";\n}", "LYR-SEM0099", "export 'f' returns 'string', which C cannot take")]
    [InlineData("@Export\npub fn f(xs: int[]): void {\n}", "LYR-SEM0099", "parameter 'xs' of export 'f' has type 'int[]', which C cannot hand")]
    [InlineData("@Export(\"2x\")\npub fn f(): void {\n}", "LYR-SEM0099", "'2x' is no C function's name")]
    [InlineData("@Export(\"lyr_f\")\npub fn f(): void {\n}", "LYR-SEM0099", "'lyr_f' begins as the runtime's own names do")]
    [InlineData("@Export(1)\npub fn f(): void {\n}", "LYR-SEM0099", "'@Export' takes the C symbol as a string written out")]
    [InlineData("@Export(\"dup\")\npub fn f(): void {\n}\n\n@Export(\"dup\")\npub fn g(): void {\n}", "LYR-SEM0099", "'dup' is exported already by another function")]
    [InlineData("@Export\npub inline fn f(): void {\n}", "LYR-SEM0099", "'f' is 'inline'")]
    [InlineData("pub struct S {\n    @Export\n    pub fn m(): void {\n    }\n}", "LYR-SEM0065", "'@Export' cannot sit on a method")]
    public void An_export_is_checked(string declarations, string code, string why)
    {
        var dir = Package(("lyric.toml", GeoManifest), ("src/lib.lyr", declarations + "\n"));
        var (exit, _, error) = Run("check", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains($"error[{code}]: {why}", error);
    }

    [Fact]
    public void Shared_says_which_library_lib_builds()
    {
        var dir = Geo();
        var (exit, _, error) = Run("build", "-C", dir, "--shared");
        Assert.Equal(2, exit);
        Assert.Contains("'--shared' says which library '--lib' builds", error);
    }

    [Fact]
    public void A_library_starts_from_src_lib()
    {
        var dir = Package(("lyric.toml", GeoManifest), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--lib");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0001]: no such file", error);
        Assert.Contains("lib.lyr", error);
    }

    /// <summary>What a C program links beside the archive: the system's libraries the runtime calls
    /// — and on Linux the unwinder its backtraces need, which zig cc does not link by itself.</summary>
    private static string[] SystemLibraries() => Target.Host.Os switch
    {
        TargetOs.Windows => ["-lshell32", "-lws2_32"],
        TargetOs.MacOs => [],
        _ => ["-lpthread", "-lm", "-lunwind"],
    };

    private static string RunHost(string dir, string[] link)
    {
        var ran = BuildHost(dir, link);
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        return ran.Stdout.Replace("\r\n", "\n");
    }

    /// <summary>The C program of the package, built beside the library — where Windows finds a
    /// DLL — and run.</summary>
    private static ProcessRunner.Result BuildHost(string dir, string[] link)
    {
        var zig = CCompiler.Locate(CCompilerKind.Zig) ?? throw new InvalidOperationException("zig is required to build a C program");
        var o = OutOf(dir);
        var exe = Path.Combine(o, "host" + Target.Host.ExecutableSuffix);
        var built = ProcessRunner.Run(zig.Path,
            ["cc", "-fno-sanitize=undefined", Path.Combine(dir, "host.c"), "-I", o, .. link, "-o", exe], TimeSpan.FromMinutes(5));
        Assert.True(built.ExitCode == 0, built.Stderr + built.Stdout);
        return ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
    }
}
