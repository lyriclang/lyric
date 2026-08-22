using Lyric.Bytecode;
using Lyric.Core;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// The compiler must answer exactly what the interpreter answers.
///
/// <para><b>Why this is the only test that matters for a JIT.</b> A host is meant to develop on
/// the interpreter — where breakpoints, stepping and hot reload work — and ship with compilation
/// on. That makes any divergence between the two the worst failure a language can have: it works
/// while you build it and breaks when you hand it out, and nothing on the way there can catch
/// it.</para>
///
/// <para>So every case here loads the SAME module twice, once each way, in one process, and
/// demands the same bits. That is also why the assertions are on bits rather than on a rendered
/// value: two answers that print alike and differ in a low bit is exactly the bug this is for.
/// </para>
///
/// <para>The whole suite can be run this way too — <c>LYRIC_JIT=1</c> compiles every program in
/// the process — which covers far more programs than could be written out here. These cases pin
/// the parts where the two engines could most plausibly drift apart: NaN, unsigned comparison,
/// integer wrap, and the shift count.</para>
/// </summary>
public class JitAgreementTests
{
    /// <summary>
    /// The attribute is load-bearing, not decoration: a function with no caller the compiler can
    /// see is inlined away and pruned, and an attributed function is a ROOT. Without it every
    /// case here would compile to a module containing only 'main'.
    /// </summary>
    private const string Head =
        """
        import std.core { OnFunction };

        pub struct Hook :: [OnFunction] { }

        """;

    private static BytecodeModule Compile(string source)
    {
        source = Head + source;

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
        Assert.False(de.HasErrors, "source did not compile:\n" + writer);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);

        return BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!));
    }

    private static string RepoRoot(
        [System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    /// <summary>Runs one function both ways and returns what each said.</summary>
    private static void Agree(string source, string function, params LyrValue[] arguments)
    {
        var module = Compile(source);

        var interpreted = LoadedProgram.Load(module);
        var compiled = LoadedProgram.Load(module, jit: true);

        // By suffix: what a virtual module is called is the harness's business, not the test's.
        var index = -1;
        for (var i = 0; i < module.Functions.Count; i++)
            if (module.Functions[i].Name.EndsWith("." + function, StringComparison.Ordinal))
                index = i;

        Assert.True(
            index >= 0,
            $"no function '{function}' among "
            + string.Join(", ", module.Functions.Select(f => f.Name)));

        var a = interpreted.Invoke(index, arguments);
        var b = compiled.Invoke(index, arguments);

        Assert.True(
            a.Bits == b.Bits,
            $"'{function}': interpreted {a.Bits:X16}, compiled {b.Bits:X16}");
    }

    [Fact]
    public void Integer_arithmetic_agrees()
    {
        Agree(
            """
            @Hook
            pub fn run(n: int): int {
                var acc = 0;
                var i = 0;
                while (i < n) {
                    acc = acc + i * 3 - 1;
                    i = i + 1;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(1000));
    }

    [Fact]
    public void Integer_overflow_wraps_the_same_way()
    {
        // Lyric wraps; .NET's 'add' wraps too, but only because the emitter does not use the
        // checked form. Worth pinning: a 'add.ovf' would throw where the interpreter wraps.
        Agree(
            """
            @Hook
            pub fn run(n: int): int {
                var acc = 9223372036854775807;
                var i = 0;
                while (i < n) {
                    acc = acc + 1;
                    i = i + 1;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(5));
    }

    [Fact]
    public void Float_arithmetic_agrees()
    {
        Agree(
            """
            @Hook
            pub fn run(n: int): float {
                var acc = 0.0;
                var i = 0;
                while (i < n) {
                    acc = acc + 1.5 * 2.0 - 0.25;
                    i = i + 1;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(1000));
    }

    [Fact]
    public void NaN_compares_the_same_way()
    {
        // The sharpest disagreement available. The interpreter uses ordinary C# operators, where
        // every comparison with NaN is false except '!='. In IL that is 'clt' for '<' but
        // '!cgt.un' for '<=' -- and the obvious wrong emit, '!cgt', answers TRUE for NaN.
        Agree(
            """
            @Hook
            pub fn run(n: int): int {
                let nan = 0.0 / 0.0;
                var flags = 0;

                if (nan < 1.0) { flags = flags + 1; }
                if (nan <= 1.0) { flags = flags + 2; }
                if (nan > 1.0) { flags = flags + 4; }
                if (nan >= 1.0) { flags = flags + 8; }
                if (nan == nan) { flags = flags + 16; }
                if (nan != nan) { flags = flags + 32; }

                return flags + n;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(0));
    }

    [Fact]
    public void Negative_numbers_compare_signed()
    {
        // If the emitter reached for the unsigned compare, -1 would be the largest number there
        // is and every one of these would flip.
        Agree(
            """
            @Hook
            pub fn run(n: int): int {
                var flags = 0;

                if (0 - 1 < 1) { flags = flags + 1; }
                if (0 - 1 > 1) { flags = flags + 2; }
                if (0 - 5 <= 0 - 5) { flags = flags + 4; }

                return flags + n;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(0));
    }

    [Fact]
    public void Shifts_agree()
    {
        Agree(
            """
            @Hook
            pub fn run(n: int): int {
                var acc = 0;
                var i = 0;
                while (i < n) {
                    acc = acc + (1 << i) + (0 - 1024 >> 3) + (i & 7) + (i | 16) + (i ^ 3);
                    i = i + 1;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(40));
    }

    [Fact]
    public void Booleans_agree()
    {
        Agree(
            """
            @Hook
            pub fn run(n: int): bool {
                var flag = false;
                var i = 0;
                while (i < n) {
                    flag = !flag;
                    i = i + 1;
                }

                return flag;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(7));
    }

    [Fact]
    public void A_refused_function_still_runs()
    {
        // Arrays are not compiled yet, so this one stays on the interpreter -- and the point of
        // the test is that it still produces the right answer under 'jit: true'. A refusal has to
        // cost speed and nothing else.
        Agree(
            """
            @Hook
            pub fn run(n: int): float {
                let data = [1.5] * 8;
                var acc = 0.0;
                var i = 0;
                while (i < n) {
                    acc = acc + data[i & 7];
                    i = i + 1;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(100));
    }

    [Fact]
    public void A_compiled_function_called_from_interpreted_code_agrees()
    {
        // The mixed shape a game actually has: the outer function touches something the compiler
        // declines, the inner one is pure arithmetic. The call has to cross from a frame into
        // compiled code and come back with the right value on the stack.
        Agree(
            """
            fn inner(x: int): int {
                var acc = 0;
                var i = 0;
                while (i < x) {
                    acc = acc + i;
                    i = i + 1;
                }

                return acc;
            }

            @Hook
            pub fn run(n: int): int {
                let data = [0] * 4;
                var acc = 0;
                var i = 0;
                while (i < n) {
                    acc = acc + inner(i) + data[i & 3];
                    i = i + 1;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(50));
    }
}
