using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A package's native part and the C functions the program declares (<c>extern "C"</c>): C
/// sources compiled with the program, include directories, libraries, per operating system more
/// of each; scalars across, everything else refused. Through <c>Main</c>, so in the console
/// collection.
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
    [InlineData("extern \"C\" fn f(x: ?int): int;", "parameter 'x' of extern 'f' has type '?int'")]
    [InlineData("struct P { x: int }\nextern \"C\" fn f(p: P): int;", "parameter 'p' of extern 'f' has type 'P', which does not cross into C — 'P' is laid out by the compiler")]
    [InlineData("enum E { A, B }\nextern \"C\" fn f(): E;", "extern 'f' returns 'E', which does not come back from C — 'E' is laid out by the compiler")]
    [InlineData("@Layout(C)\nstruct P { s: string }", "field 's' of '@Layout(C)' struct 'P' has type 'string', which C does not hold")]
    [InlineData("@Layout(C)\nenum E { A }\n@Layout(C)\nstruct P { e: E }", "field 'e' of '@Layout(C)' struct 'P' has type 'E', which C does not hold — an enum crosses as a parameter or a result")]
    [InlineData("import std.ffi { CStr };\n@Layout(C)\nstruct P { s: ?CStr }", "field 's' of '@Layout(C)' struct 'P' has type '?CStr', which C does not hold — an optional pointer is wider")]
    [InlineData("@Layout(uint8)\nstruct P { x: int }", "'@Layout(uint8)' makes an enum an integer")]
    [InlineData("@Layout(C)\nenum E { A(int), B }", "variant 'A' carries a payload")]
    [InlineData("@Layout(C)\nclass K { }", "'K' is a class")]
    [InlineData("@Layout(C)\nstruct G<T> { x: int }", "'G' is generic")]
    [InlineData("@Layout(packed)\nstruct P { x: int }", "'@Layout' takes C's layout or an enum's integer type")]
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

    private const string BoundaryC = """
        #include <stdint.h>
        #include <stddef.h>
        typedef struct { int32_t x; double y; uint8_t ok; } P;
        P shift(P p, int32_t d) { p.x += d; p.y *= 2; p.ok = !p.ok; return p; }
        static int cell = 41;
        void *cell_at(void) { return &cell; }
        int64_t read_cell(void *p) { return *(int *)p + 1; }
        const char *greet(void) { return "hi there"; }
        const char *nothing(void) { return NULL; }
        int64_t length_or(const char *s) { return s == NULL ? -1 : (int64_t)strlen(s); }
        uint8_t next_color(uint8_t c) { return (uint8_t)((c + 1) % 3); }
        uint8_t bad_color(void) { return 7; }

        """;

    private const string BoundaryLyric = """
        import std.io { println };
        import std.ffi { Ptr, CStr };
        import std.core { Utf8Error };

        @Layout(C)
        struct P { x: int32, y: float, ok: bool }

        @Layout(uint8)
        enum Color { Red, Green, Blue }

        extern "C" {
            fn strlen(s: CStr): uint;
            fn shift(p: P, d: int32): P;
            fn cell_at(): Ptr<int32>;
            fn read_cell(p: Ptr<int32>): int;
            fn greet(): CStr;
            fn nothing(): ?CStr;
            fn length_or(s: ?CStr): int;
            fn next_color(c: Color): Color;
            fn bad_color(): Color;
        }

        fn name(c: Color): string {
            return match (c) { .Red => "red", .Green => "green", .Blue => "blue" };
        }

        """;

    private static string Boundary(string body) => Package(
        ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n"),
        ("native/boundary.c", "#include <string.h>\n" + BoundaryC),
        ("src/main.lyr", BoundaryLyric + "fn main(): void throws Utf8Error {\n" + body + "}\n"));

    /// <summary>A string into a <c>CStr</c>, a <c>@Layout(C)</c> struct by value both ways, a
    /// pointer out of C and back, a C string and a null one back, an enum as its integer.</summary>
    [Fact]
    public void Pointers_C_strings_structs_and_enums_cross()
    {
        var dir = Boundary("""
                println(f"{strlen("hello")}");
                let p = shift(P { x = 1, y = 1.5, ok = true }, 10);
                println(f"{p.x} {p.y} {p.ok}");
                println(f"{read_cell(cell_at())}");
                let g = greet();
                println(f"{g.length()} {try g.toString()}");
                println(f"{nothing() == null} {length_or(null)} {length_or(greet())}");
                println(name(next_color(Color.Blue)));
                println(name(next_color(Color.Red)));

            """);
        Assert.Equal("5\n11 3.0 false\n42\n8 hi there\ntrue -1 8\nred\ngreen\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>An integer C returns for an enum that no variant stands for stops the program.</summary>
    [Fact]
    public void An_integer_no_variant_stands_for_panics()
    {
        var dir = Boundary("    println(name(bad_color()));\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix),
            [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Contains("panic [LYR-RT0021]: C returned 7, which is no variant of app.main.Color (it has 3)", ran.Stderr);
    }

    [Theory]
    [InlineData("extern \"C\" { }", "LYR-PAR0032", "an extern block declares functions")]
    [InlineData("@Cold\nextern \"C\" { fn f(): int; }", "LYR-PAR0042", "an attribute stands before one function, and this is a block of them")]
    [InlineData("fn f(a: int, ...): int {\n    return a;\n}", "LYR-PAR0026", "a bare '...' is a C function's variadic arguments")]
    [InlineData("extern \"C\" fn f(...): int;", "LYR-SEM0099", "'f' takes C's variadic arguments, and they follow a parameter")]
    public void An_extern_block_is_checked(string declaration, string code, string why)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", declaration + "\n\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains($"error[{code}]: {why}", error);
    }

    /// <summary>C's variadic arguments: any number after the parameters, each as it crosses — a
    /// string as its bytes — and as C promotes it: a <c>float32</c> a <c>double</c>, an <c>int8</c>
    /// and a <c>bool</c> an <c>int</c>. A Lyric variadic function beside it is what it was.</summary>
    [Fact]
    public void C_variadic_arguments_are_promoted()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", """
            import std.ffi { Ptr, CStr };

            extern "C" {
                fn printf(format: CStr, ...): int32;
                fn fflush(stream: ?Ptr<uint8>): int32;
            }

            fn sum(xs: int...): int {
                var total = 0;
                for (x in xs) {
                    total += x;
                }
                return total;
            }

            fn main(): void {
                let small: int8 = -3;
                let half: float32 = 1.5;
                let n = printf("%d %.2f %s %lld|", small, half, "text", sum(1, 2, 3));
                printf("%d %d\n", n, true);
                fflush(null);
            }

            """));
        Assert.Equal("-3 1.50 text 6|15 1\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Theory]
    [InlineData("printf(\"%d\", [1, 2])", "a 'int[]' does not cross into C among the variadic arguments of 'printf'")]
    [InlineData("printf()", "call expects at least 1 argument(s), got 0")]
    public void A_C_variadic_argument_is_checked(string call, string why)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr",
            "import std.ffi { CStr };\n\nextern \"C\" fn printf(format: CStr, ...): int32;\n\nfn main(): void {\n    let _ = "
            + call + ";\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains(why, error);
    }

    /// <summary>Memory C manages, in an <c>unsafe</c> block: values written and read through a
    /// pointer and its offsets, back and forth, as another type, a <c>@Layout(C)</c> struct behind
    /// one; an array's and a slice's elements handed to C, which is safe.</summary>
    [Fact]
    public void Memory_is_read_and_written_in_an_unsafe_block()
    {
        var dir = Package(
            ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n"),
            ("native/sum.c", "#include <stdint.h>\n\nint64_t sum(const int64_t *xs, int64_t n) {\n"
                             + "    int64_t s = 0;\n    for (int64_t i = 0; i < n; i++) s += xs[i];\n    return s;\n}\n"),
            ("src/main.lyr", """
                import std.io { println };
                import std.ffi { Ptr, alloc, free };

                @Layout(C)
                struct Pair { a: int32, b: float }

                extern "C" fn sum(xs: Ptr<int>, n: int): int;

                fn main(): void {
                    unsafe {
                        let p = alloc<int>(3);
                        p.write(10);
                        p.offset(1).write(20);
                        p.offset(2).write(12);
                        println(f"{p.read() + p.offset(1).read() + p.offset(2).read()} {sum(p, 3)}");
                        println(f"{p.cast<uint8>().offset(8).read()} {p.offset(2).offset(-1).read()}");
                        free(p);
                        let q = alloc<Pair>(2);
                        q.offset(1).write(Pair { a = 7, b = 2.5 });
                        let second = q.offset(1).read();
                        println(f"{second.a} {second.b} {q.read().a}");
                        free(q);
                    }
                    let xs = [1, 2, 3, 4];
                    println(f"{sum(xs.asPtr(), 4)} {sum(xs[1..3].asPtr(), 2)}");
                }

                """));
        Assert.Equal("42 42\n20 20\n7 2.5 0\n10 5\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Theory]
    [InlineData("let p = alloc<int>(1);", "LYR-SEM0233", "'alloc' holds to what the compiler cannot see")]
    [InlineData("unsafe {\n        let p = alloc<int>(1);\n    }\n    let q = [1].asPtr();\n    q.write(2);",
        "LYR-SEM0233", "'Ptr.write' holds to what the compiler cannot see")]
    [InlineData("let x = [1].asPtr().read();", "LYR-SEM0233", "'Ptr.read' holds")]
    [InlineData("let q = [1].asPtr().offset(1);", "LYR-SEM0233", "'Ptr.offset' holds")]
    [InlineData("let q = [1].asPtr().cast<uint8>();", "LYR-SEM0233", "'Ptr.cast' holds")]
    [InlineData("free([1].asPtr());", "LYR-SEM0233", "'free' holds")]
    [InlineData("unsafe {\n        let p = alloc<string>(1);\n    }", "LYR-SEM0099", "'alloc' moves a 'string', which C does not hold")]
    [InlineData("unsafe {\n        let s = [\"a\"].asPtr().read();\n    }", "LYR-SEM0099", "'Ptr.read' moves a 'string'")]
    public void A_memory_operation_is_checked(string body, string code, string why)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr",
            "import std.ffi { Ptr, alloc, free };\n\nfn main(): void {\n    " + body + "\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains($"error[{code}]: {why}", error);
    }

    private const string CallbackC = """
        #include <stdint.h>
        #include <stdlib.h>
        #ifdef _WIN32
        #include <windows.h>
        #else
        #include <pthread.h>
        #endif

        int64_t apply(int64_t (*f)(int64_t), int64_t x) { return f(f(x)); }
        void each(void (*f)(int64_t), int64_t n) { for (int64_t i = 0; i < n; i++) f(i); }
        static int64_t triple(int64_t x) { return 3 * x; }
        int64_t (*c_triple(void))(int64_t) { return triple; }

        static void (*away)(int64_t);
        #ifdef _WIN32
        static DWORD WINAPI run_away(LPVOID unused) { (void)unused; away(7); return 0; }
        void elsewhere(void (*f)(int64_t)) {
            away = f;
            HANDLE t = CreateThread(NULL, 0, run_away, NULL, 0, NULL);
            WaitForSingleObject(t, INFINITE);
        }
        #else
        static void *run_away(void *unused) { (void)unused; away(7); return NULL; }
        void elsewhere(void (*f)(int64_t)) {
            away = f;
            pthread_t t;
            pthread_create(&t, NULL, run_away, NULL);
            pthread_join(t, NULL);
        }
        #endif

        """;

    private const string CallbackDeclarations = """
        import std.io { println };
        import std.ffi { Ptr };
        import std.task { spawn, yieldNow };

        extern "C" {
            fn qsort(base: Ptr<int>, count: uint, size: uint, compare: extern "C" fn(Ptr<int>, Ptr<int>) -> int32): void;
            fn apply(f: extern "C" fn(int) -> int, x: int): int;
            fn each(f: extern "C" fn(int) -> void, n: int): void;
            fn c_triple(): extern "C" fn(int) -> int;
            fn elsewhere(f: extern "C" fn(int) -> void): void;
        }

        """;

    private static string Callbacks(string rest) => Package(
        ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n\n[native.linux]\nlibs = [\"pthread\"]\n"),
        ("native/callback.c", CallbackC),
        ("src/main.lyr", CallbackDeclarations + rest));

    /// <summary>C calls Lyric: a named function through qsort and through C's own call, a lambda
    /// that captures nothing; and Lyric calls a C function's pointer C returned.</summary>
    [Fact]
    public void C_calls_a_Lyric_function_through_its_pointer()
    {
        var dir = Callbacks("""
            fn ascending(a: Ptr<int>, b: Ptr<int>): int32 {
                var order: int32 = 0;
                unsafe {
                    let x = a.read();
                    let y = b.read();
                    order = if (x < y) -1 else if (x > y) 1 else 0;
                }
                return order;
            }

            fn plusTen(x: int): int {
                return x + 10;
            }

            fn main(): void {
                let xs = [5, 3, 8, 1, 2];
                qsort(xs.asPtr(), 5, 8, ascending);
                println(f"{xs[0]} {xs[1]} {xs[2]} {xs[3]} {xs[4]}");
                println(f"{apply(plusTen, 1)} {apply((x) => x * 2 + 1, 2)}");
                let triple = c_triple();
                println(f"{triple(4)}");
            }

            """);
        Assert.Equal("1 2 3 5 8\n21 11\n12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A callback may park: C's frames lie on the task's stack while another task runs.</summary>
    [Fact]
    public void A_callback_parks_with_C_frames_below_it()
    {
        var dir = Callbacks("""
            fn step(i: int): void {
                println(f"callback {i}");
                try! yieldNow();
            }

            fn main(): void throws Error {
                let other = spawn(() => {
                    for (i in 0..2) {
                        println(f"other {i}");
                        try yieldNow();
                    }
                });
                each(step, 2);
                try other.await();
            }

            """);
        Assert.Equal("callback 0\nother 0\ncallback 1\nother 1\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A panic in a callback ends the program: C's frames below it cannot be unwound. A
    /// callback on a thread Lyric does not run ends it with LYR-RT0022.</summary>
    [Theory]
    [InlineData("fn boom(i: int): void {\n    panic(\"in the callback\");\n}\n\nfn main(): void {\n    each(boom, 1);\n    println(\"never\");\n}\n",
        "panic [LYR-RT0008]: in the callback")]
    [InlineData("fn there(i: int): void {\n    println(f\"{i}\");\n}\n\nfn main(): void {\n    elsewhere(there);\n    println(\"never\");\n}\n",
        "panic [LYR-RT0022]: C called a Lyric function on a thread Lyric does not run")]
    public void A_callback_that_cannot_go_on_ends_the_program(string rest, string report)
    {
        var dir = Callbacks(rest);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix),
            [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Contains(report, ran.Stderr);
        Assert.DoesNotContain("never", ran.Stdout);
    }

    [Theory]
    [InlineData("let n = 1;\n    let _ = apply((x) => x + n, 1);", "LYR-SEM0001")]
    [InlineData("let _ = apply(thrower, 1);", "LYR-SEM0001")]
    [InlineData("let f: fn(int) -> int = (x) => x;\n    let _ = apply(f, 1);", "LYR-SEM0001")]
    public void What_C_cannot_call_is_refused(string body, string code)
    {
        var dir = Callbacks("fn thrower(x: int): int throws Error {\n    return x;\n}\n\nfn main(): void {\n    " + body + "\n}\n");
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains($"error[{code}]", error);
        Assert.Contains("C calls a function by its address alone", error);
    }

    [Theory]
    [InlineData("extern \"C\" fn f(g: extern \"C\" fn(string) -> int): void;", "a C function's parameter of type 'string' does not cross into C")]
    [InlineData("extern \"C\" fn f(g: extern \"C\" fn(int) -> int throws Error): void;", "a C function throws nothing")]
    [InlineData("extern \"C\" fn f(g: extern \"rust\" fn(int) -> int): void;", "unknown ABI \"rust\"")]
    [InlineData("extern \"C\" fn f(g: fn(int) -> int): void;", "a Lyric function value holds its environment")]
    public void A_C_function_type_is_checked(string declaration, string why)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", declaration + "\n\nfn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains($"error[LYR-SEM0099]", error);
        Assert.Contains(why, error);
    }

    private const string ClosureC = """
        #include <stdint.h>
        #include <stddef.h>

        static void (*handler)(void *, int64_t);
        static void *handler_data;
        void on_event(void (*f)(void *, int64_t), void *userdata) { handler = f; handler_data = userdata; }
        void fire(int64_t event) { handler(handler_data, event); }

        void sort_with(int64_t *xs, int64_t n, int32_t (*cmp)(const int64_t *, const int64_t *, void *), void *userdata) {
            for (int64_t i = 1; i < n; i++)
                for (int64_t j = i; j > 0 && cmp(&xs[j - 1], &xs[j], userdata) > 0; j--) {
                    int64_t t = xs[j]; xs[j] = xs[j - 1]; xs[j - 1] = t;
                }
        }

        static int64_t (*hook)(int64_t);
        void set_hook(int64_t (*h)(int64_t)) { hook = h; }
        int64_t run_hook(int64_t x) { return hook != NULL ? hook(x) : -1; }
        int64_t (*get_hook(void))(int64_t) { return hook; }

        """;

    private static string Closures(string body) => Package(
        ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n"),
        ("native/closure.c", ClosureC),
        ("src/main.lyr", """
            import std.io { println };
            import std.ffi { Ptr, Callback };

            extern "C" {
                fn on_event(handler: extern "C" fn(Ptr<void>, int) -> void, userdata: Ptr<void>): void;
                fn fire(event: int): void;
                fn sort_with(xs: Ptr<int>, n: int, cmp: extern "C" fn(Ptr<int>, Ptr<int>, Ptr<void>) -> int32, userdata: Ptr<void>): void;
                fn set_hook(h: ?extern "C" fn(int) -> int): void;
                fn run_hook(x: int): int;
                fn get_hook(): ?extern "C" fn(int) -> int;
            }

            fn double(x: int): int {
                return x * 2;
            }

            fn main(): void {

            """ + body + "}\n"));

    /// <summary>A closure C calls with its state: the userdata first and last, the state shared with
    /// the code around it; a C function's pointer that may be null, both ways.</summary>
    [Fact]
    public void C_calls_a_closure_through_its_userdata()
    {
        var dir = Closures("""
                var seen = 0;
                let cb = Callback<extern "C" fn(Ptr<void>, int) -> void>.new((event) => {
                    seen += event;
                });
                on_event(cb.function, cb.userdata);
                fire(3);
                fire(4);
                cb.release();
                let descending = true;
                let cmp = Callback<extern "C" fn(Ptr<int>, Ptr<int>, Ptr<void>) -> int32>.new((a, b) => {
                    var order: int32 = 0;
                    unsafe {
                        let x = a.read();
                        let y = b.read();
                        order = if (x < y) -1 else if (x > y) 1 else 0;
                    }
                    return if (descending) -order else order;
                });
                let xs = [5, 3, 8, 1, 2];
                sort_with(xs.asPtr(), 5, cmp.function, cmp.userdata);
                cmp.release();
                println(f"{seen} {xs[0]} {xs[1]} {xs[2]} {xs[3]} {xs[4]}");
                set_hook(null);
                println(f"{run_hook(5)} {get_hook() == null}");
                set_hook(double);
                println(f"{run_hook(5)} {get_hook() == null}");

            """);
        Assert.Equal("7 8 5 3 2 1\n-1 true\n10 false\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_callback_released_twice_panics()
    {
        var dir = Closures("""
                let cb = Callback<extern "C" fn(Ptr<void>, int) -> void>.new((event) => { });
                cb.release();
                cb.release();
                println("never");

            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix),
            [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Contains("panic [LYR-RT0008]: a Callback released twice", ran.Stderr);
    }

    [Theory]
    [InlineData("let cb = Callback<int>.new(1);", "LYR-SEM0099", "a Callback's type is the C function's, 'extern \"C\" fn(…)', not 'int'")]
    [InlineData("let cb = Callback<extern \"C\" fn(int) -> void>.new((x) => { });", "LYR-SEM0099", "has no userdata")]
    [InlineData("let cb = Callback<extern \"C\" fn(Ptr<void>, Ptr<void>) -> void>.new((x) => { });", "LYR-SEM0099",
        "has a 'Ptr<void>' first and last")]
    [InlineData("let cb = Callback<extern \"C\" fn(Ptr<void>, int) -> void>.new((x, y) => { });", "LYR-SEM", "")]
    public void A_callback_is_checked(string line, string code, string why)
    {
        var dir = Closures("    " + line + "\n");
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains($"error[{code}", error);
        Assert.Contains(why, error);
    }

    private const string EachC = "#include <stdint.h>\n\nvoid each(void (*f)(int64_t), int64_t n) { for (int64_t i = 0; i < n; i++) f(i); }\n";

    private static string Each(string program) => Package(
        ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n"),
        ("native/each.c", EachC),
        ("src/main.lyr", program));

    /// <summary>A function C calls throws nothing; its error crosses through std.ffi's stash, which
    /// the code around the C call takes — the first of several, the running task's own.</summary>
    [Fact]
    public void An_error_crosses_C_through_the_stash()
    {
        var dir = Each("""
            import std.io { println };
            import std.ffi { stash, takeStashed };
            import std.task { spawn };

            extern "C" fn each(f: extern "C" fn(int) -> void, n: int): void;

            fn check(i: int): void {
                if (i >= 2) {
                    stash(Exception(f"item {i} is too large"));
                }
            }

            fn visit(n: int): void throws Error {
                each(check, n);
                if (let e = takeStashed()) {
                    throw e;
                }
            }

            fn main(): void throws Error {
                try visit(2);
                println("2 items: fine");
                try {
                    visit(4);
                } catch (e: Error) {
                    println(f"4 items: {e.message()}");
                }
                println(f"taken: {takeStashed() == null}");
                let t = spawn(() => {
                    try visit(3);
                });
                try {
                    t.await();
                } catch (e: Error) {
                    println(f"task: {e.message()}");
                }
            }

            """);
        Assert.Equal("2 items: fine\n4 items: item 2 is too large\ntaken: true\ntask: item 2 is too large\n",
            BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A generator suspended in a callback — C's frames on its stack — is not closed:
    /// the frames cannot be unwound, and the close is a panic that says so.</summary>
    [Fact]
    public void A_coroutine_with_C_frames_is_not_closed()
    {
        var dir = Each("""
            import std.io { println };
            import std.task { Cancelled };

            extern "C" fn each(f: extern "C" fn(int) -> void, n: int): void;

            fn step(i: int): void {
                try {
                    yield i;
                } catch (_: Cancelled) {
                }
            }

            fn numbers(): Coroutine<int> {
                yield -1;
                each(step, 3);
            }

            fn main(): void throws Error {
                let g = numbers();
                println(f"{g.next() ?? 99} {g.next() ?? 99}");
                try g.close();
                println("never");
            }

            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix),
            [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Equal("-1 0\n", ran.Stdout.Replace("\r\n", "\n"));
        Assert.Contains("panic [LYR-RT0014]: a coroutine closed with C frames on its stack", ran.Stderr);
    }

    private const string HandleC = """
        #include <stdint.h>

        void lyr_gc_collect(void);

        static void (*handler)(void *, int64_t);
        static void *handler_data;
        void on_event(void (*f)(void *, int64_t), void *userdata) { handler = f; handler_data = userdata; }
        void fire(int64_t event) { handler(handler_data, event); }
        void collect(void) { lyr_gc_collect(); }

        """;

    private static string Handles(string main) => Package(
        ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n"),
        ("native/handle.c", HandleC),
        ("src/main.lyr", """
            import std.io { println };
            import std.ffi { Ptr, GcHandle };

            class Counter {
                var total: int = 0,

                mut fn add(x: int): void {
                    this.total += x;
                }
            }

            class Other {
                var unused: int = 0,
            }

            extern "C" {
                fn on_event(handler: extern "C" fn(Ptr<void>, int) -> void, userdata: Ptr<void>): void;
                fn fire(event: int): void;
                fn collect(): void;
            }

            fn handler(userdata: Ptr<void>, event: int): void {
                unsafe {
                    let counter = GcHandle<Counter>.from(userdata).get();
                    counter.add(event);
                }
            }

            """ + main));

    /// <summary>An object held for C across collections, its handle's pointer C's userdata, read
    /// again from that pointer by a function C calls.</summary>
    [Fact]
    public void A_handle_holds_an_object_for_C()
    {
        var dir = Handles("""
            fn main(): void {
                let handle = GcHandle.pin(Counter { });
                on_event(handler, handle.asPtr());
                fire(3);
                collect();
                fire(4);
                collect();
                println(f"{handle.get().total}");
                handle.release();
            }

            """);
        Assert.Equal("7\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Theory]
    [InlineData("    let handle = GcHandle.pin(Counter { });\n    unsafe {\n        let _ = GcHandle<Other>.from(handle.asPtr()).get();\n    }\n",
        "panic [LYR-RT0023]: a GcHandle of app.main.Counter read as a handle of app.main.Other")]
    [InlineData("    let handle = GcHandle.pin(Counter { });\n    handle.release();\n    let _ = handle.get();\n",
        "panic [LYR-RT0008]: a GcHandle read after its release")]
    public void A_handle_read_wrongly_panics(string body, string report)
    {
        var dir = Handles("fn main(): void {\n" + body + "}\n");
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix),
            [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Contains(report, ran.Stderr);
    }

    [Theory]
    [InlineData("let _ = GcHandle.pin(3);", "LYR-SEM0099", "a GcHandle holds an object of a class")]
    [InlineData("let _ = GcHandle<Counter>.from([1].asPtr().cast<void>());", "LYR-SEM0233", "'GcHandle.from' holds")]
    public void A_handle_is_checked(string line, string code, string why)
    {
        var dir = Handles("fn main(): void {\n    " + line + "\n}\n");
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains($"error[{code}]: {why}", error);
    }

    private const string ThreadC = """
        #include <stdint.h>
        #include <stdlib.h>
        #ifdef _WIN32
        #include <windows.h>
        #else
        #include <pthread.h>
        #endif

        typedef struct { void (*f)(void *, int64_t); int64_t (*g)(void *, int64_t); void *userdata; int64_t n; } Job;

        static void work(Job *job) {
            for (int64_t i = 0; i < job->n; i++) {
                if (job->f != NULL) job->f(job->userdata, i); else (void)job->g(job->userdata, i);
            }
            free(job);
        }

        #ifdef _WIN32
        static DWORD WINAPI run(LPVOID arg) { work(arg); return 0; }
        static void start(Job *job, int wait) {
            HANDLE t = CreateThread(NULL, 0, run, job, 0, NULL);
            if (wait) WaitForSingleObject(t, INFINITE);
            CloseHandle(t);
        }
        #else
        static void *run(void *arg) { work(arg); return NULL; }
        static void start(Job *job, int wait) {
            pthread_t t;
            pthread_create(&t, NULL, run, job);
            if (wait) pthread_join(t, NULL); else pthread_detach(t);
        }
        #endif

        static Job *job(void (*f)(void *, int64_t), int64_t (*g)(void *, int64_t), void *userdata, int64_t n) {
            Job *j = malloc(sizeof *j);
            j->f = f; j->g = g; j->userdata = userdata; j->n = n;
            return j;
        }

        void start_ticker(void (*f)(void *, int64_t), void *userdata, int64_t n) { start(job(f, NULL, userdata, n), 0); }
        void ask_elsewhere(int64_t (*g)(void *, int64_t), void *userdata) { start(job(NULL, g, userdata, 1), 1); }

        """;

    private static string Threads(string main) => Package(
        ("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/*.c\"]\n\n[native.linux]\nlibs = [\"pthread\"]\n"),
        ("native/thread.c", ThreadC),
        ("src/main.lyr", """
            import std.io { println };
            import std.ffi { Ptr, Callback };
            import std.task { sleep };
            import std.time { Duration };

            extern "C" {
                fn start_ticker(f: extern "C" fn(Ptr<void>, int) -> void, userdata: Ptr<void>, n: int): void;
                fn ask_elsewhere(g: extern "C" fn(Ptr<void>, int) -> int, userdata: Ptr<void>): void;
            }

            """ + main));

    /// <summary>A Callback that returns nothing, called on a thread of C's own: each call goes to the
    /// thread that made it and runs there, in the order C made them, while C's thread goes on.</summary>
    [Fact]
    public void A_callback_from_a_thread_of_Cs_runs_on_its_owner()
    {
        var dir = Threads("""
            fn main(): void throws Error {
                var seen = 0;
                let cb = Callback<extern "C" fn(Ptr<void>, int) -> void>.new((i) => {
                    println(f"tick {i}");
                    seen += 1;
                });
                start_ticker(cb.function, cb.userdata, 3);
                while (seen < 3) {
                    try sleep(Duration.ofMillis(1));
                }
                cb.release();
                println(f"done {seen}");
            }

            """);
        Assert.Equal("tick 0\ntick 1\ntick 2\ndone 3\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>One that returns a value cannot wait for its owner: C's thread would block on a
    /// thread that may be blocked on it. It ends the program with LYR-RT0022.</summary>
    [Fact]
    public void A_returning_callback_from_a_thread_of_Cs_ends_the_program()
    {
        var dir = Threads("""
            fn main(): void {
                let cb = Callback<extern "C" fn(Ptr<void>, int) -> int>.new((i) => i + 1);
                ask_elsewhere(cb.function, cb.userdata);
                println("never");
            }

            """);
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var ran = Lyric5.Toolchain.ProcessRunner.Run(Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix),
            [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Contains("panic [LYR-RT0022]: C called a Lyric function on a thread Lyric does not run", ran.Stderr);
        Assert.DoesNotContain("never", ran.Stdout);
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
