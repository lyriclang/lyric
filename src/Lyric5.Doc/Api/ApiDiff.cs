using System.Text;
using System.Text.Json;

namespace Lyric5.Doc.Api;

/// <summary>A change of the public surface between two versions of a package.</summary>
/// <param name="What"><c>removed</c>, <c>added</c> or <c>changed</c>.</param>
/// <param name="Old">The line before; <c>null</c> for an addition.</param>
/// <param name="New">The line after; <c>null</c> for a removal.</param>
/// <param name="Why">What makes it breaking or not, said to the reader.</param>
public sealed record ApiChange(string What, bool Breaking, string Path, string Kind, string? Old, string? New, string Why);

/// <summary>
/// The changes between two surfaces, each judged by what a user of the old one could have written:
/// whatever such code could name and no longer finds, or finds with another signature, breaks it; an
/// addition breaks it only where the language makes the user write it out — a field without a value
/// in an initializer, a variant in a <c>match</c> over every case, an interface's member without a
/// body in each conformer.
/// </summary>
public static class ApiDiff
{
    public static IReadOnlyList<ApiChange> Compare(IReadOnlyList<ApiEntry> before, IReadOnlyList<ApiEntry> after)
    {
        var changes = new List<ApiChange>();
        var old = before.GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList());
        var now = after.GroupBy(KeyOf).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var key in old.Keys.Union(now.Keys).Order(StringComparer.Ordinal))
        {
            var was = old.GetValueOrDefault(key) ?? [];
            var isNow = now.GetValueOrDefault(key) ?? [];
            // Overloads share a key: those whose line is the same on both sides did not change.
            var gone = was.Where(e => !isNow.Any(n => n.Line == e.Line)).ToList();
            var come = isNow.Where(e => !was.Any(o => o.Line == e.Line)).ToList();
            var paired = Math.Min(gone.Count, come.Count);
            for (var i = 0; i < paired; i++)
                changes.Add(Changed(gone[i], come[i]));
            foreach (var e in gone.Skip(paired))
                changes.Add(new ApiChange("removed", true, e.Path, e.Kind, e.Line, null, "code that names it no longer compiles"));
            foreach (var e in come.Skip(paired))
                changes.Add(Added(e));
        }
        return [.. changes.OrderBy(c => c.Breaking ? 0 : 1).ThenBy(c => c.Path, StringComparer.Ordinal).ThenBy(c => c.What, StringComparer.Ordinal)];
    }

    private static string KeyOf(ApiEntry e) => e.Kind == "conformance" ? $"{e.Path} :: {e.Signature}" : $"{e.Kind} {e.Path}";

    private static ApiChange Changed(ApiEntry was, ApiEntry now)
    {
        // An interface's method that keeps its signature changed only whether it has a body.
        var (breaking, why) = was.Signature == now.Signature && now.Owner == "interface"
            ? now.Default
                ? (false, "it gained a body: a conformer may leave it out")
                : (true, "its body is gone: every conformer must write it")
            : (true, "its signature changed: code written against the old one may not compile");
        return new ApiChange("changed", breaking, now.Path, now.Kind, was.Line, now.Line, why);
    }

    private static ApiChange Added(ApiEntry e)
    {
        var (breaking, why) = (e.Kind, e.Owner) switch
        {
            ("field", "struct" or "class") when !e.Default =>
                (true, "an initializer names every field without a value: one written before lacks it"),
            ("variant", "enum") => (true, "a match over every case of the enum misses it"),
            ("method", "interface") when !e.Default => (true, "every conformer must write it"),
            ("assoc", "interface") when !e.Default => (true, "every conformer must name it"),
            _ => (false, "code written before does not see it"),
        };
        return new ApiChange("added", breaking, e.Path, e.Kind, null, e.Line, why);
    }

    /// <summary>The changes as text, breaking ones first, and a closing count.</summary>
    public static string Text(IReadOnlyList<ApiChange> changes)
    {
        var sb = new StringBuilder();
        foreach (var group in new[] { true, false })
        {
            var these = changes.Where(c => c.Breaking == group).ToList();
            if (these.Count == 0) continue;
            sb.Append(group ? "breaking:\n" : "compatible:\n");
            foreach (var c in these)
            {
                sb.Append("  ").Append(c.What).Append(' ').Append(c.Path).Append(" — ").Append(c.Why).Append('\n');
                if (c.Old is { } o) sb.Append("    - ").Append(o).Append('\n');
                if (c.New is { } n) sb.Append("    + ").Append(n).Append('\n');
            }
        }
        var breaking = changes.Count(c => c.Breaking);
        sb.Append(changes.Count == 0 ? "no change to the public surface\n"
            : $"{changes.Count} change{(changes.Count == 1 ? "" : "s")}, {breaking} breaking\n");
        return sb.ToString();
    }

    /// <summary>The changes as one JSON document: <c>{"breaking": n, "changes": [...]}</c>.</summary>
    public static string Json(IReadOnlyList<ApiChange> changes)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("breaking", changes.Count(c => c.Breaking));
            w.WriteStartArray("changes");
            foreach (var c in changes)
            {
                w.WriteStartObject();
                w.WriteString("change", c.What);
                w.WriteBoolean("breaking", c.Breaking);
                w.WriteString("path", c.Path);
                w.WriteString("kind", c.Kind);
                if (c.Old is { } o) w.WriteString("old", o);
                if (c.New is { } n) w.WriteString("new", n);
                w.WriteString("why", c.Why);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }
}
