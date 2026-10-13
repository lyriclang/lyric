using Lyric.AST;
using Lyric.Core;
using Lyric.Formatting;
using Lyric.Ir;
using Lyric.Ir.Interp;
using Lyric.Parsing;
using Lyric5.Toolchain;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>std.syntax</c> and the bridge (design/v5/spec/10 B13 M2; 13 M9b S1): the compiler's tree as
/// values and back. The round trip over every program of the emitter tests is the slice's
/// artifact: tree, values, tree, and the formatter writes the same text for both.
/// </summary>
public class SyntaxTests
{
    private static readonly Lazy<(IrModule Ir, SyntaxBridge Bridge)> Fixture = new(() =>
    {
        // the types the bridge needs are interned by their mention: an optional of each root
        const string program = """
            import std.syntax { Expr, Stmt, Decl, Type, Pattern };

            fn main(): void {
                let e: ?Expr = null;
                let s: ?Stmt = null;
                let d: ?Decl = null;
                let t: ?Type = null;
                let p: ?Pattern = null;
            }
            """;
        var result = TestCompiler.Lower("syntax-fixture.lyr", program);
        var rendered = new StringWriter();
        result.Diagnostics.RenderText(rendered);
        Assert.True(result.Ok && result.Ir is not null, rendered.ToString());
        return (result.Ir!, new SyntaxBridge(result.Ir!));
    });

    private static IrType Root(string name)
    {
        var ir = Fixture.Value.Ir;
        for (var i = 0; i < ir.Types.Count; i++)
            if (ir.Types[i].Module == "std.syntax" && ir.Types[i].Name == name) return new IrEnumType(new TypeId(i));
        throw new Xunit.Sdk.XunitException($"std.syntax.{name} is not in the fixture's module");
    }

