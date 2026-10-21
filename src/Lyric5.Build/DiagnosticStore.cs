using System.Text.Json;
using Lyric.Core;

namespace Lyric5.Build;

/// <summary>
/// The findings of a compilation kept beside its cached C, so that a build that finds the C up to
/// date says them again as the build that made it did (spec 14 §1.2): the records, their places as
/// a file's path and offsets, read back over the same files — which the cache key says are the
/// same — and rendered afresh, in the colour, the form and the count of the build that asks.
/// </summary>
public static class DiagnosticStore
{
    /// <summary>Writes the findings of <paramref name="diagnostics"/> to <paramref name="path"/>;
    /// <c>false</c>, and nothing written, where one stands in a file that is not on disk — a
    /// synthesized one —, which could not be read back.</summary>
    public static bool Save(string path, SourceManager sources, IReadOnlyList<Diagnostic> diagnostics)
    {
        var files = new List<string>();
        var index = new Dictionary<FileId, int>();
        bool Place(Span span)
        {
            if (!span.File.IsValid || index.ContainsKey(span.File)) return true;
            if (sources.OriginOf(span.File) is not null) return false;
            var file = sources.GetPath(span.File);
            if (!File.Exists(file)) return false;
            index[span.File] = files.Count;
            files.Add(Path.GetFullPath(file));
            return true;
        }
        foreach (var d in diagnostics)
        {
            if (!Place(d.Span)) return false;
            foreach (var label in d.Labels ?? []) if (!Place(label.Span)) return false;
            foreach (var note in d.Notes ?? [])
                if (!Place(note.Location) || note.Fix is { } fix && !Place(fix.Span)) return false;
        }

        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            void SpanOf(string name, Span span)
            {
                w.WriteStartArray(name);
                w.WriteNumberValue(span.File.IsValid ? index[span.File] : -1);
                w.WriteNumberValue(span.Start);
                w.WriteNumberValue(span.End);
                w.WriteEndArray();
            }
            w.WriteStartObject();
            w.WriteStartArray("files");
            foreach (var file in files) w.WriteStringValue(file);
            w.WriteEndArray();
            w.WriteStartArray("diagnostics");
            foreach (var d in diagnostics)
            {
                w.WriteStartObject();
                w.WriteString("code", d.Code);
                w.WriteString("severity", d.Severity.ToString());
                SpanOf("span", d.Span);
                w.WriteString("message", d.Message);
                if (d.PrimaryLabel is { } primary) w.WriteString("label", primary);
                w.WriteStartArray("labels");
                foreach (var label in d.Labels ?? [])
                {
                    w.WriteStartObject();
                    SpanOf("span", label.Span);
                    w.WriteString("label", label.Label);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartArray("notes");
                foreach (var note in d.Notes ?? [])
                {
                    w.WriteStartObject();
                    w.WriteString("kind", note.Kind.ToString());
                    SpanOf("span", note.Location);
                    w.WriteString("message", note.Message);
                    if (note.Fix is { } fix)
                    {
                        SpanOf("fix", fix.Span);
                        w.WriteString("text", fix.Text);
                        w.WriteString("applicability", fix.Applicability.ToString());
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        File.WriteAllBytes(path, buffer.ToArray());
        return true;
    }

    /// <summary>Says again what <see cref="Save"/> kept, as the build now in hand renders; nothing
    /// where there is nothing kept or a file it names is gone.</summary>
    public static void Replay(string path, TextWriter error)
    {
        if (!File.Exists(path)) return;
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        var sources = new SourceManager { Lyric5 = true };
        var ids = new List<FileId>();
        foreach (var file in root.GetProperty("files").EnumerateArray())
        {
            if (!File.Exists(file.GetString())) return;
            ids.Add(sources.AddFromDisk(file.GetString()!));
        }
        Span SpanOf(JsonElement at)
        {
            var file = at[0].GetInt32();
            return new Span(file < 0 ? FileId.None : ids[file], at[1].GetInt32(), at[2].GetInt32());
        }
        var engine = new DiagnosticEngine(sources);
        foreach (var d in root.GetProperty("diagnostics").EnumerateArray())
        {
            var labels = d.GetProperty("labels").EnumerateArray()
                .Select(l => new LabeledSpan(SpanOf(l.GetProperty("span")), l.GetProperty("label").GetString()!)).ToList();
            var notes = d.GetProperty("notes").EnumerateArray().Select(n =>
            {
                var note = new DiagnosticNote(SpanOf(n.GetProperty("span")), n.GetProperty("message").GetString()!)
                {
                    Kind = Enum.Parse<NoteKind>(n.GetProperty("kind").GetString()!),
                };
                return n.TryGetProperty("fix", out var fix)
                    ? note with
                    {
                        Fix = new Replacement(SpanOf(fix), n.GetProperty("text").GetString()!,
                            Enum.Parse<Applicability>(n.GetProperty("applicability").GetString()!)),
                    }
                    : note;
            }).ToList();
            engine.Report(new Diagnostic(d.GetProperty("code").GetString()!, Enum.Parse<Severity>(d.GetProperty("severity").GetString()!),
                SpanOf(d.GetProperty("span")), d.GetProperty("message").GetString()!, notes.Count == 0 ? null : notes)
            {
                PrimaryLabel = d.TryGetProperty("label", out var primary) ? primary.GetString() : null,
                Labels = labels.Count == 0 ? null : labels,
            });
        }
        engine.RenderText(error);
    }
}
