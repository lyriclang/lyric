using Lyric5.Build;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The lints' levels (spec 16 §5): three places, one mechanism — an attribute on a
/// declaration or a module's head, the manifest's <c>[lints]</c>, the command line's <c>-A</c>,
/// <c>-W</c>, <c>-D</c> —; a selector names a lint, a group or <c>warnings</c>; the narrowest
/// attribute decides, then the command line over the manifest over the lint's own level; a denied lint
/// is an error, exit 1, that names who denied it; a dependency's warnings are said with
/// <c>--warn-deps</c> alone.
/// </summary>
[Collection("console")]
public class LintTests
{
    private const string Program = """
        fn main(): void {
            let unused = 1;
        }
        """;

    private static string App(string? lints = null, string program = Program) =>
        Package(("lyric.toml", AppManifest + (lints is null ? "" : "\n[lints]\n" + lints + "\n")), ("src/main.lyr", program));

    private static (int Exit, string Err) Build(string root, params string[] options)
    {
        var (exit, _, error) = Run(["build", "-C", root, "--color", "never", .. options]);
        return (exit, error);
    }

    [Fact]
    public void A_lint_warns_at_its_own_level()
    {
        var (exit, error) = Build(App());
        Assert.Equal(0, exit);
        Assert.Contains("warning[LYR-SEM0071]: 'unused' is never used", error);
    }

