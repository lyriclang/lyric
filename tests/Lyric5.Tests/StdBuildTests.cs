using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>std.build</c> (spec 15 §8 rule 3): the target as a value, a C library a script compiles with
/// definitions and flags of its own, an archive it links, a source tree it asks for — read then,
/// the script run again —, and the environment another build system compiles C under. Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class StdBuildTests
{
    private const string CallMain = """
        import std.io { println };

        extern "C" fn value_of(n: int): int;

        fn main(): void {
            println(f"{value_of(2)}");
        }
        """;

    private static string Script(string body) => $"import std.build;\n\nfn main(): void {{\n{body}\n}}\n";

    [Fact]
    public void The_target_is_a_value()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("build.lyr", Script("    let t = build.target();\n    build.warn(f\"[{t.triple}] [{t.os}] [{t.arch}] [{t.isHost}]\");")),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        var os = Target.Host.Os switch { TargetOs.Windows => "windows", TargetOs.MacOs => "macos", _ => "linux" };
        Assert.Contains($"[{Target.Host.Triple}] [{os}] [{Target.Host.Arch}] [true]", error);
    }

    /// <summary>A C library's definitions and flags are its own: the package's own C does not see them.</summary>
    [Fact]
    public void A_C_librarys_definitions_are_its_own()
    {
        var dir = Package(("lyric.toml", AppManifest + "\n[native]\nsources = [\"native/own.c\"]\n"),
            ("build.lyr", Script("    build.cLibrary(\"lib\").sources(\"native/lib/*.c\").include(\"native/lib/inc\").define(\"SCALE\", \"10\").define(\"ONLY_LIB\").flags(\"-O2\").compile();")),
            ("native/lib/inc/scale.h", "#define OFFSET 1\n"),
            ("native/lib/value.c", "#include <stdint.h>\n#include \"scale.h\"\n#ifndef ONLY_LIB\n#error ONLY_LIB\n#endif\nint64_t lib_value(int64_t n) { return n * SCALE + OFFSET; }\n"),
            ("native/own.c", "#include <stdint.h>\n#ifdef ONLY_LIB\n#error the library's definition reached the package's own C\n#endif\nint64_t lib_value(int64_t n);\nint64_t value_of(int64_t n) { return lib_value(n) + 1; }\n"),
            ("src/main.lyr", CallMain));
        Assert.Equal("22\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A script asks for a source tree when it runs: the build reads it then, and runs the
    /// script again with it there; the tree's sources compile as the package's.</summary>
    [Fact]
    public void A_script_reads_a_source_tree_when_it_asks_for_it()
    {
        using var repo = new TestRepo();
        repo.Commit(("include/clib.h", "#include <stdint.h>\nint64_t clib_twice(int64_t n);\n"),
            ("src/clib.c", "#include \"clib.h\"\nint64_t clib_twice(int64_t n) { return 2 * n; }\n"));
        var dir = Package(("lyric.toml", AppManifest + $"\n[native.vendor]\nclib = {{ git = \"{repo.Url}\" }}\n\n[native]\nsources = [\"native/*.c\"]\n"),
            ("build.lyr", Script("    let dir = build.vendorDir(\"clib\");\n    build.warn(f\"tree at {dir}\");\n"
                                 + "    build.cLibrary(\"clib\").sources(\"@clib/src/*.c\").include(\"@clib/include\").compile();")),
            ("native/glue.c", "#include <stdint.h>\nint64_t clib_twice(int64_t n);\nint64_t value_of(int64_t n) { return clib_twice(n) + 10; }\n"),
            ("src/main.lyr", CallMain));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 0, error);
        Assert.Contains($"tree at {Path.Combine(UserCache.Root, "git", "trees", Lyric5.Build.GitCache.Id(repo.Url))}", error);
        Assert.Contains("kind = \"native\"", File.ReadAllText(Path.Combine(dir, "lyric.lock")));
        Assert.Equal("14\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_script_asks_for_a_tree_its_package_holds()
    {
        var dir = Package(("lyric.toml", AppManifest), ("build.lyr", Script("    build.warn(build.vendorDir(\"nope\"));")),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-BLD0002]: app's build.lyr asks for the source tree 'nope', which its [native.vendor] does not hold", error);
    }

    [Fact]
    public void A_C_librarys_paths_are_the_packages()
    {
        var dir = Package(("lyric.toml", AppManifest), ("build.lyr", Script("    build.cLibrary(\"x\").sources(\"../elsewhere/x.c\").compile();")),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-BLD0002]: app's build.lyr names '../elsewhere/x.c' as a source of its C library, and it is no path of the package or a tree's", error);
    }

    /// <summary>Another build system compiles C for the target with the toolchain's compiler: the
    /// script runs <c>CC</c> and <c>AR</c> of <c>cEnv()</c> itself, then links what they made.</summary>
    [Fact]
    public void A_script_compiles_with_cEnv_and_links_the_archive()
    {
        if (OperatingSystem.IsWindows()) return;   // CC is a .cmd there, which a Command starts through cmd
        const string script = """
            import std.build;
            import std.process { Command };

            fn main(): void {
                let env = build.cEnv();
                var cc = "";
                var ar = "";
                for (pair in env) {
                    let (name, value) = pair;
                    if (name == "CC") {
                        cc = value;
                    } else if (name == "AR") {
                        ar = value;
                    }
                }
                let out = f"{build.outDir()}/made";
                try {
                    if (!Command { program = "mkdir", args = ["-p", out] }.status().success()) {
                        panic("mkdir failed");
                    }
                    if (!Command { program = cc, args = ["-c", "vendor/made.c", "-o", f"{out}/made.o"], env = env }.status().success()) {
                        panic("cc failed");
                    }
                    if (!Command { program = ar, args = ["rcs", f"{out}/libmade.a", f"{out}/made.o"], env = env }.status().success()) {
                        panic("ar failed");
                    }
                } catch (e) {
                    panic(e.message());
                }
                build.linkArchive(f"{out}/libmade.a");
            }
            """;
        var dir = Package(("lyric.toml", AppManifest), ("build.lyr", script),
            ("vendor/made.c", "#include <stdint.h>\nint64_t value_of(int64_t n) { return n + 40; }\n"),
            ("src/main.lyr", CallMain));
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }
}
