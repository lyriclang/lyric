namespace Lyric.Core;

/// <summary>
/// The <c>LYR-CLI####</c> codes more than the driver reports.
///
/// <para>What each code means is the catalog's to say (<c>diagnostics.toml</c> beside this file,
/// <c>lyric explain</c>); the driver writes the rest of them where it reports them. A number is not
/// given again when its case disappears: the catalog keeps it, retired.</para>
/// </summary>
public static class CliDiagnostics
{
    /// <summary>A given file could not be read.</summary>
    public const string FileUnreadable = "LYR-CLI0001";
}
