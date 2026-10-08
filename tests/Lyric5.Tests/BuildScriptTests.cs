using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The packages' build scripts (design/v5/spec/11 W3 BS1–BS6; M8b S13a): a <c>build.lyr</c> runs
/// before its package compiles — a program of its own, its module <c>build</c> and <c>build/</c> —,
/// writes modules into <c>gen/</c> the package imports as <c>app.gen.x</c>, names C to compile and
/// libraries to link, warns; it runs again where it or an input it named changed; a dependency's
/// runs only where the root trusts it.
/// </summary>
[Collection("console")]
public class BuildScriptTests
{
    private const string Greeting = """
        import std.build;
        import std.fs;
        import std.path;
        import build.words { quoted };

        fn main(): void throws Error {
            let word = (try fs.readText("greeting.txt")).trim().toString();
            try fs.appendText(path.join(build.outDir(), "runs.txt"), "x");
            try fs.writeText(path.join(build.genDir(), "greeting.lyr"),
                f"pub fn greeting(): string {{ return {quoted(word)}; }}\npub let built = {quoted(build.profile())};\n");
            build.rerunIfChanged("greeting.txt");
        }
        """;

    private const string Words = """
        pub fn quoted(s: string): string {
            return "\"" + s + "\"";
        }
        """;

    private const string GreetingMain = """
        import app.gen.greeting { built, greeting };
        import std.io { println };

        fn main(): void {
            println(f"{greeting()} {built}");
        }
        """;

    private static int Runs(string dir) =>
        File.Exists(Path.Combine(dir, "out", "runs.txt")) ? File.ReadAllText(Path.Combine(dir, "out", "runs.txt")).Length : 0;

