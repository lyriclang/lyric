using Lyric5.Build;
using Lyric5.Doc.Api;

namespace Lyric5;

public static partial class Program
{
    /// <summary><c>api</c>: the package's public surface, one line per element, or as JSON; with
    /// <c>--diff &lt;rev&gt;</c> what changed against the package at a git revision, breaking first,
    /// exit 1 where something breaks.</summary>
    private static int Api(string[] args)
    {
        if (Verbs.Read(Verbs.Find("api")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Words.Count > 0) return Unknown($"'api' takes no file, got '{parsed.Words[0]}'");
        if (parsed.Rest is not null) return Unknown("unknown option '--' for 'api'");
        var json = parsed.Flags.Contains("--json");
        var revision = parsed.Values.GetValueOrDefault("--diff");
        if (parsed.Flags.Contains("--std") && revision is not null)
            return Unknown("'--diff' compares a package with its repository; the standard library is none");
        var baseDirectory = Path.GetFullPath(parsed.Values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(baseDirectory))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{baseDirectory}'");
            return 2;
        }

        var surface = parsed.Flags.Contains("--std")
            ? DocRun.StdSurface(DisplayVersion(), Console.Error)
            : ApiRun.PackageSurface(baseDirectory, Console.Error);
        if (surface is null) return 1;
        var entries = ApiSurface.Entries(surface);

        if (revision is null)
        {
            Console.Out.Write(json ? ApiSurface.Json(surface.Package, surface.Version, entries) : ApiSurface.Text(entries));
            return 0;
        }

        var root = Path.GetDirectoryName(Project.FindManifest(baseDirectory)!)!;
        if (ApiRun.SurfaceAt(root, revision, Console.Error) is not { } before) return 2;
        var changes = ApiDiff.Compare(ApiSurface.Entries(before), entries);
        Console.Out.Write(json ? ApiDiff.Json(changes) : ApiDiff.Text(changes));
        return changes.Any(c => c.Breaking) ? 1 : 0;
    }
}
