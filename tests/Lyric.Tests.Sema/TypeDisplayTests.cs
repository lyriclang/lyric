using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A type's display says which type it is, in the parentheses the source would need: an array of
/// optionals is <c>(?int)[]</c> and no <c>?int[]</c>; a function that returns a throwing coroutine
/// is no function that throws. The same text is the lowering's key for an instance, so two types
/// with one text were one instance there (<c>InstanceKeyTests</c> in Lyric5.Tests).
/// </summary>
public class TypeDisplayTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private const string Head = """
        import std.core { Exception };

        fn one(): int throws Exception { return 1; }
        fn gen(): Coroutine<int> throws Exception { yield try one(); }
        fn quiet(): Coroutine<int> { yield 1; }

        """;

    /// <summary>The message of the one mismatch (<c>LYR-SEM0001</c>) a program has.</summary>
    private static string Mismatch(string declarations)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Head + declarations + "\n");
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        var errors = de.Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1, string.Join("\n", de.Diagnostics));
        Assert.Equal("LYR-SEM0001", errors[0].Code);
        return errors[0].Message;
    }

    [Fact]
    public void An_array_of_optionals_is_no_optional_array()
    {
        var message = Mismatch("fn take(xs: (?int)[]): int { return 1; }\nfn use(): int { let ys: ?int[] = null; return take(ys); }");
        Assert.Contains("'?int[]'", message);
        Assert.Contains("'(?int)[]'", message);
        Assert.DoesNotContain("share this name", message);
    }

    [Fact]
    public void An_inline_array_of_optionals_keeps_its_parentheses() =>
        Assert.Contains("'(?int)[2]'", Mismatch("fn use(): int { let xs: (?int)[2] = [1, null]; let n: int = xs; return n; }"));

    /// <summary>The two that R2d tells apart in the source are told apart in a message.</summary>
    [Fact]
    public void A_function_returning_a_throwing_coroutine_is_no_function_that_throws()
    {
        var message = Mismatch("fn pass(): (Coroutine<int>) throws Exception { try one(); return quiet(); }\n"
            + "fn use(): int { let f: fn() -> (Coroutine<int> throws Exception) = pass; return 1; }");
        Assert.Contains("'fn() -> (Coroutine<int>) throws Exception'", message);
        Assert.Contains("'fn() -> (Coroutine<int> throws Exception)'", message);
    }

    [Fact]
    public void A_function_returning_a_throwing_function_keeps_its_parentheses()
    {
        var message = Mismatch("fn make(): (fn() -> int throws Exception) { return one; }\nfn use(): int { let n: int = make; return n; }");
        Assert.Contains("'fn() -> (fn() -> int throws Exception)'", message);
    }

    [Fact]
    public void An_array_of_throwing_coroutines_keeps_its_parentheses() =>
        Assert.Contains("'(Coroutine<int> throws Exception)[]'",
            Mismatch("fn use(): int { let xs: (Coroutine<int> throws Exception)[] = [gen()]; let n: int = xs; return n; }"));

    /// <summary>The bare set is 'Error' and written bare — the text ended in a blank.</summary>
    [Fact]
    public void A_bare_set_ends_the_text() =>
        Assert.Contains("'Coroutine<int> throws'",
            Mismatch("fn open(): Coroutine<int> throws { yield try one(); }\nfn use(): int { let n: int = open(); return n; }"));

    /// <summary>The controls: what needed no parentheses gets none.</summary>
    [Theory]
    [InlineData("?int[]", "int", "'?int[]'")]
    [InlineData("int[][]", "int", "'int[][]'")]
    [InlineData("fn() -> fn() -> int", "int", "'fn() -> fn() -> int'")]
    [InlineData("fn() -> Coroutine<int>", "int", "'fn() -> Coroutine<int>'")]
    [InlineData("(fn() -> int)[]", "int", "'(fn() -> int)[]'")]
    public void What_needs_no_parentheses_gets_none(string type, string other, string shown) =>
        Assert.Contains(shown, Mismatch($"fn use(x: {type}): int {{ let n: {other} = x; return n; }}"));
}
