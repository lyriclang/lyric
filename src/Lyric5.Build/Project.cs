using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// What is being built and where its files go (design/v5/spec/11 C8, P4, P5). A single
/// <c>.lyr</c> file is an implicit package named after the file (C8). <c>out/</c> lies by the
/// manifest; the search for one ends at the nearest <c>lyric.toml</c>, else at <c>.git</c> or the
/// file system root, else the file's own directory (P5). Inside it: <c>out/&lt;profile&gt;/&lt;target&gt;/&lt;name&gt;</c>
/// (P4, the target always in the path) and <c>out/cache/</c> (L7). The manifest itself comes with
/// M7: until then a project is a file.
/// </summary>
public sealed record Project(string Source, string Name, string Root)
{
    public string OutDir => Path.Combine(Root, "out");

    public string CacheDir => Path.Combine(OutDir, "cache");

    public string Executable(Profile profile, Target target) =>
        Path.Combine(OutDir, profile.Name(), target.Triple, Name + target.ExecutableSuffix);

    public static Project ForFile(string path)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        return new Project(full, Path.GetFileNameWithoutExtension(full), RootOf(directory));
    }

    /// <summary>The nearest directory upward with a manifest, else with <c>.git</c>, else the start.</summary>
    private static string RootOf(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "lyric.toml"))) return dir.FullName;
        }
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
                return dir.FullName;
        }
        return start;
    }
}
