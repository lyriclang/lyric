namespace Lyric.Core;

/// <summary>
/// The <c>LYR-CLI####</c> codes more than the driver reports.
///
/// <para>What each code means is the catalog's to say (<c>src/Lyric5.Build/diagnostics.toml</c>,
/// <c>lyric explain</c>); the driver writes the rest of them where it reports them. A number is not
/// given again when its case disappears: the catalog keeps it, retired. M12a S3 took out the 4.x
/// launcher's, the packer's and the stubs' constants — CLI0006, CLI0009, CLI0011–CLI0015,
/// CLI0017, CLI0019, CLI0020, retired there — and the names of CLI0002, CLI0004, CLI0005 and
/// CLI0008, which the Lyric 5 driver reports under other meanings.</para>
/// </summary>
public static class CliDiagnostics
{
    /// <summary>A given file could not be read.</summary>
    public const string FileUnreadable = "LYR-CLI0001";

    /// <summary>A <c>lyric.json</c> was found and could not be understood — the 4.x project file,
    /// which the language server reads until M13b takes it to <c>lyric.toml</c>.</summary>
    public const string BadProjectFile = "LYR-CLI0010";

    /// <summary>The run reported warnings and the profile or <c>--deny-warnings</c> denies them.
    /// The warnings keep their severity in the output; this error carries the policy into the exit
    /// code.</summary>
    public const string WarningsDenied = "LYR-CLI0016";

    /// <summary>A <c>lyric.json</c> names a minimum toolchain this one does not reach (the 4.x
    /// project file, as <see cref="BadProjectFile"/>).</summary>
    public const string ToolchainTooOld = "LYR-CLI0018";
}
