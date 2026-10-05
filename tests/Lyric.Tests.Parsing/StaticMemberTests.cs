using Lyric.AST;
using Lyric.Core;
using Lyric.Parsing;
using Xunit;

namespace Lyric.Tests.Parsing;

/// <summary>
/// Where <c>static</c> may stand on a member.
///
/// <para>The grammar puts <c>static</c> into <c>FunctionDecl</c>, and an enum, interface and extend
/// body all contain <c>FunctionDecl</c> — but only a struct or class body ever read it, so all three
/// gave <c>LYR-PAR0008</c> plus two follow-ups, none of them about the cause.</para>
///
/// <para>An interface declares static members too (design/v5/spec/03 T5): they have no slot and
/// are reached through a constraint; the sema keeps such an interface from becoming a value.</para>
/// </summary>
public class StaticMemberTests
{
    private static (Module module, DiagnosticEngine diag) Parse(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", source);
        var de = new DiagnosticEngine(sm);
        return (new Parser(sm, id, de).ParseModule(), de);
    }

    private static string[] Codes(DiagnosticEngine de) =>
        de.Diagnostics.Select(d => d.Code).ToArray();

    // ------------------------------------------------------------------ accepted

    [Fact]
    public void An_enum_method_may_be_static()
    {
        var (module, de) = Parse("enum E { A; static fn make(): E { return E.A; } }");
        Assert.False(de.HasErrors);
        var e = Assert.IsType<EnumDecl>(module.Declarations[0]);
        Assert.True(Assert.Single(e.Methods).IsStatic);
    }

    [Fact]
    public void An_extend_method_may_be_static()
    {
        var (module, de) = Parse("extend int { static fn zero(): int { return 0; } }");
        Assert.False(de.HasErrors);
        var x = Assert.IsType<ExtendDecl>(module.Declarations[0]);
        Assert.True(Assert.Single(x.Methods).IsStatic);
    }

    [Fact]
    public void The_modifiers_keep_the_order_of_the_grammar()
    {
        // 'pub' then 'static' then 'mut' then 'fn'. 'mut static' does not exist.
        var (module, de) = Parse("enum E { A; pub static fn make(): E { return E.A; } }");
        Assert.False(de.HasErrors);
        var m = Assert.Single(Assert.IsType<EnumDecl>(module.Declarations[0]).Methods);
        Assert.True(m.IsPublic);
        Assert.True(m.IsStatic);
        Assert.False(m.IsMut);
    }

    [Fact]
    public void A_static_method_stands_beside_instance_methods()
    {
        var (module, de) = Parse(
            "enum E { A; fn tag(): int { return 1; } static fn first(): E { return E.A; } }");
        Assert.False(de.HasErrors);
        var methods = Assert.IsType<EnumDecl>(module.Declarations[0]).Methods;
        Assert.Equal([false, true], methods.Select(m => m.IsStatic));
    }

    [Fact]
    public void A_member_without_static_is_not_marked_static()
    {
        // The counter-check: a parser that set the flag unconditionally would pass the tests above.
        var (module, _) = Parse("enum E { A; fn tag(): int { return 1; } }");
        Assert.False(Assert.Single(Assert.IsType<EnumDecl>(module.Declarations[0]).Methods).IsStatic);
    }

    [Fact]
    public void A_class_and_a_struct_body_are_untouched()
    {
        var (module, de) = Parse(
            "class K { static let n: int = 3; static fn get(): int { return K.n; } }\n"
            + "struct S { v: int, static fn make(): S { return S { v = 1 }; } }");
        Assert.False(de.HasErrors);
        Assert.IsType<ClassDecl>(module.Declarations[0]);
        Assert.IsType<StructDecl>(module.Declarations[1]);
    }

    // ------------------------------------------------------------------ interfaces

    [Fact]
    public void An_interface_member_may_be_static()
    {
        // 03 T5: a static interface member declares; it is reached through a constraint,
        // 'T.make()', as the conformer's own static. (Lyric 4 refused it with LYR-PAR0041.)
        var (module, de) = Parse("interface I { static fn make(): int; fn tag(): int; }");
        Assert.False(de.HasErrors);
        var members = Assert.IsType<InterfaceDecl>(module.Declarations[0]).Members;
        Assert.Equal([true, false], members.Select(m => m.IsStatic));
    }

    [Fact]
    public void An_interface_declares_a_constant()
    {
        // 03 T5: 'static let zero: Self;' declares, every conformer answers. Whether a value
        // stands there is the checker's question (LYR-SEM0127), as for a static fn's body.
        var (module, de) = Parse("interface I { static let zero: Self; fn tag(): int; }");
        Assert.False(de.HasErrors);
        var iface = Assert.IsType<InterfaceDecl>(module.Declarations[0]);
        Assert.Equal(["zero"], iface.Statics.Select(s => s.Name));
        Assert.Equal(["tag"], iface.Members.Select(m => m.Name));
    }

    [Fact]
    public void A_block_adds_a_constant()
    {
        // 05 §6: the block's constant is its type's — 'int.answer'.
        var (module, de) = Parse("extend int { static let answer: int = 42; fn twice(): int { return this * 2; } }");
        Assert.False(de.HasErrors);
        var block = Assert.IsType<ExtendDecl>(module.Declarations[0]);
        Assert.Equal(["answer"], block.Statics.Select(s => s.Name));
        Assert.Equal(["twice"], block.Methods.Select(m => m.Name));
    }

    // An enum's body takes one too (the review's M8a-10): EnumBodyTests. LYR-PAR0040, which
    // refused it there, is retired with the two tests that pinned it.
}
