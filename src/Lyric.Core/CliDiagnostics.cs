namespace Lyric.Core;

/// <summary>
/// The <c>LYR-CLI####</c> codes more than the driver reports.
///
/// <para>What each code means is the catalog's to say (<c>diagnostics.toml</c> beside this file,
/// <c>lyric explain</c>); the driver writes the rest of them where it reports them. A number is not
/// given again when its case disappears: the catalog keeps it, retired — the 4.x launcher's, the
/// packer's and the stubs' codes among them, and CLI0016, since a profile that denies warnings
/// makes each warning the error.</para>
/// </summary>
public static class CliDiagnostics
{
    /// <summary>A given file could not be read.</summary>
    public const string FileUnreadable = "LYR-CLI0001";

    /// <summary>A <c>lyric.json</c> was found and could not be understood — the 4.x project file,
    /// which the language server still reads.</summary>
    public const string BadProjectFile = "LYR-CLI0010";

    /// <summary>A <c>lyric.json</c> names a minimum toolchain this one does not reach (the 4.x
    /// project file, as <see cref="BadProjectFile"/>).</summary>
    public const string ToolchainTooOld = "LYR-CLI0018";
}
