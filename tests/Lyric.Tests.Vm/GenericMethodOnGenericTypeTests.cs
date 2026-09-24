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
/// A GENERIC METHOD ON A GENERIC TYPE — <c>Box&lt;int&gt;.map&lt;string&gt;</c>.
///
/// <para>Reported from three sides and open since the branches landed: <c>Result&lt;T,E&gt;.map&lt;U&gt;</c>,
/// <c>List&lt;T&gt;.map&lt;U&gt;</c> and <c>Iterator.toList</c> all have this shape, and all three are
/// written as free functions in the stdlib because of it. The method takes its <c>T</c> from the
/// instance and its <c>U</c> from the call, and the lowering bound only one of the two: the
/// instance request has no room for the method's own arguments, so <c>U</c> reached the type table
/// as a bare name.</para>
///
/// <para>WHAT IT ANSWERED IS WHY THIS WAS HARD TO PLACE: "a non-primitive field type", reported at
/// the method's RETURN TYPE. A sentence about a field, for a method — the message came from the one
/// place in the type table that gives up on an unresolvable name, and it names the most common
/// caller rather than the one it had.</para>
///
/// <para>The two-sided substitution was already there — <c>InstanceTable.Request</c> takes an owner
/// instance and binds the owner's parameters before the method's — and the interface twin
/// (<c>Iterator&lt;int&gt;.map&lt;string&gt;</c>) had used it all along. Only the class, struct and
/// enum path never asked for it. An earlier attempt changed the request alone and was backed out:
/// the return type and the argument materialization need both sides bound as well, and two of the
/// three is a module that compiles and behaves wrongly.</para>
///
/// <para>THE READ-BACK IS THE TEST, as in <see cref="GenericMethodReceiverTests"/>: the failures in
/// this family are arity and layout mistakes, and <c>check</c> never emits.</para>
/// </summary>
public class GenericMethodOnGenericTypeTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    /// <summary>Compiles, writes the bytes, READS THEM BACK, and runs.</summary>
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

        var diagnostics = new StringWriter();
        de.RenderText(diagnostics);
        Assert.False(de.HasErrors, "source did not compile: " + diagnostics);

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true);
        Assert.NotNull(ir);

        return Interpreter.Run(BytecodeReader.ReadOrThrow(BytecodeWriter.Write(ir!)),
            NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null)).AsI64;
    }

    /// <summary>The smallest form of it, and the one that answered LYR-IR0001.</summary>
    [Fact]
    public void A_method_may_be_generic_on_top_of_its_type()
        => Assert.Equal(1, Run("""
            class Box<T> { v: T, fn both<U>(u: U): U { return u; } }
            fn main(): int { let b = Box<int> { v = 0 }; return b.both<int>(1); }
            """));

    /// <summary>
    /// The shape the stdlib wants: a result type built from BOTH sides, and a lambda over the
    /// instance's own element type.
    ///
    /// <para>This is <c>Result.map</c> and <c>List.map</c> in miniature. It is also what a fix
    /// limited to the request alone would have failed on — <c>Box&lt;U&gt;</c> as the return type
    /// mentions the method's parameter, not the instance's.</para>
    /// </summary>
    [Fact]
    public void The_result_may_be_built_from_the_methods_own_parameter()
        => Assert.Equal(21, Run("""
            class Box<T> {
                v: T,
                fn map<U>(f: fn(T) -> U): Box<U> { return Box<U> { v = f(this.v) }; }
                fn get(): T { return this.v; }
            }
            fn main(): int {
                let b = Box<int> { v = 20 };
                return b.map<int>((n: int): int => n + 1).get();
            }
            """));

    /// <summary>A result mentioning both sides at once, through a second generic type.</summary>
    [Fact]
    public void A_result_may_mention_the_types_parameter_and_the_methods()
        => Assert.Equal(3, Run("""
            struct Pair<A, B> { a: A, b: B, }
            class Box<T> {
                v: T,
                fn pair<U>(u: U): Pair<T, U> { return Pair<T, U> { a = this.v, b = u }; }
            }
            fn main(): int {
                let b = Box<int> { v = 3 };
                return b.pair<bool>(true).a;
            }
            """));

    /// <summary>
    /// The receiver is there and is USABLE — the body reads a field through it.
    ///
    /// <para>The sibling defect: a generic method monomorphized without a <c>this</c> slot produced
    /// a module its own reader refused. Here the receiver additionally comes from an INSTANCE, so
    /// the slot has to carry <c>Box&lt;int&gt;</c> rather than <c>Box</c>.</para>
    /// </summary>
    [Fact]
    public void The_receiver_is_the_instance_the_call_named()
        => Assert.Equal(5, Run("""
            class Box<T> { v: T, fn take<U>(u: U): T { return this.v; } }
            fn main(): int { let b = Box<int> { v = 5 }; return b.take<bool>(true); }
            """));

    /// <summary>
    /// TWO OWNER INSTANCES, one method, the same method type argument.
    ///
    /// <para>A table keyed by the method's arguments alone would hand both calls the same function,
    /// and one of the two would read a field of the wrong type. The return types differ here so
    /// that a collapse cannot go unnoticed.</para>
    /// </summary>
    [Fact]
    public void Two_owner_instances_get_their_own_method_instance()
        => Assert.Equal(14, Run("""
            class Box<T> { v: T, fn tell<U>(u: U): T { return this.v; } }
            fn main(): int {
                let a = Box<int> { v = 4 };
                let b = Box<bool> { v = true };
                var n = a.tell<int>(0);
                if (b.tell<int>(0)) { n = n + 10; }
                return n;
            }
            """));

    /// <summary>A STATIC one on a generic type: same instantiation, no receiver.</summary>
    [Fact]
    public void A_static_generic_method_on_a_generic_type_takes_no_receiver()
        => Assert.Equal(6, Run("""
            class Box<T> { v: T, static fn of<U>(u: U): U { return u; } }
            fn main(): int { return Box<int>.of<int>(6); }
            """));

    [Fact]
    public void A_struct_owner_works_the_same_way()
        => Assert.Equal(7, Run("""
            struct Cell<T> { v: T, fn pick<U>(u: U, t: T): U { return u; } }
            fn main(): int { let c = Cell<string> { v = "s" }; return c.pick<int>(7, "s"); }
            """));

    [Fact]
    public void An_enum_owner_works_the_same_way()
        => Assert.Equal(8, Run("""
            enum Opt<T> {
                None,
                Some(T);

                fn or<U>(fallback: U): U { return fallback; }
            }
            fn main(): int { let o: Opt<int> = Opt<int>.Some(1); return o.or<int>(8); }
            """));

    /// <summary>
    /// The type argument NAMES THE CALLER'S parameter: <c>b.both&lt;T&gt;(t)</c> inside
    /// <c>wrap&lt;T&gt;</c>.
    ///
    /// <para>Which type that is only the enclosing instantiation knows. Left unresolved it reaches
    /// the request as a bare parameter, which refuses it — correctly, since there is no instance to
    /// build for a name.</para>
    /// </summary>
    [Fact]
    public void A_type_argument_naming_the_callers_parameter_is_resolved()
        => Assert.Equal(9, Run("""
            class Box<T> { v: T, fn both<U>(u: U): U { return u; } }
            fn wrap<T>(b: Box<T>, t: T): T { return b.both<T>(t); }
            fn main(): int { let b = Box<int> { v = 0 }; return wrap<int>(b, 9); }
            """));

    /// <summary>Written type arguments are not required; the inference settles it as for any call.</summary>
    [Fact]
    public void The_method_type_argument_may_be_inferred()
        => Assert.Equal(10, Run("""
            class Box<T> { v: T, fn both<U>(u: U): U { return u; } }
            fn main(): int { let b = Box<int> { v = 0 }; return b.both(10); }
            """));

    /// <summary>
    /// The control: a NON-generic method on a generic type keeps its old route.
    ///
    /// <para>It goes through <c>RequestMethod</c> and is named <c>Box&lt;int&gt;.get</c>, without
    /// the angle brackets the new path adds — the two namings must not meet.</para>
    /// </summary>
    [Fact]
    public void A_plain_method_on_a_generic_type_is_unchanged()
        => Assert.Equal(11, Run("""
            class Box<T> { v: T, fn get(): T { return this.v; } }
            fn main(): int { let b = Box<int> { v = 11 }; return b.get(); }
            """));

    /// <summary>
    /// Both routes on ONE type, in one program: the plain method and the generic one.
    ///
    /// <para>The point is the naming. <c>Box&lt;int&gt;.both</c> and <c>Box&lt;int&gt;.both&lt;int&gt;</c>
    /// are different keys, and a program using both is what would show it if they were not.</para>
    /// </summary>
    [Fact]
    public void The_two_routes_coexist_on_one_instance()
        => Assert.Equal(12, Run("""
            class Box<T> {
                v: T,
                fn get(): T { return this.v; }
                fn both<U>(u: U): U { return u; }
            }
            fn main(): int { let b = Box<int> { v = 2 }; return b.get() + b.both<int>(10); }
            """));
}
