using System.Runtime.CompilerServices;

namespace Lyric.Core;

/// <summary>
/// The compiler's recursions over types check here that there is stack left, and end in an
/// exception where there is not.
/// </summary>
/// <remarks>
/// <para>A stack overflow cannot be caught: the process ends with the runtime's "Stack overflow"
/// and a few thousand frames, exit 134, and no word about the program that was being compiled. A
/// recursion that asks first ends in <see cref="CompilerDepthException"/>, which the command line
/// reports like every failure of its own (<c>LYR-ICE0001</c>) with
/// <see cref="CompilerPosition"/>'s place.</para>
///
/// <para>The question is the runtime's (<see cref="RuntimeHelpers.TryEnsureSufficientExecutionStack"/>),
/// not a counter: it measures the stack that is left on this thread, so no number has to be
/// guessed per recursion, and a thread with a small stack is covered as well. Roslyn guards its
/// recursive visitors the same way.</para>
/// </remarks>
public static class StackGuard
{
    /// <summary>Throws when the stack is nearly spent; <paramref name="doing"/> names the
    /// recursion for the report ("lowering a type").</summary>
    public static void Check(string doing)
    {
        if (RuntimeHelpers.TryEnsureSufficientExecutionStack()) return;
        throw new CompilerDepthException(
            $"{doing} did not end before the compiler's stack was spent — a cycle in the compiler, or a program nested deeper than it can follow");
    }
}

/// <summary>A recursion of the compiler went as deep as its stack allows (<see cref="StackGuard"/>).</summary>
public sealed class CompilerDepthException(string message) : Exception(message);

/// <summary>
/// Where the compiler is at work — the function it checks or lowers — kept for one purpose: a
/// report of its own failure that names a place in the program.
/// </summary>
/// <remarks>
/// A crash has a stack trace of the compiler and, until this, nothing about the program: whoever
/// hit it had to bisect their source to find the declaration. The phases leave the place here as
/// they go; it costs two stores per function. Per thread, because a language server compiles on
/// several.
/// </remarks>
public static class CompilerPosition
{
    [ThreadStatic] private static SourceManager? _sources;
    [ThreadStatic] private static string? _phase;
    [ThreadStatic] private static string? _name;
    [ThreadStatic] private static Span _at;

    /// <summary>A compilation begins: the places that follow are in these sources.</summary>
    public static void Begin(SourceManager sources)
    {
        _sources = sources;
        _phase = null;
        _name = null;
    }

    /// <summary>The compiler turns to <paramref name="name"/> — <paramref name="phase"/> says what
    /// it does there ("checking", "lowering").</summary>
    public static void At(string phase, string name, Span at)
    {
        _phase = phase;
        _name = name;
        _at = at;
    }

    /// <summary>"checking 'total' at src/main.lyr:5:1" — or <c>null</c> before the first
    /// declaration, or where the place cannot be read back.</summary>
    public static string? Describe()
    {
        if (_sources is null || _phase is null) return null;
        try
        {
            var position = _sources.LocateStart(_at);
            return $"{_phase} '{_name}' at {_sources.GetPath(_at.File)}:{position}";
        }
        catch (Exception)
        {
            // A report of a failure does not fail over its own decoration.
            return $"{_phase} '{_name}'";
        }
    }
}
