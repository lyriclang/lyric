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
/// Iterator chaining, executed (2.17) — and the mechanism under it: a method with type parameters
/// of its own.
///
/// <para>Such a method gets NO vtable slot and cannot have one: a slot holds one function, and
/// this is one function per instantiation. It is monomorphized and called directly, which is
/// sound because it may not be overridden — the default IS the implementation for every receiver.
/// That is also what makes it reachable through an interface VALUE, and chaining needs exactly
/// that: <c>xs.iter()</c> hands out a value, not a constrained type parameter.</para>
///
/// <para>These run rather than inspect. Laziness and ORDER are what a chain gets wrong when the
/// monomorphization is subtly off, and neither is visible in a disassembly.</para>
/// </summary>
public class IteratorChainingTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static LoadedProgram Compile(string source)
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
        var after = new StringWriter();
        de.RenderText(after);
        Assert.True(ir is not null, "lowering produced nothing: " + after);

        return LoadedProgram.Load(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null));
    }

    private static long Call(LoadedProgram program, string name) =>
        program.Invoke(program.IndexOfFunction(name)).AsI64;

    private const string Source = """
        import std.iter { collectArray, sum, count, Iterator };
        import std.collections { List };

        fn upTo(n: int): List<int> {
            let xs = List<int>.empty();
            var i = 1;
            while (i <= n) {
                xs.push(i);
                i = i + 1;
            }
            return xs;
        }

        """;

    [Fact]
    public void A_chain_of_three_adapters_produces_what_the_nested_calls_did()
    {
        var program = Compile(Source + """
            pub fn go(): int {
                let out = collectArray<int>(
                    upTo(6).iter().map<int>((n: int) => n * 2).filter((n: int) => n > 4).take(2));

                return out.length * 100 + out[0] * 10 + out[1];
            }
            """);

        Assert.Equal(2 * 100 + 6 * 10 + 8, Call(program, "main.go"));
    }

    [Fact]
    public void A_chain_stays_lazy()
    {
        // 'take(2)' must stop the source, not filter a finished list: the counter says how far the
        // chain actually pulled. A chain that materialized would report 6.
        var program = Compile(Source + """
            class Counting :: [Iterator<int>] {
                at: int = 0,
                pulled: int = 0,

                pub mut fn next(): ?int {
                    if (this.at >= 6) {
                        return null;
                    }
                    this.at = this.at + 1;
                    this.pulled = this.pulled + 1;
                    return this.at;
                }
            }

            pub fn go(): int {
                let source = Counting { };
                let out = collectArray<int>(source.take(2));
                return out.length * 100 + source.pulled;
            }
            """);

        Assert.Equal(2 * 100 + 2, Call(program, "main.go"));
    }

    [Fact]
    public void The_element_type_may_change_along_the_chain()
    {
        // 'map<U>' is the generic member, so the monomorphization happens per U actually used.
        var program = Compile(Source + """
            pub fn go(): int {
                let words = upTo(3).iter().map<string>((n: int) => "x");
                return count<string>(words);
            }
            """);

        Assert.Equal(3, Call(program, "main.go"));
    }

    [Fact]
    public void Two_instantiations_of_one_method_stay_apart()
    {
        // 'map<int>' and 'map<string>' are two functions. If the monomorphization keyed them
        // together, one of these would return the other's values.
        var program = Compile(Source + """
            pub fn go(): int {
                let doubled = sum(upTo(3).iter().map<int>((n: int) => n * 2));
                let labelled = count<string>(upTo(3).iter().map<string>((n: int) => "x"));
                return doubled * 10 + labelled;
            }
            """);

        Assert.Equal(12 * 10 + 3, Call(program, "main.go"));
    }

    [Fact]
    public void A_generic_member_works_on_a_concrete_instance_receiver()
    {
        // Not through an Iterator<T> value but on the ARRAY ITERATOR itself, which is an instance
        // of a generic class. That receiver took the instance path, where the member''s own type
        // parameters are unbound: ''zip<B>'' returns ''Iterator<(T, B)>'' and reported an
        // unsupported type argument at the interface''s own declaration. Free adapters covered it
        // up until they went with 3.0.
        var program = Compile("""
            import std.iter { ArrayIterator, count, sum };

            pub fn go(): int {
                let a = ArrayIterator<int> { source = [1, 2, 3], index = 0 };
                let b = ArrayIterator<string> { source = ["x", "y"], index = 0 };
                let paare = count<(int, string)>(a.zip(b));

                let c = ArrayIterator<int> { source = [1, 2, 3], index = 0 };
                return paare * 100 + sum(c.map<int>((n: int) => n * 2));
            }
            """);

        Assert.Equal(2 * 100 + 12, Call(program, "main.go"));
    }

    [Fact]
    public void A_generic_method_reaches_a_receiver_of_its_own_interface_type()
    {
        // The case a slot could not serve: the receiver is an interface VALUE, and a generic
        // member has no slot. It is called directly all the same, because it cannot be overridden.
        var program = Compile(Source + """
            pub fn twice(source: Iterator<int>): Iterator<int> {
                return source.map<int>((n: int) => n * 2);
            }

            pub fn go(): int {
                return sum(twice(upTo(3).iter()));
            }
            """);

        Assert.Equal(12, Call(program, "main.go"));
    }
}
