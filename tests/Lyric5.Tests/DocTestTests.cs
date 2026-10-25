using Lyric5.Build;
using Lyric5.Doc;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Doc-tests: the Lyric blocks of documentation comments, run by <c>lyric test</c> as tests of
/// their own — named after what they document and where, counted, filtered and written as JSON with
/// the others — and the standard library's, each built and checked here as <c>lyric test</c> checks
/// a package's.
/// </summary>
[Collection("console")]
public class DocTestTests
{
    private const string Manifest = "[package]\nname = \"geo\"\nversion = \"0.1.0\"\n";

    /// <summary>One block of each kind: a module's, a member's with its output, a function's that
    /// only builds and one that must fail.</summary>
    private const string Lib = """
        //! Shapes of the plane.
        //!
        //! ```lyric
        //! import geo { Point };
        //! import std.io { println };
        //!
        //! println(Point.origin().x);
        //! ```
        //!
        //! ```output
        //! 0
        //! ```

        /// A place in the plane.
        pub struct Point {
            pub x: int,
            pub y: int,

            /// The point at the origin.
            ///
            /// ```lyric
            /// import std.io { println };
            ///
            /// let p = Point.origin();
            /// println(f"{p.x},{p.y}");
            /// ```
            ///
            /// ```output
            /// 0,0
            /// ```
            pub static fn origin(): Point {
                return Point { x = 0, y = 0 };
            }
        }

        /// The sum of two numbers.
        ///
        /// ```lyric,no_run
        /// let s = add(1, 2);
        /// ```
        ///
        /// A string is no number:
        ///
        /// ```lyric,compile_fail=LYR-SEM0001
        /// let s: int = add(1, "two");
        /// ```
        pub fn add(a: int, b: int): int {
            return a + b;
        }

        """;

    private static string Geo(string lib = Lib) => Package(("lyric.toml", Manifest), ("src/lib.lyr", lib));

    [Fact]
    public void Every_kind_of_doc_test_runs_and_holds()
    {
        var (exit, said, error) = RunRaw("test", "-C", Geo());
        Assert.True(exit == 0, said + error);
        Assert.Contains("ok    doc: geo (src/lib.lyr:3)", said);
        Assert.Contains("ok    doc: geo.Point.origin (src/lib.lyr:21)", said);
        Assert.Contains("ok    doc: geo.add (src/lib.lyr:38)", said);
        Assert.Contains("ok    doc: geo.add (src/lib.lyr:44)", said);
        Assert.Contains("4 tests: 4 passed, 0 failed, 0 skipped", said);
    }

    [Fact]
    public void A_wrong_output_is_red_with_its_file_and_line()
    {
        var (exit, said, _) = RunRaw("test", "-C", Geo(Lib.Replace("    /// 0,0\n", "    /// 0,1\n")));
        Assert.Equal(1, exit);
        Assert.Contains("FAIL  doc: geo.Point.origin (src/lib.lyr:21)", said);
        Assert.Contains("the output block says\n      0,1\n      the program printed\n      0,0", said.Replace("\r\n", "\n"));
    }

    [Fact]
    public void A_block_that_fails_with_another_code_is_red()
    {
        var (exit, said, _) = RunRaw("test", "-C", Geo(Lib.Replace("compile_fail=LYR-SEM0001", "compile_fail=LYR-SEM0002")));
        Assert.Equal(1, exit);
        Assert.Contains("FAIL  doc: geo.add (src/lib.lyr:44)", said);
        Assert.Contains("it should fail with LYR-SEM0002 alone", said);
    }

    [Fact]
    public void A_filter_takes_doc_tests_by_their_names()
    {
        var (exit, said, error) = RunRaw("test", "-C", Geo(), "-f", "Point.origin");
        Assert.True(exit == 0, said + error);
        Assert.Contains("ok    doc: geo.Point.origin (src/lib.lyr:21)", said);
        Assert.Contains("1 test: 1 passed, 0 failed, 0 skipped", said);
    }