    private static Module Parse(string displayName, string text)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual(displayName, text);
        var de = new DiagnosticEngine(sm);
        var module = new Parser(sm, id, de).ParseModule();
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return module;
    }

    private static Stmt[] ParseStatements(string text)
    {
        var sm = new SourceManager { Lyric5 = true };
        var id = sm.AddVirtual("<stmts>", text);
        var de = new DiagnosticEngine(sm);
        var stmts = new Parser(sm, id, de).ParseStatements();
        Assert.False(de.HasErrors, string.Join("\n", de.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
        return stmts;
    }

    /// <summary>The statement through the values and back, both sides as the formatter writes a
    /// built tree.</summary>
    private static (string Before, string After) RoundTrip(Stmt stmt)
    {
        var bridge = Fixture.Value.Bridge;
        var type = Root("Stmt");
        var value = bridge.Stmt(stmt, type);
        return (AstFormatter.FormatNode(stmt), AstFormatter.FormatNode(bridge.StmtOf(value, type)));
    }

    private static (string Before, string After) RoundTrip(Decl decl)
    {
        var bridge = Fixture.Value.Bridge;
        var type = Root("Decl");
        var value = bridge.Decl(decl, type);
        return (AstFormatter.FormatNode(decl), AstFormatter.FormatNode(bridge.DeclOf(value, type)));
    }

    [Theory]
    [InlineData("while (x < 3) { x++; }")]
    [InlineData("let (a, b) = pair;")]
    [InlineData("if (let .Some(v) = opt) { use(v); } else if (c) { other(); } else { last(); }")]
    [InlineData("outer: for ((k, v) in m) { if (k == 0) { continue outer; } total += v; }")]
    [InlineData("let r = match (s) { .Circle(r) if r > 0.0 => r * r, Rect { w, h = 2 } => w, _ => 0.0 };")]
    [InlineData("let f = (a: int, &b: int): int throws E => a + b;")]
    [InlineData("xs.each { it * 2 };")]
    [InlineData("xs.fold(0) { acc, x => acc + x };")]
    [InlineData("let v = try? parse(s) ?? -1;")]
    [InlineData("try { risky(); } catch (e: IoError) { log(e); } catch (e in [A, B]) { other(e); } catch (_) { }")]
    [InlineData("let t: (x: int, y: string) = (1, \"a\");")]
    [InlineData("let g: fn(&int, string) -> ?int throws E = h;")]
    [InlineData("defer close(f);")]
    [InlineData("let n = loop { if (done()) { break 3; } };")]
    [InlineData("let p = Point { x = 1, y };")]
    [InlineData("let q = p with { pos.x = 2 };")]
    [InlineData("let s = f\"{a + 1:x} and {b}\";")]
    [InlineData("let z = -1.5e3 + 'c' + \"q\\\"t\\n\" + b\"ab\" + 0x10u8;")]
    [InlineData("yield x[1..=3][..2];")]
    [InlineData("comptime for (f in fields<T>()) { total += 1; }")]
    [InlineData("let y = twice!(x, 1) + run! { 2 };")]
    [InlineData("retry!(3) { try fetch(url) }")]
    public void A_statement_survives_the_round_trip(string text)
    {
        var stmts = ParseStatements(text);
        Assert.Single(stmts);
        var (before, after) = RoundTrip(stmts[0]);
        Assert.Equal(before, after);
    }

    [Theory]
    [InlineData("@Derive(ToJson)\npub struct P<T :: [Display] = int> :: [Debug, Walker by legs] { pub x: int, var legs: T = 0, static let zero: int = 0; fn f(): void { } }")]
    [InlineData("pub enum E<T> :: [Equatable] { A, B(int, T), C { x: int }; type Item = T; static let all: E<T>[] = []; fn g(): void { } }")]
    [InlineData("sealed interface Shape :: [Debug] { type Out :: [Display]; static let sides: int; fn area(): float; private fn h(): int { return 1; } }")]
    [InlineData("extend<T :: [Display]> List<T> :: [Show] { type Item = T; static let k: int = 1; fn show(): string { return \"\"; } }")]
    [InlineData("pub opaque type Handle = int;")]
    [InlineData("pub import a.b { x, y as z };")]
    [InlineData("import a.b as c;")]
    [InlineData("extern \"C\" fn puts(s: string): int = \"puts\";")]
    [InlineData("inline fn twice(xs: int..., @callerExpr(actual) msg: string = \"\"): (int) throws [A, B] { return xs[0]; }")]
    [InlineData("pub let names: string[] = [\"a\"];")]
    public void A_declaration_survives_the_round_trip(string text)
    {
        var module = Parse("<decls>", text);
        Assert.Single(module.Declarations);
        var (before, after) = RoundTrip(module.Declarations[0]);
        Assert.Equal(before, after);
    }

    public static IEnumerable<object[]> Programs_() =>
        Directory.GetFiles(CEmitterTests.Programs, "*.lyr").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n)
            .Select(n => new object[] { n! });

    /// <summary>The artifact (13 M9b S1): every declaration of every program of the emitter tests
    /// goes through the values and comes back the same. What <c>std.syntax</c> has no form for
    /// (the rounds of a <c>comptime for</c> over fields, <c>using let</c>, a local function) is
    /// counted, not hidden.</summary>
    [Theory]
    [MemberData(nameof(Programs_))]
    public void A_program_survives_the_round_trip(string name)
    {
        var text = File.ReadAllText(Path.Combine(CEmitterTests.Programs, name + ".lyr"));
        var module = Parse($"programs/{name}.lyr", text);
        var skipped = new List<string>();
        foreach (var decl in module.Declarations)
        {
            try
            {
                var (before, after) = RoundTrip(decl);
                Assert.True(before == after, $"{name}: a declaration changed in the round trip\n--- before\n{before}\n--- after\n{after}");
            }
            catch (ComptimeFailure f) when (f.Code == "LYR-CT0005")
            {
                skipped.Add(f.Message);
            }
        }
        Assert.True(skipped.Count <= 2, $"{name}: {skipped.Count} declarations have no form:\n{string.Join("\n", skipped)}");
    }

    [Fact]
    public void Statements_with_blocks_are_written_whole()
    {
        var stmts = ParseStatements("while (x < 3) { x++; }");
        Assert.Equal("while (x < 3) {\n    x++;\n}", AstFormatter.FormatStatements(stmts));
        var bridge = Fixture.Value.Bridge;
        var type = Root("Stmt");
        var array = Value.Of(new ArrayObj([bridge.Stmt(stmts[0], type)]));
        Assert.Equal("while (x < 3) {\n    x++;\n}", bridge.SourceOfStatements(array, new IrArrayType(type)));
    }

    [Fact]
    public void A_text_that_is_no_syntax_is_a_panic()
    {
        var bridge = Fixture.Value.Bridge;
        var failure = Assert.Throws<ComptimeFailure>(() => bridge.ParseExpr("1 +"));
        Assert.Equal("LYR-CT0001", failure.Code);
        Assert.Contains("the text is no syntax", failure.Message);
    }
}
