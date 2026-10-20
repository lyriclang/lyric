using Lyric.Core;

namespace Lyric5.Build;

/// <summary>
/// What the command line says of the lints (spec 16 §5): the levels <c>-A</c>, <c>-W</c> and
/// <c>-D</c> set with a lint, a group or <c>warnings</c>, in their order, and whether the warnings
/// of the dependencies and of <c>std</c> are said (<c>--warn-deps</c>). The driver reads it once
/// per command and hands it to the projects it builds.
/// </summary>
public sealed record LintArgs(IReadOnlyList<LintSettings.Rule> Rules, bool WarnDeps)
{
    public static LintArgs None { get; } = new([], false);

    /// <summary>A text that changes when the levels do, for the key of a build's cache: the same
    /// sources under other levels go through the front end again.</summary>
    public string Fingerprint =>
        string.Join(",", Rules.Select(r => $"{r.Level}:{r.Selector}")) + (WarnDeps ? ";warn-deps" : "");
}
