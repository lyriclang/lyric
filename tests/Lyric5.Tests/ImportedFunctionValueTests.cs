using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// A function of another module as a VALUE (design/v5/spec/03 T17, 07 V3): by the name a
/// selective import binds — its own, or another —, through the module's name, and through a
/// module that passes it on. Called, each of these worked; as a value the lowering knew a
/// function only by its own symbol and refused the import's (<c>LYR-IR0001</c>), or took the
/// module's name for a receiver and crashed (<c>LYR-ICE0001</c>). Through <c>Main</c>, so in the
/// console collection.
/// </summary>
[Collection("console")]
public class ImportedFunctionValueTests
{
    private const string Util = "pub fn twice(n: int): int {\n    return n * 2;\n}\n\npub fn pair(a: int): int {\n    return a;\n}\n\npub fn pair(a: int, b: int): int {\n    return a + b;\n}\n";
    private const string Facade = "pub import app.util { twice };\npub import app.util;\n";
    private const string Hooks = "pub let handler: fn(int) -> int = (n: int) => n * 2;\n";

    private static string Output(string imports, string value, string setup = "")
    {
        var main = $"import std.io {{ println }};\n{imports}\n\nfn apply(f: fn(int) -> int, n: int): int {{\n    return f(n);\n}}\n\n"
            + $"fn main(): void {{\n{setup}    println(f\"{{{value}}}\");\n}}\n";
        var dir = Package(("lyric.toml", AppManifest), ("src/util.lyr", Util), ("src/facade.lyr", Facade),
            ("src/hooks.lyr", Hooks), ("src/main.lyr", main));
        return BuildAndRun(dir, "app", "build", "-C", dir);
    }

    [Theory]
    // the name a selective import binds: the function's own, another, one passed on
    [InlineData("import app.util { twice };", "apply(twice, 4)", "")]
    [InlineData("import app.util { twice as double };", "apply(double, 4)", "")]
    [InlineData("import app.facade { twice };", "apply(twice, 4)", "")]
    [InlineData("import app.util { twice as double };", "f(4)", "    let f = double;\n")]
    // through the module's name: qualified, under an alias, passed on, a namespace passed on
    [InlineData("import app.util;", "apply(util.twice, 4)", "")]
    [InlineData("import app.util as u;", "apply(u.twice, 4)", "")]
    [InlineData("import app.facade;", "apply(facade.twice, 4)", "")]
    [InlineData("import app.facade;", "apply(facade.util.twice, 4)", "")]
    [InlineData("import app.util;", "g(4)", "    let g: fn(int) -> int = util.twice;\n")]
    public void An_imported_function_is_a_value(string imports, string value, string setup) =>
        Assert.Equal("8\n", Output(imports, value, setup));

    /// <summary>A module's binding that HOLDS a function, by the name an import binds: called
    /// (<c>LYR-IR0001</c> before, "call to 'handler' (not a function or method)") and handed on.</summary>
    [Theory]
    [InlineData("import app.hooks { handler };", "handler(4)", "")]
    [InlineData("import app.hooks { handler as h };", "h(4)", "")]
    [InlineData("import app.hooks { handler as h };", "apply(h, 4)", "")]
    [InlineData("import app.hooks;", "hooks.handler(4)", "")]
    public void An_imported_binding_that_holds_a_function_is_called(string imports, string value, string setup) =>
        Assert.Equal("8\n", Output(imports, value, setup));

    /// <summary>Controls: what worked before — the call, and one of two of a name chosen by the
    /// type the position expects.</summary>
    [Theory]
    [InlineData("import app.util { twice as double };", "double(4)")]
    [InlineData("import app.util;", "util.twice(4)")]
    [InlineData("import app.util { pair as both };", "apply(both, 8)")]
    public void An_imported_function_is_called_and_chosen_as_before(string imports, string value) =>
        Assert.Equal("8\n", Output(imports, value));
}
