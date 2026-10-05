using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// A block's parameter is bound by its target or by a fixation of another parameter's constraint
/// (lyric-spec 05 §13 rules 1, 2). One that neither names is bound by nothing, and the block
/// reaches no type: <c>extend&lt;T, U&gt; Box&lt;T&gt;</c>. It was taken without a word, and a call of
/// its member said the receiver "does not satisfy the block's constraints" — of which there are
/// none (<c>LYR-SEM0134</c>). Said at the block now (<c>LYR-SEM0170</c>), once.
/// </summary>
public class UnboundBlockParameterTests
{
    private const string Types = """
        struct Box<T> { v: T }
        interface Named { fn name(): string; }
        interface Source { type Item; fn get(): Self.Item; }

        """;

    private static List<Diagnostic> Errors(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Types + source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de, singleProgram: false);
        return de.Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
    }

    private static Diagnostic One(string source, string code)
    {
        var errors = Errors(source);
        Assert.True(errors.Count == 1 && errors[0].Code == code,
            $"expected exactly one {code}, got:\n" + string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
        return errors[0];
    }

    [Theory]
    [InlineData("extend<T, U> Box<T> { fn one(): int { return 1; } }", "'U'")]
    [InlineData("extend<U> Box<int> { fn one(): int { return 1; } }", "'U'")]
    [InlineData("extend<T, U :: [Named]> Box<T> { fn one(): int { return 1; } }", "'U'")]
    [InlineData("extend<T :: [Named], U, V> T { fn one(): int { return 1; } }", "'U', 'V'")]
    public void A_parameter_nothing_names_is_said_at_the_block(string block, string named)
    {
        var error = One(block, "LYR-SEM0170");
        Assert.Contains(named, error.Message);
        // at the first parameter nothing names, where it is declared
        Assert.Equal("U", (Types + block).Substring(error.Span.Start, error.Span.Length));
    }

    /// <summary>The call is not a second error: the block said what is wrong with it.</summary>
    [Fact]
    public void A_call_of_such_a_blocks_member_adds_no_error() =>
        One("""
            extend<T, U> Box<T> { fn one(): int { return 1; } }
            fn f(b: Box<int>): int { return b.one(); }
            """, "LYR-SEM0170");

    [Theory]
    [InlineData("extend<T> Box<T> { fn one(): int { return 1; } }")]
    [InlineData("extend<T :: [Named]> T { fn one(): int { return 1; } }")]
    [InlineData("extend<T> T[] { fn one(): int { return 1; } }")]
    [InlineData("extend<A, B> (A, B) { fn one(): int { return 1; } }")]
    // bound by a fixation: 'T' is the 'Item' of what 'S' is (05 §13 rule 2)
    [InlineData("extend<S :: [Source<Item = T>], T> Box<S> { fn item(): T { return this.v.get(); } }")]
    public void A_parameter_the_target_or_a_fixation_names_is_bound(string block)
    {
        var errors = Errors(block);
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(d => $"{d.Code}: {d.Message}")));
    }
}
