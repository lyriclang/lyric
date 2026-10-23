using System.Globalization;
using System.Text;

namespace Lyric.Core.Rendering;

/// <summary>
/// One diagnostic as text, after rustc:
///
/// <code>
/// error[LYR-SEM0176]: 'x' is bound in this scope already
///  --> src/main.lyr:3:9
///   |
/// 2 |     let x = 1;
///   |         - first bound here
/// 3 |     let x = 2;
///   |         ^ bound again
///   |
///   = note: …
/// </code>
///
/// <para>The head names severity, code and message; the arrow the primary place; the gutter the
/// line numbers of every labelled line of a file together, the primary marked <c>^</c>, the
/// secondary <c>-</c>, each with its text. A label in another file gets a <c>:::</c> block of its
/// own. A note without a place is a <c>= note:</c> line, advice a <c>= help:</c> line; a note with
/// a place is a block of its own (<c>note:</c>, arrow, snippet), and so is a suggestion, with the
/// line as it would read.</para>
///
/// <para>Columns: a tab is four, a wide character two, a combining one none, a control character
/// one stand-in (<see cref="DiagnosticEngine.Printable"/>) — the marks stand under what they mark.
/// The arrow's column counts characters as the editor does. A line wider than
/// <see cref="RenderStyle.Width"/> is cut around its marks, with <c>…</c> where it was cut.</para>
/// </summary>
public sealed class TextRenderer(SourceManager sources, RenderStyle style)
{
    private const string Bold = "1";
    private const string Red = "1;91";
    private const string Yellow = "1;93";
    private const string Green = "1;92";
    private const string Cyan = "1;96";
    private const string Blue = "1;94";

    /// <summary>The context kept left of the first mark when a long line is cut.</summary>
    private const int CutMargin = 10;

    /// <summary>Writes <paramref name="diagnostic"/>, a blank line after it.</summary>
    public void Render(TextWriter output, Diagnostic diagnostic)
    {
        var level = diagnostic.Severity switch
        {
            Severity.Error => ("error", Red),
            Severity.Warning => ("warning", Yellow),
            _ => ("hint", Cyan),
        };
        output.Write(Paint($"{level.Item1}[{diagnostic.Code}]", level.Item2));
        output.Write(Paint(": " + DiagnosticEngine.Printable(diagnostic.Message), Bold));
        output.Write('\n');

        var notes = diagnostic.Notes ?? [];
        var gutter = GutterWidth(diagnostic);
        var drewSnippet = false;
        if (diagnostic.Span.File.IsValid)
        {
            var labels = new List<Label> { new(diagnostic.Span, diagnostic.PrimaryLabel, true) };
            foreach (var secondary in diagnostic.Labels ?? [])
                if (secondary.Span.File.IsValid) labels.Add(new(secondary.Span, secondary.Label, false));
            DrawPlaces(output, labels, gutter, level.Item2, primaryFile: diagnostic.Span.File, arrowAt: diagnostic.Span);
            drewSnippet = true;
        }

        // in telling order: a chain of notes (a synthesis inside an expansion) reads innermost first
        foreach (var note in notes)
        {
            if (note.Fix is { } fix) { DrawSuggestion(output, note, fix, gutter); drewSnippet = true; }
            else if (note.Location.File.IsValid)
            {
                var (word, colour) = WordOf(note.Kind);
                output.Write(Paint(word, colour) + Paint(": " + DiagnosticEngine.Printable(note.Message), Bold) + "\n");
                DrawPlaces(output, [new(note.Location, null, true)], gutter, colour, note.Location.File, note.Location);
                drewSnippet = true;
            }
            else
            {
                if (drewSnippet) output.Write(Paint(new string(' ', gutter + 1) + "|", Blue) + "\n");
                drewSnippet = false;
                var (word, _) = WordOf(note.Kind);
                output.Write(Paint(new string(' ', gutter + 1) + "=", Blue) + " " + Paint(word + ":", Bold) + " "
                             + DiagnosticEngine.Printable(note.Message) + "\n");
            }
        }
        output.Write('\n');
    }

    private static (string Word, string Colour) WordOf(NoteKind kind) =>
        kind is NoteKind.Help or NoteKind.Suggestion ? ("help", Cyan) : ("note", Green);

    private readonly record struct Label(Span Span, string? Text, bool Primary);

    /// <summary>A label on one line: the characters it covers there, and its text where the
    /// label ends on this line.</summary>
    private readonly record struct Mark(int Line, int From, int To, bool Primary, string? Text);

