using System.Text;
using System.Text.RegularExpressions;

namespace Lyric5.Build;

/// <summary>A revision of a repository the build read (design/v5/spec/07 P5): the commit it was and
/// the hash of its package content.</summary>
/// <param name="Version">The package's own version, from its manifest — for a person reading the lock.</param>
public sealed record Locked(string Name, string Version, GitSource Git, string Commit, string Hash);

/// <summary>
/// <c>lyric.lock</c> beside the root manifest (design/v5/spec/07 P5): every revision of a git
/// repository the resolution read, with its commit and the hash of its content — minimal version
/// selection makes the choice, the lock makes the content reproducible. Written by the toolchain,
/// checked in with the manifest; a dependency's own lock is not read.
/// </summary>
public static partial class LockFile
{
    public const string FileName = "lyric.lock";

    /// <summary>The lock of the program built from <paramref name="root"/>.</summary>
    public static string For(Manifest root) => Path.Combine(root.Root, FileName);

    /// <summary>What <paramref name="file"/> holds; nothing where there is no file.</summary>
    /// <exception cref="ManifestException">The lock is not TOML, or not a lock.</exception>
    public static List<Locked> Read(string file)
    {
        if (!File.Exists(file)) return [];
        TomlTable document;
        try
        {
            document = Toml.Parse(File.ReadAllText(file));
        }
        catch (TomlException e)
        {
            throw new ManifestException("LYR-PKG0001", file, e.Line, e.Column, e.Message);
        }
        if (!document.TryGet("version", out var version) || version is not 1L)
            throw new ManifestException("LYR-PKG0002", file, 1, 1, "a lock this toolchain reads says version = 1 — 'lyric update' writes it anew");
        var entries = new List<Locked>();
        if (!document.TryGet("package", out var found)) return entries;
        if (found is not TomlArray { OfTables: true } packages)
            throw new ManifestException("LYR-PKG0002", file, 1, 1, "[[package]] is an array of tables");
        foreach (TomlTable package in packages)
        {
            string Field(string key) => package.TryGet(key, out var value) && value is string text
                ? text
                : throw new ManifestException("LYR-PKG0002", file, package.Line, 1, $"a locked package needs a string '{key}'");
            var name = Field("name");
            var packageVersion = Field("version");
            // a lock an earlier toolchain wrote may hold a URL as the manifest spelled it (M7-10)
            var url = GitSource.Normalize(Field("git"));
            var revisions = new[] { "tag", "branch", "rev" }.Where(package.Contains).ToList();
            if (revisions.Count > 1)
                throw new ManifestException("LYR-PKG0002", file, package.Line, 1, $"'{name}' is locked at one revision");
            var git = revisions.Count == 0
                ? new GitSource(url, GitRefKind.Default, null)
                : new GitSource(url, revisions[0] switch { "tag" => GitRefKind.Tag, "branch" => GitRefKind.Branch, _ => GitRefKind.Rev }, Field(revisions[0]));
            var commit = Field("commit");
            var hash = Field("hash");
            if (!CommitPattern().IsMatch(commit) || !HashPattern().IsMatch(hash))
                throw new ManifestException("LYR-PKG0002", file, package.Line, 1, $"'{name}' is locked to no commit or no hash");
            entries.Add(new Locked(name, packageVersion, git, commit, hash));
        }
        return entries;
    }

    /// <summary>The lock's text: its entries sorted, so the same resolution writes the same bytes.</summary>
    public static string Text(IEnumerable<Locked> entries)
    {
        var text = new StringBuilder();
        text.Append("# Written by lyric (07 P5): each revision of a git repository the build reads,\n");
        text.Append("# the commit it is and the hash of its package content. Check it in with lyric.toml.\n");
        text.Append("version = 1\n");
        foreach (var entry in Sorted(entries))
        {
            text.Append("\n[[package]]\n");
            text.Append($"name = {Quote(entry.Name)}\n");
            text.Append($"version = {Quote(entry.Version)}\n");
            text.Append($"git = {Quote(entry.Git.Url)}\n");
            var key = entry.Git.Kind switch { GitRefKind.Tag => "tag", GitRefKind.Branch => "branch", GitRefKind.Rev => "rev", _ => null };
            if (key is not null) text.Append($"{key} = {Quote(entry.Git.Ref!)}\n");
            text.Append($"commit = {Quote(entry.Commit)}\n");
            text.Append($"hash = {Quote(entry.Hash)}\n");
        }
        return text.ToString();
    }

    /// <summary>Writes the lock when its text changes: a resolution that read nothing new leaves
    /// the file alone. No lock is begun for a program that reads nothing from git.</summary>
    public static void Write(string file, IReadOnlyList<Locked> entries)
    {
        if (entries.Count == 0 && !File.Exists(file)) return;
        var text = Text(entries);
        if (File.Exists(file) && File.ReadAllText(file) == text) return;
        var partial = $"{file}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(partial, text);
        File.Move(partial, file, overwrite: true);
    }

    public static IEnumerable<Locked> Sorted(IEnumerable<Locked> entries) =>
        entries.OrderBy(e => e.Name, StringComparer.Ordinal)
            .ThenBy(e => e.Git.Url, StringComparer.Ordinal)
            .ThenBy(e => e.Git.Kind)
            .ThenBy(e => e.Git.Ref, StringComparer.Ordinal);

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    [GeneratedRegex("^[0-9a-f]{40}([0-9a-f]{24})?$")]
    private static partial Regex CommitPattern();

    [GeneratedRegex("^h1:[A-Za-z0-9+/]{43}=$")]
    private static partial Regex HashPattern();
}
