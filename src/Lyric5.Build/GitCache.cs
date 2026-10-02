using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// Where the packages read from git are kept (design/v5/spec/11 W2 P9; 07 P10): in the user's
/// cache, not the project — <c>git/db/</c> a bare copy of each repository, <c>git/checkouts/</c>
/// each revision's package content (W2 P8), written once and then only read, by every project
/// that asks for it. Offline, the cache answers alone: nothing is fetched. A repository is
/// fetched at most once by one resolution.
/// </summary>
/// <param name="root">The cache's directory.</param>
/// <param name="offline">Whether fetching is forbidden (<c>--offline</c>).</param>
/// <param name="refresh">The packages whose repositories are fetched before anything is read of
/// them — <c>lyric update</c>'s: a moved tag moves too.</param>
public sealed partial class GitCache(string root, bool offline, Func<string, bool>? refresh = null)
{
    private readonly HashSet<string> _fetched = new(StringComparer.Ordinal);

    private static readonly TimeSpan FetchTime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LocalTime = TimeSpan.FromMinutes(1);

    /// <summary>The user's: <c>git/</c> in the user cache — <c>~/.cache/lyric/git</c> on Linux.</summary>
    public static GitCache ForUser(bool offline, Func<string, bool>? refresh = null) =>
        new(Path.Combine(UserCache.Root, "git"), offline, refresh);

