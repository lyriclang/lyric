namespace Lyric.Core;

/// <summary>
/// The toolchain version: one number for every binary of the suite.
///
/// <para>Separate from the bytecode format version: a third-party runtime carries its own
/// toolchain version and the same format version.</para>
/// </summary>
public static class ToolchainVersion
{
    /// <summary>
    /// Raised at a release tag, here and in <c>Directory.Build.props</c>. MSBuild cannot read a C#
    /// constant, so both exist and a test compares them against the generated assembly attribute.
    ///
    /// <para>The editor clients (<c>lyriclang/vscode-lyric</c>, <c>lyriclang/jetbrains-lyric</c>)
    /// version independently, in their own repositories.</para>
    /// </summary>
    public const string Value = "4.5.0";

    /// <summary>
    /// Reads <c>MAJOR.MINOR</c> or <c>MAJOR.MINOR.PATCH</c>, plain decimal digits and nothing else.
    /// A missing patch is zero: a project that says <c>4.5</c> means the release, not a point on
    /// it.
    /// </summary>
    public static bool TryParse(string text, out (int Major, int Minor, int Patch) version)
    {
        version = default;
        var parts = text.Split('.');
        if (parts.Length is < 2 or > 3) return false;

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)) return false;
            if (!int.TryParse(parts[i], out numbers[i])) return false;
        }

        version = (numbers[0], numbers[1], numbers[2]);
        return true;
    }

    /// <summary>Whether this toolchain is at least <paramref name="minimum"/>: the check a
    /// <c>lyric.json</c>'s <c>toolchain</c> key asks for. A minimum this method cannot read is
    /// not satisfied; the caller has validated the spelling before asking.</summary>
    public static bool Satisfies(string minimum) =>
        TryParse(minimum, out var required) && TryParse(Value, out var current)
        && current.CompareTo(required) >= 0;
}
