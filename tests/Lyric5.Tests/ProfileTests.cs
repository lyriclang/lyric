using Lyric5.Build;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Build profiles (design/v5/spec/11 W2 P3): the four built in, changed or named by the root
/// manifest with <c>inherits</c>; their fields — <c>opt</c>, <c>debugInfo</c>, <c>lto</c>,
/// <c>denyWarnings</c>, <c>overflowChecks</c> (03 T2), <c>fastMath</c> (01 L10) —; the field
/// flags over the profile, and <c>LYRIC_PROFILE</c> under <c>--profile</c> (11 C4). Through
/// <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class ProfileTests
{
    private static Manifest Read(string profiles)
    {
        var dir = Package(("lyric.toml", AppManifest + "\n" + profiles));
        return Manifest.Read(Path.Combine(dir, "lyric.toml"));
    }

    /// <summary>The built-in profiles' flags are what the build always gave them: a profile record
    /// changed no object's cache key.</summary>
    [Fact]
    public void The_built_in_profiles_keep_their_flags()
    {
        Assert.Equal(["-O0", "-g"], BuildProfile.Of(Profile.Debug).Codegen);
        Assert.Equal(["-O2", "-g", "-DNDEBUG"], BuildProfile.Of(Profile.Release).Codegen);
        Assert.Equal(["-O1", "-g", "-fno-omit-frame-pointer"], BuildProfile.Of(Profile.Asan).Codegen);
        Assert.Equal(["-O1", "-g", "-fno-omit-frame-pointer"], BuildProfile.Of(Profile.Tsan).Codegen);
        Assert.Empty(BuildProfile.Of(Profile.Release).ProgramFlags);
    }

    [Fact]
    public void A_field_is_a_flag()
    {
        var release = BuildProfile.Of(Profile.Release) with { Opt = 3, DebugInfo = false, Lto = true, FastMath = true };
        Assert.Equal(["-O3", "-g0", "-DNDEBUG", "-flto"], release.Codegen);
        Assert.Equal(["-ffast-math", "-ffp-contract=fast"], release.ProgramFlags);
    }

    [Fact]
    public void A_manifest_changes_a_built_in_profile()
    {
        var release = Profiles.Resolve(Read("[profile.release]\nlto = true\nopt = 3\n"), "release");
        Assert.Equal(BuildProfile.Of(Profile.Release) with { Lto = true, Opt = 3 }, release);
        Assert.Equal(BuildProfile.Of(Profile.Debug), Profiles.Resolve(Read("[profile.release]\nlto = true\n"), "debug"));
    }

    /// <summary>A profile of the manifest's own is the one it inherits from — as the manifest has
    /// that one — with its own fields over it, under its own name.</summary>
    [Fact]
    public void A_profile_of_the_manifest_inherits_from_one()
    {
        var manifest = Read("[profile.release]\nlto = true\n\n[profile.staging]\ninherits = \"release\"\ndebugInfo = false\n\n"
                            + "[profile.bench]\ninherits = \"staging\"\nopt = 3\n");
        Assert.Equal(BuildProfile.Of(Profile.Release) with { Name = "staging", Lto = true, DebugInfo = false },
            Profiles.Resolve(manifest, "staging"));
        Assert.Equal(BuildProfile.Of(Profile.Release) with { Name = "bench", Lto = true, DebugInfo = false, Opt = 3 },
            Profiles.Resolve(manifest, "bench"));
        Assert.Equal(["debug", "release", "asan", "tsan", "bench", "staging"], Profiles.Known(manifest));
    }

    [Theory]
    [InlineData("[profile.release]\ninherits = \"debug\"\n", "LYR-PKG0002", "[profile.release] is built in and inherits from nothing")]
    [InlineData("[profile.staging]\nopt = 1\n", "LYR-PKG0002", "[profile.staging] is the manifest's own and inherits from a profile")]
    [InlineData("[profile.staging]\ninherits = \"prod\"\n", "LYR-PKG0002", "inherits from 'prod', which is no profile")]
    [InlineData("[profile.a]\ninherits = \"b\"\n\n[profile.b]\ninherits = \"a\"\n", "LYR-PKG0002", "inherits from itself")]
    [InlineData("[profile.debug]\nopt = 4\n", "LYR-PKG0002", "'opt' of [profile.debug] is a level from 0 to 3")]
    [InlineData("[profile.debug]\nlto = \"yes\"\n", "LYR-PKG0002", "'lto' of [profile.debug] is true or false")]
    [InlineData("[profile.debug]\nspeed = 3\n", "LYR-PKG0003", "[profile.debug] has no key 'speed'")]
    [InlineData("[profile.Fast]\ninherits = \"release\"\n", "LYR-PKG0002", "'Fast' is no profile name")]
    public void A_profile_table_is_checked(string profiles, string code, string why)
    {
        var e = Assert.Throws<ManifestException>(() => Read(profiles));
        Assert.Equal(code, e.Code);
        Assert.Contains(why, e.Message);
    }

    private const string Increment = "import std.io { println };\n\nfn inc(x: int32): int32 {\n    return x + 1;\n}\n\n"
                                     + "fn main(): void {\n    println(f\"{inc(2147483647)}\");\n}\n";

    /// <summary>Overflow panics in every profile (03 T2) — unless a profile says otherwise, and then
    /// <c>+</c> wraps.</summary>
    [Fact]
    public void A_profile_without_overflow_checks_wraps()
    {
        var dir = Package(("lyric.toml", AppManifest + "\n[profile.debug]\noverflowChecks = false\n"), ("src/main.lyr", Increment));
        Assert.Equal("-2147483648\n", BuildAndRun(dir, "app", "build", "-C", dir));

        var plain = Package(("lyric.toml", AppManifest), ("src/main.lyr", Increment));
        var (exit, _, error) = Run("build", "-C", plain);
        Assert.True(exit == 0, error);
        var host = Target.Host;
        var ran = ProcessRunner.Run(Path.Combine(plain, "out", "debug", host.Triple, "app" + host.ExecutableSuffix), [], TimeSpan.FromMinutes(1));
        Assert.Equal(101, ran.ExitCode);
        Assert.Contains("LYR-RT0002", ran.Stderr);
    }

    [Fact]
    public void A_field_flag_changes_the_profile_for_one_build()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Increment));
        Assert.Equal("-2147483648\n", BuildAndRun(dir, "app", "build", "-C", dir, "--no-overflow-checks"));
        var (exit, output, error) = Run("build", "-C", dir, "--emit", "c", "--no-overflow-checks");
        Assert.True(exit == 0, error);
        // The whole program wraps, the standard library's code too: the profile is the program's.
        Assert.Contains("LYR_WRAP_ADD(int32_t, ", output);
        Assert.DoesNotContain("LYR_CHECKED_ADD", output);
    }

    /// <summary>A profile that denies warnings fails a build that warns: the warnings stay
    /// warnings, one error says why.</summary>
    [Fact]
    public void A_profile_that_denies_warnings_fails_a_build_that_warns()
    {
        var main = "import std.io { println };\n\nfn main(): void {\n}\n";
        var dir = Package(("lyric.toml", AppManifest + "\n[profile.debug]\ndenyWarnings = true\n"), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.Equal(1, exit);
        Assert.Contains("warning[", error);
        Assert.Contains("error[LYR-CLI0006]: 1 warning, and the profile 'debug' denies warnings (denyWarnings)", error);
        Assert.Equal(0, Run("build", "-C", dir, "--no-deny-warnings").Exit);
    }

    [Fact]
    public void A_profile_of_the_manifest_builds_into_its_own_directory()
    {
        var dir = Package(("lyric.toml", AppManifest + "\n[profile.staging]\ninherits = \"release\"\n"),
            ("src/main.lyr", "import std.io { println };\n\nfn main(): void {\n    println(\"staged\");\n}\n"));
        var (exit, _, error) = Run("build", "-C", dir, "--profile", "staging");
        Assert.True(exit == 0, error);
        var host = Target.Host;
        Assert.True(File.Exists(Path.Combine(dir, "out", "staging", host.Triple, "app" + host.ExecutableSuffix)));
    }

    [Fact]
    public void LYRIC_PROFILE_names_the_profile_where_the_command_line_does_not()
    {
        var dir = Package(("lyric.toml", AppManifest + "\n[profile.staging]\ninherits = \"debug\"\n"),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var saved = Environment.GetEnvironmentVariable("LYRIC_PROFILE");
        try
        {
            Environment.SetEnvironmentVariable("LYRIC_PROFILE", "staging");
            Assert.Equal(0, Run("build", "-C", dir).Exit);
            var host = Target.Host;
            Assert.True(File.Exists(Path.Combine(dir, "out", "staging", host.Triple, "app" + host.ExecutableSuffix)));

            Environment.SetEnvironmentVariable("LYRIC_PROFILE", "prod");
            var (exit, _, error) = Run("build", "-C", dir);
            Assert.Equal(2, exit);
            Assert.Contains("error[LYR-CLI0003]: unknown profile 'prod': debug, release, asan, tsan, staging", error);
            Assert.Equal(0, Run("build", "-C", dir, "--profile", "debug").Exit);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LYRIC_PROFILE", saved);
        }
    }

    [Theory]
    [InlineData(new[] { "--opt", "4" }, "'--opt' is a level from 0 to 3, not '4'")]
    [InlineData(new[] { "--lto", "--no-lto" }, "'--lto' and '--no-lto' ask for opposite things")]
    [InlineData(new[] { "--profile", "fast" }, "unknown profile 'fast': debug, release, asan, tsan")]
    public void A_profile_option_is_checked(string[] options, string why)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = Run(["build", "-C", dir, .. options]);
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-CLI0003]: " + why, error);
    }
}
