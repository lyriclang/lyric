namespace Lyric.Core;

/// <summary>
/// The toolchain version: one number for every binary of the suite.
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
    public const string Value = "5.0.0";
}
