namespace Lyric.Core;

/// <summary>
/// The pipeline steps as a user sees them.
///
/// <para>The boundaries are where <c>SourceCompiler</c> calls the libraries in turn. Anything
/// finer would have to thread a progress notion through lexer, parser, resolver and sema.</para>
/// </summary>
public enum Phase
{
    /// <summary>Read the source file from disk.</summary>
    Read,

    /// <summary>Tokenize and parse.</summary>
    Parse,

    /// <summary>Load imported modules.</summary>
    Load,

    /// <summary>Resolve names, build symbol tables.</summary>
    Resolve,

    /// <summary>Type checking.</summary>
    Check,

    /// <summary>AST to mid-level IR.</summary>
    Lower,

    /// <summary>Checking the IR invariants. A phase of its own, so the share it takes appears in every
    /// <c>--verbose</c> run.</summary>
    Verify,

/// <summary>IR to <c>.lyrbc</c> bytes.</summary>
    Emit,
}

/// <summary>
/// Which phases THIS BUILD actually runs.
///
/// <para>The list is no constant: the verifier is a debug build's by default and a switch for
/// everyone else, as LLVM's is on in assert builds and reachable otherwise; the reasoning is at
/// <c>ModuleLowerer.VerifyByDefault</c>. It stands here rather than in the frontend, because the
/// tooling tests need it too — they drive the binaries as processes and deliberately do not
/// reference the frontend.</para>
///
/// <para>Written twice, it drifts: the test of the <c>--verbose</c> table carried the phase list as a
/// literal and was therefore red in release while debug stayed green. A rule two places have to know
/// belongs at the one place both of them see.</para>
/// </summary>
public static class Pipeline
{
    /// <summary>The environment variable that turns the IR verification on or off explicitly.</summary>
    public const string VerifyEnvironmentVariable = "LYRIC_VERIFY_IR";

    /// <summary>
    /// Does this process check the IR invariants?
    ///
    /// <para>A debug build says yes, a release build says no, and <c>LYRIC_VERIFY_IR</c> overrides
    /// both. The variable exists because the answer used to be the build configuration alone, and
    /// every CI job of this repository builds <c>--configuration Release</c>: the verifier ran on no
    /// path that went through <c>SourceCompiler</c> — not the tooling tests, not the conformance
    /// suite, not the examples. The unit tests were unaffected only because every one of them passes
    /// <c>verify:</c> explicitly, which is a discipline, not a guarantee.</para>
    ///
    /// <para>A diagnostic switch of the same kind as <c>LYRIC_PROFILE</c> and <c>LYRIC_JIT</c>, and
    /// read once: a value arriving mid-process would make two compilations in one run disagree.</para>
    /// </summary>
    public static bool VerifiesIr { get; } =
        Environment.GetEnvironmentVariable(VerifyEnvironmentVariable) switch
        {
            "1" or "on" or "true" => true,
            "0" or "off" or "false" => false,
            _ => BuiltWithAssertions,
        };

    /// <summary>What the build configuration says, before the variable is consulted.</summary>
    public static bool BuiltWithAssertions =>
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>The phases in pipeline order, without the ones this build skips.</summary>
    public static IReadOnlyList<Phase> OfThisBuild { get; } =
        Enum.GetValues<Phase>()
            .Where(phase => phase != Phase.Verify || VerifiesIr)
            .ToArray();
}

/// <summary>How a phase is named in the output.</summary>
public static class PhaseNames
{
    /// <summary>The short form for the timing table, lower-case like a command.</summary>
    public static string Short(Phase phase) => phase switch
    {
        Phase.Read => "read",
        Phase.Parse => "parse",
        Phase.Load => "load",
        Phase.Resolve => "resolve",
        Phase.Check => "check",
        Phase.Lower => "lower",
        Phase.Verify => "verify",
        Phase.Emit => "emit",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "unhandled phase"),
    };

    /// <summary>The progressive form for the live line: what the compiler is doing right now.</summary>
    public static string Progressive(Phase phase) => phase switch
    {
        Phase.Read => "Reading",
        Phase.Parse => "Parsing",
        Phase.Load => "Loading",
        Phase.Resolve => "Resolving",
        Phase.Check => "Checking",
        Phase.Lower => "Lowering",
        Phase.Verify => "Verifying",
        Phase.Emit => "Emitting",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "unhandled phase"),
    };
}
