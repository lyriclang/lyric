using Lyric5.Site;

// site --out <dir> --version <name> [--root <repository>] [--ref <git ref>] [--spec <checkout>]
//
// Writes <dir>/<name>/: the start page, the Book, the diagnostic catalog and the releases, and the
// specification from a checkout of lyric-spec at the commit spec.pin names. The repository defaults to
// the one the tool runs in; the ref is what links into the repository's files point at — main unless
// given, a release's tag for a release.

const string Usage = "usage: site --out <dir> --version <name> [--root <repository>] [--ref <git ref>] [--spec <checkout>]";
string? output = null, version = null, root = null, gitRef = null, specDir = null;
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
return 0;
