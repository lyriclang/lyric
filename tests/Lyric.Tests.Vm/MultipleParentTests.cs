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
/// An interface with several parents (2.16), executed.
///
/// <para>These run rather than inspect, because the question the old rule was built on is a
/// RUNTIME one: does a parent's default method still find its own members behind a child-typed
/// receiver? The answer the reasoning predicted was no — hence "at most one parent" and a note
/// about thunks. The answer the machine gives is yes: the dispatch table is keyed by (concrete
/// type, interface), and the lowering emits a row per interface in the transitive closure, so
/// every parent keeps its own slot numbering and nothing is remapped.</para>
///
/// <para>Which leaves exactly one thing a second parent costs, and it is not in the runtime: two
/// parents contributing the same NAME. That is refused in the sema — see
/// <c>Lyric.Tests.Sema.InterfaceInheritanceTests</c> — because one slot cannot hold two methods
/// and no rule picks correctly between them.</para>
/// </summary>
public class MultipleParentTests
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
        Assert.NotNull(ir);

        return LoadedProgram.Load(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null));
    }

    private static long Call(LoadedProgram program, string name, params LyrValue[] arguments) =>
        program.Invoke(program.IndexOfFunction(name), arguments).AsI64;

    private const string TwoParents = """
        pub interface Counted { fn count(): int; }

        pub interface Scaled {
            fn scale(): int;

            // A DEFAULT on the second parent, calling its own abstract member. This is the case
            // the single-parent rule existed for.
            fn scaledTwice(): int { return this.scale() * 2; }
        }

        pub interface Item :: [Counted, Scaled] {
            fn total(): int { return this.count() * this.scale(); }
        }

        pub class Box :: [Item] {
            n: int = 0,
            pub fn count(): int { return 3; }
            pub fn scale(): int { return 5; }
        }

        pub fn viaCounted(c: Counted): int { return c.count(); }
        pub fn viaScaled(s: Scaled): int { return s.scaledTwice(); }
        pub fn viaItem(i: Item): int { return i.total(); }

        pub fn make(): Box { return Box { }; }

        """;

    [Fact]
    public void A_call_through_the_first_parent_finds_its_own_row()
    {
        var program = Compile(TwoParents + """
            pub fn go(): int { return viaCounted(make()); }
            """);

        Assert.Equal(3, Call(program, "main.go"));
    }

    [Fact]
    public void A_default_on_the_SECOND_parent_runs_behind_a_child_receiver()
    {
        // The case the old rule predicted would break: 'scaledTwice' is compiled once, against
        // Scaled, and dispatches 'this.scale()' through Scaled's own row — which exists for Box
        // regardless of where Scaled sits in Item's parent list.
        var program = Compile(TwoParents + """
            pub fn go(): int { return viaScaled(make()); }
            """);

        Assert.Equal(10, Call(program, "main.go"));
    }

    [Fact]
    public void A_default_on_the_CHILD_reaches_members_of_both_parents()
    {
        var program = Compile(TwoParents + """
            pub fn go(): int { return viaItem(make()); }
            """);

        Assert.Equal(15, Call(program, "main.go"));
    }

    [Fact]
    public void A_diamond_shares_the_ancestor_rather_than_duplicating_it()
    {
        // Base is reached along two paths and is ONE member all the same: the slot list walks a
        // shared ancestor once, so 'id' has one slot and one implementation fills it.
        var program = Compile("""
            pub interface Base { fn id(): int; }
            pub interface Left :: [Base] { fn left(): int { return this.id() + 1; } }
            pub interface Right :: [Base] { fn right(): int { return this.id() + 2; } }
            pub interface Both :: [Left, Right] { }

            pub class Impl :: [Both] {
                pub fn id(): int { return 10; }
            }

            pub fn go(): int {
                let b: Both = Impl { };
                return b.id() + b.left() + b.right();
            }
            """);

        Assert.Equal(10 + 11 + 12, Call(program, "main.go"));
    }

    [Fact]
    public void The_order_of_the_parent_list_does_not_change_what_runs()
    {
        // The slot list follows the written order, so the two spellings produce different slot
        // NUMBERS — and the same answers. Which is the point: a slot number is internal, and a
        // call names the interface it goes through.
        const string Body = """
            pub interface A { fn a(): int; }
            pub interface B { fn b(): int { return 2; } }

            pub class Impl :: [C] {
                pub fn a(): int { return 1; }
            }

            pub fn go(): int {
                let c: C = Impl { };
                return c.a() * 10 + c.b();
            }
            """;

        Assert.Equal(12, Call(Compile("pub interface C :: [A, B] { }\n\n" + Body), "main.go"));
        Assert.Equal(12, Call(Compile("pub interface C :: [B, A] { }\n\n" + Body), "main.go"));
    }

    [Fact]
    public void A_type_satisfies_a_constraint_through_either_parent()
    {
        // Implication holds for implementing types, and with two parents it holds twice.
        var program = Compile(TwoParents + """
            pub fn sum<T :: [Counted, Scaled]>(t: T): int { return t.count() + t.scale(); }

            pub fn go(): int { return sum(make()); }
            """);

        Assert.Equal(8, Call(program, "main.go"));
    }
}
