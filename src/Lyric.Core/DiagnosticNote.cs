namespace Lyric.Core;

/// <summary>What a note is.</summary>
public enum NoteKind
{
    /// <summary>A note: another place that belongs to the finding ("previous definition here"), or,
    /// without a location, the context said in words ("synthesized as: …"). The renderer draws the
    /// first in the source and says the second as <c>= note:</c>.</summary>
    Place,

    /// <summary>Advice in words: what to do about it.</summary>
    Help,

    /// <summary>A change to the source that would resolve it, with its <see cref="Replacement"/>.</summary>
    Suggestion,

    /// <summary>What kind of finding this is, said once beside the message ("a compiler bug").</summary>
    Category,
}

/// <summary>Whether a suggested change may be applied without a reader: <c>lyric fix</c>
/// applies the safe ones, an editor offers both.</summary>
public enum Applicability
{
    Safe,
    NeedsReview,
}

/// <summary>Text to put in place of a span.</summary>
public readonly record struct Replacement(Span Span, string Text, Applicability Applicability);

/// <summary>
/// A secondary remark under a diagnostic: another PLACE that belongs to the same finding
/// ("previous definition here"), advice, a suggested change, or the finding's category.
///
/// <para>Notes point at locations, never at literature — a message that cites a document ages in
/// two directions at once, and both have happened here before.</para>
/// </summary>
/// <param name="Location">Where the note points. <c>default</c> means the note has no place and
/// renders as text alone, the same convention <see cref="Diagnostic.Span"/> follows.</param>
public readonly record struct DiagnosticNote(Span Location, string Message)
{
    /// <summary>A note that is text alone.</summary>
    public DiagnosticNote(string message) : this(default, message) { }

    private readonly NoteKind? _kind;

    /// <summary>What the note is. Unless set, a <see cref="NoteKind.Place"/> — what every note said
    /// before kinds existed, "note:"; advice is <see cref="NoteKind.Help"/> where its reporter says so
    /// (a default of Help for a note without a place would turn every "synthesized as" and every
    /// explanation into advice).</summary>
    public NoteKind Kind
    {
        get => _kind ?? NoteKind.Place;
        init => _kind = value;
    }

    /// <summary>The change a <see cref="NoteKind.Suggestion"/> proposes; <c>null</c> otherwise.</summary>
    public Replacement? Fix { get; init; }

    /// <summary>A suggestion: <paramref name="message"/> says it, <paramref name="fix"/> is the change.</summary>
    public static DiagnosticNote Suggest(string message, Replacement fix) =>
        new(fix.Span, message) { Kind = NoteKind.Suggestion, Fix = fix };
}
