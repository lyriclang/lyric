// Frozen from release/v4.6.0 (002b7a4b) by tooling/from4/freeze.py: Lyric 4.6 as it was. Do not edit.
namespace Lyric4.Core;

/// <param name="Notes">Secondary remarks, in telling order. <c>null</c> and empty mean the same
/// thing; <c>null</c> keeps the common case allocation-free.</param>
public readonly record struct Diagnostic(
    string Code,
    Severity Severity,
    Span Span,
    string Message,
    IReadOnlyList<DiagnosticNote>? Notes = null
    );