    /// <summary>The snippet blocks of <paramref name="labels"/>: the primary file's under the arrow,
    /// every other file's under a <c>:::</c>.</summary>
    private void DrawPlaces(TextWriter output, List<Label> labels, int gutter, string primaryColour, FileId primaryFile, Span arrowAt)
    {
        var files = labels.Select(l => l.Span.File).Distinct().OrderBy(f => f == primaryFile ? 0 : 1).ThenBy(f => f.Value);
        foreach (var file in files)
        {
            var here = labels.Where(l => l.Span.File == file).ToList();
            var at = file == primaryFile ? arrowAt : here[0].Span;
            var position = sources.LocateStart(Clamp(at));
            var arrow = file == primaryFile ? "-->" : ":::";
            output.Write(new string(' ', gutter) + Paint(arrow, Blue) + " "
                         + DiagnosticEngine.Printable(style.Display(sources.GetPath(file)))
                         + $":{position.Line}:{position.Column}\n");
            output.Write(Paint(new string(' ', gutter + 1) + "|", Blue) + "\n");
            DrawLines(output, file, here.SelectMany(MarksOf).ToList(), gutter, primaryColour);
        }
    }

    /// <summary>The marks a label makes: one on its line, or, across lines, one from its start to
    /// the end of the first line and one from the first word of the last line to its end — the
    /// text there.</summary>
    private IEnumerable<Mark> MarksOf(Label label)
    {
        var span = Clamp(label.Span);
        var start = sources.LocateStart(span);
        var end = sources.LocateEnd(span);
        // a span that ends right after a line break ends on the line before
        if (end.Line > start.Line && end.Column == 1)
            end = new LinePosition(end.Line - 1, sources.GetLineText(span.File, end.Line - 1).Length + 1);
        if (end.Line == start.Line)
        {
            yield return new Mark(start.Line, start.Column - 1, Math.Max(end.Column - 1, start.Column), label.Primary, label.Text);
            yield break;
        }
        var first = sources.GetLineText(span.File, start.Line);
        yield return new Mark(start.Line, start.Column - 1, Math.Max(first.Length, start.Column), label.Primary, null);
        var last = sources.GetLineText(span.File, end.Line);
        var indent = last.Length - last.TrimStart().Length;
        yield return new Mark(end.Line, Math.Min(indent, end.Column - 1), Math.Max(end.Column - 1, Math.Min(indent, end.Column - 1) + 1),
            label.Primary, label.Text);
    }

    private Span Clamp(Span span)
    {
        var length = sources.GetText(span.File).Length;
        var start = Math.Clamp(span.Start, 0, length);
        return new Span(span.File, start, Math.Clamp(span.End, start, length));
    }

    /// <summary>The lines that carry marks, in order: a gap of one line is shown, a longer one is
    /// <c>...</c>.</summary>
    private void DrawLines(TextWriter output, FileId file, List<Mark> marks, int gutter, string primaryColour)
    {
        var lines = marks.Select(m => m.Line).Distinct().Order().ToList();
        int? previous = null;
        foreach (var line in lines)
        {
            if (previous is { } p && line - p == 2) DrawLine(output, file, p + 1, [], gutter, primaryColour);
            else if (previous is { } q && line - q > 2) output.Write(Paint("...", Blue) + "\n");
            DrawLine(output, file, line, marks.Where(m => m.Line == line).ToList(), gutter, primaryColour);
            previous = line;
        }
    }

