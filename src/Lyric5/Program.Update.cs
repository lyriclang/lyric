using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

public static partial class Program
{
    // --- update --------------------------------------------------------------------------------

    /// <summary>
    /// <c>lyric update [&lt;package&gt;…]</c>: the packages from git
    /// read anew — every one, or those named; the others stay at their locked commits — and
    /// <c>lyric.lock</c> written. A branch moves to its head, a moved tag to its commit; a tag of
    /// the root manifest that is a version is raised to the newest of its line first, in the
    /// manifest — a dependency's own manifest is its own.
    /// </summary>
    private static int Update(string[] args)
    {
        if (Verbs.Read(Verbs.Find("update")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Rest is not null) return Unknown("unknown option '--' for 'update'");
        var names = parsed.Words;
        var offline = parsed.Flags.Contains("--offline");
        var start = Path.GetFullPath(parsed.Values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(start))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{start}'");
            return 2;
        }
        if (Project.FindManifest(start) is not { } file)
        {
            Say.Error(Console.Error, "LYR-CLI0004", "no lyric.toml here or above");
            return 2;
        }
        try
        {
            var manifest = Manifest.Read(file);
            var before = LockFile.Read(LockFile.For(manifest));
            bool Named(string name) => names.Count == 0 || names.Contains(name);
            // the manifest as it was, written back where the raised one is refused — as 'add' does
            var original = File.ReadAllText(file);
            if (Raise(file, manifest, Named, offline) is not { } raised) return 2;
            manifest = raised;
            PackageGraph graph;
            try { graph = Project.Resolve(manifest, offline, refresh: Named, keep: entry => !Named(entry.Name)); }
            catch (ManifestException)
            {
                File.WriteAllText(file, original);
                throw;
            }
            ToolchainCheck.Check(graph, DisplayVersion());
            var unknown = names.Where(n => graph.Locked.All(entry => entry.Name != n)).ToList();
            if (unknown.Count > 0)
                return Unknown($"no package '{unknown[0]}' is read from git here"
                               + (graph.Locked.Count > 0 ? $": {string.Join(", ", graph.Locked.Select(e => e.Name).Distinct())}" : ""));
            Report(before, graph.Locked);
            return 0;
        }
        catch (ManifestException refused)
        {
            Say.Manifest(Console.Error, refused);
            return refused.Exit;
        }
    }

    /// <summary>The root manifest's tags that are versions, each raised to the newest tag of its
    /// line the repository has, the line rewritten in place; the manifest as read again.
    /// <c>null</c> after refusing a dependency written as a table, which is the person's to edit.</summary>
    private static Manifest? Raise(string file, Manifest manifest, Func<string, bool> named, bool offline)
    {
        var git = GitCache.ForUser(offline);
        var text = File.ReadAllText(file);
        var changed = false;
        foreach (var dependency in manifest.Dependencies)
        {
            if (!named(dependency.Name) || dependency.Git is not { Kind: GitRefKind.Tag, Ref: { } tag }
                || SemVer.OfTag(tag) is not { } current) continue;
            if (SemVer.Newest(current, git.Tags(dependency, manifest.File)) is not { } newest) continue;
            string? edited;
            try { edited = ManifestEdit.SetTag(text, dependency.Name, newest); }
            catch (InvalidOperationException other)
            {
                Say.Error(Console.Error, "LYR-CLI0008", $"{other.Message}");
                return null;
            }
            if (edited is null) continue;
            text = edited;
            changed = true;
            Console.Out.WriteLine($"  raised   {dependency.Name} {tag} -> {newest} (lyric.toml)");
        }
        if (!changed) return manifest;
        File.WriteAllText(file, text);
        return Manifest.Read(file);
    }

    /// <summary>What the update changed in the lock, a line per revision of a repository.</summary>
    private static void Report(IReadOnlyList<Locked> before, IReadOnlyList<Locked> after)
    {
        var old = before.ToDictionary(e => (e.Name, e.Git));
        var changed = false;
        foreach (var entry in after)
        {
            if (!old.Remove((entry.Name, entry.Git), out var was))
                Console.Out.WriteLine($"  added    {entry.Name} {entry.Version} ({entry.Git}) {entry.Commit[..12]}");
            else if (was.Commit != entry.Commit)
                Console.Out.WriteLine($"  updated  {entry.Name} {entry.Version} ({entry.Git}) {was.Commit[..12]} -> {entry.Commit[..12]}");
            else continue;
            changed = true;
        }
        foreach (var gone in LockFile.Sorted(old.Values))
        {
            Console.Out.WriteLine($"  removed  {gone.Name} {gone.Version} ({gone.Git})");
            changed = true;
        }
        if (!changed) Console.Out.WriteLine(after.Count == 0 ? "no package is read from git" : "lyric.lock is up to date");
    }
}