    [Fact]
    public void The_json_form_lists_the_doc_tests_with_the_others()
    {
        var (exit, said, error) = RunRaw("test", "-C", Geo(), "--json");
        Assert.True(exit == 0, said + error);
        Assert.Contains("{\"type\":\"test\",\"version\":1,\"name\":\"doc: geo.add (src/lib.lyr:44)\",\"outcome\":\"passed\"}", said);
        Assert.Contains("{\"type\":\"tests\",\"version\":1,\"passed\":4,\"failed\":0,\"skipped\":0}", said);
        // the block that must fail is the doc-test's to read, not a finding of the run
        Assert.Contains("\"errors\":0", said);
    }

    [Fact]
    public void A_block_names_what_it_documents_and_imports_it()
    {
        var tests = DocTests.Extract("src/lib.lyr", "geo", Lib);
        Assert.Equal(["doc: geo (src/lib.lyr:3)", "doc: geo.Point.origin (src/lib.lyr:21)", "doc: geo.add (src/lib.lyr:38)",
            "doc: geo.add (src/lib.lyr:44)"], tests.Select(t => t.Name));
        // the module's block imports nothing; a member's its type; a function's the function
        Assert.DoesNotContain("import geo { Point }; import", tests[0].Program);
        Assert.Contains("import geo { Point };", tests[1].Program);
        Assert.Contains("import geo { add };", tests[2].Program);
    }

    [Fact]
    public void A_program_kept_as_written_keeps_its_lines()
    {
        const string text = "/// ```lyric,compile_fail=LYR-SEM0001\n/// fn main(): void {\n///     let x: int = f();\n/// }\n/// ```\npub fn f(): string {\n    return \"\";\n}\n";
        var test = Assert.Single(DocTests.Extract("src/lib.lyr", "geo", text));
        var lines = test.Program.Split('\n');
        Assert.Equal("import geo { f }; fn main(): void {", lines[0]);
        Assert.Equal("    let x: int = f();", lines[1]);
    }

    [Fact]
    public void A_snippet_the_form_refuses_is_a_red_test_of_its_module()
    {
        var (exit, said, _) = RunRaw("test", "-C", Geo(Lib.Replace("lyric,no_run", "lyric,should_panic")));
        Assert.Equal(1, exit);
        Assert.Contains("FAIL  doc: geo (src/lib.lyr)", said);
        Assert.Contains("unknown snippet kind", said);
    }

    /// <summary>The standard library's doc-tests, by name.</summary>
    public static TheoryData<string> StdDocTests()
    {
        var data = new TheoryData<string>();
        foreach (var test in StdTests()) data.Add(test.Name);
        return data;
    }

    private static IEnumerable<DocTest> StdTests()
    {
        var std = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "stdlib5", "std");
        foreach (var file in Directory.EnumerateFiles(std, "*.lyr", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(std, file).Replace('\\', '/');
            var module = "std." + relative[..^4].Replace('/', '.');
            foreach (var test in DocTests.Extract("stdlib5/std/" + relative, module, File.ReadAllText(file))) yield return test;
        }
    }

    /// <summary>Every module of std shows its typical use in a <c># Examples</c> section of its
    /// module doc, as one Lyric block.</summary>
    [Fact]
    public void Every_module_of_the_standard_library_shows_an_example()
    {
        var std = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "stdlib5", "std");
        var missing = new List<string>();
        foreach (var file in Directory.EnumerateFiles(std, "*.lyr", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var doc = File.ReadLines(file).TakeWhile(l => l.StartsWith("//!")).ToList();
            if (!doc.Contains("//! # Examples") || !doc.Any(l => l.StartsWith("//! ```lyric")))
                missing.Add(Path.GetRelativePath(std, file).Replace('\\', '/'));
        }
        Assert.True(missing.Count == 0, "modules without an example: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(StdDocTests))]
    public void A_doc_test_of_the_standard_library_does_what_its_fence_says(string name)
    {
        var test = StdTests().Single(t => t.Name == name);
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {}\n"));
        var project = Project.ProgramsOf(dir)![0];
        var compiler = CCompiler.Locate() ?? throw new InvalidOperationException("no C compiler");
        var failure = TestRun.RunDocTest(project, test, BuildProfile.Of(Profile.Debug), Target.Host, compiler, "5.0.0-test");
        Assert.True(failure is null, $"{name}: {failure}");
    }
}
