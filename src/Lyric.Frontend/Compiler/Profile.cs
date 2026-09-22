namespace Lyric.Compiler;

/// <summary>
/// A named bundle of the compile options a person chooses between, rather than one at a time.
///
/// <para>Two, and the table below is the whole definition. <c>debug</c> keeps every frame and
/// every name: a backtrace or a debugger that shows the optimizer's world instead of the program's
/// is lying politely. <c>release</c> runs the optimizations and drops the slot names, and keeps
/// the source map, because a panic in production that names its line is worth the bytes. Every
/// field stays individually overridable on a command line, so a profile is a starting point and
/// not a wall.</para>
///
/// <para>ONE table, here. The compiler, the build runner, the test runner, the debug adapter and
/// the embedding API all read it. A second copy would drift, and the drift would be a profile
/// that means two things.</para>
/// </summary>
/// <param name="Optimize">Whether the IR optimizations run: inlining, scalar replacement,
/// devirtualization.</param>
/// <param name="SourceMap">Whether the module carries line numbers.</param>
/// <param name="DebugInfo">Whether the module carries slot and field names.</param>
/// <param name="DenyWarnings">Whether a run that reports warnings fails.</param>
public sealed record Profile(string Name, bool Optimize, bool SourceMap, bool DebugInfo,
    bool DenyWarnings)
{
    public static Profile Debug { get; } =
        new("debug", Optimize: false, SourceMap: true, DebugInfo: true, DenyWarnings: false);

    public static Profile Release { get; } =
        new("release", Optimize: true, SourceMap: true, DebugInfo: false, DenyWarnings: false);

    public static IReadOnlyList<Profile> All { get; } = [Debug, Release];

    /// <summary>The environment variable that names the default profile of a process.</summary>
    public const string EnvironmentVariable = "LYRIC_PROFILE";

    /// <summary>
    /// What a compile is when nobody says: <c>debug</c>, unless <c>LYRIC_PROFILE</c> names the
    /// other one.
    ///
    /// <para>The variable is a diagnostic switch of the same kind as <c>LYRIC_JIT</c>, and for the
    /// same purpose: running a whole suite in the other shape without writing the choice into
    /// every test. An explicit profile on a command line or on the host options wins over it. A
    /// name the variable carries that is not a profile leaves the default alone, because a
    /// library has nowhere to complain; a tool that takes the name on its command line refuses
    /// one it does not know.</para>
    /// </summary>
    public static Profile Default { get; } =
        Named(Environment.GetEnvironmentVariable(EnvironmentVariable) ?? string.Empty) ?? Debug;

    /// <summary>The profile of this name, or <c>null</c> when there is none.</summary>
    public static Profile? Named(string name) => name switch
    {
        "debug" => Debug,
        "release" => Release,
        _ => null,
    };

    /// <summary>The compile options this profile stands for. The roots, the progress sink and the
    /// diagnostic switches are the caller's to add.</summary>
    public CompilerOptions Options() => new()
    {
        Optimize = Optimize,
        SourceMap = SourceMap,
        DebugInfo = DebugInfo,
    };
}
