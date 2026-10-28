using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>A change to the toolchains under the home was refused, or a toolchain could not be had:
/// the environment's failure, exit 2, with its code and advice.</summary>
public sealed class ToolchainException(string code, string message, params string[] help) : Exception(message)
{
    public string Code { get; } = code;
    public string[] Help { get; } = help;
}

/// <summary>
/// Where the installed toolchains live: <c>~/.lyric</c>, or the directory <c>LYRIC_HOME</c> names.
/// <c>toolchains/&lt;version&gt;/</c> holds each as its archive unpacked it, <c>config.toml</c> names
/// the default (<c>default = "&lt;version&gt;"</c>), and <c>bin</c> is a link to the default's
/// directory — a symbolic link, on Windows a junction —, so that the one directory on the PATH
/// runs the default with its standard library and runtime beside it, and changing the default
/// replaces a link, never a binary that may be running.
/// </summary>
public sealed class ToolchainHome(string root)
{
    /// <summary>The command's name until the line takes the plain one.</summary>
    public const string BinaryName = "lyric5";

    public const string ConfigName = "config.toml";

    /// <summary>The prefix of a directory an install unpacks into before it is in place.</summary>
    private const string PartialPrefix = ".partial-";

    public string Root { get; } = Path.GetFullPath(root);
    public string Toolchains => Path.Combine(Root, "toolchains");
    public string Bin => Path.Combine(Root, "bin");
    public string Config => Path.Combine(Root, ConfigName);

