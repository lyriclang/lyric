using Lyric.Core;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The switch that turns the 3.3 encapsulation rules from advice into law at 4.0.
///
/// <para>Worth its own tests because nothing else will notice it: the flip happens on a version
/// bump, in a release nobody is thinking about this in, and the whole point is that it needs no
/// commit of its own. A rule that silently kept warning past 4.0 would look exactly like a rule
/// that worked.</para>
/// </summary>
public class EncapsulationPolicyTests
{
    [Theory]
    [InlineData("3.2.0")]
    [InlineData("3.3.0")]
    [InlineData("3.99.7")]
    public void A_three_warns(string toolchain) =>
        Assert.Equal(Severity.Warning, EncapsulationPolicy.Level(toolchain));

    [Theory]
    [InlineData("4.0.0")]
    [InlineData("4.1.0")]
    [InlineData("5.0.0")]
    public void A_four_or_later_fails_the_build(string toolchain) =>
        Assert.Equal(Severity.Error, EncapsulationPolicy.Level(toolchain));

    [Fact]
    public void The_tree_itself_is_still_in_the_warning_phase()
    {
        // Reads the real ToolchainVersion. When this fails, it is because the tree went to 4.0 —
        // and then the migration notes have to be true, not the test relaxed.
        Assert.Equal(Severity.Warning, EncapsulationPolicy.Level());
    }

    [Theory]
    [InlineData("")]
    [InlineData("nightly")]
    [InlineData("3")]
    public void A_version_this_cannot_read_leans_lenient(string toolchain) =>
        // A toolchain whose own version string is unparseable has a worse problem than this rule,
        // and failing every build over it would bury the real one.
        Assert.Equal(Severity.Warning, EncapsulationPolicy.Level(toolchain));
}
