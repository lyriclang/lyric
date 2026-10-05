using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// Whose a <c>throws</c> is behind a return type that may carry a set of its own (the review's
/// M6-6; spec chapter 10 §2): a coroutine here, a task with the real library. Without
/// parentheses it is refused (<c>LYR-SEM0165</c>) — but in a generator, whose clause is its
/// coroutine's and nobody else's. One message: the body is not held to a return type its clause
/// leaves open.
/// </summary>
public class ThrowsWhoseTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private const string Head = """
        import std.core { Exception };

        fn one(): int throws Exception { return 1; }
        fn gen(): Coroutine<int> throws Exception { yield try one(); }
        fn quiet(): Coroutine<int> { yield 1; }

        """;

    private static DiagnosticEngine Check(string declarations)
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
        return de;
    }

    private static void Silent(string declarations)
    {
        var de = Check(declarations);
        Assert.True(de.Diagnostics.Count == 0, string.Join("\n", de.Diagnostics));
    }

    private static Diagnostic Refused(string declarations)
    {
        var de = Check(declarations);
        Assert.True(de.Diagnostics.Count == 1, string.Join("\n", de.Diagnostics));
        Assert.Equal("LYR-SEM0165", de.Diagnostics[0].Code);
        return de.Diagnostics[0];
    }

    /// <summary>The control, and the exception (10 §1 rule 6): a generator's clause is its
    /// coroutine's — the head of every program here.</summary>
    [Fact]
    public void A_generators_clause_is_its_coroutines() => Silent("");

    [Fact]
    public void A_function_that_hands_a_coroutine_on_says_whose_the_set_is()
    {
        var error = Refused("fn pass(): Coroutine<int> throws Exception { return gen(); }");
        Assert.Contains("'Coroutine<int>'", error.Message);
        Assert.Contains("the coroutine's or the function's", error.Message);
        var note = Assert.Single(error.Notes!).Message;
        Assert.Contains("'(Coroutine<int> throws …)'", note);
        Assert.Contains("'(Coroutine<int>) throws …'", note);
    }

    /// <summary>One message whichever was meant: the body's value is not held against a return
    /// type the clause leaves open — neither the throwing coroutine nor the plain one.</summary>
    [Theory]
    [InlineData("fn pass(): Coroutine<int> throws Exception { return gen(); }")]
    [InlineData("fn pass(): Coroutine<int> throws Exception { try one(); return quiet(); }")]
    public void The_refusal_is_the_one_message(string source) => Refused(source);

    [Theory]
    [InlineData("fn pass(): (Coroutine<int> throws Exception) { return gen(); }")]
    [InlineData("fn pass(): (Coroutine<int>) throws Exception { try one(); return quiet(); }")]
    [InlineData("fn pass(): (Coroutine<int> throws Exception) throws Exception { try one(); return gen(); }")]
    public void Parentheses_say_whose_it_is(string source) => Silent(source);

    /// <summary>In parentheses the two mean what they say: the throwing coroutine does not fit
    /// the plain one that the function's own clause leaves.</summary>
    [Fact]
    public void Behind_the_parentheses_the_set_is_the_functions()
    {
        var de = Check("fn pass(): (Coroutine<int>) throws Exception { return gen(); }");
        Assert.Equal("LYR-SEM0001", Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error).Code);
    }

    /// <summary>A '?' in front changes nothing: as a type, '?Coroutine&lt;int&gt; throws E' is the
    /// optional of the throwing coroutine. An array carries no set, and one that carries its
    /// own already leaves nothing open.</summary>
    [Fact]
    public void Under_an_optional_the_question_is_the_same()
    {
        Refused("fn pass(): ?Coroutine<int> throws Exception { return null; }");
        Silent("fn pass(): (?Coroutine<int>) throws Exception { try one(); return null; }");
        Silent("fn pass(): ?(Coroutine<int> throws Exception) throws Exception { try one(); return null; }");
        Silent("fn pass(): Coroutine<int>[] throws Exception { try one(); return []; }");
    }

    [Fact]
    public void A_member_without_a_body_is_no_generator()
    {
        Refused("interface Source { fn open(): Coroutine<int> throws Exception; }");
        Silent("interface Source { fn open(): (Coroutine<int> throws Exception); }");
    }

    /// <summary>A function TYPE's return type is one too — and it is asked once, though a type is
    /// resolved wherever it is used.</summary>
    [Fact]
    public void A_function_types_return_type_is_asked_the_same()
    {
        Refused("fn take(f: fn() -> Coroutine<int> throws Exception): int { return 1; }\nfn use(): int { return take(gen) + take(gen); }");
        Silent("fn take(f: fn() -> (Coroutine<int> throws Exception)): int { return 1; }\nfn use(): int { return take(gen); }");
        Silent("fn take(f: fn() -> (Coroutine<int>) throws Exception): int { return 1; }\nfn use(): int { return take(quiet); }");
    }

    [Fact]
    public void A_lambdas_written_return_type_is_asked_the_same()
    {
        // (The binding is never used, which is a warning of its own: the errors are asked.)
        var de = Check("fn use(): int { let m = (): Coroutine<int> throws Exception => gen(); return 1; }");
        Assert.Equal("LYR-SEM0165", Assert.Single(de.Diagnostics, d => d.Severity == Severity.Error).Code);
        de = Check("fn use(): int { let m = (): (Coroutine<int> throws Exception) => gen(); return 1; }");
        Assert.DoesNotContain(de.Diagnostics, d => d.Severity == Severity.Error);
        // A generator lambda's set is its coroutine's, like a generator function's.
        de = Check("fn use(): int { let m = (): Coroutine<int> throws Exception => { yield try one(); }; return 1; }");
        Assert.DoesNotContain(de.Diagnostics, d => d.Severity == Severity.Error);
    }
}
