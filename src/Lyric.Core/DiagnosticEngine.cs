using System.Text;

namespace Lyric.Core;

public sealed class DiagnosticEngine(SourceManager sourceManager)
{
    private readonly SourceManager _sourceManager = sourceManager ?? throw new ArgumentNullException(nameof(sourceManager));
    
    private readonly List<Diagnostic> _diagnostics = new();
    
    public int Count => Diagnostics.Count;
    public int ErrorCount => Diagnostics.Count(d => d.Severity == Severity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == Severity.Warning);
    public bool HasErrors => ErrorCount > 0;
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    public void Report(string code, Severity severity, Span span, string message)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        Report(new Diagnostic(code, severity, span, message));
    }

    /// <summary>Reports with secondary remarks, in telling order.</summary>
    public void Report(string code, Severity severity, Span span, string message,
        params DiagnosticNote[] notes)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(message);
        Report(new Diagnostic(code, severity, span, message, notes));
    }

    public void Report(Diagnostic diagnostic)
    {
        if (_muted > 0)
        {
            // A probe counts the errors of ITS level only: a speculative check nested inside it
            // (overload resolution) refuses candidates on purpose, and those are not failures
            // of what the probe asks about.
            if (diagnostic.Severity == Severity.Error && _probe is { } probe && _muted == probe.Level) probe.Errors++;
            return;
        }
        if (_sourceManager.OriginOf(diagnostic.Span.File) is { } origin)
        {
            // Nobody wrote a synthesized file, so a hint or warning about its style is nobody's
            // to act on, and an error in it is the WRITTEN declaration's: a field whose type
            // carries no Equatable, say. The error moves to the node that asked for the
            // synthesis, named, and the line it stood on comes along so the field can be found.
            if (diagnostic.Severity is not Severity.Error) return;
            var line = _sourceManager.LocateStart(diagnostic.Span).Line;
            var notes = new List<DiagnosticNote>
            {
                new($"synthesized as: {_sourceManager.GetLineText(diagnostic.Span.File, line).Trim()}")
            };
            if (diagnostic.Notes is { } more) notes.AddRange(more);
            diagnostic = diagnostic with
            {
                Span = origin.At,
                Message = $"{origin.Label}: {diagnostic.Message}",
                Notes = notes,
            };
        }
        _diagnostics.Add(diagnostic);
    }

    private int _muted;

    /// <summary>
    /// Stops recording until the returned scope is disposed. For a SPECULATIVE check — one whose
    /// question is "would this fit", asked so that something else can be decided.
    ///
    /// <para>Overload resolution is the case it exists for: the argument types have to be known
    /// before a candidate can be chosen, and typing them against the wrong candidate would report
    /// mismatches the program does not have. The chosen candidate is checked again afterwards,
    /// for real, and reports what it finds.</para>
    ///
    /// <para>Nested scopes count, so a speculative check inside a speculative check stays quiet
    /// until both are done.</para>
    /// </summary>
    public IDisposable Mute()
    {
        _muted++;
        return new MuteScope(this);
    }

    private ProbeScope? _probe;

    /// <summary>
    /// Mutes, and COUNTS the errors that would have been reported, for a check whose question is
    /// "does this hold at all": an implicit synthesis (04 D7) is kept where its body checks and
    /// withdrawn where it does not, and nobody is told either way. One probe at a time.
    /// </summary>
    public ProbeScope Probe()
    {
        if (_probe is not null) throw new InvalidOperationException("a probe is already open");
        _muted++;
        return _probe = new ProbeScope(this, _muted);
    }

    public sealed class ProbeScope(DiagnosticEngine owner, int level) : IDisposable
    {
        internal int Level { get; } = level;
        public int Errors { get; internal set; }
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            owner._muted--;
            owner._probe = null;
        }
    }

    private sealed class MuteScope(DiagnosticEngine owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            owner._muted--;
        }
    }

    public IReadOnlyList<Diagnostic> SortedSnapshot()
    {
        var copy = new List<Diagnostic>(Diagnostics);
        copy.Sort(new DiagnosticsComparer());
        return copy;
    }

    /// <summary>
    /// A string as it may be written to a terminal: every control character replaced by ONE visible
    /// stand-in.
    ///
    /// <para>A diagnostic quotes what the program wrote — the offending source line, a string
    /// literal inside a message, a file path. All three are attacker-controlled when the source is,
    /// and all three went to stderr raw: a literal holding <c>ESC ] 0 ; … BEL</c> retitled the
    /// reader's terminal, and <c>ESC [ 31 m</c> recoloured everything after it. Compiling a file is
    /// not consenting to let it drive the terminal.</para>
    ///
    /// <para>ONE character out for one character in, deliberately. The caret line below counts
    /// columns in the original text, so an escape expanded to <c>\x1b</c> would slide the caret off
    /// its target — the diagnostic would stop lying about the terminal and start lying about the
    /// position. The Control Pictures block (U+2400…) names the character it replaces without
    /// costing a column; C1 and anything else unprintable become U+FFFD, which claims nothing.</para>
    ///
    /// <para>Tab is left alone: it drives no terminal and the alignment it produces is the one the
    /// file asked for.</para>
    /// </summary>
    public static string Printable(string text)
    {
        var needs = false;
        foreach (var c in text)
            if (IsControl(c)) { needs = true; break; }
        if (!needs) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(!IsControl(c) ? c
                : c < 0x20 ? (char)(0x2400 + c)   // the Control Pictures glyph for that very code
                : c == 0x7F ? '␡'            // the one for DEL, which has no arithmetic form
                : '�');                      // C1 and the rest: something was here
        return sb.ToString();

        static bool IsControl(char c) => c != '\t' && (c < 0x20 || c == 0x7F || (c >= 0x80 && c <= 0x9F));
    }

    public void RenderText(TextWriter output)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));

        foreach (var diagnostic in SortedSnapshot())
        {
            if (!diagnostic.Span.File.IsValid)
            {
                output.Write($"{diagnostic.Severity.ToDisplayString()}[{diagnostic.Code}]: {Printable(diagnostic.Message)}");
                output.Write("\n");
                RenderNotes(output, diagnostic);
                output.Write("\n");
                continue;
            }

            LinePosition diagPos = _sourceManager.LocateStart(diagnostic.Span);
            output.WriteLine
            (
                $"{Printable(_sourceManager.GetPath(diagnostic.Span.File))}:" +
                $"{diagPos.Line}:{diagPos.Column}: " +
                $"{diagnostic.Severity.ToDisplayString()}[{diagnostic.Code}]: {Printable(diagnostic.Message)}"
            );
            output.WriteLine(Printable(_sourceManager.GetLineText(diagnostic.Span.File, diagPos.Line)));
            for (int i = 0; i < diagPos.Column - 1; i++)
            {
                output.Write(" ");
            }

            var endPos = _sourceManager.LocateEnd(diagnostic.Span);
            for (int i = 0;
                 i < (diagPos.Line == endPos.Line ? Math.Max(diagnostic.Span.Length, 1) : 1); // Single-line with Length 0 gets one Caret
                 i++)
            {
                output.Write("^");
            }
            output.Write("\n");
            RenderNotes(output, diagnostic);
            output.Write("\n");
        }
    }

    /// <summary>
    /// The notes, indented under their diagnostic. Indented lines deliberately do not match the
    /// <c>path:line:col:</c> head format, so an editor's problem matcher sees one problem, not
    /// one per note.
    /// </summary>
    private void RenderNotes(TextWriter output, Diagnostic diagnostic)
    {
        foreach (var note in diagnostic.Notes ?? [])
        {
            if (note.Location.File.IsValid)
            {
                var position = _sourceManager.LocateStart(note.Location);
                output.WriteLine($"  note: {Printable(note.Message)} — " +
                    $"{Printable(_sourceManager.GetPath(note.Location.File))}:{position.Line}:{position.Column}");
            }
            else
            {
                output.WriteLine($"  note: {Printable(note.Message)}");
            }
        }
    }

    public void RenderJson(TextWriter output)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        
        output.Write("{\"diagnostics\":[");
        bool first = true;
        foreach (var diagnostic in SortedSnapshot())
        {
            if (first) first = false;
            else output.Write(",");

            // A diagnostic without notes serializes exactly as it always did; the key appears only
            // when there is something to put under it.
            string optionalNotesPart = "";
            if (diagnostic.Notes is { Count: > 0 } notes)
            {
                var parts = notes.Select(note =>
                    $"{{{JsonPositionPart(note.Location)}\"message\":\"{JsonEscapedStringHelper(note.Message)}\"}}");
                optionalNotesPart = $"\"notes\":[{string.Join(",", parts)}],";
            }

            output.Write
            (
                $"{{\"code\":\"{JsonEscapedStringHelper(diagnostic.Code)}\",\"severity\":\"{diagnostic.Severity.ToDisplayString()}\","
                + JsonPositionPart(diagnostic.Span)
                + optionalNotesPart
                + $"\"message\":\"{JsonEscapedStringHelper(diagnostic.Message)}\"}}"
            );
        }
        output.Write("]}");
    }

    /// <summary>The <c>file</c>/<c>start</c>/<c>end</c> keys of a location, trailing comma
    /// included — empty for a span that has no file.</summary>
    private string JsonPositionPart(Span span)
    {
        if (!span.File.IsValid) return "";

        var startPos = _sourceManager.LocateStart(span);
        var endPos = _sourceManager.LocateEnd(span);
        return $"\"file\":\"{JsonEscapedStringHelper(_sourceManager.GetPath(span.File))}\","
               + $"\"start\":{{\"line\":{startPos.Line},\"column\":{startPos.Column},\"offset\":{span.Start}}},"
               + $"\"end\":{{\"line\":{endPos.Line},\"column\":{endPos.Column},\"offset\":{span.End}}},";
    }

    private string JsonEscapedStringHelper(string text)
    {
        StringBuilder sb = new();
        
        foreach (var c in text)
        {
            if (c == '"')
                sb.Append("\\\"");
            else if (c == '\\')
                sb.Append("\\\\");
            else if (c == '\n')
                sb.Append("\\n");
            else if (c == '\r')
                sb.Append("\\r");
            else if (c == '\t')
                sb.Append("\\t");
            else if (c == '\b')
                sb.Append("\\b");
            else if (c == '\f') 
                sb.Append("\\f");
            // DEL and C1 alongside C0: JSON permits them raw, and a consumer that prints this line
            // to a terminal would then be driven by it — 0x9B is CSI in the 8-bit encoding. Escaped
            // here rather than replaced as in the text renderer, because the JSON is read by a
            // program and has to carry the byte the file really held.
            else if (c < 0x20 || c == 0x7F || (c >= 0x80 && c <= 0x9F))
                sb.Append($"\\u{(int)c:x4}");
            else sb.Append(c);
        }
        return sb.ToString();
    }
}