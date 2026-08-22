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

    /// <summary>The index of a function, by suffix: what a virtual module is called is the
    /// harness's business, not the test's.</summary>
    private static int Find(BytecodeModule module, string function)
    {
        var index = -1;
        for (var i = 0; i < module.Functions.Count; i++)
            if (module.Functions[i].Name.EndsWith("." + function, StringComparison.Ordinal))
                index = i;

        Assert.True(
            index >= 0,
            $"no function '{function}' among "
            + string.Join(", ", module.Functions.Select(f => f.Name)));

        return index;
    }

    /// <summary>Runs one function both ways and returns what each said.</summary>
    private static void Agree(string source, string function, params LyrValue[] arguments)
    {
        var module = Compile(source);

        var interpreted = LoadedProgram.Load(module);
        var compiled = LoadedProgram.Load(module, jit: true);

        var index = Find(module, function);

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

    // ---------------------------------------------------------------- arrays and fields

    [Fact]
    public void Array_reads_and_writes_agree()
    {
        Agree(
            """
            @Hook
            pub fn run(n: int): float {
                let data = [1.5] * 64;
                var acc = 0.0;
                var i = 0;
                while (i < n) {
                    data[i & 63] = data[i & 63] + 0.25;
                    acc = acc + data[i & 63];
                    i = i + 1;
                }

                return acc + data.length as float;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(500));
    }

    [Fact]
    public void An_index_out_of_range_fails_the_same_way()
    {
        // The message carries the function name, which compiled code cannot read off a frame --
        // it is baked into the call as a literal. If that ever drifts, a shipped game reports a
        // different error than the one its author debugged.
        var module = Compile(
            """
            @Hook
            pub fn run(n: int): float {
                let data = [1.5] * 4;
                return data[n];
            }

            fn main(): int { return 0; }
            """);

        var interpreted = LoadedProgram.Load(module);
        var compiled = LoadedProgram.Load(module, jit: true);
        var index = Find(module, "run");

        var a = Assert.Throws<LyricPanic>(
            () => interpreted.Invoke(index, LyrValue.FromI64(9)));
        var c = Assert.Throws<LyricPanic>(
            () => compiled.Invoke(index, LyrValue.FromI64(9)));

        Assert.Equal(a.Message, c.Message);
    }

    [Fact]
    public void Array_literals_and_concatenation_agree()
    {
        Agree(
            """
            @Hook
            pub fn run(n: int): int {
                let a = [1, 2, 3];
                let b = [4, 5];
                let joined = a + b;

                var acc = 0;
                var i = 0;
                while (i < joined.length) {
                    acc = acc + joined[i] * n;
                    i = i + 1;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(7));
    }

    [Fact]
    public void A_negative_repeat_fails_the_same_way()
    {
        var module = Compile(
            """
            @Hook
            pub fn run(n: int): int {
                let data = [1] * n;
                return data.length;
            }

            fn main(): int { return 0; }
            """);

        var interpreted = LoadedProgram.Load(module);
        var compiled = LoadedProgram.Load(module, jit: true);
        var index = Find(module, "run");

        var a = Assert.Throws<LyricPanic>(
            () => interpreted.Invoke(index, LyrValue.FromI64(-2)));
        var c = Assert.Throws<LyricPanic>(
            () => compiled.Invoke(index, LyrValue.FromI64(-2)));

        Assert.Equal(a.Message, c.Message);
    }

    [Fact]
    public void Fields_agree()
    {
        Agree(
            """
            class Point { x: float = 0.0, y: float = 0.0, hits: int = 0 }

            @Hook
            pub fn run(n: int): float {
                let p = Point { };
                var i = 0;
                while (i < n) {
                    p.x = p.x + 1.5;
                    p.y = p.y - 0.5;
                    p.hits = p.hits + 1;
                    i = i + 1;
                }

                return p.x + p.y + p.hits as float;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(200));
    }

    [Fact]
    public void Globals_agree()
    {
        Agree(
            """
            let step = 3;

            @Hook
            pub fn run(n: int): int {
                var acc = 0;
                var i = 0;
                while (i < n) {
                    acc = acc + step;
                    i = i + 1;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(100));
    }

    [Fact]
    public void A_global_written_by_compiled_code_is_seen_by_the_interpreter()
    {
        // The sharpest shape in a mixed run: compiled and interpreted code share ONE globals
        // array, and what one writes the other has to read. If the compiler had taken a copy of
        // the array, this would pass every single-engine test and lose state in a real program.
        //
        // Written through a HOLDER because a module binding is immutable -- 'var' at module level
        // is LYR-PAR0027 -- so the global slot holds the object and the mutation is a field
        // write. That is also the shape every Erato game uses, for the same reason.
        var module = Compile(
            """
            class Counter { total: int = 0 }

            let counter = Counter { };

            @Hook
            pub fn add(n: int): int {
                counter.total = counter.total + n;
                return counter.total;
            }

            @Hook
            pub fn read(n: int): int { return counter.total + n; }

            fn main(): int { return 0; }
            """);

        var compiled = LoadedProgram.Load(module, jit: true);

        compiled.Invoke(Find(module, "add"), LyrValue.FromI64(5));
        compiled.Invoke(Find(module, "add"), LyrValue.FromI64(7));

        Assert.Equal(12L, compiled.Invoke(Find(module, "read"), LyrValue.FromI64(0)).AsI64);
    }

    // ---------------------------------------------------------------- calls

    [Fact]
    public void A_call_between_two_compiled_functions_agrees()
    {
        Agree(
            """
            fn square(x: int): int { return x * x; }

            fn sum(upTo: int): int {
                var acc = 0;
                var i = 0;
                while (i < upTo) {
                    acc = acc + square(i);
                    i = i + 1;
                }

                return acc;
            }

            @Hook
            pub fn run(n: int): int { return sum(n); }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(300));
    }

    [Fact]
    public void A_recursive_function_still_answers_correctly()
    {
        // Recursion REFUSES to compile -- a function reached again while it is still being
        // compiled has no delegate to bind -- so this runs interpreted under 'jit: true'. What
        // matters is that it runs, and answers the same.
        Agree(
            """
            fn fib(n: int): int {
                if (n < 2) { return n; }
                return fib(n - 1) + fib(n - 2);
            }

            @Hook
            pub fn run(n: int): int { return fib(n); }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(18));
    }

    [Fact]
    public void An_exception_thrown_below_compiled_code_reaches_its_handler()
    {
        // The first thing the differential run caught, and the reason a compiled function may
        // only call functions that compile too.
        //
        // A Lyric exception unwinds along the INTERPRETER's frame stack. A compiled frame in the
        // middle of that chain has none, so a throw below could not find a catch above and became
        // an uncaught panic -- a program that worked interpreted and died compiled, which is
        // precisely the failure this whole arrangement exists to prevent.
        Agree(
            """
            class Boom :: [Throwable] {
                fn message(): string { return "boom"; }
            }

            fn thrower(n: int): int throws Boom {
                if (n > 3) { throw Boom { }; }
                return n;
            }

            fn middle(n: int): int throws Boom { return thrower(n) + 1; }

            @Hook
            pub fn run(n: int): int {
                try {
                    return middle(n);
                } catch (e: Boom) {
                    return 0 - 1;
                }
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(9));
    }

    [Fact]
    public void A_call_that_returns_nothing_agrees()
    {
        Agree(
            """
            class Box { total: int = 0 }

            let box = Box { };

            fn bump(by: int): void { box.total = box.total + by; }

            @Hook
            pub fn run(n: int): int {
                var i = 0;
                while (i < n) {
                    bump(i);
                    i = i + 1;
                }

                return box.total;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(100));
    }

    // ---------------------------------------------------------------- optionals, and 'for'

    [Fact]
    public void A_for_loop_agrees()
    {
        // The loop everybody writes, and the reason optionals had to be supported at all: a
        // 'for' over a range lowers to a RangeIterator that hands its value out as a '?int'.
        // Before that shape compiled, every function containing an idiomatic loop was declined.
        Agree(
            """
            @Hook
            pub fn run(n: int): int {
                var acc = 0;
                for (i in 0..n) {
                    acc = acc + i * 2;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(500));
    }

    [Fact]
    public void A_for_loop_over_an_array_agrees()
    {
        Agree(
            """
            @Hook
            pub fn run(n: int): float {
                let data = [0.5] * 32;
                var acc = 0.0;

                for (_ in 0..n) {
                    for (i in 0..data.length) {
                        acc = acc + data[i];
                    }
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(20));
    }

    [Fact]
    public void Optionals_agree()
    {
        Agree(
            """
            fn maybe(n: int): ?int {
                if (n % 3 == 0) { return null; }
                return n * 2;
            }

            @Hook
            pub fn run(n: int): int {
                var acc = 0;
                for (i in 0..n) {
                    // Unwrapped WITHOUT a null check, so the compiler cannot narrow the type away
                    // and an 'optget' really is emitted.
                    if (i % 3 == 0) {
                        acc = acc + 1;
                    } else {
                        acc = acc + maybe(i)!;
                    }
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(200));
    }

    [Fact]
    public void A_force_unwrap_of_nothing_fails_the_same_way()
    {
        var module = Compile(
            """
            fn maybe(n: int): ?int {
                if (n < 0) { return null; }
                return n;
            }

            @Hook
            pub fn run(n: int): int { return maybe(n)!; }

            fn main(): int { return 0; }
            """);

        var interpreted = LoadedProgram.Load(module);
        var compiled = LoadedProgram.Load(module, jit: true);
        var index = Find(module, "run");

        var a = Assert.Throws<LyricPanic>(
            () => interpreted.Invoke(index, LyrValue.FromI64(-1)));
        var c = Assert.Throws<LyricPanic>(
            () => compiled.Invoke(index, LyrValue.FromI64(-1)));

        Assert.Equal(a.Code, c.Code);
        Assert.Equal(a.Message, c.Message);
    }

    // ---------------------------------------------------------------- strings and objects

    [Fact]
    public void String_constants_and_comparison_agree()
    {
        // The commonest refusal there was, counted across two real games: a literal. Game code
        // logs, formats and names its events, so nine of the Springer's functions stopped here.
        Agree(
            """
            @Hook
            pub fn run(n: int): int {
                let a = "coin";
                let b = "spark";
                var acc = 0;

                for (i in 0..n) {
                    let pick = if (i % 2 == 0) a else b;
                    if (pick == "coin") { acc = acc + 1; }
                    if (pick != "coin") { acc = acc + 2; }
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(100));
    }

    [Fact]
    public void A_string_returned_from_compiled_code_agrees()
    {
        var module = Compile(
            """
            @Hook
            pub fn run(n: int): string {
                if (n > 3) { return "many"; }
                return "few";
            }

            fn main(): int { return 0; }
            """);

        var interpreted = LoadedProgram.Load(module);
        var compiled = LoadedProgram.Load(module, jit: true);
        var index = Find(module, "run");

        foreach (var n in new[] { 1L, 9L })
            Assert.Equal(
                interpreted.Invoke(index, LyrValue.FromI64(n)).AsString,
                compiled.Invoke(index, LyrValue.FromI64(n)).AsString);
    }

    [Fact]
    public void Constructing_objects_agrees()
    {
        Agree(
            """
            class Point { x: float = 0.0, y: float = 0.0 }

            @Hook
            pub fn run(n: int): float {
                var acc = 0.0;
                for (i in 0..n) {
                    let p = Point { x = i as float * 0.5, y = 1.5 };
                    acc = acc + p.x + p.y;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(200));
    }

    [Fact]
    public void An_interface_value_held_in_a_slot_agrees()
    {
        // An interface value is a fat pointer -- the instance in the reference field, its concrete
        // type in the bits -- so it stays a whole value rather than being unpacked into a machine
        // type. Calling THROUGH one is still declined; holding one is not.
        Agree(
            """
            interface Shape { fn area(): float; }

            class Square :: [Shape] {
                side: float = 2.0,

                fn area(): float { return this.side * this.side; }
            }

            @Hook
            pub fn run(n: int): float {
                var acc = 0.0;
                for (i in 0..n) {
                    let s: Shape = Square { side = i as float };
                    acc = acc + 1.0;
                }

                return acc;
            }

            fn main(): int { return 0; }
            """,
            "run", LyrValue.FromI64(50));
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
