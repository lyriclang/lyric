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
/// Overloading (v3.0): several functions of one name, told apart by what they TAKE.
///
/// <para>The second answer this language has to "one name, several types" — generics being the
/// first — and admitted deliberately. These tests hold the part a reader has to be able to predict:
/// which one runs.</para>
/// </summary>
public class OverloadTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static long Run(string source)
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
        return Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null)).AsI64;
    }

    [Fact]
    public void The_argument_type_picks_the_function()
    {
        Assert.Equal(21, Run("""
            fn code(n: int): int { return 1; }
            fn code(s: string): int { return 20; }

            fn main(): int {
                return code(7) + code("seven");
            }
            """));
    }

    [Fact]
    public void The_argument_count_picks_it_too()
    {
        Assert.Equal(312, Run("""
            fn code(a: int): int { return 2; }
            fn code(a: int, b: int): int { return 10; }
            fn code(a: int, b: int, c: int): int { return 300; }

            fn main(): int {
                return code(1) + code(1, 2) + code(1, 2, 3);
            }
            """));
    }

    [Fact]
    public void An_exact_match_beats_one_that_would_convert()
    {
        // '2' is an int and there is an int overload, so the float one does not get it — the
        // literal adapts only where nothing takes it as written.
        Assert.Equal(1, Run("""
            fn kind(n: int): int { return 1; }
            fn kind(f: float): int { return 2; }

            fn main(): int {
                return kind(2);
            }
            """));
    }

    [Fact]
    public void A_literal_still_adapts_when_nothing_takes_it_as_written()
    {
        Assert.Equal(2, Run("""
            fn kind(f: float): int { return 2; }
            fn kind(s: string): int { return 3; }

            fn main(): int {
                return kind(2);
            }
            """));
    }

    [Fact]
    public void A_concrete_parameter_beats_a_type_parameter()
    {
        // The generic one takes anything, which is exactly why it takes it last: a function
        // written for THIS type says more about the call than one written for every type.
        Assert.Equal(5, Run("""
            fn size<T>(value: T): int { return 9; }
            fn size(n: int): int { return 5; }

            fn main(): int {
                return size(1);
            }
            """));
    }

    [Fact]
    public void Methods_overload_like_functions()
    {
        Assert.Equal(107, Run("""
            class Slot {
                v: int = 0,
                fn put(n: int): void { this.v = this.v + n; }
                fn put(s: string): void { this.v = this.v + 100; }
            }

            fn main(): int {
                let s = Slot { };
                s.put(7);
                s.put("x");
                return s.v;
            }
            """));
    }

    [Fact]
    public void An_own_member_still_beats_an_extension_that_fits_as_well()
    {
        // The rule that predates overloading, and it stays: only a BETTER fit takes a call away
        // from the type's own member.
        Assert.Equal(23, Run("""
            class Player {
                hp: int = 0,
                fn get(): int { return 3; }
            }

            extend Player {
                fn get(): int { return 900; }
                fn get(bonus: int): int { return 20 + bonus; }
            }

            fn main(): int {
                let p = Player { };
                return p.get() + p.get(0);
            }
            """));
    }

    [Fact]
    public void The_expected_type_picks_an_overload_used_as_a_value()
    {
        Assert.Equal(14, Run("""
            fn step(n: int): int { return n + 1; }
            fn step(s: string): int { return 0; }

            fn twice(f: fn(int) -> int, n: int): int { return f(f(n)); }

            fn main(): int {
                return twice(step, 12);
            }
            """));
    }

    [Fact]
    public void Overloads_keep_their_own_names_in_the_module()
    {
        // The bytecode carries one name per function and the verifier refuses duplicates, so the
        // overloads are separated there too — by what they take, readably.
        var sm = new SourceManager();
        var id = sm.AddVirtual("lib.lyr", """
            pub fn show(n: int): int { return n; }
            pub fn show(s: string): int { return 1; }
            pub fn show(n: int, s: string): int { return 2; }
            """);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);
        Assert.False(de.HasErrors);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true, libraryRoots: true);
        Assert.NotNull(ir);

        var names = ir!.Functions.Select(f => f.Name).ToArray();
        Assert.Contains("main.show(int)", names);
        Assert.Contains("main.show(string)", names);
        Assert.Contains("main.show(int, string)", names);
    }
}
