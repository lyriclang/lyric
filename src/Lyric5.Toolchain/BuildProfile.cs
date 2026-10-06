namespace Lyric5.Toolchain;

/// <summary>
/// A build profile (design/v5/spec/11 W2 P3): one of the four built in — <c>debug</c>,
/// <c>release</c>, and the sanitizers' <c>asan</c> and <c>tsan</c> (01 C7) — or one a manifest
/// names, which inherits from one. A profile is its fields over its base; the base decides what no
/// field does: the sanitizer, the frame pointers it needs, the C runtime's assertions.
/// </summary>
/// <param name="Name">What <c>--profile</c> names, and the directory under <c>out/</c>.</param>
/// <param name="Base">The built-in profile it comes from.</param>
/// <param name="Opt">The C optimization level, 0 to 3.</param>
/// <param name="DebugInfo">Debug information in the binary.</param>
/// <param name="Lto">Link-time optimization over the program and the runtime — ThinLTO; no
/// built-in profile asks for it (the review's B11: after the IR optimizer its gain was within the
/// noise, its cost a third more build time). <see cref="CBuild"/> keeps it off the link line and
/// off macOS.</param>
/// <param name="DenyWarnings">A warning fails the build.</param>
/// <param name="OverflowChecks">Integer overflow panics (03 T2); off only where a profile says so
/// — then <c>+ - *</c> and negation wrap.</param>
/// <param name="FastMath">The program's floating point is no longer IEEE-exact (01 L10).</param>
public sealed record BuildProfile(string Name, Profile Base, int Opt, bool DebugInfo, bool Lto,
    bool DenyWarnings, bool OverflowChecks, bool FastMath)
{
    /// <summary>The names <c>--profile</c> knows without a manifest.</summary>
    public static readonly IReadOnlyList<string> BuiltIn = ["debug", "release", "asan", "tsan"];

    public static BuildProfile Of(Profile profile) => profile switch
    {
        Profile.Debug => new("debug", profile, 0, true, false, false, true, false),
        Profile.Release => new("release", profile, 2, true, false, false, true, false),
        Profile.Asan => new("asan", profile, 1, true, false, false, true, false),
        _ => new("tsan", profile, 1, true, false, false, true, false),
    };

    /// <summary>A built-in profile where a profile is asked for.</summary>
    public static implicit operator BuildProfile(Profile profile) => Of(profile);

    /// <summary>The built-in profile of that name, or <c>null</c>.</summary>
    public static BuildProfile? Named(string name) =>
        name switch
        {
            "debug" => Of(Profile.Debug),
            "release" => Of(Profile.Release),
            "asan" => Of(Profile.Asan),
            "tsan" => Of(Profile.Tsan),
            _ => null,
        };

    /// <summary>ASan and TSan runtimes ship with clang, not with zig cc (01 C7).</summary>
    public bool RequiresClang => Base.RequiresClang();

    /// <summary>Optimization and debug flags every unit of the profile gets.</summary>
    public IReadOnlyList<string> Codegen
    {
        get
        {
            var flags = new List<string> { $"-O{Opt}", DebugInfo ? "-g" : "-g0" };
            if (Base == Profile.Release) flags.Add("-DNDEBUG");
            if (Base is Profile.Asan or Profile.Tsan) flags.Add("-fno-omit-frame-pointer");
            if (Lto) flags.Add("-flto=thin");
            return flags;
        }
    }

    /// <summary>The sanitizer's instrumentation, the base's.</summary>
    public IReadOnlyList<string> Instrumentation => Base.Instrumentation();

    /// <summary>What the program's own units add: fast-math is the program's, never the runtime's,
    /// whose formatting of a float stays exact.</summary>
    public IReadOnlyList<string> ProgramFlags => FastMath ? ["-ffast-math", "-ffp-contract=fast"] : [];
}
