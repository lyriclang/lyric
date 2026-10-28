using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Bindings (spec 15 §5.4): a module of the package made from a C header for the build's target
/// — functions with their variadic arguments, structs, enums, typedefs, pointers to functions,
/// constants —, what it cannot write listed at its end, made anew only where what it was made
/// of changed; and <c>lyric bindgen</c> (spec 14 §2 rule 6). Through <c>Main</c>, so in the
/// console collection.
/// </summary>
[Collection("console")]
public class BindingsTests
{
    /// <summary>A header with one of each thing the generator writes, and one of each it leaves out.</summary>
    internal const string GeoH = """
        #ifndef GEO_H
        #define GEO_H
        #include <stdint.h>
        #include <stddef.h>
        #include <stdbool.h>
        #define GEO_MAX 10
        #define GEO_HEX 0x1F
        #define GEO_NAME "geo"
        #define GEO_PI 3.14f
        #define GEO_SQ(x) ((x) * (x))
        typedef struct { float x, y; } Vec2;
        typedef struct Box { Vec2 min, max; float m[2][2]; const char *label; struct Box *next; } Box;
        typedef struct Handle Handle;
        typedef enum { SHAPE_DOT = 1, SHAPE_LINE, SHAPE_BOX = 1 << 3 } Shape;
        typedef int (*Visit)(int value, const char *name);
        union Any { int i; float f; };
        struct Packed { int a : 3; };
        int GeoAdd(int a, int b);
        float geo_length(Vec2 v);
        int geo_visit(Visit f, int n);
        long long geo_sum(int n, ...);
        const char *geo_name(void);
        size_t geo_len(const char *s);
        Handle *geo_open(void);
        bool geo_flag(bool on);
        float geo_area(Box b);
        static inline int geo_twice(int x) { return 2 * x; }
        extern int geo_count;
        void geo_any(union Any a);
        #endif
        """;

    internal const string GeoC = """
        #include "geo.h"
        #include <math.h>
        #include <stdarg.h>
        #include <string.h>
        int geo_count = 0;
        int GeoAdd(int a, int b) { return a + b; }
        float geo_length(Vec2 v) { return sqrtf(v.x * v.x + v.y * v.y); }
        int geo_visit(Visit f, int n) { return f ? f(n, "visited") : -1; }
        long long geo_sum(int n, ...) { va_list ap; va_start(ap, n); long long s = 0; for (int i = 0; i < n; i++) s += va_arg(ap, long long); va_end(ap); return s; }
        const char *geo_name(void) { return "geo"; }
        size_t geo_len(const char *s) { return strlen(s); }
        Handle *geo_open(void) { return NULL; }
        bool geo_flag(bool on) { return !on; }
        float geo_area(Box b) { return (b.max.x - b.min.x) * (b.max.y - b.min.y) + b.m[1][1]; }
        void geo_any(union Any a) { (void)a; }
        """;

    private const string GeoMain = """
        import std.io { println };
        import std.ffi { CStr };
        import app.geo { GeoAdd, geo_length, geo_visit, geo_sum, geo_name, geo_len, geo_open, geo_flag, Vec2, Shape, SHAPE_LINE, SHAPE_BOX, GEO_MAX, GEO_HEX, GEO_NAME };

        fn twice(value: int32, _name: ?CStr): int32 {
            return value * 2;
        }

        fn main(): void {
            let s: Shape = SHAPE_BOX;
            let name = geo_name();
            println(f"{GeoAdd(2, 3)} {geo_length(Vec2 { x = 3.0, y = 4.0 })} {geo_visit(twice, 21)} {geo_sum(3, 1, 2, 3)} {geo_len("abcd")}");
            println(f"{SHAPE_LINE} {s} {GEO_MAX} {GEO_HEX} {GEO_NAME} {name!.length()} {geo_open() == null} {geo_flag(false)}");
        }
        """;

    private static string Geo(string main = GeoMain, string native = "") =>
        Package(("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\ninclude = [\"native\"]\n"
                               + "bindings = [{ header = \"native/geo.h\", module = \"geo\" }]\n" + native),
            ("native/geo.h", GeoH), ("native/geo.c", GeoC), ("src/main.lyr", main));

    private static string Module(string dir, Target? target = null) =>
        File.ReadAllText(Path.Combine(dir, "out", "cache", "bindings", (target ?? Target.Host).Triple, "app", "geo.lyr"));

