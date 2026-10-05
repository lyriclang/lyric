using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A trailing block behind named arguments (design/v5/spec/04 D5 F6): the block is a positional
/// argument, and a positional one after a named one is refused (<c>LYR-SEM0119</c>). It was not
/// refused — the parser had dropped the names, and the call went by position without a word.
/// </summary>
public class TrailingBlockCallTests
{
    private const string Head = """
        fn apply(a: int, b: int, f: fn(int) -> int): int { return f(a - b); }

        """;

    private static DiagnosticEngine Check(string declarations)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Head + declarations + "\n");
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    [Fact]
    public void A_trailing_block_after_named_arguments_is_refused_and_said_once()
    {
        var error = Assert.Single(Check("fn use(): int { return apply(b: 1, a: 10) { it * 2 }; }").Diagnostics);
        Assert.Equal("LYR-SEM0119", error.Code);
        Assert.Contains("a trailing block is a positional argument", error.Message);
    }

    /// <summary>The controls: by position the block is the last argument, and by name a lambda
    /// is an argument like any other.</summary>
    [Theory]
    [InlineData("fn use(): int { return apply(10, 1) { it * 2 }; }")]
    [InlineData("fn use(): int { return apply(b: 1, a: 10, f: (x) => x * 2); }")]
    [InlineData("fn use(): int { return apply(10, 1, f: (x) => x * 2); }")]
    public void What_the_rule_allows_is_silent(string source) =>
        Assert.Empty(Check(source).Diagnostics);

    /// <summary>The other positional argument after a named one keeps its message.</summary>
    [Fact]
    public void A_plain_positional_argument_after_a_named_one_is_said_as_before()
    {
        var de = Check("fn use(): int { return apply(a: 10, 1, (x) => x * 2); }");
        Assert.Contains(de.Diagnostics, d => d.Code == "LYR-SEM0119" && d.Message.StartsWith("a positional argument after a named one"));
    }
}
