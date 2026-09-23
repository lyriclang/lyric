using Lyric.Compiler;
using Lyric.Core;
using Lyric.Ir;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Ir;

/// <summary>
/// The profile table, pinned: the two bundles are a contract every tool reads, so their content
/// is asserted here once rather than discovered by whichever tool a person happens to run.
/// </summary>
public sealed class ProfileTests
{
    [Fact]
    public void The_two_profiles_are_what_the_guide_says()
    {
        Assert.Equal(("debug", false, true, true, false),
            (Profile.Debug.Name, Profile.Debug.Optimize, Profile.Debug.SourceMap,
                Profile.Debug.DebugInfo, Profile.Debug.DenyWarnings));
        Assert.Equal(("release", true, true, false, false),
            (Profile.Release.Name, Profile.Release.Optimize, Profile.Release.SourceMap,
                Profile.Release.DebugInfo, Profile.Release.DenyWarnings));

        Assert.Same(Profile.Debug, Profile.Named("debug"));
        Assert.Same(Profile.Release, Profile.Named("release"));
        Assert.Null(Profile.Named("fast"));
        Assert.Equal(2, Profile.All.Count);
    }

    [Fact]
    public void The_default_is_debug_unless_the_environment_says_release()
    {
        // The suite itself may run under LYRIC_PROFILE=release — that is what the variable is
        // for — so the pin is the rule, not the value.
        var expected = Environment.GetEnvironmentVariable(Profile.EnvironmentVariable) == "release"
            ? Profile.Release
            : Profile.Debug;
        Assert.Same(expected, Profile.Default);

        // And the bare options ARE the default profile, which is what every tool that builds a
        // CompilerOptions without naming one gets.
        var options = new CompilerOptions();
        Assert.Equal(expected.Optimize, options.Optimize);
        Assert.Equal(expected.SourceMap, options.SourceMap);
        Assert.Equal(expected.DebugInfo, options.DebugInfo);
    }

    [Fact]
    public void A_profile_becomes_options_field_for_field()
    {
        var release = Profile.Release.Options();
        Assert.True(release.Optimize);
        Assert.True(release.SourceMap);
        Assert.False(release.DebugInfo);
        Assert.Equal(IrPasses.All, release.Passes);
        Assert.True(release.Fusion);

        var debug = Profile.Debug.Options();
        Assert.False(debug.Optimize);
        Assert.True(debug.DebugInfo);
    }

    /// <summary>
    /// A single pass can be taken out, and taking the inliner out is visible in the IR: the call
    /// stays a call. Pinned here because the command-line switch is only worth having if the
    /// flag it sets reaches the pass.
    /// </summary>
    [Fact]
    public void A_pass_can_be_taken_out_alone()
    {
        const string source = """
            fn step(a: int): int { return a + 1; }

            fn main(): int { return step(41); }
            """;

        var inlined = IrPrinter.Dump(Lower(source, IrPasses.All));
        var kept = IrPrinter.Dump(Lower(source, IrPasses.All & ~IrPasses.Inline));

        Assert.DoesNotContain("call main.step", inlined);
        Assert.Contains("call main.step", kept);

        // Without the optimizer the set is not consulted: All with optimize off keeps the call too.
        Assert.Contains("call main.step", IrPrinter.Dump(Lower(source, IrPasses.All, optimize: false)));
    }

    /// <summary>
    /// The verifier runs once when nothing optimized the module and twice when something did.
    ///
    /// <para>The count IS the decision, so it is pinned rather than left to the timing table: the first
    /// run accuses this lowering, the second accuses a pass, and a build that skips the passes has
    /// nothing for the second to say. That is why the debug profile pays for one and the release
    /// profile for two.</para>
    ///
    /// <para>Pinned against <c>optimize</c> rather than against a profile, because that is the switch
    /// the lowering sees; the profile only sets it (<see cref="Profile.Debug"/> off,
    /// <see cref="Profile.Release"/> on, asserted above).</para>
    /// </summary>
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void The_verifier_runs_once_without_the_passes_and_twice_with_them(bool optimize, int runs)
    {
        const string source =
            """
            fn step(n: int): int { return n + 1; }
            fn main(): int { return step(1); }
            """;

        var timings = new LoweringTimings();
        Lower(source, IrPasses.All, optimize, timings);

        Assert.True(timings.Verified);
        Assert.Equal(runs, timings.Runs);
    }

    private static IrModule Lower(string source, IrPasses passes, bool optimize = true,
        LoweringTimings? timings = null)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);
        if (de.HasErrors)
        {
            var writer = new StringWriter();
            de.RenderText(writer);
            Assert.Fail("source did not type-check:\n" + writer);
        }

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true, optimize: optimize,
            passes: passes, timings: timings);
        Assert.NotNull(ir);
        return ir!;
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));
}