    [Fact]
    public void A_header_becomes_a_module_the_program_calls()
    {
        var dir = Geo();
        Assert.Equal("5 5.0 42 6 4\n2 8 10 31 geo 3 true true\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>What the module holds, as the spec says to write it — and what it leaves out, listed.</summary>
    [Fact]
    public void The_module_writes_what_crosses_and_lists_the_rest()
    {
        var dir = Geo();
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var module = Module(dir);
        foreach (var line in new[]
                 {
                     "import std.ffi { CStr, Ptr };",
                     "@Layout(C)\npub struct Vec2 {\n    pub x: float32,\n    pub y: float32,\n}",
                     // fields hold no optional; an array of arrays is one array
                     "@Layout(C)\npub struct Box {\n    pub min: Vec2,\n    pub max: Vec2,\n    pub m: float32[4],\n    pub label: CStr,\n    pub next: Ptr<Box>,\n}",
                     "pub type Shape = int32;",
                     "pub let SHAPE_DOT: Shape = 1;\npub let SHAPE_LINE: Shape = 2;\npub let SHAPE_BOX: Shape = 8;",
                     "pub type Visit = extern \"C\" fn(int32, ?CStr) -> int32;",
                     "pub extern \"C\" fn GeoAdd(a: int32, b: int32): int32;",
                     "pub extern \"C\" fn geo_visit(f: ?Visit, n: int32): int32;",
                     "pub extern \"C\" fn geo_sum(n: int32, ...): int64;",
                     "pub extern \"C\" fn geo_name(): ?CStr;",
                     "pub extern \"C\" fn geo_len(s: ?CStr): uint;",
                     // a struct C declares without its fields is an address
                     "pub extern \"C\" fn geo_open(): ?Ptr<void>;",
                     // an integer as C types its literal: the first of int32 and int64 it fits
                     "pub let GEO_MAX: int32 = 10;",
                     "pub let GEO_HEX: int32 = 31;",
                     "pub let GEO_NAME: string = \"geo\";",
                     "// Left out:",
                     "// - GEO_PI (geo.h:9): its body is no integer and no string literal",
                     "// - GEO_SQ (geo.h:10): a function-like macro is no declaration",
                     "// - union Any (geo.h:16): a union does not cross",
                     "// - struct Packed (geo.h:17): its field 'a' is a bit field, which does not cross",
                     "// - geo_twice (geo.h:27): a static or inline function has no symbol to link",
                     "// - geo_count (geo.h:28): a variable does not cross",
                     "// - geo_any (geo.h:29): its parameter 'a' is a union ('Any')",
                 })
            Assert.Contains(line, module);
        // nothing of the system's headers
        Assert.DoesNotContain("int8_t", module);
        Assert.DoesNotContain("pub type size_t", module);
    }

    /// <summary>No lint looks at the module: C's names are not Lyric's, and most of it goes unused.</summary>
    [Fact]
    public void No_lint_looks_at_the_module()
    {
        // the module is compiled where it is imported: C's SHAPE_DOT would be named wrong, and Box
        // hides the prelude's
        var dir = Geo("import std.io { println };\nimport app.geo { GeoAdd };\n\nfn main(): void {\n    println(f\"{GeoAdd(1, 2)}\");\n}\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("geo.lyr", error);
        Assert.DoesNotContain("warning", error);
    }

    /// <summary>C's types differ between targets: the module is the target's — a <c>long</c> is 32
    /// bits on Windows, a <c>char</c> unsigned on Linux for Arm —, and the program links for each.</summary>
    [CrossTargetFact]
    public void The_module_is_made_for_each_target()
    {
        var dir = Geo(native: "\n[native.windows]\ndefines = [\"GEO_WINDOWS\"]\n");
        File.AppendAllText(Path.Combine(dir, "native", "geo.h"), "\nlong geo_long(long x);\nchar geo_char(char c);\n");
        File.AppendAllText(Path.Combine(dir, "native", "geo.c"), "\nlong geo_long(long x) { return x; }\nchar geo_char(char c) { return c; }\n");
        foreach (var target in Target.Tier1)
        {
            var (exit, _, error) = Run("build", "-C", dir, "--target", target.Triple);
            Assert.True(exit == 0, $"{target}: {error}");
            var module = Module(dir, target);
            Assert.Contains($"for {target.Triple}", module);
            Assert.Contains(target.Os == TargetOs.Windows ? "fn geo_long(x: int32): int32;" : "fn geo_long(x: int64): int64;", module);
            Assert.Contains(target.Arch == "aarch64" && target.Os == TargetOs.Linux ? "fn geo_char(c: uint8): uint8;" : "fn geo_char(c: int8): int8;", module);
        }
    }

    /// <summary>The module is made anew only where what it was made of changed: a second build leaves
    /// it, a header it reads changed makes it anew.</summary>
    [Fact]
    public void The_module_is_made_anew_where_a_header_changed()
    {
        var dir = Geo();
        Assert.Equal(0, Run("build", "-C", dir).Exit);
        var file = Path.Combine(dir, "out", "cache", "bindings", Target.Host.Triple, "app", "geo.lyr");
        var made = File.GetLastWriteTimeUtc(file);
        File.SetLastWriteTimeUtc(file, made.AddMinutes(-5));
        Assert.Equal(0, Run("build", "-C", dir).Exit);
        Assert.Equal(made.AddMinutes(-5), File.GetLastWriteTimeUtc(file));
        // a header the bound one includes, beside it
        File.WriteAllText(Path.Combine(dir, "native", "more.h"), "int geo_more(void);\n");
        File.WriteAllText(Path.Combine(dir, "native", "geo.h"), GeoH.Replace("#include <stdbool.h>", "#include <stdbool.h>\n#include \"more.h\""));
        Assert.Equal(0, Run("build", "-C", dir).Exit);
        Assert.Contains("pub extern \"C\" fn geo_more(): int32;", Module(dir));
        File.WriteAllText(Path.Combine(dir, "native", "more.h"), "int geo_most(void);\n");
        Assert.Equal(0, Run("build", "-C", dir).Exit);
        Assert.Contains("pub extern \"C\" fn geo_most(): int32;", Module(dir));
    }

    [Fact]
    public void A_header_that_does_not_compile_stops_the_build()
    {
        var dir = Geo();
        File.WriteAllText(Path.Combine(dir, "native", "geo.h"), "int geo_add(int a, int b)\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-BLD0005]: app's bindings of 'native/geo.h' for", error);
        Assert.Contains("geo.h:1:26: error: expected function body after function declarator", error);
    }

    [Fact]
    public void A_module_that_lies_under_src_too_is_refused()
    {
        var dir = Geo();
        File.WriteAllText(Path.Combine(dir, "src", "geo.lyr"), "pub fn x(): int {\n    return 1;\n}\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-RES0016]: the module app.geo is made from 'native/geo.h' and lies in src/ too: 'src/geo.lyr'", error);
    }

    /// <summary>A package that declares the package as a dependency imports its bindings as any of
    /// its modules — and the bound package's own C, its header's functions, comes with it.</summary>
    [Fact]
    public void A_dependent_imports_a_packages_bindings()
    {
        var dir = Package(
            ("lyric.toml", AppManifest + "\n[dependencies]\ngeom = { path = \"geom\" }\n"),
            ("src/main.lyr", "import std.io { println };\nimport geom.c { GeoAdd, GEO_MAX };\n\nfn main(): void {\n    println(f\"{GeoAdd(GEO_MAX, 1)}\");\n}\n"),
            ("geom/lyric.toml", "[package]\nname = \"geom\"\nversion = \"0.1.0\"\n\n[native]\nsources = [\"native/*.c\"]\n"
                                + "bindings = [{ header = \"native/geo.h\", module = \"c\" }]\n"),
            ("geom/src/lib.lyr", "pub fn nothing(): void {\n}\n"),
            ("geom/native/geo.h", GeoH), ("geom/native/geo.c", GeoC));
        Assert.Equal("11\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary><c>lyric bindgen</c> writes what the build makes, with the package's include and defines.</summary>
    [Fact]
    public void Bindgen_writes_the_module_the_build_makes()
    {
        var dir = Geo();
        Assert.Equal(0, Run("build", "-C", dir).Exit);
        var (exit, output, error) = Run("bindgen", "native/geo.h", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Equal(Module(dir), output);
        var (other, windows, _) = Run("bindgen", "native/geo.h", "-C", dir, "--target", "x86_64-windows-gnu");
        Assert.Equal(0, other);
        Assert.Contains("// The bindings of native/geo.h for x86_64-windows-gnu", windows);
    }

    [Theory]
    [InlineData("bindings = \"native/geo.h\"\n", "'bindings' of [native] is an array of tables")]
    [InlineData("bindings = [{ header = \"native/geo.h\" }]\n", "an entry of 'bindings' in [native] is not one of tables")]
    [InlineData("bindings = [{ header = \"../geo.h\", module = \"geo\" }]\n", "'../geo.h' in 'bindings' of [native] is no path of the package or a tree's")]
    [InlineData("bindings = [{ header = \"native/geo.h\", module = \"Geo\" }]\n", "'Geo' in 'bindings' of [native] is no module path")]
    [InlineData("bindings = [{ header = \"@ray/raylib.h\", module = \"rl\" }]\n", "'@ray/raylib.h' in 'bindings' of [native] names the source tree 'ray', which [native.vendor] does not hold")]
    public void A_binding_is_written_so(string native, string message)
    {
        var dir = Package(("lyric.toml", AppManifest + "\n[native]\n" + native), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0002]: " + message, error);
    }
}
