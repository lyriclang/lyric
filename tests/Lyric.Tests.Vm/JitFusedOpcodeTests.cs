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
/// The fused instructions of format 3.6, compiled.
///
/// <para>The two halves of this VM were built in parallel on separate branches: the interpreter
/// learned to execute FEWER instructions (M32) while the emitter learned to execute them
/// natively. They met here, and the meeting is not automatic — the emitter refuses what it does
/// not understand, so without these four cases it would decline exactly the loops that matter.
/// Every <c>while</c> in the language carries a <c>brcmp</c> now, and every accumulator a
/// <c>binlk</c>.</para>
///
/// <para>What makes them EASIER to compile than the pairs they replace: they carry their operands
/// as slots, so nothing goes through the evaluation stack. <c>binlk add f64 l1 = l1, 1.5</c> is a
/// load, a constant, an add and a store; <c>brcmpk lt i64 l0, k -&gt; t, f</c> is a comparison and
/// a branch.</para>
/// </summary>
public class JitFusedOpcodeTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static BytecodeModule Compile(string source)
    {
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

        var writer = new StringWriter();
        de.RenderText(writer);
        Assert.False(de.HasErrors, "source did not compile: " + writer);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);
        return BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!));
    }

    /// <summary>Loads twice — once interpreted, once compiled — and demands the same answer plus
    /// a compilation that actually happened.</summary>
    private static (long Interpreted, long Compiled, IReadOnlyList<(string Function, string Reason)> Refusals)
        Both(string source, string function, params LyrValue[] arguments)
    {
        var module = Compile(source);

        var plain = LoadedProgram.Load(module,
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null));
        var jitted = LoadedProgram.Load(module,
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null), jit: true);

        var interpreted = plain.Invoke(plain.IndexOfFunction(function), arguments).AsI64;
        var compiled = jitted.Invoke(jitted.IndexOfFunction(function), arguments).AsI64;

        return (interpreted, compiled, jitted.JitRefusals);
    }

    [Fact]
    public void A_counting_loop_compiles_and_agrees()
    {
        // Three instructions per iteration since 3.6, and all three are fused or a jump: without
        // the four cases this function is refused outright.
        var (interpreted, compiled, refusals) = Both("""
            pub fn count(n: int): int {
                var i = 0;
                while (i < n) {
                    i = i + 1;
                }
                return i;
            }
            """, "main.count", LyrValue.FromI64(1000));

        Assert.Equal(1000, interpreted);
        Assert.Equal(interpreted, compiled);
        Assert.DoesNotContain(refusals, r => r.Function.Contains("count", StringComparison.Ordinal));
    }

    [Fact]
    public void An_accumulator_loop_compiles_and_agrees()
    {
        var (interpreted, compiled, refusals) = Both("""
            pub fn total(n: int): int {
                var i = 0;
                var acc = 0;
                while (i < n) {
                    acc = acc + 3;
                    i = i + 1;
                }
                return acc;
            }
            """, "main.total", LyrValue.FromI64(100));

        Assert.Equal(300, interpreted);
        Assert.Equal(interpreted, compiled);
        Assert.DoesNotContain(refusals, r => r.Function.Contains("total", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("a < b", 1, 2, 1)]
    [InlineData("a < b", 2, 1, 0)]
    [InlineData("a <= b", 2, 2, 1)]
    [InlineData("a > b", 3, 1, 1)]
    [InlineData("a >= b", 1, 3, 0)]
    [InlineData("a == b", 4, 4, 1)]
    [InlineData("a != b", 4, 4, 0)]
    public void Every_fused_comparison_branches_the_same_way(
        string condition, long a, long b, long expected)
    {
        var (interpreted, compiled, _) = Both($$"""
            pub fn pick(a: int, b: int): int {
                if ({{condition}}) {
                    return 1;
                }
                return 0;
            }
            """, "main.pick", LyrValue.FromI64(a), LyrValue.FromI64(b));

        Assert.Equal(expected, interpreted);
        Assert.Equal(interpreted, compiled);
    }

    [Theory]
    [InlineData("+", 7, 3, 10)]
    [InlineData("-", 7, 3, 4)]
    [InlineData("*", 7, 3, 21)]
    [InlineData("/", 7, 3, 2)]
    [InlineData("%", 7, 3, 1)]
    [InlineData("&", 6, 3, 2)]
    [InlineData("|", 6, 3, 7)]
    [InlineData("^", 6, 3, 5)]
    public void Every_fused_operation_computes_the_same(string op, long a, long b, long expected)
    {
        var (interpreted, compiled, _) = Both($$"""
            pub fn apply(a: int, b: int): int {
                var out = 0;
                out = a {{op}} b;
                return out;
            }
            """, "main.apply", LyrValue.FromI64(a), LyrValue.FromI64(b));

        Assert.Equal(expected, interpreted);
        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void A_fused_operation_writing_into_its_own_source_agrees()
    {
        // 'acc = acc - b': the destination is also an operand, which the interpreter handles by
        // reading both before writing. Compiled, the same has to hold.
        var (interpreted, compiled, _) = Both("""
            pub fn shrink(a: int, b: int): int {
                var acc = a;
                acc = acc - b;
                acc = acc - b;
                return acc;
            }
            """, "main.shrink", LyrValue.FromI64(10), LyrValue.FromI64(3));

        Assert.Equal(4, interpreted);
        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void A_fused_float_constant_keeps_its_precision()
    {
        var (interpreted, compiled, _) = Both("""
            pub fn near(n: int): int {
                var acc = 0.0;
                var i = 0;
                while (i < n) {
                    acc = acc + 0.1;
                    i = i + 1;
                }

                // The comparison is the point: a float rebuilt at the wrong width would drift.
                return if (acc > 0.99 && acc < 1.01) 1 else 0;
            }
            """, "main.near", LyrValue.FromI64(10));

        Assert.Equal(1, interpreted);
        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void An_unsigned_fused_comparison_stays_unsigned()
    {
        // The tag decides the machine operation on both engines, and a signed comparison here
        // would answer the other way.
        var (interpreted, compiled, _) = Both("""
            pub fn below(a: uint, b: uint): int {
                if (a < b) {
                    return 1;
                }
                return 0;
            }
            """, "main.below", LyrValue.FromBits(1), LyrValue.FromBits(ulong.MaxValue));

        Assert.Equal(1, interpreted);
        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void A_fused_division_by_zero_panics_on_both_engines()
    {
        var module = Compile("""
            pub fn divide(a: int, b: int): int {
                var out = 0;
                out = a / b;
                return out;
            }
            """);

        foreach (var jit in new[] { false, true })
        {
            var program = LoadedProgram.Load(module,
                NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null), jit: jit);

            var panic = Assert.Throws<LyricPanic>(() => program.Invoke(
                program.IndexOfFunction("main.divide"),
                LyrValue.FromI64(1), LyrValue.FromI64(0)));

            Assert.Equal(VmDiagnostics.DivisionByZero, panic.Code);
        }
    }
}
