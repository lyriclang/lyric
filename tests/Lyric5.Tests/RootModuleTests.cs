using Lyric5.Build;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// The root module (design/v5/spec/07 V1 M1, the review's M7-1): a package's <c>src/lib.lyr</c>
/// is the module of the package's own name — <c>import geo { Circle }</c> —, the one exception
/// to "a module is named by its path"; there is no module <c>geo.lib</c>. It may stand beside
/// <c>main.lyr</c> and beside a directory <c>lib/</c>, and a name it declares is not also one of
/// the package's modules (07 V6 K1) — asked of the package's files, not of what a program
/// happened to load. Through <c>Main</c>, so in the console collection.
/// </summary>
[Collection("console")]
public class RootModuleTests
{
    private static string Toml(string name, string dependencies = "") =>
        $"[package]\nname = \"{name}\"\nversion = \"0.1.0\"\n{(dependencies.Length > 0 ? "\n" + dependencies : "")}";

    private static readonly string App = Toml("app", "[dependencies]\ngeo = { path = \"deps/geo\" }\n");

    private const string Lib = "pub struct Circle {\n    pub r: int,\n}\n\npub fn area(c: Circle): int {\n    return 3 * c.r * c.r;\n}\n";
    private const string Shapes = "pub fn rect(w: int, h: int): int {\n    return w * h;\n}\n";

    private static string Main(string imports, string value) =>
        $"import std.io {{ println }};\n{imports}\n\nfn main(): void {{\n    println(f\"{{{value}}}\");\n}}\n";

