namespace Lyric.Core;

/// <summary>
/// How bad a diagnostic is: <c>Error</c>, <c>Warning</c> or <c>Hint</c>. The stage belongs to the CODE: one code always reports at one
/// severity, which is what lets a catalogue say what a code means without asking who raised it.
/// </summary>
public enum Severity
{
    /// <summary>The program is rejected.</summary>
    Error,

    /// <summary>The program compiles and something about it deserves fixing. Warnings never fail
    /// a build by themselves; <c>--deny-warnings</c> makes them fail it in CI.</summary>
    Warning,

    /// <summary>A suggestion: the program is fine, a smaller or clearer form exists.</summary>
    Hint
}

public static class SeverityExtensions
{
    public static string ToDisplayString(this Severity severity)
    {
        return severity.ToString().ToLower();
    }
}