    /// <summary>The name the cache keeps a repository under: its own name, to be read, and a hash
    /// of its URL, to be unique.</summary>
    public static string Id(string url)
    {
        var last = url.TrimEnd('/').Split('/', ':')[^1];
        if (last.EndsWith(".git", StringComparison.Ordinal)) last = last[..^4];
        var name = NotInAName().Replace(last, "_");
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..16];
        return name.Length > 0 ? $"{name}-{hash}" : hash;
    }

    /// <summary>The directory holding the package <paramref name="dependency"/> reads from git, at
    /// the revision it names — at the commit <paramref name="locked"/> holds, when the lock holds
    /// one (07 P5) —, the commit, and the hash of the package's content. What the cache does not
    /// hold is fetched first.</summary>
    /// <param name="declaring">The manifest naming the dependency, for what is refused.</param>
    /// <exception cref="ManifestException">The repository cannot be reached, or offline the cache
    /// does not hold the revision (<c>LYR-PKG0007</c>); the repository has no such revision, or no
    /// package at its root (<c>LYR-PKG0006</c>); the locked commit is not there, or its content is
    /// not what the lock holds (<c>LYR-PKG0008</c>).</exception>
    public (string Directory, string Commit, string Hash) Checkout(Dependency dependency, string declaring, Locked? locked = null)
    {
        var id = Id(dependency.Git!.Url);
        var db = Path.Combine(root, "db", id);
        var commit = locked is null ? Resolve(dependency, db, declaring) : Locate(dependency, db, locked.Commit, declaring);
        var checkout = Path.Combine(root, "checkouts", id, commit);
        if (!Directory.Exists(checkout)) Export(dependency, db, commit, checkout, declaring);
        var hash = PackageContent.Hash(checkout);
        if (locked is null || hash == locked.Hash) return (checkout, commit, hash);
        // The checkout is the cache's, written from the commit: written anew, it is the commit's
        // content — if that is still not what the lock holds, the lock is wrong, or the repository.
        DeleteTree(checkout);
        Export(dependency, db, commit, checkout, declaring);
        hash = PackageContent.Hash(checkout);
        if (hash == locked.Hash) return (checkout, commit, hash);
        throw new ManifestException("LYR-PKG0008", declaring, dependency.Line, 1,
            $"the content of '{dependency.Name}' at commit {commit[..12]} of {dependency.Git!.Url} is not what {LockFile.FileName} "
            + $"holds — 'lyric update {dependency.Name}' resolves it anew");
    }

    /// <summary>The commit the lock holds, from the cache; fetched when the cache lacks it.</summary>
    private string Locate(Dependency dependency, string db, string commit, string declaring)
    {
        if (Has(dependency, db, commit, declaring)) return commit;
        if (offline) throw NotCached(dependency, declaring);
        if (Directory.Exists(db)) Fetch(dependency, db, declaring);
        else Clone(dependency, db, declaring);
        if (Has(dependency, db, commit, declaring)) return commit;
        throw new ManifestException("LYR-PKG0008", declaring, dependency.Line, 1,
            $"{LockFile.FileName} holds commit {commit[..12]} of {dependency.Git!.Url} for '{dependency.Name}', which the "
            + $"repository does not have — 'lyric update {dependency.Name}' resolves it anew");
    }

    private static bool Has(Dependency dependency, string db, string commit, string declaring) =>
        Directory.Exists(db)
        && Invoke(dependency, declaring, ["--git-dir", db, "rev-parse", "--verify", "--quiet", commit + "^{commit}"], LocalTime).Exit == 0;

    /// <summary>The commit the revision names. A tag or a commit the cache holds is not fetched
    /// again; a branch — or no revision, the default branch — moves, and is fetched whenever
    /// fetching is allowed.</summary>
    private string Resolve(Dependency dependency, string db, string declaring)
    {
        var git = dependency.Git!;
        var fetched = false;
        if (!Directory.Exists(db))
        {
            if (offline) throw NotCached(dependency, declaring);
            Clone(dependency, db, declaring);
            fetched = true;
        }
        if (!fetched && !offline && (git.Kind is GitRefKind.Branch or GitRefKind.Default || refresh?.Invoke(dependency.Name) == true))
        {
            Fetch(dependency, db, declaring);
            fetched = true;
        }
        var commit = RevParse(dependency, db, declaring);
        if (commit is null && !fetched && !offline)
        {
            Fetch(dependency, db, declaring);
            fetched = true;
            commit = RevParse(dependency, db, declaring);
        }
        if (commit is not null) return commit;
        if (!fetched) throw NotCached(dependency, declaring);
        throw new ManifestException("LYR-PKG0006", declaring, dependency.Line, 1,
            $"{git.Url} has no {git.Revision} for '{dependency.Name}'");
    }

    private static ManifestException NotCached(Dependency dependency, string declaring) =>
        new("LYR-PKG0007", declaring, dependency.Line, 1,
            $"'{dependency.Name}' is read from {dependency.Git}, which the cache does not hold, and --offline fetches nothing",
            exit: 2);

    private void Clone(Dependency dependency, string db, string declaring)
    {
        _fetched.Add(db);
        Directory.CreateDirectory(Path.GetDirectoryName(db)!);
        var partial = $"{db}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        Run(dependency, declaring, ["clone", "--bare", "--quiet", "--", dependency.Git!.Url, partial], FetchTime);
        try { Directory.Move(partial, db); }
        catch (IOException) when (Directory.Exists(db)) { DeleteTree(partial); }
    }

    /// <summary>Every branch and tag as the repository has them now; a moved tag moves here too.
    /// Once per repository and resolution.</summary>
    private void Fetch(Dependency dependency, string db, string declaring)
    {
        if (!_fetched.Add(db)) return;
        Run(dependency, declaring, ["--git-dir", db, "fetch", "--quiet", "--force", "--", dependency.Git!.Url,
            "+refs/heads/*:refs/heads/*", "+refs/tags/*:refs/tags/*"], FetchTime);
    }

    private static string? RevParse(Dependency dependency, string db, string declaring)
    {
        var git = dependency.Git!;
        var revision = git.Kind switch
        {
            GitRefKind.Tag => $"refs/tags/{git.Ref}",
            GitRefKind.Branch => $"refs/heads/{git.Ref}",
            GitRefKind.Rev => git.Ref!,
            _ => "HEAD",
        };
        var result = Invoke(dependency, declaring, ["--git-dir", db, "rev-parse", "--verify", "--quiet", revision + "^{commit}"], LocalTime);
        return result.Exit == 0 ? result.Text : null;
    }

    /// <summary>The package content of the commit (11 W2 P8) into <paramref name="checkout"/>:
    /// written beside it and moved in whole, so a checkout that exists is complete.</summary>
    private static void Export(Dependency dependency, string db, string commit, string checkout, string declaring)
    {
        // core.autocrlf off: the files are the commit's bytes on every machine.
        var archive = Run(dependency, declaring,
            ["-c", "core.autocrlf=false", "--git-dir", db, "archive", "--format=tar", commit], FetchTime).Output;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using (var reader = new TarReader(new MemoryStream(archive)))
        {
            while (reader.GetNextEntry() is { } entry)
            {
                // Regular files alone: a link can point out of the package.
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
                using var data = new MemoryStream();
                entry.DataStream?.CopyTo(data);
                files[entry.Name] = data.ToArray();
            }
        }
        if (!files.TryGetValue("lyric.toml", out var text))
            throw new ManifestException("LYR-PKG0006", declaring, dependency.Line, 1,
                $"there is no package at the root of {dependency.Git} — no lyric.toml there");
        // The package's own manifest says what the package is made of.
        var manifest = Manifest.Parse(Encoding.UTF8.GetString(text), Path.Combine(checkout, "lyric.toml"));

        var partial = $"{checkout}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        var inside = Path.GetFullPath(partial) + Path.DirectorySeparatorChar;
        foreach (var (name, bytes) in files)
        {
            if (!PackageContent.Holds(manifest, name)) continue;
            var target = Path.GetFullPath(Path.Combine(partial, name));
            if (!target.StartsWith(inside, StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        Directory.CreateDirectory(partial);
        try { Directory.Move(partial, checkout); }
        catch (IOException) when (Directory.Exists(checkout)) { DeleteTree(partial); }
    }

    /// <summary>git that has to succeed: what it cannot reach is the environment's failure (exit 2).</summary>
    private static Git.Result Run(Dependency dependency, string declaring, string[] arguments, TimeSpan timeout)
    {
        var result = Invoke(dependency, declaring, arguments, timeout);
        if (result.Exit == 0) return result;
        var why = result.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? $"git exited with {result.Exit}";
        throw new ManifestException("LYR-PKG0007", declaring, dependency.Line, 1,
            $"'{dependency.Name}' cannot be read from {dependency.Git!.Url}: {why}", exit: 2);
    }

    private static Git.Result Invoke(Dependency dependency, string declaring, string[] arguments, TimeSpan timeout)
    {
        try { return Git.Run(arguments, timeout); }
        catch (GitMissingException)
        {
            throw new ManifestException("LYR-PKG0007", declaring, dependency.Line, 1,
                $"'{dependency.Name}' is read from git, and git was not found on the PATH", exit: 2);
        }
    }

    /// <summary>git keeps its objects read-only, which a plain recursive delete on Windows refuses.</summary>
    private static void DeleteTree(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, recursive: true);
    }

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex NotInAName();
}
