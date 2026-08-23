using Lyric.Core;

namespace Lyric.Sema;

/// <summary>
/// When the encapsulation rules of 3.3 stop being advice and start being law.
///
/// <para>Two rules arrive together — an <c>as</c> through an opaque alias belongs to the module
/// that declares it, and a field belongs to its module unless it says <c>pub</c> — and both are
/// breaking. The project's way through that is the deprecation cycle it already ran for the eleven
/// forms 3.0 removed: warn for a release line, fail at the major.</para>
///
/// <para><b>Why the toolchain decides rather than a future commit.</b> The same reasoning
/// <see cref="DeprecationPromise"/> is built on: a transition that depends on somebody remembering
/// is a transition that slips. The version bump lands on the release branch and the rule comes due
/// there, in the build, without a second change anyone has to make.</para>
///
/// <para><b>Why a warning at all, given the promise was always the other way.</b> Grammar.md has
/// said since v1.15 that a script cannot forge an opaque handle, and it was not true — code
/// relying on that is relying on a bug. Failing it outright would still be a build that broke
/// without warning, on a claim its author never read. One release line of noise is cheap next to
/// that.</para>
/// </summary>
public static class EncapsulationPolicy
{
    /// <summary>The release the rules become errors in. Named here so the diagnostics can say it
    /// without repeating a literal nobody would find on a rename.</summary>
    public const string Enforced = "4.0";

    /// <summary>
    /// How loudly to report a violation: a warning while the tree claims a 3.x, an error from 4.0.
    /// </summary>
    /// <param name="toolchain">Overrides the tree's own version. For the tests, which have to be
    /// able to stand on both sides of the boundary without a release happening.</param>
    public static Severity Level(string? toolchain = null) =>
        MajorOf(toolchain ?? ToolchainVersion.Value) >= 4 ? Severity.Error : Severity.Warning;

    /// <summary>The major, or 0 for a version this cannot read — which reports as a warning, the
    /// lenient direction. A toolchain whose own version string is unparseable has a worse problem
    /// than this rule.</summary>
    private static int MajorOf(string version)
    {
        var dot = version.IndexOf('.');
        var head = dot < 0 ? version : version[..dot];
        return int.TryParse(head, out var major) ? major : 0;
    }
}