    /// <summary>The home <c>LYRIC_HOME</c> names, else <c>.lyric</c> in the user's home directory.</summary>
    public static ToolchainHome ForUser()
    {
        var configured = Environment.GetEnvironmentVariable("LYRIC_HOME");
        return new ToolchainHome(!string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lyric"));
    }

    public string DirectoryOf(string version) => Path.Combine(Toolchains, version);

    /// <summary>The binary of the installed <paramref name="version"/>.</summary>
    public string BinaryOf(string version) => Path.Combine(DirectoryOf(version), BinaryName + Target.Host.ExecutableSuffix);

    public bool IsInstalled(string version) => File.Exists(Path.Combine(DirectoryOf(version), ToolchainManifest.FileName));

    /// <summary>The installed versions, the newest first; a directory an install has not finished
    /// is none.</summary>
    public IReadOnlyList<string> Installed()
    {
        if (!Directory.Exists(Toolchains)) return [];
        return Directory.EnumerateDirectories(Toolchains)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !name.StartsWith('.') && IsInstalled(name))
            .OrderByDescending(name => SemVer.Parse(name), Comparer<SemVer?>.Create((a, b) => a is null ? (b is null ? 0 : -1) : a.CompareTo(b)))
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The installed version <paramref name="asked"/> names: itself, or a version whose
    /// build part (<c>+…</c>) it leaves out.</summary>
    public string? Resolve(string asked) =>
        Installed().FirstOrDefault(v => v == asked) ?? Installed().FirstOrDefault(v => WithoutBuild(v) == asked);

    public static string WithoutBuild(string version) => version.IndexOf('+') is var plus and >= 0 ? version[..plus] : version;

    /// <summary>The channel a version is published on: a development build's (<c>-dev.…</c>) is
    /// <c>dev</c>, every other <c>stable</c>.</summary>
    public static string ChannelOf(string version) =>
        SemVer.Parse(version) is { Pre: { } pre } && (pre == "dev" || pre.StartsWith("dev.", StringComparison.Ordinal))
            ? ChannelIndex.Dev
            : ChannelIndex.Stable;

    /// <summary>The default named in <c>config.toml</c>; <c>null</c> where none is.</summary>
    /// <exception cref="ToolchainException"><c>LYR-CLI0025</c>: the file is not one the toolchain reads.</exception>
    public string? Default()
    {
        if (!File.Exists(Config)) return null;
        Lyric.Core.TomlTable table;
        try { table = Lyric.Core.Toml.Parse(File.ReadAllText(Config)); }
        catch (Exception e) when (e is Lyric.Core.TomlException or IOException)
        {
            throw new ToolchainException("LYR-CLI0025", $"{Config} cannot be read: {e.Message}");
        }
        foreach (var key in table.Keys)
            if (key != "default")
                throw new ToolchainException("LYR-CLI0025", $"{Config} holds '{key}', and the one key it takes is 'default'");
        return table.TryGet("default", out var value)
            ? value as string ?? throw new ToolchainException("LYR-CLI0025", $"{Config}: 'default' is a version in quotes")
            : null;
    }

    /// <summary>Makes the installed <paramref name="version"/> the default: <c>config.toml</c>
    /// written, <c>bin</c> pointed at it.</summary>
    public void SetDefault(string version)
    {
        Directory.CreateDirectory(Root);
        var partial = $"{Config}.{Environment.ProcessId}.tmp";
        File.WriteAllText(partial, $"default = \"{version}\"\n");
        File.Move(partial, Config, overwrite: true);
        LinkBin(version);
    }

    /// <summary><c>bin</c> as a link to the directory of <paramref name="version"/>: replaced where
    /// it is a link, refused where something else stands there.</summary>
    private void LinkBin(string version)
    {
        var info = new DirectoryInfo(Bin);
        // a link, even one whose toolchain is gone, is replaced; a junction is removed as a directory
        if (info.LinkTarget is not null)
        {
            if (OperatingSystem.IsWindows()) Directory.Delete(Bin);
            else File.Delete(Bin);
        }
        else if (info.Exists || File.Exists(Bin))
            throw new ToolchainException("LYR-CLI0025", $"{Bin} is no link the toolchain made", "move it away; the toolchain makes it a link to the default");
        if (OperatingSystem.IsWindows())
        {
            // a junction: a symbolic link needs a privilege or the developer mode, a junction neither
            var made = ProcessRunner.Run("cmd.exe", ["/d", "/c", "mklink", "/J", Bin, DirectoryOf(version)], TimeSpan.FromSeconds(30));
            if (made.ExitCode != 0)
                throw new ToolchainException("LYR-CLI0025", $"no junction {Bin}: {(made.Stderr + made.Stdout).Trim()}");
        }
        else Directory.CreateSymbolicLink(Bin, Path.Combine("toolchains", version));
    }

    /// <summary>Removes the installed <paramref name="version"/>; not the default, nor the
    /// toolchain at <paramref name="running"/>, the directory of the one that runs.</summary>
    public void Remove(string version, string running)
    {
        var dir = DirectoryOf(version);
        if (SamePath(dir, running))
            throw new ToolchainException("LYR-CLI0025", $"lyric {version} is the toolchain that runs this command", "remove it from another toolchain");
        if (Default() == version)
            throw new ToolchainException("LYR-CLI0025", $"lyric {version} is the default", "make another the default first: lyric5 toolchain default <version>");
        // renamed aside first, so that a removal cut short leaves no toolchain half there
        var aside = Path.Combine(Toolchains, $"{PartialPrefix}{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.Move(dir, aside);
        Directory.Delete(aside, recursive: true);
    }

    /// <summary>A fresh directory to unpack into, beside the toolchains so that moving it into place
    /// is a rename; the ones a run cut short over an hour ago are removed first.</summary>
    public string Partial()
    {
        Directory.CreateDirectory(Toolchains);
        foreach (var old in Directory.EnumerateDirectories(Toolchains, PartialPrefix + "*"))
        {
            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(old) < TimeSpan.FromHours(1)) continue;
            try { Directory.Delete(old, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        var dir = Path.Combine(Toolchains, $"{PartialPrefix}{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>The directory of the toolchain that runs: where its binary lies, through the links
    /// on the way — <c>bin</c> leads to the default's own directory.</summary>
    public static string Running(string baseDirectory)
    {
        var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        try
        {
            if (new DirectoryInfo(dir).ResolveLinkTarget(returnFinalTarget: true) is { } target) return Path.GetFullPath(target.FullName);
        }
        catch (IOException) { }
        return dir;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
