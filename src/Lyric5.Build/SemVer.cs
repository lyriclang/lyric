using System.Text.RegularExpressions;

namespace Lyric5.Build;

/// <summary>
/// A semantic version (semver.org 2.0): <c>MAJOR.MINOR.PATCH</c>, a pre-release after <c>-</c>,
/// build data after <c>+</c> that no comparison reads. A git tag names one — <c>v1.2.0</c> or
/// <c>1.2.0</c> — and a dependency on it asks for that version or a later one of its line
/// (design/v5/spec/07 P2, P4).
/// </summary>
public sealed partial record SemVer(long Major, long Minor, long Patch, string? Pre) : IComparable<SemVer>
{
    public static SemVer? Parse(string text)
    {
        var match = Pattern().Match(text);
        if (!match.Success) return null;
        return long.TryParse(match.Groups[1].Value, out var major) && long.TryParse(match.Groups[2].Value, out var minor)
               && long.TryParse(match.Groups[3].Value, out var patch)
            ? new SemVer(major, minor, patch, match.Groups[4].Success ? match.Groups[4].Value : null)
            : null;
    }

    /// <summary>The version a tag names, <c>v1.2.0</c> or <c>1.2.0</c>; <c>null</c> for another tag.</summary>
    public static SemVer? OfTag(string tag) => Parse(tag.StartsWith('v') ? tag[1..] : tag);

    /// <summary>Of <paramref name="tags"/>, the one naming the greatest version of
    /// <paramref name="current"/>'s line above it (the review's M7-8): never another major — below
    /// 1, another minor —, and a pre-release only where the current one is one. <c>null</c> where
    /// none is greater.</summary>
    public static string? Newest(SemVer current, IEnumerable<string> tags) =>
        tags.Select(t => (Tag: t, Version: OfTag(t)))
            .Where(t => t.Version is { } v && v.Line == current.Line && (v.Pre is null || current.Pre is not null)
                        && v.CompareTo(current) > 0)
            .MaxBy(t => t.Version)
            .Tag;

    /// <summary>The versions a requirement accepts besides its own, Cargo's caret (07 P2): the same
    /// major version; below 1, the same minor; below 0.1, itself alone. Two versions of one line
    /// are one package for a program, two lines are two (P3).</summary>
    public (long, long, long) Line => Major > 0 ? (Major, -1, -1) : Minor > 0 ? (0, Minor, -1) : (0, 0, Patch);

    /// <summary>Precedence (semver.org §11): the numbers, then a version with a pre-release below
    /// the one without, pre-releases identifier by identifier — numbers below words, numbers by
    /// value, words by their characters, and a shorter list below a longer one it begins.</summary>
    public int CompareTo(SemVer? other)
    {
        if (other is null) return 1;
        var numbers = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (numbers != 0) return numbers;
        if (Pre is null || other.Pre is null) return (Pre is null).CompareTo(other.Pre is null);
        var mine = Pre.Split('.');
        var theirs = other.Pre.Split('.');
        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            var a = long.TryParse(mine[i], out var x);
            var b = long.TryParse(theirs[i], out var y);
            var order = (a, b) switch
            {
                (true, true) => x.CompareTo(y),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(mine[i], theirs[i]),
            };
            if (order != 0) return order;
        }
        return mine.Length.CompareTo(theirs.Length);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(Pre is null ? "" : "-" + Pre)}";

    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();
}
