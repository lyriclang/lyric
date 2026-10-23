using Lyric5.Site;

// site --out <dir> --version <name> [--root <repository>] [--ref <git ref>]
//
// Writes <dir>/<name>/: the start page and the releases. The repository defaults to the one the
// tool runs in; the ref is what links into the repository's files point at — main unless given,
// a release's tag for a release.

string? output = null, version = null, root = null, gitRef = null;
for (var i = 0; i < args.Length; i++)
{
    var value = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--out": output = value; i++; break;
        case "--version": version = value; i++; break;
        case "--root": root = value; i++; break;
        case "--ref": gitRef = value; i++; break;
        default:
            Console.Error.WriteLine($"site: unknown argument '{args[i]}'");
            return 2;
    }
}
if (output is null || version is null)
{
    Console.Error.WriteLine("usage: site --out <dir> --version <name> [--root <repository>] [--ref <git ref>]");
    return 2;
}

root ??= SiteBuilder.FindRepository(AppContext.BaseDirectory);
gitRef ??= "main";
var pages = SiteBuilder.Build(root, version, gitRef);
var target = Path.Combine(output, version);
Lyric5.Doc.SiteWriter.Write(target, pages);
Console.WriteLine($"site: {pages.Count} pages in {target}");
return 0;
