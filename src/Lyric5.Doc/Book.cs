using System.Text;
using System.Text.RegularExpressions;

namespace Lyric5.Doc;

/// <summary>A chapter of the Book: its label in the contents, its Markdown file beside
/// <c>SUMMARY.md</c>, and the name of its page, the file's name without <c>.md</c>.</summary>
public sealed record Chapter(string Label, string File, string Slug);

/// <summary>The Book's contents: its title and the chapters in reading order.</summary>
public sealed record BookSummary(string Title, IReadOnlyList<Chapter> Chapters);

/// <summary>
/// The Book's form. <c>SUMMARY.md</c> gives the title as its first <c>#</c> heading and the
/// chapters as a list of links, one per line, in the order they are read:
/// <c>- [Getting started](getting-started.md)</c>. A line of the list that holds no such link is
/// an error, so a chapter cannot drop out of the Book unseen.
/// </summary>
public static partial class Book
{
    [GeneratedRegex(@"^\s*[-*]\s+\[(?<label>[^\]]+)\]\((?<file>[^)\s]+)\)\s*$")]
    private static partial Regex ChapterLine();

    /// <summary>Reads the summary <paramref name="text"/>; <paramref name="name"/> is its file's
    /// name, for the messages.</summary>
    /// <exception cref="InvalidDataException">The summary has no title, a list line that is no
    /// chapter link, or a chapter twice.</exception>
    public static BookSummary ReadSummary(string text, string name)
    {
        string? title = null;
        var chapters = new List<Chapter>();
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (title is null && line.StartsWith("# ", StringComparison.Ordinal))
            {
                title = line[2..].Trim();
                continue;
            }
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal) && !trimmed.StartsWith("* ", StringComparison.Ordinal))
                continue;
            var m = ChapterLine().Match(line);
            if (!m.Success) throw new InvalidDataException($"{name}:{i + 1}: a list line is a chapter, '- [Label](file)'");
            var file = m.Groups["file"].Value;
            if (chapters.Any(c => c.File == file)) throw new InvalidDataException($"{name}:{i + 1}: '{file}' is in the Book twice");
            chapters.Add(new Chapter(m.Groups["label"].Value, file, Path.GetFileNameWithoutExtension(file)));
        }
        if (title is null) throw new InvalidDataException($"{name} has no '# ' title");
        return new BookSummary(title, chapters);
    }

    /// <summary>The page path of a chapter under the version root: <c>book/getting-started/</c>.</summary>
    public static string PageOf(Chapter chapter) => $"book/{chapter.Slug}/";
}

/// <summary>What the Book's suite does with a snippet.</summary>
public enum SnippetKind
{
    /// <summary><c>```lyric</c>: builds, runs, exits with 0, and prints its <c>output</c> block
    /// where one follows.</summary>
    Run,

    /// <summary><c>```lyric,no_run</c>: builds.</summary>
    NoRun,

    /// <summary><c>```lyric,compile_fail=LYR-SEM0001</c>: the check fails with that code and
    /// no other error.</summary>
    CompileFail,
}

/// <summary>A Lyric snippet of a chapter.</summary>
/// <param name="Line">The line of its opening fence in the chapter, from 1.</param>
/// <param name="Code">The snippet as written.</param>
/// <param name="Output">The <c>output</c> block right below a snippet that runs, or the
/// <c>diagnostic</c> block right below one that must fail — what the check says —, ending with a
/// newline; <c>null</c> where there is none.</param>
/// <param name="Error">The code a <see cref="SnippetKind.CompileFail"/> snippet fails with.</param>
public sealed partial record Snippet(string File, int Line, SnippetKind Kind, string Code, string? Output, string? Error)
{
    /// <summary>Where the snippet stands: <c>first-program.md:42</c>.</summary>
    public override string ToString() => $"{File}:{Line}";

