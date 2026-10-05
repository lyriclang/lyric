using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// An enum's constants (design/v5/spec/08; the review's M8a-10): a <c>static let</c> behind the
/// variants is the enum's under its name, typed and checked as a struct's is — its own scope,
/// no <c>this</c>, not written.
/// </summary>
public class EnumStaticTests
{
    private const string Head = """
        enum Level {
            Low, High;
            static let fallback: Level = Level.Low;
            static let count: int = 2;
            static let all = [Level.Low, Level.High];
            fn rank(): int { return match (this) { .Low => 0, .High => 1 }; }
        }

        """;

    private static DiagnosticEngine Check(string declarations, string head = Head)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", head + declarations + "\n");
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
        return de;
    }

    private static void Silent(string declarations)
    {
        var de = Check(declarations);
        Assert.True(de.Diagnostics.Count == 0, string.Join("\n", de.Diagnostics));
    }

    private static Diagnostic OneError(string declarations, string head = Head)
    {
        var errors = Check(declarations, head).Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
        Assert.True(errors.Count == 1, string.Join("\n", errors));
        return errors[0];
    }

    [Fact]
    public void A_constant_is_read_under_the_enums_name() =>
        Silent("fn use(): int { return Level.fallback.rank() + Level.count + Level.all.length(); }");

    /// <summary>Its type is the one it writes, or its initializer's.</summary>
    [Fact]
    public void A_constant_has_its_type()
    {
        Silent("fn use(): int { let l: Level = Level.fallback; let n: int = Level.count; let a: Level[] = Level.all; return n + l.rank() + a.length(); }");
        Assert.Equal("LYR-SEM0001", OneError("fn use(): string { return Level.count; }").Code);
    }

    [Fact]
    public void An_initializer_is_held_to_the_written_type() =>
        Assert.Equal("LYR-SEM0001", OneError("fn use(): int { return 0; }",
            "enum Level { Low, High; static let count: int = Level.Low; }\n").Code);

    [Fact]
    public void A_constant_is_not_written() =>
        Assert.Equal("LYR-SEM0019", OneError("fn use(): void { Level.count = 3; }").Code);

    /// <summary>There is no instance where a constant is filled.</summary>
    [Fact]
    public void An_initializer_has_no_this() =>
        Assert.Equal("LYR-SEM0008", OneError("fn use(): int { return 0; }",
            "enum Level { Low, High; static let mine: int = this.rank(); fn rank(): int { return 0; } }\n").Code);

    /// <summary>A constant and a variant share the enum's names.</summary>
    [Fact]
    public void A_constant_named_like_a_variant_is_refused() =>
        Assert.Equal("LYR-RES0001", OneError("fn use(): int { return 0; }",
            "enum Level { Low, High; static let Low: int = 1; }\n").Code);
}
