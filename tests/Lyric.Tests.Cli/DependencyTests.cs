using Lyric.Core;

namespace Lyric.Tests.Cli;

/// <summary>
/// <c>lyric.json</c> v2: a project stands on other projects (<c>dependencies</c>), says what it
/// is called (<c>name</c>) and what it needs (<c>toolchain</c>).
///
/// <para>A dependency owns its first segment exactly as a native root does, and the table every
/// tool works from is FLAT: one segment, one directory, for the whole program. The tests below
/// pin the three rules that fall out of that — transitive entries arrive, the root's own entry
/// pins a segment, and two dependencies disagreeing without the root's word is a refusal that
/// names both.</para>
///
/// <para>Through the binaries, like <see cref="ProjectFileTests"/>: the file is found by walking
/// up from the entry file, and a dependency's path is relative to the file that names it.</para>
/// </summary>
public sealed class DependencyTests
{
    /// <summary>A workspace of sibling projects; each test lays out the ones it needs.</summary>
    private static TemporaryDirectory Workspace() => Toolchain.TempDirectory();

    private static void Library(TemporaryDirectory ws, string name, string source,
        string manifest = """{ "sourceRoot": "src" }""")
    {
        ws.Write(Path.Combine(name, "lyric.json"), manifest);
        ws.Write(Path.Combine(name, "src", name + ".lyr"), source);
    }

    private static string App(TemporaryDirectory ws, string manifest, string main)
    {
        ws.Write(Path.Combine("app", "lyric.json"), manifest);
        ws.Write(Path.Combine("app", "src", "main.lyr"), main);
        return Path.Combine(ws.Path, "app", "src", "main.lyr");
    }

    [Fact]
    public void A_dependency_owns_its_segment_and_everything_under_it()
    {
        using var ws = Workspace();
        Library(ws, "geometry", "module geometry;\n\npub fn one(): int { return 1; }\n");
        ws.Write(Path.Combine("geometry", "src", "geometry", "shapes.lyr"),
            "module geometry.shapes;\n\npub fn square(n: int): int { return n * n; }\n");

        var main = App(ws, """{ "sourceRoot": "src", "dependencies": { "geometry": "../geometry" } }""",
            "import geometry { one };\nimport geometry.shapes { square };\n\n"
            + "fn main(): int { return one() + square(3); }\n");

        var run = Toolchain.Lyric("run", main);
        Assert.Equal("", run.Err);
        Assert.Equal(10, run.ExitCode);
    }

    [Fact]
    public void A_dependency_without_a_manifest_is_its_own_root()
    {
        // A directory of modules is a project with its directory as the root — what every
        // project was before lyric.json existed, and what a bare checkout still is.
        using var ws = Workspace();
        ws.Write(Path.Combine("plain", "plain.lyr"), "module plain;\n\npub fn seven(): int { return 7; }\n");

        var main = App(ws, """{ "sourceRoot": "src", "dependencies": { "plain": "../plain" } }""",
            "import plain { seven };\n\nfn main(): int { return seven(); }\n");

        Assert.Equal(7, Toolchain.Lyric("run", main).ExitCode);
    }

    [Fact]
    public void Dependencies_are_transitive()
    {
        using var ws = Workspace();
        Library(ws, "mathx", "module mathx;\n\npub fn twice(n: int): int { return n * 2; }\n");
        Library(ws, "geometry",
            "module geometry;\n\nimport mathx { twice };\n\npub fn perimeter(side: int): int { return twice(twice(side)); }\n",
            """{ "sourceRoot": "src", "dependencies": { "mathx": "../mathx" } }""");

        // The app names geometry alone; mathx arrives because geometry needs it.
        var main = App(ws, """{ "sourceRoot": "src", "dependencies": { "geometry": "../geometry" } }""",
            "import geometry { perimeter };\n\nfn main(): int { return perimeter(5); }\n");

        var run = Toolchain.Lyric("run", main);
        Assert.Equal("", run.Err);
        Assert.Equal(20, run.ExitCode);
    }

    [Fact]
    public void The_root_project_pins_a_segment_its_dependencies_disagree_on()
    {
        using var ws = Workspace();
        ws.Write(Path.Combine("c_one", "c.lyr"), "module c;\n\npub fn v(): int { return 1; }\n");
        ws.Write(Path.Combine("c_two", "c.lyr"), "module c;\n\npub fn v(): int { return 2; }\n");
        Library(ws, "lib_b", "module lib_b;\n\nimport c { v };\n\npub fn viaB(): int { return v(); }\n",
            """{ "sourceRoot": "src", "dependencies": { "c": "../c_two" } }""");

        // lib_b says c is c_two; the app says c is c_one. The app is the root, so its word
        // stands for everybody, lib_b included — one segment, one directory, for the program.
        var main = App(ws, """
            { "sourceRoot": "src", "dependencies": { "lib_b": "../lib_b", "c": "../c_one" } }
            """,
            "import lib_b { viaB };\nimport c { v };\n\nfn main(): int { return viaB() * 10 + v(); }\n");

        var run = Toolchain.Lyric("run", main);
        Assert.Equal("", run.Err);
        Assert.Equal(11, run.ExitCode);
    }

