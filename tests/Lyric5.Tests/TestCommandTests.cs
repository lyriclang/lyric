using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric test</c> (design/v5/spec/10 B12, 07 V4): the package's <c>@Test</c> functions — beside
/// the code under <c>src/</c>, and under <c>tests/</c> — built into one program and run, each in a
/// task of its own; the program's exit is the command's. Through <c>Main</c>, so in the console
/// collection.
/// </summary>
[Collection("console")]
public class TestCommandTests
{
    /// <summary>A module with a test beside its code: the test sees what is private to it (V4).</summary>
    private const string MathModule =
        "import std.test { assertEq };\n\n"
        + "pub fn add(a: int, b: int): int {\n    return a + b;\n}\n\n"
        + "fn twice(x: int): int {\n    return x * 2;\n}\n\n"
        + "@Test\nfn twiceDoubles(): void {\n    assertEq(twice(4), 8);\n}\n";

    /// <summary>A module under tests/: a module of the package, which sees what is not private.</summary>
    private const string ArithTests =
        "import std.test { assertEq, assertTrue };\nimport app.math { add };\n\n"
        + "@Test\nfn addsTwo(): void {\n    assertEq(add(2, 3), 5);\n}\n\n"
        + "@Test\nfn holds(): void {\n    assertTrue(add(1, 1) == 2);\n}\n";

    private static string App(params (string Path, string Text)[] more) =>
        Package([("lyric.toml", AppManifest), ("src/math.lyr", MathModule), ("tests/arith.lyr", ArithTests), .. more]);

    [Fact]
    public void Tests_beside_the_code_and_under_tests_run_in_the_order_of_their_modules()
    {
        var dir = App();
        var (exit, output, error) = Run("test", "-C", dir);
        Assert.True(exit == 0, output + error);
        Assert.Equal("ok    app.math.twiceDoubles\nok    app.tests.arith.addsTwo\nok    app.tests.arith.holds\n\n"
                     + "3 tests: 3 passed, 0 failed, 0 skipped\n", output);
    }

    [Fact]
    public void A_failed_assertion_fails_its_test_and_the_run_goes_on()
    {
        var dir = App(("tests/wrong.lyr",
            "import std.test { assertEq };\nimport app.math { add };\n\n"
            + "@Test\nfn adds(): void {\n    assertEq(add(2, 2), 5);\n}\n\n@Test\nfn after(): void {\n}\n"));
        var (exit, output, error) = Run("test", "-C", dir);
        Assert.True(exit == 1, output + error);
        Assert.Contains("FAIL  app.tests.wrong.adds\n      panic [LYR-RT0008]: got 4, expected 5\n", output);
        Assert.Contains("ok    app.tests.wrong.after\n", output);
        Assert.EndsWith("5 tests: 4 passed, 1 failed, 0 skipped\n", output);
    }

    [Fact]
    public void An_error_that_leaves_a_test_fails_it()
    {
        var dir = App(("tests/throws.lyr",
            "@Test\nfn refuses(): void throws Error {\n    throw Exception { text = \"not today\" };\n}\n"));
        var (exit, output, error) = Run("test", "-C", dir);
        Assert.True(exit == 1, output + error);
        Assert.Contains("FAIL  app.tests.throws.refuses\n      error: not today\n", output);
    }

    [Fact]
    public void A_skipped_test_is_named_and_not_run()
    {
        var dir = App(("tests/later.lyr",
            "@Test { skip = \"needs a network\" }\nfn fetches(): void {\n    panic(\"ran\");\n}\n"));
        var (exit, output, error) = Run("test", "-C", dir);
        Assert.True(exit == 0, output + error);
        Assert.Contains("skip  app.tests.later.fetches: needs a network\n", output);
        Assert.EndsWith("4 tests: 3 passed, 0 failed, 1 skipped\n", output);
    }

    [Fact]
    public void The_filter_runs_the_tests_whose_name_holds_it()
    {
        var dir = App();
        var (exit, output, error) = Run("test", "-C", dir, "-f", "adds");
        Assert.True(exit == 0, output + error);
        Assert.Equal("ok    app.tests.arith.addsTwo\n\n1 test: 1 passed, 0 failed, 0 skipped\n", output);
        var (none, said, _) = Run("test", "-C", dir, "--filter", "nothing");
        Assert.Equal(0, none);
        Assert.Equal("no test's name holds 'nothing'\n", said);
    }

    [Fact]
    public void A_library_has_tests_and_a_package_without_any_says_so()
    {
        // App() has no src/main.lyr: a library.
        var (exit, output, error) = Run("test", "-C", App());
        Assert.True(exit == 0, output + error);
        var bare = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (none, said, _) = Run("test", "-C", bare);
        Assert.Equal(0, none);
        Assert.Equal("no tests\n", said);
    }

    [Fact]
    public void A_refused_test_is_exit_1_and_a_build_reaches_no_test()
    {
        var dir = App(("src/main.lyr", "import app.math { add };\nimport std.io { println };\n\nfn main(): void {\n    println(f\"{add(1, 2)}\");\n}\n"),
            ("tests/broken.lyr", "@Test\nfn takes(x: int): void {\n}\n"));
        var (exit, _, error) = Run("test", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("LYR-SEM0154", error);
        Assert.Contains("a test takes no parameters", error);
        // The program builds: nothing of tests/ is compiled for it (15 §7).
        Assert.Equal("3\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void A_module_under_both_roots_is_refused()
    {
        var dir = App(("src/tests/arith.lyr", "pub fn x(): int {\n    return 1;\n}\n"));
        var (exit, _, error) = Run("test", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-RES0015]: the module 'app.tests.arith' lies under src/tests/ and under tests/", error);
    }

    [Fact]
    public void A_file_is_no_package_to_test()
    {
        var (exit, _, error) = Run("test", "main.lyr");
        Assert.Equal(2, exit);
        Assert.Contains("'test' runs a package's tests and takes no file", error);
    }
}
