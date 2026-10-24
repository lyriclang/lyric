using System.Diagnostics;
using System.Runtime.InteropServices;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Builds, one after another, every runtime variant the native tests link on this machine, into
/// the two caches they read: the test classes' own (<c>lyric5-test-cache</c>) and the toolchain's
/// (<c>~/.cache/lyric/runtime</c>, through the driver). Run first, alone, it takes the first
/// build of each variant out of the suite, where a dozen parallel tests otherwise wait on the one
/// that builds it — on a slow machine past their timeouts. Asked for with
/// <c>LYRIC5_WARM_CACHES=1</c>; <c>LYRIC5_WARM_REPORT</c> names a file for the times.
/// </summary>
[Collection("console")]
public class CacheWarmupTests
{
    private static bool Asked => Environment.GetEnvironmentVariable("LYRIC5_WARM_CACHES") == "1";

    private static bool Sanitizers =>
        Environment.GetEnvironmentVariable("LYRIC5_SANITIZERS") is "1" or "all"
        && OperatingSystem.IsLinux() && RuntimeInformation.OSArchitecture == Architecture.X64;

    [Fact]
    public void Every_runtime_variant_is_built_before_the_suite()
    {
        if (!Asked) return;
        var report = new List<string>();
        void Timed(string what, Action build)
        {
            var clock = Stopwatch.StartNew();
            build();
            report.Add($"{clock.Elapsed.TotalSeconds,7:0.0}s  {what}");
        }

        foreach (var profile in new[] { Profile.Debug, Profile.Release })
        foreach (var gc in new[] { Collector.Immix, Collector.Boehm })
        {
            var variant = BuildProfile.Of(profile) with { Gc = gc };
            Timed($"test cache  {profile} {gc}", () => RuntimeBuildTests.BuildTest("hello", variant));
        }
        Timed("test cache  Debug Immix verify",
            () => RuntimeBuildTests.BuildTest("hello", BuildProfile.Of(Profile.Debug) with { Gc = Collector.Immix, GcVerify = true }));
        if (Sanitizers)
        {
            var clang = CCompiler.Locate(CCompilerKind.Clang) ?? throw new InvalidOperationException("the sanitizer profiles need clang on PATH");
            Timed("test cache  Asan", () => RuntimeBuildTests.BuildTest("hello", BuildProfile.Of(Profile.Asan), clang));
            Timed("test cache  Tsan", () => RuntimeBuildTests.BuildTest("hello", BuildProfile.Of(Profile.Tsan), clang));
        }

        var app = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n}\n"));
        string[][] builds =
        [
            ["--profile", "debug", "--gc", "immix"], ["--profile", "debug", "--gc", "boehm"],
            ["--profile", "release", "--gc", "immix"], ["--profile", "release", "--gc", "boehm"],
            ["--profile", "release", "--lto"],
        ];
        foreach (var options in builds)
            Timed($"user cache  {string.Join(' ', options)}", () =>
            {
                var (exit, _, error) = Run(["build", "-C", app, .. options]);
                Assert.True(exit == 0, error);
            });

        if (Environment.GetEnvironmentVariable("LYRIC5_WARM_REPORT") is { Length: > 0 } file)
            File.WriteAllLines(file, report);
    }
}