    [Theory]
    [InlineData("import geo { Circle, area };", "area(Circle { r = 2 })", "12\n")]
    [InlineData("import geo;", "geo.area(geo.Circle { r = 1 })", "3\n")]
    [InlineData("import geo as g;", "g.area(g.Circle { r = 3 })", "27\n")]
    public void A_packages_lib_is_the_module_of_its_name(string import, string value, string printed)
    {
        var dir = Package(("lyric.toml", App), ("src/main.lyr", Main(import, value)),
            ("deps/geo/lyric.toml", Toml("geo")), ("deps/geo/src/lib.lyr", Lib));
        Assert.Equal(printed, BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>The root module as a facade: what it passes on with <c>pub import</c> is its own
    /// (07 V3 I4) — a name, and a submodule as a namespace.</summary>
    [Theory]
    [InlineData("pub import geo.shapes { rect };\n", "import geo { rect };", "rect(3, 4)")]
    [InlineData("pub import geo.shapes;\n", "import geo;", "geo.shapes.rect(3, 4)")]
    public void The_root_module_passes_on_what_its_submodules_declare(string lib, string import, string value)
    {
        var dir = Package(("lyric.toml", App), ("src/main.lyr", Main(import, value)),
            ("deps/geo/lyric.toml", Toml("geo")), ("deps/geo/src/lib.lyr", lib), ("deps/geo/src/shapes.lyr", Shapes));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void There_is_no_module_named_lib()
    {
        var dir = Package(("lyric.toml", App), ("src/main.lyr", Main("import geo.lib { Circle, area };", "area(Circle { r = 2 })")),
            ("deps/geo/lyric.toml", Toml("geo")), ("deps/geo/src/lib.lyr", Lib));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        Assert.Contains("error[LYR-RES0003]: cannot find module 'geo.lib'", error);
        Assert.Contains("'src/lib.lyr' is the module 'geo'", error);
    }

    /// <summary>A package's own modules import its root module the same way; <c>lib.lyr</c> and
    /// <c>main.lyr</c> stand side by side.</summary>
    [Fact]
    public void A_program_imports_its_packages_root_module()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/lib.lyr", "pub fn answer(): int {\n    return 42;\n}\n"),
            ("src/main.lyr", Main("import app { answer };", "answer()")));
        Assert.Equal("42\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>A directory is a namespace (07 M5): <c>src/lib/</c> beside <c>src/lib.lyr</c>
    /// holds the modules <c>geo.lib.…</c>, and <c>geo.lib</c> itself is none.</summary>
    [Fact]
    public void A_directory_named_lib_is_a_namespace_beside_the_root_module()
    {
        var dir = Package(("lyric.toml", App),
            ("src/main.lyr", Main("import geo { area, Circle };\nimport geo.lib.extra { twice };", "twice(area(Circle { r = 1 }))")),
            ("deps/geo/lyric.toml", Toml("geo")), ("deps/geo/src/lib.lyr", Lib),
            ("deps/geo/src/lib/extra.lyr", "pub fn twice(n: int): int {\n    return n * 2;\n}\n"));
        Assert.Equal("6\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>Control: a package with a root module is still imported module by module.</summary>
    [Fact]
    public void A_submodule_is_imported_beside_the_root_module()
    {
        var dir = Package(("lyric.toml", App), ("src/main.lyr", Main("import geo.shapes { rect };", "rect(3, 4)")),
            ("deps/geo/lyric.toml", Toml("geo")), ("deps/geo/src/lib.lyr", Lib), ("deps/geo/src/shapes.lyr", Shapes));
        Assert.Equal("12\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>One namespace per module (07 V6 K1): the root module's <c>shapes</c> beside the
    /// module <c>geo.shapes</c> — which this program never imports.</summary>
    [Fact]
    public void A_name_of_the_root_module_is_not_also_a_module_of_the_package()
    {
        var dir = Package(("lyric.toml", App), ("src/main.lyr", Main("import geo { shapes };", "shapes()")),
            ("deps/geo/lyric.toml", Toml("geo")), ("deps/geo/src/lib.lyr", "pub fn shapes(): int {\n    return 1;\n}\n"),
            ("deps/geo/src/shapes.lyr", Shapes));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        Assert.Contains("error[LYR-RES0012]: 'shapes' in module 'geo' is also the module 'geo.shapes'", error);
    }

    /// <summary>The same question of any module, answered by the package's files: nothing here
    /// imports <c>app.net.http</c>.</summary>
    [Fact]
    public void A_name_is_not_also_a_submodule_nobody_imports()
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/net.lyr", "pub fn http(): int {\n    return 1;\n}\n"),
            ("src/net/http.lyr", "pub fn get(): int {\n    return 2;\n}\n"),
            ("src/main.lyr", Main("import app.net;", "net.http()")));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        Assert.Contains("error[LYR-RES0012]: 'http' in module 'app.net' is also the module 'app.net.http'", error);
    }

    /// <summary>A module that imports its own submodule under its name: 'http' in 'app.net' is
    /// the module 'app.net.http', one meaning — in any module, not in a root module alone.</summary>
    [Fact]
    public void A_module_imports_its_submodule_under_its_own_name()
    {
        var dir = Package(("lyric.toml", AppManifest),
            ("src/net.lyr", "import app.net.http;\n\npub fn viaNet(): int {\n    return http.get();\n}\n"),
            ("src/net/http.lyr", "pub fn get(): int {\n    return 2;\n}\n"),
            ("src/main.lyr", Main("import app.net { viaNet };", "viaNet()")));
        Assert.Equal("2\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>Controls for the question: the root module's own file is no module
    /// <c>geo.lib</c>, and a directory of the name is a namespace, not a module.</summary>
    [Fact]
    public void A_name_beside_lib_or_beside_a_directory_is_one_name()
    {
        var dir = Package(("lyric.toml", App), ("src/main.lyr", Main("import geo { lib, extra };", "lib() + extra()")),
            ("deps/geo/lyric.toml", Toml("geo")),
            ("deps/geo/src/lib.lyr", "pub fn lib(): int {\n    return 1;\n}\n\npub fn extra(): int {\n    return 2;\n}\n"),
            ("deps/geo/src/extra/more.lyr", Shapes));
        Assert.Equal("3\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    [Fact]
    public void The_root_modules_file_is_named_by_the_package()
    {
        var dir = Package(("lyric.toml", Toml("geo")), ("src/lib.lyr", Lib), ("src/shapes.lyr", Shapes),
            ("src/lib/extra.lyr", Shapes));
        var manifest = Manifest.Read(Path.Combine(dir, "lyric.toml"));
        Assert.Equal("geo", Project.ModulePathOf(manifest, Path.Combine(dir, "src", "lib.lyr")));
        Assert.Equal("geo.shapes", Project.ModulePathOf(manifest, Path.Combine(dir, "src", "shapes.lyr")));
        Assert.Equal("geo.lib.extra", Project.ModulePathOf(manifest, Path.Combine(dir, "src", "lib", "extra.lyr")));
    }

    /// <summary>A library is checked module by module under the names its files have (07 B6):
    /// the root module's diagnostics name it by the package.</summary>
    [Fact]
    public void A_library_checks_its_root_module_under_the_packages_name()
    {
        var dir = Package(("lyric.toml", Toml("geo")), ("src/lib.lyr", "pub fn shapes(): int {\n    return 1;\n}\n"),
            ("src/shapes.lyr", Shapes));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        Assert.Contains("error[LYR-RES0012]: 'shapes' in module 'geo' is also the module 'geo.shapes'", error);
    }
}
