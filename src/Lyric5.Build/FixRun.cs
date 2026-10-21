using System.Text;
using Lyric.Compiler;
using Lyric.Core;

namespace Lyric5.Build;

/// <summary>
/// <c>lyric fix</c> (spec 16 §6): the safe suggestions of the findings in the package's own files,
/// applied. A pass reads the findings, takes the changes that do not overlap — in each file, in
/// source order —, and writes the files; the next pass reads the changed text, until a pass finds
/// nothing to change or ten have run. What stays — a finding without a safe change, an error — is
/// said as <c>build</c> says it, and a change that needs a reader is one of those.
/// </summary>
public static class FixRun
{
    private const int Passes = 10;

    /// <param name="checks">The front end on each program of the package, or on the library.</param>
    /// <param name="ours">Whether a file is the package's own: a dependency's and std's are not changed.</param>
    /// <param name="root">What the paths said are relative to.</param>
    public static int Run(IReadOnlyList<Func<CompileResult>> checks, Func<string, bool> ours, string root, bool dryRun,
        TextWriter output, TextWriter error)
    {
        var events = Lyric.Core.Rendering.RenderStyle.Current.Events;
        var changed = new Dictionary<string, int>(StringComparer.Ordinal);
        List<CompileResult> results;
        for (var pass = 0; ; pass++)
        {
            results = [.. checks.Select(check => check())];
            var fixes = SafeFixes(results, ours);
            if (fixes.Count == 0 || pass == Passes) break;
            foreach (var (path, list) in fixes.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                var before = File.ReadAllText(path);
                var after = SourceEdits.Apply(before, list.Select(f => f.Fix));
                var shown = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (dryRun)
                {
                    Diff(output, shown, before, after);
                    continue;
                }
                File.WriteAllText(path, after);
                changed[shown] = changed.GetValueOrDefault(shown) + list.Count;
                foreach (var (fix, code, line, column) in list)
                    events?.Fixed(shown, line, column, code, fix.Text);
            }
            if (dryRun) break;
        }
        if (events is null)
            foreach (var (shown, count) in changed.OrderBy(c => c.Key, StringComparer.Ordinal))
                error.WriteLine($"       Fixed {shown} ({count} {(count == 1 ? "change" : "changes")})");
        // what stays, said once for a finding two programs share
        var clean = true;
        var said = new HashSet<(string, int, string)>();
        foreach (var result in results)
        {
            var left = result.Diagnostics.Diagnostics
                .Where(d => !d.Span.File.IsValid || said.Add((result.Sources.GetPath(d.Span.File), d.Span.Start, d.Code))).ToList();
            result.Diagnostics.Suppress(d => !left.Contains(d));
            clean &= result.Render(error);
        }
        return clean ? 0 : 1;
    }

    /// <summary>The changes one pass makes: by file, those that do not overlap one before them.</summary>
    private static Dictionary<string, List<(Replacement Fix, string Code, int Line, int Column)>> SafeFixes(
        List<CompileResult> results, Func<string, bool> ours)
    {
        var byPath = new Dictionary<string, List<(Replacement Fix, string Code, int Line, int Column)>>(StringComparer.Ordinal);
        foreach (var result in results)
            foreach (var diagnostic in result.Diagnostics.Diagnostics)
                foreach (var note in diagnostic.Notes ?? [])
                {
                    if (note.Fix is not { Applicability: Applicability.Safe } fix || !fix.Span.File.IsValid) continue;
                    if (result.Sources.OriginOf(fix.Span.File) is not null) continue;
                    var path = Path.GetFullPath(result.Sources.GetPath(fix.Span.File));
                    if (!ours(path)) continue;
                    var at = result.Sources.LocateStart(fix.Span);
                    if (!byPath.TryGetValue(path, out var list)) byPath[path] = list = [];
                    list.Add((fix, diagnostic.Code, at.Line, at.Column));
                }
        foreach (var (path, list) in byPath.ToList())
        {
            var kept = new List<(Replacement Fix, string Code, int Line, int Column)>();
            var reached = -1;
            foreach (var item in list.OrderBy(f => f.Fix.Span.Start).ThenBy(f => f.Fix.Span.End))
            {
                // one change asked twice — by two programs, by two names of one import — is made once
                if (kept.Count > 0 && kept[^1].Fix.Span == item.Fix.Span && kept[^1].Fix.Text == item.Fix.Text) continue;
                if (item.Fix.Span.Start < reached) continue;
                kept.Add(item);
                reached = Math.Max(reached, item.Fix.Span.End);
            }
            byPath[path] = kept;
        }
        return byPath;
    }

    /// <summary>The change to one file as a unified diff, three lines around each change. The lines
    /// between the first and the last change are matched as a longest common subsequence; past a
    /// size where that table would not fit, they are one change.</summary>
    private static void Diff(TextWriter output, string shown, string before, string after)
    {
        var a = before.Split('\n');
        var b = after.Split('\n');
        var head = 0;
        while (head < a.Length && head < b.Length && a[head] == b[head]) head++;
        var tail = 0;
        while (tail < a.Length - head && tail < b.Length - head && a[^(tail + 1)] == b[^(tail + 1)]) tail++;
        // the script: ' ' kept, '-' taken out, '+' put in — with the line of each side it stands at
        var script = new List<(char Op, string Text, int A, int B)>();
        for (var i = 0; i < head; i++) script.Add((' ', a[i], i, i));
        int n = a.Length - head - tail, m = b.Length - head - tail;
        if ((long)n * m <= 4_000_000)
        {
            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[head + i] == b[head + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            int x = 0, y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[head + x] == b[head + y]) { script.Add((' ', a[head + x], head + x, head + y)); x++; y++; }
                else if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1])) { script.Add(('-', a[head + x], head + x, head + y)); x++; }
                else { script.Add(('+', b[head + y], head + x, head + y)); y++; }
            }
        }
        else
        {
            for (var i = 0; i < n; i++) script.Add(('-', a[head + i], head + i, head));
            for (var j = 0; j < m; j++) script.Add(('+', b[head + j], head + n, head + j));
        }
        for (var i = 0; i < tail; i++) script.Add((' ', a[a.Length - tail + i], a.Length - tail + i, b.Length - tail + i));

        var text = new StringBuilder();
        text.Append($"--- a/{shown}\n+++ b/{shown}\n");
        var k = 0;
        while (k < script.Count)
        {
            if (script[k].Op == ' ') { k++; continue; }
            // a hunk: three kept lines before the change, three after the last change that follows
            // within six kept lines of the one before
            var start = Math.Max(0, k - 3);
            var end = k;
            while (true)
            {
                while (end < script.Count && script[end].Op != ' ') end++;
                var next = end;
                while (next < script.Count && script[next].Op == ' ') next++;
                if (next < script.Count && next - end <= 6) { end = next; continue; }
                end = Math.Min(script.Count, end + 3);
                break;
            }
            var hunk = script.GetRange(start, end - start);
            var oldCount = hunk.Count(l => l.Op != '+');
            var newCount = hunk.Count(l => l.Op != '-');
            text.Append($"@@ -{hunk[0].A + 1},{oldCount} +{hunk[0].B + 1},{newCount} @@\n");
            foreach (var line in hunk) text.Append(line.Op).Append(line.Text).Append('\n');
            k = end;
        }
        output.Write(text.ToString());
    }
}
