namespace Lyric5.Build;

/// <summary>
/// Two texts as lines kept, taken out and put in: what <c>lyric fix --dry-run</c> and
/// <c>lyric fmt --check --diff</c> print, and the edits an editor's formatting sends.
/// </summary>
public static class LineDiff
{
    /// <summary>One line of the script: <c>' '</c> kept, <c>'-'</c> taken out, <c>'+'</c> put in,
    /// with the index of each side it stands at.</summary>
    public readonly record struct Step(char Op, string Text, int A, int B);

    /// <summary>
    /// The script from <paramref name="a"/> to <paramref name="b"/>: the common head and tail kept,
    /// the middle by the longest common subsequence where it is small enough to count — every
    /// line out and in where it is not.
    /// </summary>
    public static List<Step> Script(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var head = 0;
        while (head < a.Count && head < b.Count && a[head] == b[head]) head++;
        var tail = 0;
        while (tail < a.Count - head && tail < b.Count - head && a[a.Count - 1 - tail] == b[b.Count - 1 - tail]) tail++;
        var script = new List<Step>();
        for (var i = 0; i < head; i++) script.Add(new(' ', a[i], i, i));
        int n = a.Count - head - tail, m = b.Count - head - tail;
        if ((long)n * m <= 4_000_000)
        {
            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[head + i] == b[head + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            int x = 0, y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[head + x] == b[head + y]) { script.Add(new(' ', a[head + x], head + x, head + y)); x++; y++; }
                else if (x < n && (y == m || lcs[x + 1, y] >= lcs[x, y + 1])) { script.Add(new('-', a[head + x], head + x, head + y)); x++; }
                else { script.Add(new('+', b[head + y], head + x, head + y)); y++; }
            }
        }
        else if (Myers(a, b, head, n, m, MaxChanges) is { } middle)
        {
            script.AddRange(middle);
        }
        else
        {
            for (var i = 0; i < n; i++) script.Add(new('-', a[head + i], head + i, head));
            for (var j = 0; j < m; j++) script.Add(new('+', b[head + j], head + n, head + j));
        }
        for (var i = 0; i < tail; i++) script.Add(new(' ', a[a.Count - tail + i], a.Count - tail + i, b.Count - tail + i));
        return script;
    }

    /// <summary>The changes past which a middle too large for the table is one change: the
    /// greedy search keeps a row per change.</summary>
    private const int MaxChanges = 4000;

    /// <summary>
    /// The middle by Myers's greedy search — the fewest lines out and in, in time and space that
    /// grow with the changes rather than with the lines: a large file with a few changed lines.
    /// <c>null</c> where it takes more than <paramref name="max"/> changes.
    /// </summary>
    private static List<Step>? Myers(IReadOnlyList<string> a, IReadOnlyList<string> b, int head, int n, int m, int max)
    {
        var offset = max + 1;
        var v = new int[2 * offset + 1];
        var trace = new List<int[]>();
        var found = -1;
        for (var d = 0; d <= max && found < 0; d++)
        {
            // the row before this change, from diagonal -d-1 to d+1
            trace.Add(v[(offset - d - 1)..(offset + d + 2)]);
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]) ? v[offset + k + 1] : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[head + x] == b[head + y]) { x++; y++; }
                v[offset + k] = x;
                if (x >= n && y >= m) { found = d; break; }
            }
        }
        if (found < 0) return null;

        // back from the end, a change and the kept lines after it per row
        var steps = new List<Step>();
        int px = n, py = m;
        for (var d = found; d > 0; d--)
        {
            var row = trace[d];
            var k = px - py;
            var down = k == -d || (k != d && row[k - 1 + d + 1] < row[k + 1 + d + 1]);
            var prevK = down ? k + 1 : k - 1;
            var prevX = row[prevK + d + 1];
            var prevY = prevX - prevK;
            while (px > prevX + (down ? 0 : 1) && py > prevY + (down ? 1 : 0))
            {
                px--; py--;
                steps.Add(new(' ', a[head + px], head + px, head + py));
            }
            if (down) { py--; steps.Add(new('+', b[head + py], head + px, head + py)); }
            else { px--; steps.Add(new('-', a[head + px], head + px, head + py)); }
        }
        while (px > 0 && py > 0) { px--; py--; steps.Add(new(' ', a[head + px], head + px, head + py)); }
        steps.Reverse();
        return steps;
    }
}