    [Theory]
    [InlineData("unused-variable")]
    [InlineData("LYR-SEM0071")]
    [InlineData("unused")]
    [InlineData("warnings")]
    public void A_selector_names_a_lint_its_code_its_group_or_all(string selector)
    {
        var (exit, error) = Build(App(), "-D", selector);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0071]: 'unused' is never used", error);
        Assert.Contains($"note: 'unused-variable' is denied by '-D {selector}' on the command line", error);
    }

    [Fact]
    public void The_manifest_sets_a_level_and_the_command_line_overrides_it()
    {
        var root = App("deny = [\"unused-variable\"]");
        var (denied, error) = Build(root);
        Assert.Equal(1, denied);
        Assert.Contains("note: 'unused-variable' is denied by [lints] deny in lyric.toml", error);
        Assert.Equal(0, Build(root, "-W", "unused-variable").Exit);
        Assert.DoesNotContain("LYR-SEM0071", Build(App("allow = [\"unused\"]")).Err);
    }

    [Fact]
    public void Within_one_place_the_more_specific_decides_among_equals_the_later()
    {
        // the group denied, the lint itself allowed: the lint decides
        Assert.DoesNotContain("LYR-SEM0071", Build(App("deny = [\"unused\"]\nallow = [\"unused-variable\"]")).Err);
        // the same selector twice on the command line: the later
        Assert.Equal(0, Build(App(), "-D", "unused", "-A", "unused").Exit);
        Assert.Equal(1, Build(App(), "-A", "unused", "-D", "unused").Exit);
    }

    [Fact]
    public void The_narrowest_attribute_decides_over_the_package()
    {
        var root = App(program: """
            @Deny("unused")
            fn strict(): void {
                let denied = 1;
            }

            @Allow("unused-variable")
            fn quiet(): void {
                let allowed = 2;
            }

            @Deny("warnings")
            struct Holder {
                @Warn("unused-variable")
                fn inner(): void {
                    let warned = 3;
                }
            }

            fn main(): void {
                strict();
                quiet();
                let holder = Holder { };
                holder.inner();
            }
            """);
        var (exit, error) = Build(root, "-A", "warnings");
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0071]: 'denied' is never used", error);
        Assert.Contains("note: 'unused-variable' is denied here by '@Deny(\"unused\")'", error);
        Assert.DoesNotContain("'allowed'", error);
        Assert.Contains("warning[LYR-SEM0071]: 'warned' is never used", error);
    }

    [Fact]
    public void An_allow_that_takes_back_nothing_warns_and_a_name_that_names_nothing_is_refused()
    {
        var useless = App(program: """
            @Allow("unused-import")
            fn tidy(): void { }

            fn main(): void {
                tidy();
            }
            """);
        Assert.Contains("warning[LYR-SEM0180]: this '@Allow' takes back nothing: no 'unused-import' is met in what it sits on",
            Build(useless).Err);
        var wrong = App(program: """
            @Allow("unused-varable")
            fn tidy(): void { }

            fn main(): void {
                tidy();
            }
            """);
        var (exit, error) = Build(wrong);
        Assert.Equal(1, exit);
        Assert.Contains("error[LYR-SEM0179]: '@Allow' names a lint by its name, its group or its code — there is no lint 'unused-varable'; did you mean 'unused-variable'?", error);
        var (refused, _, said) = Run("build", "-C", App(), "-D", "unused-varable");
        Assert.Equal(2, refused);
        Assert.Contains("no lint, group or warning code 'unused-varable'; did you mean 'unused-variable'?", said);
        var manifest = Assert.Throws<ManifestException>(() =>
            Manifest.Read(Path.Combine(App("allow = [\"unusd\"]"), "lyric.toml")));
        Assert.Contains("did you mean 'unused'?", manifest.Message);
    }

    /// <summary>An <c>@Allow</c> a narrower level overrules decides nothing: it warns as a stale one does.</summary>
    [Fact]
    public void An_allow_a_narrower_level_overrules_warns()
    {
        var root = App(program: """
            @Allow("unused")
            struct Holder {
                @Warn("unused-variable")
                fn loud(): void {
                    let unused = 1;
                }
            }

            fn main(): void {
                let holder = Holder { };
                holder.loud();
            }
            """);
        var error = Build(root).Err;
        Assert.Contains("warning[LYR-SEM0071]: 'unused' is never used", error);
        Assert.Contains("warning[LYR-SEM0180]: this '@Allow' takes back nothing: no 'unused' is met in what it sits on", error);
    }

    [Fact]
    public void A_dependencys_warnings_are_said_with_warn_deps_alone()
    {
        var root = Package(
            ("dep/lyric.toml", "[package]\nname = \"dep\"\nversion = \"0.1.0\"\n"),
            ("dep/src/lib.lyr", "pub fn helper(): int {\n    let unusedInDep = 1;\n    return 2;\n}\n"),
            ("app/lyric.toml", AppManifest + "\n[dependencies]\ndep = { path = \"../dep\" }\n"),
            ("app/src/main.lyr", "import dep { helper };\n\nfn main(): void {\n    let _ = helper();\n}\n"));
        var app = Path.Combine(root, "app");
        var (exit, error) = Build(app);
        Assert.Equal(0, exit);
        Assert.DoesNotContain("unusedInDep", error);
        Assert.Contains("warning[LYR-SEM0071]: 'unusedInDep' is never used", Build(app, "--warn-deps").Err);
        // the command line's levels are the root package's: a dependency stays at its own
        Assert.Equal(0, Build(app, "-D", "warnings").Exit);
    }

    /// <summary>Another package's levels never fail this build: a dependency that denies its own
    /// warnings says them as warnings here, with <c>--warn-deps</c>.</summary>
    [Fact]
    public void A_dependencys_deny_is_a_warning_here()
    {
        var root = Package(
            ("dep/lyric.toml", "[package]\nname = \"dep\"\nversion = \"0.1.0\"\n\n[lints]\ndeny = [\"warnings\"]\n"),
            ("dep/src/lib.lyr", "pub fn helper(): int {\n    let unusedInDep = 1;\n    return 2;\n}\n"),
            ("app/lyric.toml", AppManifest + "\n[dependencies]\ndep = { path = \"../dep\" }\n"),
            ("app/src/main.lyr", "import dep { helper };\n\nfn main(): void {\n    let _ = helper();\n}\n"));
        var (exit, error) = Build(Path.Combine(root, "app"), "--warn-deps");
        Assert.Equal(0, exit);
        Assert.Contains("warning[LYR-SEM0071]: 'unusedInDep' is never used", error);
    }

    /// <summary><c>--warn-deps</c> says what <c>std</c> warns of itself too; without it, nothing of std's.</summary>
    [Fact]
    public void Std_warns_with_warn_deps_alone()
    {
        static bool FromStd(string error) =>
            error.Split('\n').Any(l => l.Contains("stdlib5") && (l.Contains("warning[") || l.Contains("-->")));
        Assert.False(FromStd(Build(App(program: "fn main(): void { }\n")).Err));
        Assert.True(FromStd(Build(App(program: "fn main(): void { }\n"), "--warn-deps").Err));
    }

    /// <summary>An up-to-date build runs no front end: what decides the findings is in its key,
    /// or a denial after a plain build would pass on the plain build's C.</summary>
    [Fact]
    public void A_denial_after_a_plain_build_still_fails()
    {
        var root = App();
        Assert.Equal(0, Build(root).Exit);
        Assert.Equal(1, Build(root, "-D", "warnings").Exit);
        Assert.Equal(1, Build(root, "--deny-warnings").Exit);
    }

    [Fact]
    public void Explain_names_a_lint_by_its_name()
    {
        var (exit, output, _) = RunRaw("explain", "unused-import");
        Assert.Equal(0, exit);
        Assert.StartsWith("LYR-SEM0072: ", output);
        Assert.Contains("lint 'unused-import', group 'unused', warn unless said", output);
    }
}