    [Fact]
    public void A_script_writes_a_module_the_program_imports()
    {
        var dir = Package(("lyric.toml", AppManifest), ("greeting.txt", "hello\n"), ("build.lyr", Greeting),
            ("build/words.lyr", Words), ("src/main.lyr", GreetingMain));
        Assert.Equal("hello debug\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.True(File.Exists(Path.Combine(dir, "gen", "greeting.lyr")));
    }

    [Fact]
    public void A_script_runs_again_only_where_it_or_an_input_changed()
    {
        var dir = Package(("lyric.toml", AppManifest), ("greeting.txt", "hello\n"), ("build.lyr", Greeting),
            ("build/words.lyr", Words), ("src/main.lyr", GreetingMain));
        Assert.Equal("hello debug\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal("hello debug\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal(1, Runs(dir));
        File.WriteAllText(Path.Combine(dir, "greeting.txt"), "again\n");
        Assert.Equal("again debug\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal(2, Runs(dir));
        File.AppendAllText(Path.Combine(dir, "build.lyr"), "\n// a change to the script\n");
        Assert.Equal("again debug\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal(3, Runs(dir));
        // gen/ taken away: the script writes it again
        Directory.Delete(Path.Combine(dir, "gen"), recursive: true);
        Assert.Equal("again debug\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Equal(4, Runs(dir));
    }

    [Fact]
    public void A_dependencys_script_runs_only_where_the_root_trusts_it()
    {
        const string geoScript = """
            import std.build;
            import std.fs;
            import std.path;

            fn main(): void throws Error {
                try fs.writeText(path.join(build.genDir(), "pi.lyr"), "pub let pi = 3;\n");
            }
            """;
        const string geoLib = """
            import geo.gen.pi { pi };

            pub fn circle(r: int): int {
                return pi * r * r;
            }
            """;
        const string main = """
            import geo { circle };
            import std.io { println };

            fn main(): void {
                println(f"{circle(2)}");
            }
            """;
        var manifest = AppManifest + "\n[dependencies]\ngeo = { path = \"../geo\" }\n";
        var root = Package(("app/lyric.toml", manifest), ("app/src/main.lyr", main),
            ("geo/lyric.toml", "[package]\nname = \"geo\"\nversion = \"1.0.0\"\n"), ("geo/build.lyr", geoScript), ("geo/src/lib.lyr", geoLib));
        var app = Path.Combine(root, "app");
        var (exit, _, error) = Run("build", "-C", app);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-PKG0012]: package 'geo' has a build script", error);
        Assert.Contains("lyric.toml:6:1", error.Replace('\\', '/'));
        File.WriteAllText(Path.Combine(app, "lyric.toml"), manifest + "\n[trust]\nbuild-scripts = [\"geo\"]\n");
        Assert.Equal("12\n", BuildAndRun(app, "app", "build", "-C", app));
    }

    [Fact]
    public void A_scripts_C_is_compiled_with_its_flags_and_linked()
    {
        const string script = """
            import std.build;

            fn main(): void {
                build.cFlags("-DTWICE=2");
                build.compileC("native/twice.c");
            }
            """;
        const string twice = "#include <stdint.h>\nint64_t twice_of(int64_t n) { return n * TWICE; }\n";
        const string main = """
            import std.io { println };

            extern "C" fn twice_of(n: int): int;

            fn main(): void {
                println(f"{twice_of(21)}");
            }
            """;
        var dir = Package(("lyric.toml", AppManifest), ("build.lyr", script), ("native/twice.c", twice), ("src/main.lyr", main));
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_script_that_fails_fails_the_build_and_shows_what_it_wrote()
    {
        const string script = """
            import std.io { println };

            fn main(): void {
                println("about to give up");
                panic("no schema found");
            }
            """;
        var dir = Package(("lyric.toml", AppManifest), ("build.lyr", script), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-BLD0002]: app's build.lyr ended with exit 101", error);
        Assert.Contains("about to give up", error);
        Assert.Contains("no schema found", error);
    }

    [Fact]
    public void A_script_that_does_not_compile_stops_the_build()
    {
        var dir = Package(("lyric.toml", AppManifest), ("build.lyr", "fn main(): void {\n    let x: int = \"text\";\n}\n"),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0001]", error);
        Assert.Contains("error[LYR-BLD0002]: app's build.lyr does not build", error);
    }

    private const string ToolsLib = """
        pub fn shout(s: string): string {
            return s + "!";
        }
        """;

    private const string ShoutScript = """
        import std.build;
        import std.fs;
        import std.path;
        import tools { shout };

        fn main(): void throws Error {
            try fs.writeText(path.join(build.genDir(), "said.lyr"), f"pub let said = \"{shout("hi")}\";\n");
        }
        """;

    [Fact]
    public void A_script_imports_its_build_dependency_and_the_program_does_not()
    {
        const string main = """
            import app.gen.said { said };
            import std.io { println };

            fn main(): void {
                println(said);
            }
            """;
        var manifest = AppManifest + "\n[build-dependencies]\ntools = { path = \"../tools\" }\n";
        var root = Package(("app/lyric.toml", manifest), ("app/build.lyr", ShoutScript), ("app/src/main.lyr", main),
            ("tools/lyric.toml", "[package]\nname = \"tools\"\nversion = \"1.0.0\"\n"), ("tools/src/lib.lyr", ToolsLib));
        var app = Path.Combine(root, "app");
        Assert.Equal("hi!\n", BuildAndRun(app, "app", "build", "-C", app));

        // the program's graph holds no build dependency (11 W3 BS3)
        File.WriteAllText(Path.Combine(app, "src", "main.lyr"),
            "import tools { shout };\nimport std.io { println };\n\nfn main(): void {\n    println(shout(\"no\"));\n}\n");
        var (exit, _, error) = Run("build", "-C", app);
        Assert.Equal(1, exit);
        Assert.Contains("tools", error);
    }

    [Fact]
    public void A_build_dependency_from_git_is_held_in_the_lock()
    {
        using var repo = new TestRepo();
        repo.Commit(("lyric.toml", "[package]\nname = \"tools\"\nversion = \"1.0.0\"\n"), ("src/lib.lyr", ToolsLib));
        repo.Tag("v1.0.0");
        var manifest = AppManifest + $"\n[build-dependencies]\ntools = {{ git = \"{repo.Url}\", tag = \"v1.0.0\" }}\n";
        const string main = "import app.gen.said { said };\nimport std.io { println };\n\nfn main(): void {\n    println(said);\n}\n";
        var dir = Package(("lyric.toml", manifest), ("build.lyr", ShoutScript), ("src/main.lyr", main));
        Assert.Equal("hi!\n", BuildAndRun(dir, "app", "build", "-C", dir));
        Assert.Contains("tools", File.ReadAllText(Path.Combine(dir, "lyric.lock")));
        // --locked takes that lock as it is
        Assert.Equal("hi!\n", BuildAndRun(dir, "app", "build", "-C", dir, "--locked"));
    }

    [Fact]
    public void A_scripts_warning_is_shown_with_the_build()
    {
        const string script = """
            import std.build;

            fn main(): void {
                build.warn("the schema is old");
            }
            """;
        var dir = Package(("lyric.toml", AppManifest), ("build.lyr", script), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(0, exit);
        Assert.Contains("warning[LYR-BLD0003]: app's build.lyr: the schema is old", error);
    }
}
