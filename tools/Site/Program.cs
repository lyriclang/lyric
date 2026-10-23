using Lyric5.Site;

// site --out <dir> --version <name> [--root <repository>] [--ref <git ref>] [--spec <checkout>] [--pagefind <binary>]
//
// Writes <dir>/<name>/: the start page, the Book, the diagnostic catalog and the releases, and the
// specification from a checkout of lyric-spec at the commit spec.pin names. The repository defaults to
// the one the tool runs in; the ref is what links into the repository's files point at — main unless
// given, a release's tag for a release. Pagefind, when it is found, indexes the version for the search
// box; without it the site is written all the same and the box stays off.

const string Usage = "usage: site --out <dir> --version <name> [--root <repository>] [--ref <git ref>] [--spec <checkout>] [--pagefind <binary>]";
string? output = null, version = null, root = null, gitRef = null, specDir = null, pagefind = null;
for (var i = 0; i < args.Length; i++)
{
    var value = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--out": output = value; i++; break;
        case "--version": version = value; i++; break;
        case "--root": root = value; i++; break;
        case "--ref": gitRef = value; i++; break;
        case "--spec": specDir = value; i++; break;
        case "--pagefind": pagefind = value; i++; break;
        default:
            Console.Error.WriteLine($"site: unknown argument '{args[i]}'");
            return 2;
    }
}
if (output is null || version is null)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

root ??= SiteBuilder.FindRepository(AppContext.BaseDirectory);
gitRef ??= "main";
SpecSource? spec = null;
if (specDir is not null)
{
    try { spec = SpecSource.Open(specDir, SiteBuilder.SpecPin(root)); }
    catch (InvalidOperationException refused)
    {
        Console.Error.WriteLine($"site: {refused.Message}");
        return 1;
    }
}
var pages = SiteBuilder.Build(root, version, gitRef, spec);
var target = Path.Combine(output, version);
Lyric5.Doc.SiteWriter.Write(target, pages);
Console.WriteLine($"site: {pages.Count} pages in {target}");

if (Search.Locate(root, pagefind) is { } binary)
{
    try { Search.Index(binary, target); }
    catch (InvalidOperationException failed)
    {
        Console.Error.WriteLine($"site: {failed.Message}");
        return 1;
    }
    Console.WriteLine($"site: search index in {Path.Combine(target, Search.IndexDirectory)}");
}
else if (pagefind is not null)
{
    Console.Error.WriteLine($"site: no Pagefind at {pagefind}");
    return 1;
}
else
    Console.Error.WriteLine($"site: no Pagefind {PagefindPin.Of(root).Version} found (--pagefind, LYRIC_PAGEFIND or the cache) — the site has no search");
return 0;
