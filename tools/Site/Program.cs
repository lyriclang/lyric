using Lyric5.Site;

// site --out <dir> --version <name> [--root <repository>] [--ref <git ref>] [--spec <checkout>] [--pagefind <binary>] [--index]
// site legacy --pages <work tree of the published site> [--dry-run]
//
// Writes <dir>/<name>/: the start page, the Book, the diagnostic catalog and the releases, and the
// specification from a checkout of lyric-spec at the commit spec.pin names. The repository defaults to
// the one the tool runs in; the ref is what links into the repository's files point at — main unless
// given, a release's tag for a release. Pagefind, when it is found, indexes the version for the search
// box; without it the site is written all the same and the box stays off. --index adds the version to
// <dir>/versions.json, the list the version switch reads, and writes <dir>/index.html, which leads to
// the newest stable version, or to dev while there is none.
//
// legacy moves the 1.x–4.x documentation of the published site under legacy/ (Legacy.Plan says what
// it does); --dry-run prints the steps and changes nothing.

if (args.Length > 0 && args[0] == "legacy") return LegacyCommand(args[1..]);

const string Usage = "usage: site --out <dir> --version <name> [--root <repository>] [--ref <git ref>] [--spec <checkout>] [--pagefind <binary>] [--index]";
string? output = null, version = null, root = null, gitRef = null, specDir = null, pagefind = null;
var index = false;
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
        case "--index": index = true; break;
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
if (index && !Versions.IsVersionName(version))
{
    Console.Error.WriteLine($"site: '{version}' is no version of the site: dev or major.minor");
    return 2;
}
if (index)
{
    // Before anything is written: the old site's list at the root must go to legacy/ first.
    try { Versions.Read(output); }
    catch (InvalidOperationException refused)
    {
        Console.Error.WriteLine($"site: {refused.Message}");
        return 1;
    }
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

if (index)
{
    var list = Versions.Update(output, version);
    Console.WriteLine($"site: {list.Count} versions in {Path.Combine(output, Versions.ListFile)}, the root leads to {Versions.Landing(list).Path}");
}
return 0;

static int LegacyCommand(string[] args)
{
    const string usage = "usage: site legacy --pages <work tree of the published site> [--dry-run]";
    string? pages = null;
    var dryRun = false;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--pages" when i + 1 < args.Length: pages = args[++i]; break;
            case "--dry-run": dryRun = true; break;
            default:
                Console.Error.WriteLine(usage);
                return 2;
        }
    }
    if (pages is null)
    {
        Console.Error.WriteLine(usage);
        return 2;
    }

    IReadOnlyList<LegacyStep> steps;
    try { steps = dryRun ? Legacy.Plan(pages) : Legacy.Apply(pages); }
    catch (InvalidOperationException refused)
    {
        Console.Error.WriteLine($"site legacy: {refused.Message}");
        return 1;
    }
    foreach (var step in steps) Console.WriteLine((dryRun ? "would " : "") + step);
    var moved = steps.Where(s => s.Kind is "move" or "move-file").ToList();
    Console.WriteLine($"site legacy: {steps.Count} steps; {moved.Count} moved ({moved.Sum(s => s.Files)} files, {moved.Sum(s => s.Bytes)} bytes)"
        + (steps.Count == 0 ? " — nothing to do" : dryRun ? " — nothing changed (dry run)" : ""));
    return 0;
}
