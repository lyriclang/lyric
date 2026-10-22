using System.Text;

namespace Lyric.Core;

/// <summary>One code of the catalog.</summary>
public sealed record CatalogEntry(string Code, string Title, string Severity, string Area, string Summary)
{
    /// <summary>The rule, why, and what to do — errors and warnings.</summary>
    public string? Explanation { get; init; }

    /// <summary>A program that reports the code, and the same program put right.</summary>
    public string? Wrong { get; init; }
    public string? Right { get; init; }

    /// <summary>A program that reports a hint or panics with the code.</summary>
    public string? Program { get; init; }

    /// <summary>What shows a code no program can — a command line, a manifest; not checked.</summary>
    public string? Example { get; init; }

    /// <summary>Why the code is no longer reported; its number stays taken.</summary>
    public string? Retired { get; init; }

    /// <summary>A warning's lint (spec 16 §5): its name, its group, its level unless said.
    /// Every warning has one; nothing else has.</summary>
    public string? Lint { get; init; }
    public string? Group { get; init; }
    public LintLevel Level { get; init; }
}

/// <summary>What a lint does where it is met (16 §5): nothing, a warning, or an error.</summary>
public enum LintLevel
{
    Allow,
    Warn,
    Deny,
}

/// <summary>
/// The diagnostic catalog, <c>diagnostics.toml</c> beside this file and embedded in the toolchain: every
/// code the toolchain reports, its title, severity, area and summary; for errors and warnings an
/// explanation with a wrong and a right program; for hints and panics a program; for a code no program
/// shows an example. <c>lyric explain</c> prints an entry; the spec's Appendix A is generated from it
/// (<c>tooling/diagnostics/appendix.py</c>); <c>CatalogTests</c> hold it against the compiler — every
/// code reported is listed, every program reports what it says.
/// </summary>
public static class DiagnosticCatalog
{
    public static readonly string[] Severities = ["error", "warning", "hint", "panic"];

    public static readonly string[] Areas =
    [
        "01-lexical", "02-grammar", "03-types", "04-modules", "05-interfaces", "06-errors", "07-statements",
        "08-expressions", "09-patterns", "10-concurrency", "11-metaprogramming", "12-stdlib", "13-abi", "14-cli",
        "15-project", "16-diagnostics",
    ];

    /// <summary>The groups a lint belongs to; <c>warnings</c> names every lint.</summary>
    public static readonly string[] Groups =
        ["unused", "correctness", "resources", "concurrency", "deprecation", "style", "perf", "docs"];

    /// <summary>The group of every lint.</summary>
    public const string AllLints = "warnings";

    private static readonly string[] Keys =
    [
        "title", "severity", "lint", "group", "level", "area", "summary", "explanation", "wrong", "right", "program",
        "example", "retired",
    ];

    private static readonly Lazy<IReadOnlyDictionary<string, CatalogEntry>> Embedded = new(() =>
    {
        using var stream = typeof(DiagnosticCatalog).Assembly.GetManifestResourceStream("diagnostics.toml")
                           ?? throw new InvalidOperationException("the toolchain carries no diagnostics.toml");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return Read(reader.ReadToEnd()).ToDictionary(e => e.Code, StringComparer.Ordinal);
    });

    /// <summary>The toolchain's own catalog, by code.</summary>
    public static IReadOnlyDictionary<string, CatalogEntry> Entries => Embedded.Value;

    private static readonly Lazy<IReadOnlyDictionary<string, CatalogEntry>> ByLint = new(() =>
        Entries.Values.Where(e => e.Lint is not null).ToDictionary(e => e.Lint!, StringComparer.Ordinal));

    /// <summary>The lints, by name.</summary>
    public static IReadOnlyDictionary<string, CatalogEntry> Lints => ByLint.Value;

    /// <summary>The selector closest to <paramref name="selector"/> in spelling — a lint's name, a
    /// group, <c>warnings</c> — at most two edits away, for a "did you mean"; <c>null</c> for none.</summary>
    public static string? Nearest(string selector)
    {
        string? found = null;
        var best = 3;
        foreach (var candidate in Lints.Keys.Concat(Groups).Append(AllLints))
        {
            var d = Distance(selector, candidate);
            if (d < best) (best, found) = (d, candidate);
        }
        return found;
    }