    private void DrawLine(TextWriter output, FileId file, int line, List<Mark> marks, int gutter, string primaryColour)
    {
        var layout = Layout.Of(sources.GetLineText(file, line));
        // where the marks stand, in display columns
        var spans = marks.Select(m =>
        {
            var from = layout.Column(m.From);
            return (From: from, To: Math.Max(layout.Column(m.To), from + 1), m.Primary, m.Text);
        }).ToList();

        // the window of the line that is shown: all of it, or the part around the marks
        var (left, right) = (0, layout.Width);
        if (layout.Width > style.Width)
        {
            var lo = spans.Count > 0 ? spans.Min(s => s.From) : 0;
            left = Math.Max(0, Math.Min(lo - CutMargin, layout.Width - style.Width));
            right = Math.Min(layout.Width, left + style.Width);
        }
        var shift = left > 0 ? 1 : 0;   // the '…' in front takes one column

        var number = line.ToString(CultureInfo.InvariantCulture).PadLeft(gutter);
        var text = layout.Slice(left, right);
        output.Write(Paint(number + " |", Blue) + (text.Length > 0 || left > 0 ? " " : "")
                     + (left > 0 ? "…" : "") + text + (right < layout.Width ? "…" : "") + "\n");
        if (spans.Count == 0) return;

        var shown = spans
            .Select(s => (From: Math.Clamp(s.From - left, 0, right - left) + shift,
                          To: Math.Clamp(s.To - left, 0, right - left) + shift, s.Primary, s.Text))
            .Select(s => s with { To = Math.Max(s.To, s.From + 1) })
            .ToList();

        // the marks: secondary first, the primary over them
        var width = shown.Max(s => s.To);
        var cells = new char[width];
        var primary = new bool[width];
        Array.Fill(cells, ' ');
        foreach (var s in shown.Where(s => !s.Primary))
            for (var c = s.From; c < s.To; c++) cells[c] = '-';
        foreach (var s in shown.Where(s => s.Primary))
            for (var c = s.From; c < s.To; c++) { cells[c] = '^'; primary[c] = true; }

        var texts = shown.Where(s => !string.IsNullOrEmpty(s.Text)).OrderBy(s => s.From).ToList();
        // the rightmost text stands beside the marks when nothing reaches past its own
        var inline = texts.Count > 0 && texts[^1].To == width ? texts[^1] : default((int From, int To, bool Primary, string? Text)?);
        var markLine = new StringBuilder();
        for (var c = 0; c < width;)
        {
            var run = c;
            while (run < width && cells[run] == cells[c] && primary[run] == primary[c]) run++;
            var piece = new string(cells[c], run - c);
            markLine.Append(cells[c] == ' ' ? piece : Paint(piece, primary[c] ? primaryColour : Blue));
            c = run;
        }
        if (inline is { } beside)
            markLine.Append(' ').Append(Paint(DiagnosticEngine.Printable(beside.Text!), beside.Primary ? primaryColour : Blue));
        output.Write(Paint(new string(' ', gutter + 1) + "|", Blue) + " " + markLine.ToString().TrimEnd() + "\n");

        // the other texts, right to left, each under a connector from its mark
        var stacked = inline is null ? texts : texts.Take(texts.Count - 1).ToList();
        for (var i = stacked.Count - 1; i >= 0; i--)
        {
            output.Write(Paint(new string(' ', gutter + 1) + "|", Blue) + " " + Connectors(stacked, i + 1, null) + "\n");
            output.Write(Paint(new string(' ', gutter + 1) + "|", Blue) + " " + Connectors(stacked, i, stacked[i]) + "\n");
        }
    }

    /// <summary>A line of <c>|</c> under the first <paramref name="count"/> texts' marks, and the
    /// text of <paramref name="last"/> at its own column.</summary>
    private string Connectors(List<(int From, int To, bool Primary, string? Text)> texts, int count,
        (int From, int To, bool Primary, string? Text)? last)
    {
        var sb = new StringBuilder();
        var column = 0;
        for (var i = 0; i < count; i++)
        {
            sb.Append(' ', texts[i].From - column).Append(Paint("|", Blue));
            column = texts[i].From + 1;
        }
        if (last is { } at)
            sb.Append(' ', Math.Max(0, at.From - column)).Append(DiagnosticEngine.Printable(at.Text!));
        return sb.ToString().TrimEnd();
    }

