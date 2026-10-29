using Lyric.Core;
using Lyric.Core.Rendering;

namespace Lyric5.Build;

/// <summary>
/// The findings of the driver and the build that no compilation's engine holds — a usage error, a
/// manifest's refusal, a build script that failed: drawn as text the way the renderer draws a
/// compiler's (16 §1), or, under <c>--json</c>, written as the same events (16 §4). They share the
/// compiler's form, its relative paths and its colour, and they reach the JSON stream too.
/// </summary>
public static class Say
{
    /// <summary>An error without a place, its advice as <c>= help:</c> lines.</summary>
    public static void Error(TextWriter error, string code, string message, params string[] help) =>
        Emit(error, new SourceManager(), new Diagnostic(code, Severity.Error, default, message, Helps(help)));

    /// <summary>An error without a place, with notes of its own.</summary>
    public static void ErrorWithNotes(TextWriter error, string code, string message, DiagnosticNote[] notes) =>
        Emit(error, new SourceManager(), new Diagnostic(code, Severity.Error, default, message, notes.Length == 0 ? null : notes));

    /// <summary>A warning without a place.</summary>
    public static void Warning(TextWriter error, string code, string message, params string[] help) =>
        Emit(error, new SourceManager(), new Diagnostic(code, Severity.Warning, default, message, Helps(help)));

    /// <summary>A manifest's refusal at its line and column: the file is read again so the finding
    /// shows the line it is about; a file that cannot be read gives the place in words.</summary>
    public static void Manifest(TextWriter error, ManifestException refused)
    {
        var sources = new SourceManager();
        Span at = default;
        DiagnosticNote[] notes = [];
        try
        {
            var file = sources.AddFromDisk(refused.File);
            var line = Math.Clamp(refused.Line, 1, sources.LineCount(file));
            var offset = sources.OffsetAt(file, line, Math.Max(1, refused.Column));
            if (offset < 0) offset = sources.OffsetAt(file, line, 1);
            at = new Span(file, offset, offset);
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or ArgumentException)
        {
            notes = [new DiagnosticNote($"at {refused.File}:{refused.Line}:{refused.Column}")];
        }
        Emit(error, sources, new Diagnostic(refused.Code, Severity.Error, at, refused.Message, notes));
    }

    /// <summary>A finding with its place in <paramref name="sources"/> — a declaration the front end
    /// read, a manifest's line.</summary>
    public static void At(TextWriter error, SourceManager sources, Diagnostic d) => Emit(error, sources, d);

    private static DiagnosticNote[]? Helps(string[] help) =>
        help.Length == 0 ? null : [.. help.Select(h => new DiagnosticNote(h) { Kind = NoteKind.Help })];

    private static void Emit(TextWriter error, SourceManager sources, Diagnostic d)
    {
        var engine = new DiagnosticEngine(sources);
        engine.Report(d);
        engine.RenderText(error, RenderStyle.Current);
    }
}
