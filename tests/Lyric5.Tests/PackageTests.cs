using Lyric5.Build;

namespace Lyric5.Tests;

/// <summary>
/// Packages (design/v5/spec/07 V1, V7; 11 C8, P2, P5) through the driver: a package's modules are
/// named by their paths with the package's name in front and live under <c>src/</c>; its program is
/// <c>src/main.lyr</c>, or the module a command names; a <c>module</c> header is refused; a single
/// file is a package of its own and imports only <c>std</c>; a module path names its file exactly.
/// Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class PackageTests
{
    private static readonly string Manifest = "[package]\nname = \"app\"\nversion = \"0.1.0\"\n";

    private static readonly string Main_ = """
        import std.io { println };
        import app.util { answer };

        fn main(): void {
            println(f"answer {answer()}");
        }
        """;

    private static readonly string Util = """
        pub fn answer(): int {
            return 42;
        }
        """;

    [Fact]
    public void A_package_builds_from_its_manifest()
    {
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", Main_), ("src/util.lyr", Util));
        Assert.Equal("answer 42\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_module_of_a_package_is_a_program_too()
    {
        var tool = """
            import std.io { println };
            import app.util { answer };

            fn main(): void {
                println(f"tool {answer() + 1}");
            }
            """;
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", Main_), ("src/util.lyr", Util), ("src/tools/gen.lyr", tool));
        Assert.Equal("tool 43\n", BuildAndRun(dir, "gen", "build", Path.Combine(dir, "src", "tools", "gen.lyr")));
        Assert.Equal("answer 42\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A <c>main</c> in an imported module is an ordinary function (07 M7a): the 4.x rule,
    /// one <c>main</c> per compilation (SEM0021), was the VM's — one entry per <c>.lyrbc</c>.</summary>
    [Fact]
    public void A_main_in_an_imported_module_is_an_ordinary_function()
    {
        var main = """
            import std.io { println };
            import app.tool;

            fn main(): void {
                println(f"tool {tool.main()}");
            }
            """;
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", main), ("src/tool.lyr", "pub fn main(): int {\n    return 3;\n}\n"));
        Assert.Equal("tool 3\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_module_without_main_is_no_program()
    {
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", Main_), ("src/util.lyr", Util));
        var (exit, _, error) = Run("build", Path.Combine(dir, "src", "util.lyr"));
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0021]: module 'app.util' has no 'main'", error);
    }

    /// <summary><c>main</c> takes no parameters (07 M7b, 08 D18): the 4.x form
    /// <c>main(args: string[])</c> passed the front end and broke the C compile (LYR-BLD0001, exit 2).</summary>
    [Fact]
    public void Main_takes_no_parameters()
    {
        var dir = Package(("args.lyr", "fn main(args: string[]): int {\n    return args.length();\n}\n"));
        var (exit, _, error) = Run("build", Path.Combine(dir, "args.lyr"), "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0021]: 'main' is 'fn main(): void' or 'fn main(): int'", error);
    }

    /// <summary>A file beside a package's <c>src/</c> is a single file whose <c>out/</c> lies by the
    /// manifest (14 §1.2, 11 P5) — it once made an <c>out/</c> of its own beside itself.</summary>
    [Fact]
    public void A_script_beside_src_builds_into_the_packages_out()
    {
        var script = "import std.io { println };\n\nfn main(): void {\n    println(\"script\");\n}\n";
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", Main_), ("src/util.lyr", Util), ("tools/gen.lyr", script));
        Assert.Equal("script\n", BuildAndRun(dir, "gen", "build", Path.Combine(dir, "tools", "gen.lyr")));
        Assert.False(Directory.Exists(Path.Combine(dir, "tools", "out")), "no out/ beside the script");
    }

    [Fact]
    public void A_module_is_named_by_its_path()
    {
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", Main_), ("src/util.lyr", Util));
        var (exit, output, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.True(exit == 0, error);
        Assert.Contains("fn app.main.main", output);
        Assert.Contains("fn app.util.answer", output);
    }

    [Fact]
    public void A_module_header_is_refused()
    {
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", Main_), ("src/util.lyr", "module app.util;\n" + Util));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("LYR-RES0008", error);
    }

    [Fact]
    public void A_single_file_imports_only_std()
    {
        var dir = Package(("main.lyr", Main_.Replace("app.util", "util")), ("util.lyr", Util));
        var (exit, _, error) = Run("build", Path.Combine(dir, "main.lyr"), "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("LYR-RES0003", error);
    }

    [Fact]
    public void A_module_path_names_its_file_exactly()
    {
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", Main_), ("src/Util.lyr", Util));
        var (exit, _, error) = Run("build", "-C", dir, "--emit", "ir");
        Assert.Equal(1, exit);
        Assert.Contains("cannot find module 'app.util'", error);
    }

    [Fact]
    public void Without_a_manifest_or_a_file_there_is_nothing_to_build()
    {
        var dir = Package(("notes.txt", "nothing"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("LYR-CLI0004", error);
    }

    [Fact]
    public void A_package_without_src_main_is_a_library()
    {
        var dir = Package(("lyric.toml", Manifest), ("src/util.lyr", Util));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("LYR-CLI0005", error);
    }

    [Fact]
    public void A_refused_manifest_is_exit_1()
    {
        var dir = Package(("lyric.toml", "[package]\nname = \"app\"\n"), ("src/main.lyr", Main_));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("LYR-PKG0002", error);
    }

    /// <summary>The C of a package is keyed by every one of its files: a changed module the entry
    /// imports builds anew (it once reused the C keyed by the entry file alone).</summary>
    [Fact]
    public void A_changed_module_builds_anew()
    {
        var dir = Package(("lyric.toml", Manifest), ("src/main.lyr", Main_), ("src/util.lyr", Util));
        Assert.Equal("answer 42\n", BuildAndRun(dir, "app", "build", "-C", dir));
        File.WriteAllText(Path.Combine(dir, "src", "util.lyr"), Util.Replace("42", "43"));
        Assert.Equal("answer 43\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>Builds through <c>Main</c>, then runs the binary — <c>run</c> would hand the
    /// program the console itself, past the test's capture.</summary>
    private static string BuildAndRun(string dir, string binary, params string[] args)
    {
        var (exit, _, error) = Run(args);
        Assert.True(exit == 0, error);
        var host = Lyric5.Toolchain.Target.Host;
        var exe = System.IO.Path.Combine(dir, "out", "debug", host.Triple, binary + host.ExecutableSuffix);
        Assert.True(File.Exists(exe), $"no binary {exe}");
        var ran = Lyric5.Toolchain.ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 0, ran.Stderr);
        return ran.Stdout.Replace("\r\n", "\n");
    }

    private static string Package(params (string Path, string Text)[] files)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lyric5-package-" + Guid.NewGuid().ToString("N"));
        foreach (var (path, text) in files)
        {
            var full = System.IO.Path.Combine(dir, path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }
        return dir;
    }

    private static (int Exit, string Out, string Err) Run(params string[] args)
    {
        var savedOut = Console.Out;
        var savedErr = Console.Error;
        var output = new StringWriter();
        var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exit = Program.Main(args);
            return (exit, output.ToString().Replace("\r\n", "\n"), error.ToString().Replace("\r\n", "\n"));
        }
        finally
        {
            Console.SetOut(savedOut);
            Console.SetError(savedErr);
        }
    }
}
