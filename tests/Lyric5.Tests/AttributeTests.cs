using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// Lyric 5's attributes: on every declaration,
/// where a marker admits them, each a value computed while the program compiles. What
/// <c>lyric test</c> reads of <c>@Test</c> stands in <c>TestCommandTests</c>.
///
/// <para>Through <c>Main</c>, so in the console collection.</para>
/// </summary>
[Collection("console")]
public class AttributeTests
{
    private const string Head = "import std.io { println };\nimport std.core { OnAny, OnField, OnCallable };\n\n"
        + "struct Tag :: [OnAny] {\n    name: string,\n}\n\nstruct Level :: [OnField] {\n    value: int,\n}\n\n"
        + "struct Retry :: [OnCallable] {\n    times: int,\n    delay: int = 0,\n}\n\n";

    private static string[] Errors(string main)
    {
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        var (exit, _, error) = Run("build", "-C", dir);
        Assert.True(exit == 1, $"exit {exit}\n{error}");
        return error.Split('\n');
    }

    /// <summary>Every declaration takes attributes — a function and its parameter, a struct,
    /// its field and its method, an enum and its variant, an interface, its associated type and
    /// its method, an alias, an extend block and its method, a module-level binding; one open field
    /// takes its value in parentheses, and a value may be computed.</summary>
    [Fact]
    public void An_attribute_sits_on_every_declaration()
    {
        const string main = Head
            + "@Tag { name = \"fn\" }\nfn f(@Tag { name = \"p\" } x: int): int {\n    return x;\n}\n\n"
            + "@Tag { name = \"s\" }\nstruct S {\n    @Level(3) a: int,\n    @Tag { name = \"m\" } fn get(): int {\n        return this.a;\n    }\n}\n\n"
            + "@Tag { name = \"e\" }\nenum E {\n    @Tag { name = \"v\" } One,\n    Two,\n}\n\n"
            + "@Tag { name = \"i\" }\ninterface I {\n    @Tag { name = \"t\" } type Item;\n    @Retry(2) fn go(): int;\n}\n\n"
            + "@Tag { name = \"a\" }\ntype Alias = int;\n\n"
            + "@Tag { name = \"x\" }\nextend S {\n    @Retry { times = 3, delay = 10 } fn twice(): int {\n        return this.a * 2;\n    }\n}\n\n"
            + "let base = \"g\";\n\n@Tag { name = base + \"!\" }\nlet g = 5;\n\n"
            + "fn main(): void {\n    let s = S { a = 4 };\n    println(f\"{f(1)} {g} {s.twice()}\");\n}\n";
        var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
        Assert.Equal("1 5 8\n", BuildAndRun(dir, "app", "build", "-C", dir));
    }

    /// <summary>An attribute makes nothing reachable: a function only an attribute marks is not
    /// built.</summary>
    [Fact]
    public void An_attributed_function_is_no_root()
    {
        var lowered = TestCompiler.Lower("main.lyr", Head + "@Tag { name = \"unused\" }\nfn unused(): void {\n}\n\nfn main(): void {\n}\n");
        Assert.True(lowered.Ok);
        Assert.DoesNotContain(lowered.Ir!.Functions, f => f.Name == "app.main.unused");
        Assert.Empty(lowered.Ir.Attributes);
    }

    /// <summary>What an attribute refuses: a marker that admits no function; the parenthesized
    /// form where it is not the one open field; a field no literal makes; a struct declaring
    /// no marker at all.</summary>
    [Theory]
    [InlineData("@Level { value = 1 }\nfn f(): void {\n}\n", "LYR-SEM0065", "'@Level' cannot sit on a function — declare 'Level' with ':: [OnFunction]'")]
    [InlineData("@Retry(1)\nfn f(): void {\n}\n", null, null)]
    [InlineData("struct Two :: [OnAny] {\n    a: int,\n    b: int,\n}\n\n@Two(1)\nfn f(): void {\n}\n", "LYR-SEM0094", "fills the one field without a default, and 'Two' has 2")]
    [InlineData("class C {\n}\n\nstruct Holds :: [OnAny] {\n    c: C,\n}\n\n@Holds { c = C { } }\nfn f(): void {\n}\n", "LYR-SEM0100", "'C' is a class")]
    [InlineData("struct Plain {\n}\n\n@Plain\nfn f(): void {\n}\n", "LYR-SEM0065", "'@Plain' cannot sit on a function")]
    [InlineData("@Tag { name = \"v\" }\nfn f(): void {\n}\n\nenum K {\n    @Level(1) A,\n}\n", "LYR-SEM0065", "'@Level' cannot sit on a variant — declare 'Level' with ':: [OnVariant]'")]
    public void An_attribute_refuses_a_place_its_marker_does_not_admit_and_a_value_no_literal_makes(string declarations, string? code, string? why)
    {
        var main = Head + declarations + "\nfn main(): void {\n}\n";
        if (code is null)
        {
            var dir = Package(("lyric.toml", AppManifest), ("src/main.lyr", main));
            var (exit, _, error) = Run("build", "-C", dir);
            Assert.True(exit == 0, error);
            return;
        }
        var errors = Errors(main);
        var line = Assert.Single(errors, l => l.Contains("error["));
        Assert.Contains($"error[{code}]", line);
        Assert.Contains(why!, line);
    }
}
