using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// 'run { x = 5 }' — an assignment in a trailing block, its ';' forgotten — reads as an
/// initializer of 'run' (design/v5/spec/08 Y4; the review's M6-2). The checker says so, and says
/// what was most likely meant. It said nothing: the name is no type, no diagnostic stood for
/// that, and the lowering stopped on the error type (an internal error).
/// </summary>
public class BraceAfterNameTests
{
    private static DiagnosticEngine Check(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private const string Run = "fn run(f: fn() -> void): void { f(); }\n";

    [Fact]
    public void An_initializer_of_a_function_says_it_is_no_type_and_names_the_semicolon()
    {
        var de = Check(Run + "fn f(): int {\n    var total = 0;\n    let r = run { total = 5 };\n    return total;\n}\n");
        var error = Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal("LYR-SEM0011", error.Code);
        Assert.Contains("'run' is no type", error.Message);
        Assert.Contains("run { total = …; }", Assert.Single(error.Notes!).Message);
    }

    /// <summary>A name from a module that is not there was said once, at its import: the
    /// initializer that names it — alone or through the module — says nothing more, while its
    /// field values are still checked.</summary>
    [Theory]
    [InlineData("import missing { Rect };\n", "Rect { x = 1, y = 2 }")]
    [InlineData("import missing { Rect };\n", "Rect { x, y }")]
    [InlineData("import missing;\n", "missing.Rect { x = 1, y = 2 }")]
    public void An_initializer_of_a_name_whose_import_failed_says_nothing_more(string import, string init)
    {
        var de = Check(import + "fn f(): void {\n    let x = 1;\n    let y = 2;\n    let r = " + init + ";\n}\n");
        var error = Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error);
        Assert.Equal("LYR-RES0003", error.Code);
    }

    [Fact]
    public void The_fields_of_such_an_initializer_are_still_checked()
    {
        var de = Check("import missing { Rect };\nfn f(): void {\n    let r = Rect { x = nowhere };\n}\n");
        Assert.Equal(["LYR-RES0003", "LYR-SEM0002"],
            de.Diagnostics.Where(d => d.Severity == Severity.Error).Select(d => d.Code).Order());
    }

    [Fact]
    public void With_its_semicolon_the_block_assigns()
    {
        var de = Check(Run + "fn f(): int {\n    var total = 0;\n    run { total = 5; };\n    return total;\n}\n");
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics));
    }
}