    /// <summary>A suggestion: the line as it would read, the new text marked <c>~</c>, or, for a
    /// change across lines, the text in the <c>= help:</c> line.</summary>
    private void DrawSuggestion(TextWriter output, DiagnosticNote note, Replacement fix, int gutter)
    {
        var span = Clamp(fix.Span);
        var start = sources.LocateStart(span);
        var end = sources.LocateEnd(span);
        if (start.Line != end.Line || fix.Text.Contains('\n'))
        {
            output.Write(Paint(new string(' ', gutter + 1) + "=", Blue) + " " + Paint("help:", Bold) + " "
                         + DiagnosticEngine.Printable(note.Message)
                         + (fix.Text.Length == 0 ? "" : ": `" + DiagnosticEngine.Printable(fix.Text) + "`") + "\n");
            return;
        }
        output.Write(Paint("help", Cyan) + Paint(": " + DiagnosticEngine.Printable(note.Message), Bold) + "\n");
        output.Write(new string(' ', gutter) + Paint("-->", Blue) + " "
                     + DiagnosticEngine.Printable(style.Display(sources.GetPath(span.File))) + $":{start.Line}:{start.Column}\n");
        output.Write(Paint(new string(' ', gutter + 1) + "|", Blue) + "\n");
        var old = sources.GetLineText(span.File, start.Line);
        var changed = old[..(start.Column - 1)] + fix.Text + old[(end.Column - 1)..];
        var layout = Layout.Of(changed);
        output.Write(Paint(start.Line.ToString(CultureInfo.InvariantCulture).PadLeft(gutter) + " |", Blue)
                     + (changed.Length > 0 ? " " + layout.Slice(0, layout.Width) : "") + "\n");
        var from = layout.Column(start.Column - 1);
        var to = layout.Column(start.Column - 1 + fix.Text.Length);
        // a deletion marks where the text was taken out
        var marks = to > from ? new string('~', to - from) : "-";
        output.Write(Paint(new string(' ', gutter + 1) + "|", Blue) + " " + new string(' ', from) + Paint(marks, Cyan) + "\n");
    }

    /// <summary>The width of the line numbers: the longest of every line the diagnostic shows.</summary>
    private int GutterWidth(Diagnostic diagnostic)
    {
        var spans = new List<Span>();
        if (diagnostic.Span.File.IsValid) spans.Add(diagnostic.Span);
        foreach (var label in diagnostic.Labels ?? []) if (label.Span.File.IsValid) spans.Add(label.Span);
        foreach (var note in diagnostic.Notes ?? [])
        {
            if (note.Fix is { } fix && fix.Span.File.IsValid) spans.Add(fix.Span);
            else if (note.Location.File.IsValid) spans.Add(note.Location);
        }
        var widest = spans.Count == 0 ? 1 : spans.Max(s => sources.LocateEnd(Clamp(s)).Line);
        return Math.Max(1, widest.ToString(CultureInfo.InvariantCulture).Length);
    }

    private string Paint(string text, string sgr) => style.Color ? $"\u001b[{sgr}m{text}\u001b[0m" : text;

    /// <summary>A source line as shown, and where each of its characters stands.</summary>
    private sealed class Layout
    {
        private readonly List<string> _pieces = [];
        private readonly List<int> _columns = [];

        /// <summary>The columns the whole line takes.</summary>
        public int Width { get; private set; }

        public static Layout Of(string line)
        {
            var layout = new Layout();
            for (var i = 0; i < line.Length; i++)
            {
                layout._columns.Add(layout.Width);
                var c = line[i];
                if (c == '\t') { layout.Add("    ", 4); continue; }
                if (char.IsHighSurrogate(c) && i + 1 < line.Length && char.IsLowSurrogate(line[i + 1]))
                {
                    var rune = char.ConvertToUtf32(c, line[i + 1]);
                    layout.Add(line.Substring(i, 2), WidthOf(rune));
                    i++;
                    layout._columns.Add(layout.Width);   // the low half stands where the pair ends
                    layout._pieces.Add("");
                    continue;
                }
                var shown = DiagnosticEngine.Printable(c.ToString());
                layout.Add(shown, shown[0] != c ? 1 : WidthOf(c));
            }
            layout._columns.Add(layout.Width);
            return layout;
        }

        private void Add(string piece, int width)
        {
            _pieces.Add(piece);
            Width += width;
        }

        /// <summary>The display column at which character <paramref name="index"/> begins; past the
        /// end, the end plus how far past.</summary>
        public int Column(int index) =>
            index < _columns.Count ? _columns[Math.Max(0, index)] : Width + (index - (_columns.Count - 1));

        /// <summary>The characters that stand wholly within columns [<paramref name="left"/>,
        /// <paramref name="right"/>).</summary>
        public string Slice(int left, int right)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < _pieces.Count; i++)
                if (_columns[i] >= left && _columns[i + 1] <= right) sb.Append(_pieces[i]);
            return sb.ToString();
        }

        private static int WidthOf(int rune)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return 0;
            return rune is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0x303E or >= 0x3041 and <= 0x33FF
                or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xA000 and <= 0xA4CF
                or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF or >= 0xFE30 and <= 0xFE4F
                or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6 or >= 0x1F300 and <= 0x1F64F
                or >= 0x1F900 and <= 0x1F9FF or >= 0x20000 and <= 0x3FFFD
                ? 2 : 1;
        }
    }
}
