using System.Text.RegularExpressions;
using Lyric5.Doc;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The Book's snippets against the compiler: every Lyric snippet of every chapter in
/// <c>docs/book/SUMMARY.md</c>, built as <c>src/main.lyr</c> of a package — a <c>lyric</c> snippet
/// run and its <c>output</c> block compared, a <c>no_run</c> one built, a <c>compile_fail</c> one
/// checked for its one error code. A case is named by the snippet's place, <c>file.md:line</c>.
/// Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public partial class BookTests
{
    private static readonly string BookDir = Path.Combine(RuntimeLayout.FindRoot(AppContext.BaseDirectory), "docs", "book");

    private static IEnumerable<Snippet> BookSnippets()
    {
        var summary = Book.ReadSummary(File.ReadAllText(Path.Combine(BookDir, "SUMMARY.md")), "SUMMARY.md");
        foreach (var chapter in summary.Chapters)
            foreach (var snippet in Snippet.Extract(chapter.File, File.ReadAllText(Path.Combine(BookDir, chapter.File))))
                yield return snippet;
    }

    public static TheoryData<string> Snippets()
    {
        var data = new TheoryData<string>();
        foreach (var snippet in BookSnippets()) data.Add(snippet.ToString());
        return data;
    }

    [GeneratedRegex(@"^error\[(?<code>LYR-[A-Z]+\d+)\]", RegexOptions.Multiline)]
    private static partial Regex ErrorCode();

    [Theory]
    [MemberData(nameof(Snippets))]
    public void A_snippet_of_the_Book_does_what_its_fence_says(string where)
    {
        var snippet = BookSnippets().Single(s => s.ToString() == where);
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", snippet.Program()));

        if (snippet.Kind == SnippetKind.CompileFail)
        {
            var (exit, _, said) = RunRaw("check", "-C", dir, "--color", "never");
            var codes = ErrorCode().Matches(said).Select(m => m.Groups["code"].Value).Distinct().ToList();
            Assert.True(exit != 0, $"{where}: the check passed; the snippet should fail with {snippet.Error}");
            Assert.True(codes.SequenceEqual([snippet.Error!]), $"{where}: expected only {snippet.Error}, the check said:\n{said}");
            if (snippet.Output is { } diagnostic)
            {
                var text = said.Replace("\r\n", "\n").Replace("src\\main.lyr", "src/main.lyr").TrimEnd('\n') + "\n";
                Assert.True(diagnostic == text, $"{where}: the diagnostic block says\n{diagnostic}\nthe check said\n{text}");
            }
            return;
        }

        var (built, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.True(built == 0, $"{where}: the snippet does not build:\n{error}");
        if (snippet.Kind == SnippetKind.NoRun) return;

        var host = Target.Host;
        var exe = Path.Combine(dir, "out", "debug", host.Triple, "app" + host.ExecutableSuffix);
        var ran = ProcessRunner.Run(exe, [], TimeSpan.FromMinutes(1));
        Assert.True(ran.ExitCode == 0, $"{where}: the program exited with {ran.ExitCode}:\n{ran.Stderr}");
        if (snippet.Output is { } expected)
            Assert.True(expected == ran.Stdout.Replace("\r\n", "\n"),
                $"{where}: the output block says\n{expected}\nthe program printed\n{ran.Stdout}");
    }

    [Fact]
    public void The_Book_shows_every_kind_of_snippet()
    {
        var kinds = BookSnippets().GroupBy(s => s.Kind).ToDictionary(g => g.Key, g => g.Count());
        Assert.All(Enum.GetValues<SnippetKind>(), kind => Assert.True(kinds.GetValueOrDefault(kind) > 0, $"no {kind} snippet"));
        Assert.Contains(BookSnippets(), s => s.Output is not null);
    }

    [Fact]
    public void A_fence_names_its_kind_and_an_output_block_belongs_to_the_snippet_above()
    {
        const string text = "# T\n\n```lyric\nprintln(1);\n```\n\n```output\n1\n```\n\n```lyric,no_run\nlet a = 1;\n```\n\n"
            + "```lyric,compile_fail=LYR-SEM0001\nlet a: int = \"x\";\n```\n\n```diagnostic\nerror[LYR-SEM0001]\n```\n\n```toml\n[package]\n```\n";
        var snippets = Snippet.Extract("t.md", text);
        Assert.Equal(["t.md:3", "t.md:11", "t.md:15"], snippets.Select(s => s.ToString()));
        Assert.Equal([SnippetKind.Run, SnippetKind.NoRun, SnippetKind.CompileFail], snippets.Select(s => s.Kind));
        Assert.Equal("1\n", snippets[0].Output);
        Assert.Null(snippets[1].Output);
        Assert.Equal("LYR-SEM0001", snippets[2].Error);
        Assert.Equal("error[LYR-SEM0001]\n", snippets[2].Output);
    }

    [Theory]
    [InlineData("```lyric,should_panic\nx;\n```\n", "unknown snippet kind")]
    [InlineData("```output\n1\n```\n", "right below a snippet that runs")]
    [InlineData("```lyric,no_run\nx;\n```\n\n```output\n1\n```\n", "right below a snippet that runs")]
    [InlineData("```lyric\nx;\n```\n\ntext between\n\n```output\n1\n```\n", "right below a snippet that runs")]
    [InlineData("```lyric\nx;\n", "not closed")]
    [InlineData("```lyric\nx;\n```\n\n```diagnostic\nerror\n```\n", "right below a snippet that must fail")]
    public void A_snippet_the_suite_cannot_read_is_refused(string text, string said)
    {
        var e = Assert.Throws<InvalidDataException>(() => Snippet.Extract("t.md", text));
        Assert.Contains(said, e.Message);
    }

    [Fact]
    public void Statements_become_the_body_of_main_with_their_imports_above()
    {
        var snippet = new Snippet("t.md", 1, SnippetKind.Run, "import std.io { println };\n\nlet a = 1;\nprintln(a);", null, null);
        Assert.Equal("import std.io { println };\n\nfn main(): void {\n    let a = 1;\n    println(a);\n}\n", snippet.Program());
    }

    [Fact]
    public void Declarations_get_an_empty_main_and_a_main_stays_as_written()
    {
        var declarations = new Snippet("t.md", 1, SnippetKind.Run, "struct P {\n    x: int,\n}", null, null);
        Assert.Equal("struct P {\n    x: int,\n}\n\nfn main(): void {}\n", declarations.Program());
        var program = new Snippet("t.md", 1, SnippetKind.Run, "fn main(): void {\n}\n", null, null);
        Assert.Equal("fn main(): void {\n}\n", program.Program());
    }

    [Fact]
    public void The_summary_lists_the_chapters_in_order_and_refuses_a_line_that_is_none()
    {
        var summary = Book.ReadSummary("# The Book\n\nIntro.\n\n- [One](one.md)\n- [Two](two.md)\n", "SUMMARY.md");
        Assert.Equal("The Book", summary.Title);
        Assert.Equal(["one", "two"], summary.Chapters.Select(c => c.Slug));
        Assert.Throws<InvalidDataException>(() => Book.ReadSummary("# B\n\n- [One](one.md)\n- one.md\n", "SUMMARY.md"));
        Assert.Throws<InvalidDataException>(() => Book.ReadSummary("# B\n\n- [One](one.md)\n- [Again](one.md)\n", "SUMMARY.md"));
    }
}
