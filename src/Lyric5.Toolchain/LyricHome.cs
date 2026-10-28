namespace Lyric5.Toolchain;

/// <summary>
/// The user's Lyric home — <c>~/.lyric</c>, or the directory <c>LYRIC_HOME</c> names — as far as
/// the C side needs it: where <c>lyric toolchain install zig</c> puts the zig this toolchain is
/// built and tested with, <c>zig/&lt;version&gt;/</c>, which the search for a C compiler takes
/// before the PATH.
/// </summary>
public static class LyricHome
{
    /// <summary>The home <c>LYRIC_HOME</c> names, else <c>.lyric</c> in the user's home directory.</summary>
    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("LYRIC_HOME");
            return Path.GetFullPath(!string.IsNullOrWhiteSpace(configured)
                ? configured
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lyric"));
        }
    }

    /// <summary>The zig version of <c>tooling/zig-version</c>, carried in the binary.</summary>
    public static string ZigVersion { get; } = ReadZigVersion();

    /// <summary>Where the zig <paramref name="version"/> is installed under <paramref name="root"/>.</summary>
    public static string ZigDirectory(string root, string version) => Path.Combine(root, "zig", version);

    /// <summary>The binary of the zig <paramref name="version"/> under <paramref name="root"/>.</summary>
    public static string ZigBinary(string root, string version) =>
        Path.Combine(ZigDirectory(root, version), "zig" + (OperatingSystem.IsWindows() ? ".exe" : ""));

    /// <summary>The binary of the pinned zig under <paramref name="root"/>, where it is installed.</summary>
    public static string? InstalledZig(string root) => ZigBinary(root, ZigVersion) is var binary && File.Exists(binary) ? binary : null;

    private static string ReadZigVersion()
    {
        using var stream = typeof(LyricHome).Assembly.GetManifestResourceStream("zig-version")
                           ?? throw new InvalidOperationException("the binary carries no zig-version");
        return new StreamReader(stream).ReadToEnd().Trim();
    }
}