    private static int Distance(string a, string b)
    {
        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) row[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var diagonal = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1));
                diagonal = above;
            }
        }
        return row[b.Length];
    }

    /// <summary>The lints a selector names (16 §5 rule 2) — a lint's name or code, a group, or
    /// <c>warnings</c> —, and how specific it is (2 a lint, 1 a group, 0 all); none for a selector
    /// that names nothing.</summary>
    public static (IReadOnlyList<CatalogEntry> Lints, int Specificity)? Select(string selector)
    {
        if (Lints.TryGetValue(selector, out var byName)) return ([byName], 2);
        if (Entries.TryGetValue(selector, out var byCode) && byCode.Lint is not null) return ([byCode], 2);
        if (selector == AllLints) return ([.. Lints.Values], 0);
        if (Groups.Contains(selector)) return ([.. Lints.Values.Where(l => l.Group == selector)], 1);
        return null;
    }

    /// <summary>
    /// The entries of a catalog text, in its order. What an entry must hold is checked here, so a
    /// broken catalog fails the first test that reads it, not a reader of <c>explain</c>.
    /// </summary>
    /// <exception cref="InvalidDataException">An entry misses what it needs or holds what it may not.</exception>
    public static IReadOnlyList<CatalogEntry> Read(string text)
    {
        var root = Toml.ParseCatalog(text);
        var entries = new List<CatalogEntry>();
        foreach (var code in root.Keys)
        {
            if (!root.TryGet(code, out var value) || value is not TomlTable table)
                throw new InvalidDataException($"{code}: an entry is a table");
            if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^LYR-[A-Z]+[0-9]{4}$"))
                throw new InvalidDataException($"'{code}' is no code");
            foreach (var key in table.Keys)
                if (!Keys.Contains(key)) throw new InvalidDataException($"{code}: no key '{key}' in an entry");
            string? Get(string key) =>
                !table.TryGet(key, out var v) ? null
                : v is string s ? s.TrimEnd('\n')
                : throw new InvalidDataException($"{code}: '{key}' is a string");
            string Need(string key) => Get(key) ?? throw new InvalidDataException($"{code}: '{key}' is missing");

            var entry = new CatalogEntry(code, Need("title"), Need("severity"), Need("area"), Need("summary"))
            {
                Explanation = Get("explanation"), Wrong = Get("wrong"), Right = Get("right"),
                Program = Get("program"), Example = Get("example"), Retired = Get("retired"),
                Lint = Get("lint"), Group = Get("group"),
                Level = Get("level") switch
                {
                    null or "warn" => LintLevel.Warn,
                    "allow" => LintLevel.Allow,
                    "deny" => LintLevel.Deny,
                    var other => throw new InvalidDataException($"{code}: level '{other}' — allow, warn or deny"),
                },
            };
            if (!Severities.Contains(entry.Severity)) throw new InvalidDataException($"{code}: severity '{entry.Severity}'");
            // a warning is a lint, and nothing else is
            var live = entry.Retired is null && entry.Severity == "warning";
            if (live != (entry.Lint is not null) || (entry.Lint is null) != (entry.Group is null) || (entry.Lint is null && Get("level") is not null))
                throw new InvalidDataException($"{code}: a warning has its lint, group and level, and nothing else has");
            if (entry.Lint is { } lint)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(lint, "^[a-z][a-z0-9]*(-[a-z0-9]+)*$"))
                    throw new InvalidDataException($"{code}: lint name '{lint}' — lowercase words joined by '-'");
                if (!Groups.Contains(entry.Group)) throw new InvalidDataException($"{code}: group '{entry.Group}'");
                if (Groups.Contains(lint) || lint == AllLints) throw new InvalidDataException($"{code}: '{lint}' names a group");
                if (entries.Any(e => e.Lint == lint)) throw new InvalidDataException($"{code}: lint '{lint}' twice");
            }
            if (!Areas.Contains(entry.Area)) throw new InvalidDataException($"{code}: area '{entry.Area}'");
            if (entry.Retired is not null)
            {
                if ((entry.Explanation ?? entry.Wrong ?? entry.Right ?? entry.Program ?? entry.Example) is not null)
                    throw new InvalidDataException($"{code}: a retired entry keeps title, severity, area and summary alone");
            }
            else if (entry.Severity is "error" or "warning")
            {
                if (entry.Explanation is null) throw new InvalidDataException($"{code}: an {entry.Severity} is explained");
                if ((entry.Wrong is null) != (entry.Right is null))
                    throw new InvalidDataException($"{code}: 'wrong' and 'right' come together");
                if (entry.Wrong is null && entry.Example is null)
                    throw new InvalidDataException($"{code}: an {entry.Severity} is shown — 'wrong' and 'right', or an 'example'");
                if (entry.Program is not null) throw new InvalidDataException($"{code}: an {entry.Severity} is shown by 'wrong'");
            }
            else
            {
                if ((entry.Wrong ?? entry.Right) is not null) throw new InvalidDataException($"{code}: a {entry.Severity} is shown by 'program'");
                if (entry.Program is null && entry.Example is null)
                    throw new InvalidDataException($"{code}: a {entry.Severity} is shown — a 'program' or an 'example'");
            }
            entries.Add(entry);
        }
        return entries;
    }

    /// <summary>The entry as <c>lyric explain</c> prints it: Markdown, read as text.</summary>
    public static string Explain(CatalogEntry entry)
    {
        var sb = new StringBuilder();
        sb.Append($"{entry.Code}: {entry.Title}\n");
        sb.Append($"{entry.Severity} · {entry.Area}");
        if (entry.Lint is { } lint) sb.Append($" · lint '{lint}', group '{entry.Group}', {entry.Level.ToString().ToLowerInvariant()} unless said");
        sb.Append("\n\n");
        if (entry.Retired is { } retired) sb.Append($"Retired: {retired}\n\n");
        sb.Append(entry.Summary).Append("\n\n");
        if (entry.Explanation is { } explanation) sb.Append(explanation).Append("\n\n");
        void Program(string heading, string? text)
        {
            if (text is null) return;
            sb.Append(heading).Append(":\n\n");
            foreach (var line in text.Split('\n')) sb.Append(line.Length == 0 ? "" : "    " + line).Append('\n');
            sb.Append('\n');
        }
        Program("Wrong", entry.Wrong);
        Program("Right", entry.Right);
        Program("Example", entry.Program ?? entry.Example);
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>A code as a reader may write it — <c>sem0042</c>, <c>SEM0042</c>, <c>LYR-SEM0042</c> —
    /// in the catalog's spelling.</summary>
    public static string Normalize(string code)
    {
        var upper = code.Trim().ToUpperInvariant();
        return upper.StartsWith("LYR-", StringComparison.Ordinal) ? upper : "LYR-" + upper;
    }
}
