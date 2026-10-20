namespace Lyric.Core;

/// <summary>
/// A package's levels for the lints (spec 16 §5; 13 M12a P8): what the command line says
/// (<c>-A</c>, <c>-W</c>, <c>-D</c>) over what the manifest says (<c>[lints]</c>) over each lint's own
/// level. Within one of them the most specific selector decides — a lint's name or code over its
/// group over <c>warnings</c> —, among equals the last. A declaration's or a module's
/// <c>@Allow</c>, <c>@Warn</c> or <c>@Deny</c> decides over all of these where it stands (the
/// checker's <c>LintFilter</c>).
/// </summary>
public sealed class LintSettings(IReadOnlyList<LintSettings.Rule> commandLine, IReadOnlyList<LintSettings.Rule> manifest)
{
    /// <summary>A selector at a level, and who said it — for the note a denied lint carries.</summary>
    public readonly record struct Rule(string Selector, LintLevel Level, string Origin);

    /// <summary>Each lint at its own level.</summary>
    public static LintSettings Default { get; } = new([], []);

    public IReadOnlyList<Rule> CommandLine { get; } = commandLine;
    public IReadOnlyList<Rule> Manifest { get; } = manifest;

    /// <summary>The level of <paramref name="lint"/> here, and who set it — <c>null</c> for its own.</summary>
    public (LintLevel Level, string? Origin) LevelOf(CatalogEntry lint)
    {
        foreach (var source in (IReadOnlyList<Rule>[])[CommandLine, Manifest])
        {
            Rule? best = null;
            var specificity = -1;
            foreach (var rule in source)
                if (DiagnosticCatalog.Select(rule.Selector) is { } named && named.Lints.Contains(lint) && named.Specificity >= specificity)
                {
                    best = rule;
                    specificity = named.Specificity;
                }
            if (best is { } decided) return (decided.Level, decided.Origin);
        }
        return (lint.Level, null);
    }

    /// <summary>The same levels with the command line's in front — the root package's.</summary>
    public LintSettings WithCommandLine(IReadOnlyList<Rule> rules) => new(rules, Manifest);
}
