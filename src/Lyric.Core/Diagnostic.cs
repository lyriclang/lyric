namespace Lyric.Core;

/// <summary>
/// One finding (design/v5/spec/11 G1): a code, its severity, the primary place, the message, and
/// what helps to read it — labelled secondary places and notes of a kind. The primary place may
/// carry a short label of its own ("expected 'int' here"); a secondary one always does ("first
/// declared here"). The renderer draws all of them in the source; the editor shows the secondary
/// ones as related information.
/// </summary>
/// <param name="Notes">Secondary remarks, in telling order. <c>null</c> and empty mean the same
/// thing; <c>null</c> keeps the common case allocation-free.</param>
public readonly record struct Diagnostic(
    string Code,
    Severity Severity,
    Span Span,
    string Message,
    IReadOnlyList<DiagnosticNote>? Notes = null
    )
{
    /// <summary>The short text under the primary place, or <c>null</c> for none.</summary>
    public string? PrimaryLabel { get; init; }

    /// <summary>The secondary places, each with its label, in telling order; <c>null</c> for none.</summary>
    public IReadOnlyList<LabeledSpan>? Labels { get; init; }

    /// <summary>Equal when every part is, the lists by their elements: two reports of one finding at
    /// one place are one.</summary>
    public bool Equals(Diagnostic other) =>
        Code == other.Code && Severity == other.Severity && Span == other.Span && Message == other.Message
        && PrimaryLabel == other.PrimaryLabel
        && (Notes ?? []).SequenceEqual(other.Notes ?? [])
        && (Labels ?? []).SequenceEqual(other.Labels ?? []);

    public override int GetHashCode() => HashCode.Combine(Code, Severity, Span, Message);
}

/// <summary>A secondary place of a diagnostic and what it says there.</summary>
public readonly record struct LabeledSpan(Span Span, string Label);
