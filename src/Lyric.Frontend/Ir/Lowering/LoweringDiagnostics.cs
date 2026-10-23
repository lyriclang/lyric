using Lyric.Core;

namespace Lyric.Ir.Lowering;

/// <summary>
/// The codes of the lowering's refusals: no catch-all — every refusal names its reason in a code
/// of its own. A refusal is the language's where the checker does not
/// check its rule itself and a program meets it in the lowering — three reasons, <c>LYR-SEM0195..0197</c>.
/// Everything else is something the checker accepted and the lowering cannot translate, the checker's
/// own refusals among them (the never-null family, a generic function as a value, a type that does not
/// cross into C: <c>LYR-SEM0005/0029/0059</c>, <c>SEM0052</c>, <c>SEM0099</c> — the lowering meeting
/// one means the checker let it through): a compiler bug, <c>LYR-ICE0002</c>, to be reported.
/// </summary>
internal static class LoweringDiagnostics
{
    /// <summary>A part of the standard library a construct is lowered through is not on the module path.</summary>
    public const string StdMissing = "LYR-SEM0195";

    /// <summary>The monomorphization does not terminate.</summary>
    public const string Unending = "LYR-SEM0197";

    /// <summary>What the checker accepted and the lowering cannot translate.</summary>
    public const string Untranslatable = "LYR-ICE0002";

    /// <summary>The note of an <see cref="Untranslatable"/>: it is the compiler's fault, not the program's.</summary>
    private const string BugNote = "the checker accepted this and the lowering cannot translate it — a compiler bug; please report it";

    /// <summary>
    /// The note for a refusal over an optional-shaped operation on a value that, after
    /// monomorphization, has no optional in it. No compiler version will lower that, because there is
    /// nothing to lower.
    /// </summary>
    public const string NeverNull = "a value of this type is never null";

    /// <summary>Reports a refusal under its code; an <see cref="Untranslatable"/> carries the note that
    /// says whose fault it is, the others the note their site gave, if any.</summary>
    public static void ReportUnsupported(DiagnosticEngine de, string code, Span span, string message,
        string? note = null)
    {
        note ??= code == Untranslatable ? BugNote : null;
        if (note is null) de.Report(code, Severity.Error, span, message);
        else de.Report(code, Severity.Error, span, message, new DiagnosticNote(note));
    }

    /// <summary>The refusal a lowering step threw.</summary>
    public static void ReportUnsupported(DiagnosticEngine de, UnsupportedConstructException refusal) =>
        ReportUnsupported(de, refusal.Code, refusal.Span, refusal.Message, refusal.Note);
}

/// <summary>
/// A refusal of the lowering, with its code (see <see cref="LoweringDiagnostics"/>) and its span, so
/// <see cref="ModuleLowerer"/> can turn it into a diagnostic with file, line and column.
///
/// <para>The separation from <see cref="InternalCompilationException"/> stays: that one keeps its stack
/// trace and ends the compilation; this one becomes an ordinary error message.</para>
/// </summary>
internal sealed class UnsupportedConstructException : Exception
{
    public string Code { get; }

    public Span Span { get; }

    /// <summary>The note this refusal carries, or <c>null</c> for its code's own.</summary>
    public string? Note { get; }

    public UnsupportedConstructException(string code, string message, Span span, string? note = null)
        : base(message)
    {
        Code = code;
        Span = span;
        Note = note;
    }
}
