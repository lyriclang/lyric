using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A link that fails, said as its findings (spec 15 §5.2 rule 12): a symbol nothing defines at the
/// <c>extern</c> line that declares it, a library the linker does not find at the manifest's line, a
/// symbol two packages define with both packages; what the build cannot read stays
/// <c>LYR-BLD0001</c>. And <c>-v</c>, which says the commands as they start. Through <c>Main</c>,
/// so in the console collection.
/// </summary>
[Collection("console")]
public class LinkDiagnosticsTests
{
    private static string Manifest(string name, string rest = "") => $"[package]\nname = \"{name}\"\nversion = \"0.1.0\"\n{rest}";

    private const string Undeclared = "import std.io { println };\n\nextern \"C\" fn missing_fn(x: int): int;\n\nfn main(): void {\n    println(f\"{missing_fn(1)}\");\n}\n";

    [Fact]
    public void A_symbol_nothing_defines_points_at_its_extern_line()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", Undeclared));
        var (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-LNK0001]: nothing the link takes defines 'missing_fn'", error);
        Assert.Contains("main.lyr:3:1", error);
        Assert.Contains("extern \"C\" fn missing_fn(x: int): int;", error);
        Assert.DoesNotContain("LYR-BLD0001", error);
        // a build whose front end is up to date runs it again for the place
        (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.Equal(2, exit);
        Assert.Contains("main.lyr:3:1", error);
    }

    [Fact]
    public void A_library_the_linker_does_not_find_points_at_the_manifests_line()
    {
        var dir = Package(("lyric.toml", Manifest("app", "\n[native]\nlibs = [\"lyric_no_such_library\"]\n")),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-LNK0002]: the linker finds no library 'lyric_no_such_library'", error);
        Assert.Contains("lyric.toml:6:", error);
        Assert.Contains("app names it here", error);
    }

    [Fact]
    public void A_library_a_build_script_names_is_said_by_its_script()
    {
        var dir = Package(("lyric.toml", Manifest("app")),
            ("build.lyr", "import std.build { linkLib };\n\nfn main(): void {\n    linkLib(\"lyric_no_such_library\");\n}\n"),
            ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-LNK0002]: the linker finds no library 'lyric_no_such_library'", error);
        Assert.Contains("app's build script named it", error);
    }

    [Fact]
    public void A_symbol_two_packages_define_names_both()
    {
        var dir = Package(
            ("lyric.toml", Manifest("app", "\n[dependencies]\ngeo = { path = \"deps/geo\" }\n\n[native]\nsources = [\"native/*.c\"]\n")),
            ("native/a.c", "int twice_c(int x) { return 2 * x; }\n"),
            ("deps/geo/lyric.toml", Manifest("geo", "\n[native]\nsources = [\"native/*.c\"]\n")),
            ("deps/geo/native/b.c", "int twice_c(int x) { return x + x; }\n"),
            ("deps/geo/src/lib.lyr", "pub fn one(): int {\n    return 1;\n}\n"),
            ("src/main.lyr", "import geo { one };\n\nfn main(): void {\n    let _ = one();\n}\n"));
        var (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-LNK0003]: 'twice_c' is defined by app and by geo", error);
        Assert.DoesNotContain("LYR-BLD0001", error);
    }

    [Fact]
    public void A_link_failure_the_build_cannot_read_stays_as_it_is()
    {
        var dir = Package(("lyric.toml", Manifest("app", "\n[native]\narchives = [\"native/libbad.a\"]\n")),
            ("native/libbad.a", "this is no archive\n"), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-BLD0001]: linking", error);
        Assert.DoesNotContain("LYR-LNK", error);
    }

    [Fact]
    public void A_header_nothing_holds_names_the_directories_searched()
    {
        var dir = Package(("lyric.toml", Manifest("app", "\n[native]\nsources = [\"native/*.c\"]\ninclude = [\"native/include\"]\n")),
            ("native/include/keep.h", "#define KEEP 1\n"),
            ("native/a.c", "#include \"no_such_header.h\"\nint a_c(void) { return 1; }\n"), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = RunRaw("build", "-C", dir, "--color", "never");
        Assert.Equal(2, exit);
        Assert.Contains("error[LYR-BLD0001]", error);
        Assert.Contains("the directories searched for headers: " + Path.Combine(dir, "native", "include"), error);
    }

    [Fact]
    public void Verbose_says_each_command_as_it_starts()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): void {\n}\n"));
        var (exit, _, error) = RunRaw("build", "-C", dir, "-v");
        Assert.True(exit == 0, error);
        var commands = error.Split('\n').Where(l => l.StartsWith("running: ", StringComparison.Ordinal)).ToList();
        Assert.Contains(commands, c => c.Contains(" -c ", StringComparison.Ordinal));
        Assert.Contains(commands, c => c.Contains(" -o ", StringComparison.Ordinal) && !c.Contains(" -c ", StringComparison.Ordinal));
        // up to date: nothing runs, nothing is said
        (exit, _, error) = RunRaw("build", "-C", dir, "--verbose");
        Assert.True(exit == 0, error);
        Assert.DoesNotContain("running: ", error);
        (exit, var output, _) = RunRaw("build", "-C", dir, "-v", "--json");
        Assert.Equal(2, exit);
        Assert.Contains("'-v' says the commands as text", output);
    }
}
