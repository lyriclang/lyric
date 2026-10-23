using Lyric5.Doc;

namespace Lyric5.Site;

/// <summary>The pages of one version of the site, from the repository's files.</summary>
public static class SiteBuilder
{
    public const string HomePath = "index.html";
    public const string ReleasesPath = "releases/index.html";

    /// <summary>The program beside the start page's headline: the request handler of the JSON
    /// server example, a function that throws and the code that calls what throws.</summary>
    public const string HomeProgramFile = "examples/jsonserver/src/main.lyr";
    public const string HomeProgramFunction = "handle";

    /// <summary>The repository above <paramref name="start"/>: the first directory that holds
    /// <c>CHANGELOG.md</c> and <c>stdlib5/</c>.</summary>
    public static string FindRepository(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "CHANGELOG.md")) && Directory.Exists(Path.Combine(dir.FullName, "stdlib5")))
                return dir.FullName;
        throw new DirectoryNotFoundException($"no repository above {start}");
    }

    /// <summary>The version's pages: the start page and the releases.</summary>
    /// <param name="gitRef">The branch or tag a link into the repository's files points at.</param>
    public static List<Page> Build(string root, string version, string gitRef)
    {
        var site = new SiteContext(version, [HomePath, ReleasesPath], SiteContext.DefaultAreas);
        var program = FunctionOf(File.ReadAllText(Path.Combine(root, HomeProgramFile)), HomeProgramFunction);
        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        return
        [
            new Page(HomePath, Pages.Home(site, HomePath, Home(program))),
            new Page(ReleasesPath, Pages.Article(site, ReleasesPath, Releases(changelog, gitRef))),
        ];
    }

    /// <summary>What the start page says, around <paramref name="program"/>.</summary>
    public static HomeContent Home(string program) => new(
        Headline: "Lyric: native programs, plainly written.",
        Lead: "A statically typed language for applications, command-line tools and servers. "
              + "It compiles through C to one binary per program, runs its tasks on coroutines with "
              + "their own stacks, and says in a function's signature what it throws.",
        Primary: new Link("Get started", "book/getting-started/"),
        Secondary: new Link("Read the Book", "book/"),
        Program: program,
        Claims:
        [
            new Claim("Native binaries",
                "A program builds into one executable for Linux, macOS or Windows — no virtual machine to install beside it."),
            new Claim("Concurrency without colouring",
                "A task runs on a stack of its own, so any function may wait for input or output; none is marked async."),
            new Claim("Errors in the signature",
                "A function names what it throws; a caller handles it or passes it on with try — no error leaves unseen."),
        ],
        Entries:
        [
            new Entry(new Link("The Book", "book/"), "The language from the first program to a server, chapter by chapter."),
            new Entry(new Link("Standard library", "std/"), "Every module, type and function of std."),
            new Entry(new Link("Specification", "spec/"), "The rules of the language, precisely."),
        ]);

    /// <summary>The releases page: every version's section of the changelog, from the first
    /// <c>## </c> heading on, with the versions as the side navigation.</summary>
    public static ArticleContent Releases(string changelog, string gitRef)
    {
        var start = changelog.IndexOf("\n## ", StringComparison.Ordinal);
        var sections = start < 0 ? "" : changelog[(start + 1)..];
        var rendered = Markdown.Render(sections, target => RepositoryLink(target, gitRef));
        var versions = rendered.Headings
            .Where(h => h.Level == 2)
            .Select(h => new Link(h.Text, "#" + h.Anchor))
            .ToList();
        return new ArticleContent("Releases", rendered.Html, versions, "Releases");
    }

    /// <summary>
    /// Where a link of a repository file points on the site: a web address or an anchor as
    /// written, a path into the repository to that file on GitHub at <paramref name="gitRef"/>.
    /// The repository's documents are not pages of the site, and a version's page keeps
    /// pointing at the files as they were for it.
    /// </summary>
    public static string RepositoryLink(string target, string gitRef)
    {
        if (target.StartsWith('#') || target.Contains("://", StringComparison.Ordinal)
            || target.StartsWith("mailto:", StringComparison.Ordinal))
            return target;
        var path = target.StartsWith("./", StringComparison.Ordinal) ? target[2..] : target.TrimStart('/');
        return $"{Layout.Repository}/blob/{gitRef}/{path}";
    }

    /// <summary>The top-level function <paramref name="name"/> of a Lyric file, from its
    /// <c>fn</c> line to the closing brace at the start of a line.</summary>
    public static string FunctionOf(string source, string name)
    {
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var first = Array.FindIndex(lines, l => l.StartsWith($"fn {name}(", StringComparison.Ordinal));
        if (first < 0) throw new InvalidDataException($"no top-level fn {name}");
        var last = Array.FindIndex(lines, first, l => l == "}");
        if (last < 0) throw new InvalidDataException($"fn {name} has no closing brace at the start of a line");
        return string.Join('\n', lines[first..(last + 1)]);
    }
}
