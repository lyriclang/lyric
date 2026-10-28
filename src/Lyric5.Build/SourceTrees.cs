using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Lyric5.Build;

/// <summary>
/// The source trees of the program's packages (<c>[native.vendor]</c>, spec 15 §5.3), read only
/// where a build needs one — where a table that holds for its target names it —, each once. A tree
/// from git comes through the cache the packages from git come through, at the commit the lock
/// holds for it, and what a build resolved is written into the lock (<c>kind = "native"</c>). A
/// tree from a URL is a file kept in the user's cache under its hash and unpacked once beside it;
/// its hash in the manifest is its lock.
/// </summary>
/// <param name="lockFile">The program's <c>lyric.lock</c>.</param>
/// <param name="git">The cache the trees from git are read through.</param>
/// <param name="cache">The user's cache: <c>vendor/</c> in it holds the URLs' files and trees.</param>
/// <param name="offline">Whether loading is forbidden (<c>--offline</c>).</param>
/// <param name="frozen">The lock is taken as it is (<c>--locked</c>): a tree it does not hold is refused.</param>
/// <param name="write">Whether the lock is written.</param>
/// <param name="held">The trees the lock holds that a manifest of the graph still names so.</param>
public sealed class SourceTrees(string lockFile, GitCache git, string cache, bool offline, bool frozen, bool write,
    IEnumerable<Locked> held)
{
    private static readonly TimeSpan LoadTime = TimeSpan.FromMinutes(10);

    private readonly List<Locked> _locked = [.. held];
    private readonly Dictionary<(string Package, string Name), string> _read = [];

    /// <summary>The trees read so far: the package, the tree's name and its directory.</summary>
    public IEnumerable<(string Package, string Name, string Directory)> Read =>
        _read.OrderBy(r => r.Key.Package, StringComparer.Ordinal).ThenBy(r => r.Key.Name, StringComparer.Ordinal)
            .Select(r => (r.Key.Package, r.Key.Name, r.Value));

    /// <summary>The directory of the tree <paramref name="name"/> of <paramref name="package"/>,
    /// read first where it is not yet.</summary>
    /// <exception cref="ManifestException">The tree cannot be read, is not what its hash or the lock
    /// says, or under <c>--locked</c> the lock does not hold it.</exception>
    public string Directory(Manifest package, string name)
    {
        if (_read.TryGetValue((package.Name, name), out var directory)) return directory;
        var tree = package.Vendor.First(t => t.Name == name);
        directory = tree.Git is not null ? FromGit(package, tree, renew: false) : FromUrl(package, tree);
        _read[(package.Name, name)] = directory;
        return directory;
    }

    /// <summary>The tree read anew at the revision it names, not at the commit the lock holds —
    /// <c>lyric update</c>'s —, and the lock written with it. What it was is returned with what it is.</summary>
    public (Locked? Before, Locked After) Renew(Manifest package, VendorTree tree)
    {
        var before = Held(package, tree);
        FromGit(package, tree, renew: true);
        return (before, Held(package, tree)!);
    }

    private Locked? Held(Manifest package, VendorTree tree) =>
        _locked.FirstOrDefault(l => l.Of == package.Name && l.Name == tree.Name && l.Git == tree.Git);

    private string FromGit(Manifest package, VendorTree tree, bool renew)
    {
        var held = renew ? null : Held(package, tree);
        if (held is null && frozen)
            throw new ManifestException("LYR-PKG0011", package.File, tree.Line, 1,
                $"the source tree '{tree.Name}' is read from {tree.Git}, which {LockFile.FileName} does not hold — --locked takes "
                + "the lock as it is; a build without it writes the lock");
        var (directory, commit, hash) = git.Checkout(new Dependency(tree.Name, null, tree.Git, tree.Line), package.File, held, tree: true);
        var entry = new Locked(tree.Name, "", tree.Git!, commit, hash, package.Name);
        if (entry != Held(package, tree))
        {
            _locked.RemoveAll(l => l.Of == package.Name && l.Name == tree.Name);
            _locked.Add(entry);
            if (write && !frozen)
            {
                // the lock as the graph wrote it, this tree's entry replaced
                var entries = LockFile.Read(lockFile).Where(l => !(l.Of == package.Name && l.Name == tree.Name)).Append(entry);
                LockFile.Write(lockFile, LockFile.Sorted(entries).ToList());
            }
        }
        return directory;
    }

    /// <summary>A URL's tree: its file loaded once under its hash, unpacked once beside it.</summary>
    private string FromUrl(Manifest package, VendorTree tree)
    {
        var store = Path.Combine(cache, "vendor");
        if (tree.Hash is not { } hash)
        {
            var loaded = Load(tree.Url!, offline, package.File, tree.Line, tree.Name);
            throw new ManifestException("LYR-PKG0002", package.File, tree.Line, 1,
                $"the source tree '{tree.Name}' is loaded from {tree.Url} without a hash, and a tree from a URL is pinned by one — "
                + $"what it loaded is hash = \"sha256:{Keep(store, loaded)}\"");
        }
        var directory = Path.Combine(store, "trees", hash);
        if (System.IO.Directory.Exists(directory)) return directory;
        var file = Path.Combine(store, "files", hash);
        byte[] bytes;
        if (File.Exists(file)) bytes = File.ReadAllBytes(file);
        else
        {
            bytes = Load(tree.Url!, offline, package.File, tree.Line, tree.Name);
            if (Keep(store, bytes) is var actual && actual != hash)
                throw new ManifestException("LYR-PKG0008", package.File, tree.Line, 1,
                    $"the file {tree.Url} sends for the source tree '{tree.Name}' has the hash sha256:{actual}, and the manifest "
                    + $"pins sha256:{hash} — check what changed; a new file needs its new hash");
        }
        Dictionary<string, byte[]> files;
        try { files = Unpack(bytes); }
        catch (Exception e) when (e is InvalidDataException or IOException)
        {
            throw new ManifestException("LYR-PKG0002", package.File, tree.Line, 1,
                $"the file of the source tree '{tree.Name}' is neither a tar compressed with gzip nor a zip that can be read: {e.Message}");
        }
        GitCache.Place(directory, files);
        return directory;
    }

    /// <summary>The bytes kept under their hash in <c>vendor/files/</c>; the hash, in lowercase hexadecimal.</summary>
    private static string Keep(string store, byte[] bytes)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var file = Path.Combine(store, "files", hash);
        if (File.Exists(file)) return hash;
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var partial = $"{file}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllBytes(partial, bytes);
        File.Move(partial, file, overwrite: true);
        return hash;
    }

    /// <summary>The file at <paramref name="url"/>, kept in the user's cache, and its hash —
    /// <c>lyric add --vendor --url</c>'s.</summary>
    /// <exception cref="ManifestException">The URL cannot be loaded (<c>LYR-PKG0007</c>, exit 2).</exception>
    public static string Hash(string url, bool offline, string declaring)
    {
        var store = Path.Combine(Lyric5.Toolchain.UserCache.Root, "vendor");
        return Keep(store, Load(url, offline, declaring, 1, null));
    }

    /// <summary>The bytes at a URL: <c>https://</c>, <c>http://</c> or <c>file://</c>.</summary>
    private static byte[] Load(string url, bool offline, string declaring, int line, string? name)
    {
        var what = name is null ? url : $"the source tree '{name}' from {url}";
        if (offline)
            throw new ManifestException("LYR-PKG0007", declaring, line, 1,
                $"{what} is not in the cache, and --offline loads nothing", exit: 2);
        try
        {
            var uri = new Uri(url);
            if (uri.IsFile) return File.ReadAllBytes(uri.LocalPath);
            using var client = new HttpClient { Timeout = LoadTime };
            using var response = client.GetAsync(uri).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"the server answers {(int)response.StatusCode} {response.ReasonPhrase}");
            return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException or UriFormatException
                                      or TaskCanceledException)
        {
            throw new ManifestException("LYR-PKG0007", declaring, line, 1, $"{what} cannot be loaded: {e.Message}", exit: 2);
        }
    }

    /// <summary>The regular files of a tar compressed with gzip or of a zip — told by their first
    /// bytes —, by their paths; a single directory every file lies in is left out.</summary>
    /// <exception cref="InvalidDataException">The bytes are neither, or cannot be read.</exception>
    internal static Dictionary<string, byte[]> Unpack(byte[] bytes)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (bytes is [0x1f, 0x8b, ..])
        {
            using var reader = new TarReader(new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress));
            while (reader.GetNextEntry() is { } entry)
            {
                // regular files alone: a link can point out of the tree
                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
                using var data = new MemoryStream();
                entry.DataStream?.CopyTo(data);
                files[Normal(entry.Name)] = data.ToArray();
            }
        }
        else if (bytes is [(byte)'P', (byte)'K', 3, 4, ..])
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/')) continue;
                using var data = new MemoryStream();
                using (var stream = entry.Open()) stream.CopyTo(data);
                files[Normal(entry.FullName)] = data.ToArray();
            }
        }
        else throw new InvalidDataException("its first bytes are neither gzip's nor a zip's");
        var tops = files.Keys.Select(path => path.IndexOf('/') is var slash and > 0 ? path[..slash] : null).Distinct().ToList();
        if (tops is [{ } top])
            return files.ToDictionary(f => f.Key[(top.Length + 1)..], f => f.Value, StringComparer.Ordinal);
        return files;
    }

    private static string Normal(string path)
    {
        path = path.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        return path.TrimStart('/');
    }
}
