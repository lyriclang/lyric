using System.Text;
using System.Text.Json;

namespace Lyric.Core.Rendering;

/// <summary>
/// The JSON form (design/v5/spec/11 G8, C6; 13 M12a P7; spec 16 §4): one line per event, each an
/// object with its <c>type</c> and the schema's <c>version</c> — <c>diagnostic</c>, <c>artifact</c>,
/// <c>test</c>, <c>summary</c>, <c>ice</c>. A reader takes a line at a time while the command runs;
/// it never waits for a closing bracket. The lines go to the standard output, where nothing else is
/// written while the form is asked for — but the program <c>lyric run</c> runs, which writes its own.
/// </summary>
public sealed class EventWriter(TextWriter output)
{
    /// <summary>The schema's version: a reader refuses a line of a version it does not know.</summary>
    public const int Version = 1;

    private readonly Lock _lock = new();

    /// <summary>The errors and the warnings said so far — what the summary counts.</summary>
    public int Errors { get; private set; }
    public int Warnings { get; private set; }

    /// <summary>A finding: the record (16 §1) with its places as file, line, column and offset, and
    /// its text form, as the text renderer would draw it without colour.</summary>
    public void Diagnostic(SourceManager sources, Diagnostic d, RenderStyle style)
    {
        var rendered = new StringWriter { NewLine = "\n" };
        new TextRenderer(sources, style with { Color = false }).Render(rendered, d);
        Line("diagnostic", w =>
        {
            w.WriteString("code", d.Code);
            w.WriteString("severity", d.Severity.ToDisplayString());
            w.WriteString("message", d.Message);
            w.WritePropertyName("span");
            Span(w, sources, style, d.Span);
            if (d.PrimaryLabel is { } label) w.WriteString("label", label);
            w.WriteStartArray("labels");
            foreach (var l in d.Labels ?? [])
            {
                w.WriteStartObject();
                w.WritePropertyName("span");
                Span(w, sources, style, l.Span);
                w.WriteString("label", l.Label);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("notes");
            foreach (var note in d.Notes ?? [])
            {
                w.WriteStartObject();
                w.WriteString("kind", note.Kind switch
                {
                    NoteKind.Help => "help",
                    NoteKind.Suggestion => "suggestion",
                    NoteKind.Category => "category",
                    _ => "note",
                });
                w.WriteString("message", note.Message);
                w.WritePropertyName("span");
                Span(w, sources, style, note.Location);
                if (note.Fix is { } fix)
                {
                    w.WriteStartObject("fix");
                    w.WritePropertyName("span");
                    Span(w, sources, style, fix.Span);
                    w.WriteString("text", fix.Text);
                    w.WriteString("applicability", fix.Applicability == Applicability.Safe ? "safe" : "needs-review");
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("rendered", rendered.ToString());
        });
        lock (_lock)
        {
            if (d.Severity == Severity.Error) Errors++;
            else if (d.Severity == Severity.Warning) Warnings++;
        }
    }

    /// <summary>What a build wrote: a program's binary, a library's archive.</summary>
    public void Artifact(string kind, string name, string path) => Line("artifact", w =>
    {
        w.WriteString("kind", kind);
        w.WriteString("name", name);
        w.WriteString("path", path);
    });

    /// <summary>A change <c>lyric fix</c> made (16 §6): the file, where the change begins, the code of
    /// the finding it answers, and the text put in.</summary>
    public void Fixed(string path, int line, int column, string code, string text) => Line("fix", w =>
    {
        w.WriteString("path", path);
        w.WriteNumber("line", line);
        w.WriteNumber("column", column);
        w.WriteString("code", code);
        w.WriteString("text", text);
    });

    /// <summary>A line the test program wrote in the form, passed on as it is: <c>lyric test</c>'s
    /// own events come from the program that runs the tests (std.test).</summary>
    public void Raw(string line)
    {
        lock (_lock) output.Write(line.TrimEnd('\r', '\n') + "\n");
    }

    /// <summary>A line a program the command runs wrote that is no event — a test's own
    /// <c>println</c> —, with the stream it came on; the JSON form stays one object a line.</summary>
    public void Output(string stream, string text) => Line("output", w =>
    {
        w.WriteString("stream", stream);
        w.WriteString("text", text);
    });

    private bool _summarized;

    /// <summary>The count that closes a command — once: <c>run</c> says it before its program runs.</summary>
    public void Summary()
    {
        lock (_lock)
        {
            if (_summarized) return;
            _summarized = true;
        }
        Line("summary", w =>
        {
            w.WriteNumber("errors", Errors);
            w.WriteNumber("warnings", Warnings);
        });
    }

    /// <summary>The toolchain's own failure (11 G4): a bug of lyric, never of the program.</summary>
    public void Ice(string code, string message, string? place, string? trace) => Line("ice", w =>
    {
        w.WriteString("code", code);
        w.WriteString("message", message);
        if (place is not null) w.WriteString("while", place);
        if (trace is not null) w.WriteString("trace", trace);
    });

    private void Line(string type, Action<Utf8JsonWriter> body)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("type", type);
            w.WriteNumber("version", Version);
            body(w);
            w.WriteEndObject();
        }
        var line = Encoding.UTF8.GetString(buffer.ToArray());
        lock (_lock)
        {
            output.Write(line + "\n");
            output.Flush();
        }
    }

    /// <summary>A place as the form writes it — the file as the text says it (relative to the
    /// package, 16 §1.5), start and end with line, column and offset —, or <c>null</c> for none.</summary>
    private static void Span(Utf8JsonWriter w, SourceManager sources, RenderStyle style, Span span)
    {
        if (!span.File.IsValid) { w.WriteNullValue(); return; }
        var length = sources.GetText(span.File).Length;
        var clamped = new Span(span.File, Math.Clamp(span.Start, 0, length), Math.Clamp(span.End, Math.Clamp(span.Start, 0, length), length));
        var start = sources.LocateStart(clamped);
        var end = sources.LocateEnd(clamped);
        w.WriteStartObject();
        w.WriteString("file", style.Display(sources.GetPath(span.File)));
        w.WriteStartObject("start");
        w.WriteNumber("line", start.Line);
        w.WriteNumber("column", start.Column);
        w.WriteNumber("offset", clamped.Start);
        w.WriteEndObject();
        w.WriteStartObject("end");
        w.WriteNumber("line", end.Line);
        w.WriteNumber("column", end.Column);
        w.WriteNumber("offset", clamped.End);
        w.WriteEndObject();
        w.WriteEndObject();
    }
}