    /// <summary>
    /// The program the snippet is: itself where it declares <c>fn main</c>; with an empty
    /// <c>fn main(): void {}</c> after it where it declares — a line at its left edge starts a
    /// function, a type, an extension or an attribute; otherwise its <c>import</c> lines, then the
    /// rest as the body of <c>fn main(): void</c>.
    /// </summary>
    public string Program()
    {
        var code = Code.EndsWith('\n') ? Code : Code + "\n";
        if (MainDeclaration().IsMatch(Code)) return code;
        if (Declaration().IsMatch(Code)) return code + "\nfn main(): void {}\n";
        var imports = new StringBuilder();
        var body = new StringBuilder();
        foreach (var line in Code.ReplaceLineEndings("\n").TrimEnd('\n').Split('\n'))
        {
            if (line.StartsWith("import ", StringComparison.Ordinal)) imports.Append(line).Append('\n');
            else if (line.Length > 0 || body.Length > 0) body.Append(line.Length == 0 ? "" : "    " + line).Append('\n');
        }
        if (imports.Length > 0) imports.Append('\n');
        return $"{imports}fn main(): void {{\n{body}}}\n";
    }

    [GeneratedRegex(@"^fn main\(", RegexOptions.Multiline)]
    private static partial Regex MainDeclaration();

    [GeneratedRegex(@"^(?:pub )?(?:fn|struct|enum|class|interface|extend|type|opaque type) |^@", RegexOptions.Multiline)]
    private static partial Regex Declaration();

    /// <summary>
    /// The Lyric snippets of a chapter, in order. A fence whose info string starts with
    /// <c>lyric</c> is a snippet: <c>lyric</c> runs, <c>lyric,no_run</c> builds,
    /// <c>lyric,compile_fail=CODE</c> fails with <c>CODE</c>. An <c>output</c> fence right below a
    /// snippet — nothing but blank lines between — is what the snippet prints; a <c>diagnostic</c>
    /// fence right below one that must fail is what the check says about it. Other fences are not
    /// checked.
    /// </summary>
    /// <exception cref="InvalidDataException">An unknown snippet kind, an unclosed fence, an
    /// <c>output</c> block below no runnable snippet, or a <c>diagnostic</c> block below no snippet
    /// that must fail.</exception>
    public static IReadOnlyList<Snippet> Extract(string file, string markdown)
    {
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var snippets = new List<Snippet>();
        Snippet? last = null;
        var lastEnd = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("```", StringComparison.Ordinal)) continue;
            var info = lines[i][3..].Trim();
            var close = Array.FindIndex(lines, i + 1, l => l.StartsWith("```", StringComparison.Ordinal));
            if (close < 0) throw new InvalidDataException($"{file}:{i + 1}: the fence is not closed");
            var text = string.Join('\n', lines[(i + 1)..close]);
            var adjacent = last is not null && lines[(lastEnd + 1)..i].All(l => l.Trim().Length == 0);

            if (info == "output")
            {
                if (!adjacent || last!.Kind != SnippetKind.Run || last.Output is not null)
                    throw new InvalidDataException($"{file}:{i + 1}: an output block stands right below a snippet that runs");
                snippets[^1] = last with { Output = text + "\n" };
                last = null;
            }
            else if (info == "diagnostic")
            {
                if (!adjacent || last!.Kind != SnippetKind.CompileFail || last.Output is not null)
                    throw new InvalidDataException($"{file}:{i + 1}: a diagnostic block stands right below a snippet that must fail");
                snippets[^1] = last with { Output = text + "\n" };
                last = null;
            }
            else if (Markdown.FenceLanguage(info) == "lyric")
            {
                var (kind, error) = KindOf(info) ?? throw new InvalidDataException($"{file}:{i + 1}: unknown snippet kind '{info}'");
                last = new Snippet(file, i + 1, kind, text, null, error);
                snippets.Add(last);
                lastEnd = close;
            }
            else last = null;
            i = close;
        }
        return snippets;
    }

    private static (SnippetKind, string?)? KindOf(string info)
    {
        if (info == "lyric") return (SnippetKind.Run, null);
        if (info == "lyric,no_run") return (SnippetKind.NoRun, null);
        const string fail = "lyric,compile_fail=";
        if (info.StartsWith(fail, StringComparison.Ordinal) && info.Length > fail.Length)
            return (SnippetKind.CompileFail, info[fail.Length..]);
        return null;
    }
}
