using System.Reflection;
using Lyric.Core;

namespace Lyric.Tests.Core;

/// <summary>
/// The toolchain version stands at two places: as a C# constant in <see cref="ToolchainVersion.Value"/>,
/// where <c>lyric --version</c> reads it, and as <c>&lt;Version&gt;</c> in
/// <c>Directory.Build.props</c>, where the file properties of the binaries need it.
///
/// <para>One of the two has to be the source, and MSBuild cannot read a C# constant. Rather than
/// arguing the duplication away, it is guarded: when the numbers drift apart this test fails rather than
/// the user, to whom <c>lyric --version</c> says something other than the file properties of the exe.
/// </para>
///
/// <para>Exactly this fault happened once already, one level deeper: the Start section was indexed
/// differently by the writer than by the reader, and 1300 tests did not notice, because both readings
/// coincided by accident in the test programs.</para>
/// </summary>
public class ToolchainVersionTests
{
    [Fact]
    public void The_csharp_constant_matches_the_version_msbuild_stamped_into_the_assembly()
    {
        var stamped = typeof(ToolchainVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        Assert.NotNull(stamped);

        // The SDK appends '+<commit-sha>' in source-link builds. What matters is the part before it: the
        // number a human typed.
        var version = stamped!.Split('+')[0];

        Assert.Equal(ToolchainVersion.Value, version);
    }

    [Theory]
    [InlineData("4.5", 4, 5, 0)]
    [InlineData("4.5.1", 4, 5, 1)]
    [InlineData("10.0.20", 10, 0, 20)]
    public void A_minimum_is_two_or_three_numbers(string text, int major, int minor, int patch)
    {
        Assert.True(ToolchainVersion.TryParse(text, out var version));
        Assert.Equal((major, minor, patch), version);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("4.5.1.2")]
    [InlineData("4.x")]
    [InlineData("")]
    [InlineData("v4.5")]
    [InlineData("4..5")]
    public void Anything_else_is_not_a_version(string text) =>
        Assert.False(ToolchainVersion.TryParse(text, out _));

    /// <summary>Relative to whatever the toolchain is today, so the test does not move with the
    /// release commits.</summary>
    [Fact]
    public void Satisfying_a_minimum_is_at_least()
    {
        Assert.True(ToolchainVersion.TryParse(ToolchainVersion.Value, out var current));
        var (major, minor, patch) = current;

        Assert.True(ToolchainVersion.Satisfies(ToolchainVersion.Value));
        Assert.True(ToolchainVersion.Satisfies($"{major}.{minor}"));
        Assert.True(ToolchainVersion.Satisfies("1.0"));
        Assert.False(ToolchainVersion.Satisfies($"{major}.{minor}.{patch + 1}"));
        Assert.False(ToolchainVersion.Satisfies($"{major}.{minor + 1}"));
        Assert.False(ToolchainVersion.Satisfies($"{major + 1}.0"));
        Assert.False(ToolchainVersion.Satisfies("not a version"));
    }
}