    [Fact]
    public void Two_dependencies_disagreeing_on_a_segment_are_refused_with_both_named()
    {
        using var ws = Workspace();
        ws.Write(Path.Combine("c_one", "c.lyr"), "module c;\n\npub fn v(): int { return 1; }\n");
        ws.Write(Path.Combine("c_two", "c.lyr"), "module c;\n\npub fn v(): int { return 2; }\n");
        Library(ws, "lib_b", "module lib_b;\n\nimport c { v };\n\npub fn viaB(): int { return v(); }\n",
            """{ "sourceRoot": "src", "dependencies": { "c": "../c_two" } }""");
        Library(ws, "lib_d", "module lib_d;\n\nimport c { v };\n\npub fn viaD(): int { return v(); }\n",
            """{ "sourceRoot": "src", "dependencies": { "c": "../c_one" } }""");

        var main = App(ws, """
            { "sourceRoot": "src", "dependencies": { "lib_b": "../lib_b", "lib_d": "../lib_d" } }
            """,
            "import lib_b { viaB };\nimport lib_d { viaD };\n\nfn main(): int { return viaB() + viaD(); }\n");

        var check = Toolchain.Lyrc("check", main);
        Assert.NotEqual(ExitCodes.Success, check.ExitCode);
        Assert.Contains(CliDiagnostics.BadProjectFile, check.Err, StringComparison.Ordinal);
        Assert.Contains("c_one", check.Err, StringComparison.Ordinal);
        Assert.Contains("c_two", check.Err, StringComparison.Ordinal);
        Assert.Contains("lyric.json to decide", check.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dependency_key_is_one_segment_and_never_std()
    {
        using var ws = Workspace();
        ws.Write(Path.Combine("x", "x.lyr"), "module x;\n");

        var dotted = App(ws, """{ "sourceRoot": "src", "dependencies": { "a.b": "../x" } }""",
            "fn main(): int { return 0; }\n");
        var refused = Toolchain.Lyrc("check", dotted);
        Assert.Contains(CliDiagnostics.BadProjectFile, refused.Err, StringComparison.Ordinal);
        Assert.Contains("'a.b' is not a module path segment", refused.Err, StringComparison.Ordinal);

        var std = App(ws, """{ "sourceRoot": "src", "dependencies": { "std": "../x" } }""",
            "fn main(): int { return 0; }\n");
        var taken = Toolchain.Lyrc("check", std);
        Assert.Contains(CliDiagnostics.BadProjectFile, taken.Err, StringComparison.Ordinal);
        Assert.Contains("standard library", taken.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dependency_that_is_not_a_directory_is_named()
    {
        using var ws = Workspace();
        var main = App(ws, """{ "sourceRoot": "src", "dependencies": { "ghost": "../nowhere" } }""",
            "fn main(): int { return 0; }\n");

        var check = Toolchain.Lyrc("check", main);
        Assert.NotEqual(ExitCodes.Success, check.ExitCode);
        Assert.Contains("dependencies.ghost", check.Err, StringComparison.Ordinal);
        Assert.Contains("nowhere", check.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dependency_brings_its_native_roots_along()
    {
        // An SDK that ships as a project: its own lyric.json names the segment its natives live
        // under, and a program depending on it may import that segment as if it had declared
        // the root itself. The host still supplies the implementations, so this is a check.
        using var ws = Workspace();
        Library(ws, "sdklib",
            "module sdklib;\n\nimport engine.input { keyDown };\n\npub fn any(): bool { return keyDown(1); }\n",
            """{ "sourceRoot": "src", "nativeRoots": { "engine": "sdk" } }""");
        ws.Write(Path.Combine("sdklib", "sdk", "engine", "input.lyr"),
            "module engine.input;\n\npub fn keyDown(key: int): bool;\n");

        var main = App(ws, """{ "sourceRoot": "src", "dependencies": { "sdklib": "../sdklib" } }""",
            "import sdklib { any };\nimport engine.input { keyDown };\n\n"
            + "fn main(): int { return if (any() || keyDown(2)) 1 else 0; }\n");

        var check = Toolchain.Lyrc("check", main);
        Assert.Equal("", check.Err);
        Assert.Equal(ExitCodes.Success, check.ExitCode);
    }

    [Fact]
    public void A_segment_belongs_to_one_root()
    {
        using var ws = Workspace();
        ws.Write(Path.Combine("x", "x.lyr"), "module x;\n");
        ws.Write(Path.Combine("sdk", "x", "n.lyr"), "module x.n;\n");

        var main = App(ws, """
            { "sourceRoot": "src", "dependencies": { "x": "../x" }, "nativeRoots": { "x": "../sdk" } }
            """,
            "fn main(): int { return 0; }\n");

        var check = Toolchain.Lyrc("check", main);
        Assert.Contains(CliDiagnostics.BadProjectFile, check.Err, StringComparison.Ordinal);
        Assert.Contains("belongs to one root", check.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_toolchain_key_is_a_minimum()
    {
        using var ws = Workspace();
        var satisfied = App(ws, """{ "sourceRoot": "src", "toolchain": "1.0" }""",
            "fn main(): int { return 3; }\n");
        Assert.Equal(3, Toolchain.Lyric("run", satisfied).ExitCode);

        var ahead = App(ws, """{ "sourceRoot": "src", "toolchain": "99.0" }""",
            "fn main(): int { return 3; }\n");
        var refused = Toolchain.Lyrc("check", ahead);
        Assert.NotEqual(ExitCodes.Success, refused.ExitCode);
        Assert.Contains(CliDiagnostics.ToolchainTooOld, refused.Err, StringComparison.Ordinal);
        Assert.Contains("needs toolchain 99.0", refused.Err, StringComparison.Ordinal);
        Assert.Contains(ToolchainVersion.Value, refused.Err, StringComparison.Ordinal);

        var misspelled = App(ws, """{ "sourceRoot": "src", "toolchain": "four" }""",
            "fn main(): int { return 3; }\n");
        var broken = Toolchain.Lyrc("check", misspelled);
        Assert.Contains(CliDiagnostics.BadProjectFile, broken.Err, StringComparison.Ordinal);
        Assert.Contains("not a version like 4.5", broken.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dependency_may_ask_for_a_newer_toolchain_too()
    {
        using var ws = Workspace();
        Library(ws, "future", "module future;\n\npub fn f(): int { return 1; }\n",
            """{ "sourceRoot": "src", "toolchain": "99.0" }""");
        var main = App(ws, """{ "sourceRoot": "src", "dependencies": { "future": "../future" } }""",
            "import future { f };\n\nfn main(): int { return f(); }\n");

        var refused = Toolchain.Lyrc("check", main);
        Assert.Contains(CliDiagnostics.ToolchainTooOld, refused.Err, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("future", "lyric.json"), refused.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_name_key_has_to_be_a_module_name()
    {
        using var ws = Workspace();
        var named = App(ws, """{ "name": "ok_1", "sourceRoot": "src" }""", "fn main(): int { return 4; }\n");
        Assert.Equal(4, Toolchain.Lyric("run", named).ExitCode);

        var spaced = App(ws, """{ "name": "my app", "sourceRoot": "src" }""", "fn main(): int { return 4; }\n");
        var refused = Toolchain.Lyrc("check", spaced);
        Assert.Contains(CliDiagnostics.BadProjectFile, refused.Err, StringComparison.Ordinal);
        Assert.Contains("not a module name", refused.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void The_test_runner_and_the_build_runner_see_the_dependencies()
    {
        using var ws = Workspace();
        Library(ws, "geometry", "module geometry;\n\npub fn one(): int { return 1; }\n");
        var main = App(ws, """{ "sourceRoot": "src", "dependencies": { "geometry": "../geometry" } }""",
            "import geometry { one };\n\nfn main(): int { return one(); }\n");
        ws.Write(Path.Combine("app", "tests", "geometry_tests.lyr"),
            "import std.test { Test, assertEq };\nimport geometry { one };\n\n"
            + "@Test\npub fn one_is_one(): void {\n    assertEq(one(), 1);\n}\n");
        ws.Write(Path.Combine("app", "build.lyr"),
            "import std.build { addExecutable };\n\npub fn build() {\n"
            + "    addExecutable(\"src/main.lyr\", \"out/app.lyrbc\");\n}\n");

        var app = Path.Combine(ws.Path, "app");
        var tests = Toolchain.Lyrtest(app);
        Assert.Equal(0, tests.ExitCode);
        Assert.Contains("1 test(s), all passed", tests.Out);

        Assert.Equal(ExitCodes.Success, Toolchain.Lyrbuild(app).ExitCode);
        Assert.Equal(1, Toolchain.Lyrvm("run", Path.Combine(app, "out", "app.lyrbc")).ExitCode);
        _ = main;
    }
}
