using System.Runtime.InteropServices;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The build's target and profile, and <c>@When</c>: constants of the build that <c>comptime if</c> chooses by, and declarations — modules —
/// only one target builds.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class TargetTests
{
    private static string App(string main) => Package(("lyric.toml", AppManifest), ("src/main.lyr", main));

    private static string Emitted(string dir, string triple)
    {
        var (exit, c, error) = Run("build", "-C", dir, "--emit", "c", "--target", triple);
        Assert.True(exit == 0, error);
        return c;
    }

    /// <summary><c>target</c> and <c>profile</c> are the build's: the host's system and machine
    /// where no target is named, the debug profile by default.</summary>
    [Fact]
    public void Target_and_profile_are_the_builds()
    {
        const string main = """
            import std.io { println };

            fn main(): void {
                println(f"{target.os} {target.arch} {target.pointerWidth} {profile.debug}");
            }
            """;
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "aarch64" : "x86_64";
        var dir = App(main);
        Assert.Equal($"{os} {arch} 64 true\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A cross-build's constants are the target's: each emission holds the branch its
    /// target chooses, and no other.</summary>
    [Fact]
    public void A_cross_build_chooses_the_targets_branch()
    {
        const string main = """
            import std.io { println };

            fn main(): void {
                comptime if (target.os == .Windows) {
                    println("branch-for-windows");
                } else if (target.arch == .Aarch64) {
                    println("branch-for-aarch64");
                } else {
                    println("branch-for-the-rest");
                }
            }
            """;
        var dir = App(main);
        foreach (var (triple, chosen) in new[]
                 {
                     ("x86_64-windows-gnu", "branch-for-windows"), ("aarch64-macos", "branch-for-aarch64"),
                     ("x86_64-linux-gnu", "branch-for-the-rest"),
                 })
        {
            var c = Emitted(dir, triple);
            foreach (var branch in new[] { "branch-for-windows", "branch-for-aarch64", "branch-for-the-rest" })
                Assert.True(c.Contains(branch) == (branch == chosen), $"{triple}: {branch}");
        }
    }

    /// <summary>A program that reads neither carries neither: <c>target</c> and <c>profile</c> are
    /// constants, which the global pruning takes out where nothing reads them.</summary>
    [Fact]
    public void A_program_that_reads_no_target_carries_none()
    {
        var quiet = TestCompiler.Lower("main.lyr", "import std.io { println };\n\nfn main(): void {\n    println(\"hello\");\n}\n");
        Assert.True(quiet.Ok);
        Assert.DoesNotContain(quiet.Ir!.Globals, g => g.Name == "target" && g.Module == "std.core");
        var reads = TestCompiler.Lower("main.lyr", "import std.io { println };\n\nfn main(): void {\n    println(f\"{target.pointerWidth}\");\n}\n");
        Assert.True(reads.Ok);
        Assert.Contains(reads.Ir!.Globals, g => g.Name == "target" && g.Module == "std.core");
    }

    /// <summary><c>@When</c> on a declaration: built where its condition holds; where it does
    /// not, checked and left out — <c>comptime if</c> chooses between the two.</summary>
    [Fact]
    public void When_leaves_a_declaration_out_of_a_build_it_does_not_hold_for()
    {
        const string main = """
            import std.io { println };

            @When(target.os == .Windows)
            fn winOnly(): string { return "only-on-windows"; }

            @When(target.os != .Windows)
            fn posixOnly(): string { return "only-on-posix"; }

            fn which(): string {
                comptime if (target.os == .Windows) {
                    return winOnly();
                } else {
                    return posixOnly();
                }
            }

            fn main(): void {
                println(which());
            }
            """;
        var dir = App(main);
        var windows = Emitted(dir, "x86_64-windows-gnu");
        Assert.Contains("only-on-windows", windows);
        Assert.DoesNotContain("only-on-posix", windows);
        var linux = Emitted(dir, "x86_64-linux-gnu");
        Assert.DoesNotContain("only-on-windows", linux);
        Assert.Contains("only-on-posix", linux);
        Assert.Equal(OperatingSystem.IsWindows() ? "only-on-windows\n" : "only-on-posix\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>One module per build: <c>@When</c> on Lyric 5's header, <c>module;</c>. The
    /// module the build leaves out binds what a C function no library has gives — a binding a
    /// module's start runs, reached or not —, and the program still links: nothing of it is
    /// built. Chosen by the profile, which every host builds alike.</summary>
    [Fact]
    public void A_module_left_out_is_not_built()
    {
        const string debugOnly = """
            @When(profile.debug)
            module;

            pub fn name(): string {
                return "the debug module";
            }
            """;
        const string releaseOnly = """
            @When(!profile.debug)
            module;

            extern "C" fn lyric_test_nowhere(): int;

            // runs before 'main' wherever the module is built: the program would not link
            let ticket = lyric_test_nowhere();

            pub fn name(): string {
                return f"the release module {ticket}";
            }
            """;
        const string main = """
            import app.checked;
            import app.fast;
            import std.io { println };

            fn main(): void {
                comptime if (profile.debug) {
                    println(checked.name());
                } else {
                    println(fast.name());
                }
            }
            """;
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main), ("src/checked.lyr", debugOnly), ("src/fast.lyr", releaseOnly));
        Assert.Equal("the debug module\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>What <c>@When</c> refuses: a use the program would run of what the build leaves out
    /// (<c>CT0006</c>), a call or a type; what it does not: the same use in the branch a
    /// <c>comptime if</c> does not take. A left-out declaration is checked; two of one name stay
    /// one too many (<c>SEM0085</c>: <c>comptime if</c> chooses between names); a header names nothing.</summary>
    [Theory]
    [InlineData("@When(target.pointerWidth == 16)\nfn f(): int { return 1; }\n\nfn main(): void {\n    let _ = f();\n}\n", "LYR-CT0006", "'f' is not built for this target")]
    [InlineData("@When(target.pointerWidth == 16)\nstruct Narrow { }\n\nfn take(n: Narrow): void { }\n\nfn main(): void {\n}\n", "LYR-CT0006", "'Narrow' is not built for this target")]
    [InlineData("@When(target.pointerWidth == 16)\nfn f(): int { return 1; }\n\nfn main(): void {\n    comptime if (target.pointerWidth == 16) {\n        let _ = f();\n    }\n}\n", null, null)]
    [InlineData("@When(target.pointerWidth == 16)\nfn f(): int { return \"one\"; }\n\nfn main(): void {\n}\n", "LYR-SEM0001", "")]
    [InlineData("@When(target.pointerWidth == 16)\nfn f(): int { return 1; }\n\n@When(target.pointerWidth != 16)\nfn f(): int { return 2; }\n\nfn main(): void {\n}\n", "LYR-SEM0085", "'f' is declared twice")]
    [InlineData("fn zero(): int { return 0; }\n\n@When(target.pointerWidth == 16)\nfn f(): int { return comptime (1 / zero()); }\n\nfn main(): void {\n}\n", null, null)]
    [InlineData("module;\n\nfn main(): void {\n}\n", null, null)]
    [InlineData("module app.main;\n\nfn main(): void {\n}\n", "LYR-RES0008", "the 'module' header is gone")]
    public void When_refuses_an_active_use_and_checks_what_it_leaves_out(string main, string? code, string? why)
    {
        var dir = App(main);
        var (exit, _, error) = Run("build", "-C", dir);
        if (code is null)
        {
            Assert.True(exit == 0, error);
            return;
        }
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        var line = Assert.Single(error.Split('\n'), l => l.Contains("error["));
        Assert.Contains($"error[{code}]", line);
        Assert.Contains(why!, line);
    }
}
