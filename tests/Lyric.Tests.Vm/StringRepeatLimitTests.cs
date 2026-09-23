using System.Runtime.CompilerServices;
using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// <c>"ab" * n</c> answers or panics; it never answers wrongly.
///
/// <para>THE WORST SHAPE A BUG CAN HAVE. The factor is an <c>int</c> in the language and a .NET
/// string is indexed by a 32-bit int, and the cast between them was unchecked — so
/// <c>"ab" * 9000000000</c> did not fail, did not run out of memory and did not panic. It returned
/// a string of 820130816 code points (18 billion modulo 2^32) and reported that length without a
/// word. The array form beside it, <c>[0] * n</c>, has answered <c>LYR-VM0006</c> since 3.4.1; the
/// string form was simply never asked.</para>
///
/// <para>The boundary is pinned from both sides. A limit that refused everything would satisfy the
/// refusal half on its own, and the shape that hid this bug for so long is exactly a call that
/// succeeds when it should not.</para>
/// </summary>
public class StringRepeatLimitTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static long Run(string body)
    {
        var source = $"import std.string;\nfn main(): int {{ {body} }}";
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);

        var diagnostics = new StringWriter();
        de.RenderText(diagnostics);
        Assert.False(de.HasErrors, "source did not compile: " + diagnostics);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);

        return Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null)).AsI64;
    }

    /// <summary>A factor past what a string can hold is a panic with a code, not a short string.</summary>
    [Theory]
    [InlineData("\"ab\" * 9000000000")]          // the measured case: answered 820130816 before
    [InlineData("\"ab\" * 2147483647")]          // 2 x int.MaxValue, just past the edge
    [InlineData("\"abcd\" * 1073741824")]        // a longer source reaches the edge at a lower factor
    public void A_repetition_that_cannot_be_held_panics(string expr)
    {
        var panic = Assert.Throws<LyricPanic>(() => Run($"let s = {expr}; return s.length();"));
        Assert.Equal("LYR-VM0006", panic.Code);
        Assert.Contains("exceeds the length a string can hold", panic.Message, StringComparison.Ordinal);
    }

    /// <summary>The control, and the half that a refuse-everything limit would fail.</summary>
    [Theory]
    [InlineData("\"ab\" * 3", 6)]
    [InlineData("\"ab\" * 0", 0)]
    [InlineData("\"ab\" * -5", 0)]               // a negative factor is the empty string, not an error
    [InlineData("\"\" * 9000000000", 0)]         // nothing repeated is nothing, at any factor
    [InlineData("\"x\" * 1000000", 1000000)]     // large and legitimate
    public void An_ordinary_repetition_still_answers(string expr, long length)
        => Assert.Equal(length, Run($"let s = {expr}; return s.length();"));

    /// <summary>The characters are still the right ones — a length alone would not say that.</summary>
    [Fact]
    public void The_result_is_the_source_repeated()
        => Assert.Equal(1, Run("let s = \"ab\" * 3; if (s == \"ababab\") { return 1; } return 0;"));
}
