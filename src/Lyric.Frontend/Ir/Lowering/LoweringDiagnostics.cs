using Lyric.Core;

namespace Lyric.Ir.Lowering;

/// <summary>
/// The diagnostic codes of the lowering (`LYR-IR####`).
///
/// <para>DELIBERATELY EXACTLY ONE CODE. The temptation would be to assign one per missing construct
/// ("LYR-IR0002: lambdas"), but codes are stable identifiers and the gaps are temporary. A code that
/// disappears once lambdas are lowered was never one. What is stable is the CATEGORY: "this compiler
/// build cannot do that yet". Which construct is meant stands in the message.</para>
///
/// <para><c>LYR-IR0002..0010</c> stay free for real, permanent lowering errors. Most of what the
/// lowering could reject has already been caught by the sema.</para>
/// </summary>
internal static class LoweringDiagnostics
{
    /// <summary>A construct or type this compiler build cannot lower yet.</summary>
    public const string NotSupported = "LYR-IR0001";

    /// <summary>
    /// The category, carried as a note rather than as a clause. A message names the construct and
    /// often says what to do about it; appending "is not supported by this compiler version yet"
    /// to such a sentence splices two together — "initializer omits field 'wood', which has no
    /// default is not supported by this compiler version yet" — and a reader has to take it apart
    /// again. On its own line it reads as the aside it is.
    /// </summary>
    private const string Category = "this compiler version cannot lower it yet";

    /// <summary>
    /// The note for a refusal that is NOT a gap: an optional-shaped operation on a value that,
    /// after monomorphization, has no optional in it. No compiler version will lower that, because
    /// there is nothing to lower — telling the reader to wait for a newer one sends them looking
    /// for a release that will never help.
    /// </summary>
    public const string NeverNull = "a value of this type is never null";

    /// <summary>Reports the one lowering code. Every site goes through here, so the note hangs in
    /// a single place and no message has to carry the category in its own text.
    ///
    /// <para><paramref name="note"/> overrides the default category for a site where it does not
    /// apply. The CODE stays one — codes are stable identifiers — and only the aside changes.
    /// </para></summary>
    public static void ReportUnsupported(DiagnosticEngine de, Span span, string message,
        string? note = null) =>
        de.Report(NotSupported, Severity.Error, span, message,
            new DiagnosticNote(note ?? Category));
}

/// <summary>
/// Signals a scope boundary of the lowering — NOT a compiler bug but valid Lyric for which the backend
/// part is still missing. Carries its span along, so <see cref="ModuleLowerer"/> can turn it into a
/// real diagnostic with file, line and column.
///
/// <para>The separation is the point: <see cref="InternalCompilationException"/> still means "the
/// compiler is broken" and keeps its stack trace and throw semantics. This one means "you wrote
/// something I cannot translate yet" and becomes an ordinary error message.</para>
/// </summary>
internal sealed class UnsupportedConstructException : Exception
{
    public Span Span { get; }

    /// <summary>The note this refusal wants instead of the default category, or <c>null</c> for
    /// the default. See <see cref="LoweringDiagnostics.NeverNull"/> for why a site needs one.
    /// </summary>
    public string? Note { get; }

    public UnsupportedConstructException(string message, Span span, string? note = null)
        : base(message)
    {
        Span = span;
        Note = note;
    }
}
