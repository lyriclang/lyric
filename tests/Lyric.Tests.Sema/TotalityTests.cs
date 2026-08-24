using System.Text;
using Lyric.Core;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Sema;

/// <summary>
/// The checker's totality contract, one stage above the parser's: NO input makes resolution or
/// type checking throw — a broken program is diagnostics, never an exception. Seeded, so a
/// failure reproduces exactly.
/// </summary>
public class TotalityTests
{
    private static void CheckAll(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("<fuzz>.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de);
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        Semantics.Analyze(comp, comp.Resolve(), de);
    }

    /// <summary>Fragments biased toward things the CHECKER has to survive: half-typed
    /// declarations, generics, conformances, patterns, operators over wrong types.</summary>
    private static readonly string[] Fragments =
    [
        "fn f", "<T>", "(x: T)", ": int", "{ return x; }", "{ }", ";",
        "struct S", ":: [A]", "{ x: int }", "enum E { A, B(int) }",
        "interface I { fn m(): int; }", "extend int { fn twice(): int { return this * 2; } }",
        "let g = ", "match (x) { 1 => 2, _ => 3 }", "if (x)", "else", "x + ", "\"s\" * ",
        "null", "this", "yield 1;", "resume x", "throw E;", "try { }", "catch (e) { }",
        "defer x;", "type A = int;", "opaque type H = int;", "import std.core;",
        "x?.y", "x!", "x ?? y", "(a, b)", "[1, 2]", "f<int>()", "S { x = 1 }",
        "for (i in 0..3) { }", "while (true) { break; }", "1.5f32", "@Deprecated",
    ];

    [Fact]
    public void Random_fragment_splices_never_throw()
    {
        var random = new Random(0x5E3A);
        for (var run = 0; run < 300; run++)
        {
            var sb = new StringBuilder();
            var parts = random.Next(1, 30);
            for (var i = 0; i < parts; i++)
            {
                sb.Append(Fragments[random.Next(Fragments.Length)]);
                sb.Append(' ');
            }
            CheckAll(sb.ToString());
        }
    }
}
