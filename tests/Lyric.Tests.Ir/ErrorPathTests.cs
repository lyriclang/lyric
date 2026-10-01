using Lyric.Core;
using Lyric.Ir;
using Lyric.Ir.Lowering;
using Lyric.Parsing;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Tests.Ir;

/// <summary>
/// The error path in the IR (design/v5/spec/05 E1-E4, E9; 01 L5): explicit edges, verified. A
/// function that may throw carries the flag; a failed call ends its block in an error branch; a
/// site's landing runs the defers it leaves and ends in the innermost try's dispatch or the
/// function's bottom — propagate where the function throws, unreachable where the sema proved
/// every site covered; sites with the same pending defers share one landing.
/// </summary>
public class ErrorPathTests
{
    private const string Prelude = """
        class Boom :: [Error] { fn message(): string { return "boom"; } }
        fn risky(): int throws Boom { return 1; }
        fn note(): void { }

        """;

    /// <summary>Lowered with the verifier on and the optimizer off: the shapes are the lowering's.
    /// <c>main</c> has to reach what a test looks at — the pruning runs either way.</summary>
    private static IrModule Lowered(string source)
    {
        var sm = new SourceManager();
        var id = sm.AddVirtual("test.lyr", Prelude + source);
        var de = new DiagnosticEngine(sm);
        var comp = new Compilation(sm, de)
        {
            ModuleLoader = StdlibLoader.ForRoot(Path.Combine(RepoRoot(), "stdlib"), sm, de),
        };
        comp.AddModule(new Parser(sm, id, de).ParseModule());
        var binding = comp.Resolve();
        var types = Semantics.Analyze(comp, binding, de);
        if (de.HasErrors)
        {
            var writer = new StringWriter();
            de.RenderText(writer);
            Assert.Fail("source did not type-check:\n" + writer);
        }

        var ir = ModuleLowerer.Lower(comp, binding, types, de, verify: true, optimize: false);
        Assert.NotNull(ir);
        return ir!;
    }

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static IrFunction Fn(IrModule module, string name) =>
        module.Functions.Single(f => f.Name == $"main.{name}");

    private static IEnumerable<IrTerminator> Terminators(IrFunction function) =>
        function.Blocks.Select(b => b.Terminator).OfType<IrTerminator>();

    private static IrBlock Dispatch(IrFunction function) =>
        function.Blocks.Single(b => b.Insts.Any(op => op is CurrentError));

    [Fact]
    public void A_function_that_may_throw_carries_the_flag()
    {
        var module = Lowered("""
            fn safe(): int { return 1; }
            fn main(): int throws Boom { return try risky() + safe(); }
            """);
        Assert.True(Fn(module, "risky").Throws);
        Assert.False(Fn(module, "safe").Throws);
    }

    [Fact]
    public void A_failed_call_goes_to_its_landing_and_the_landing_propagates()
    {
        var module = Lowered("""
            fn f(): int throws Boom { return try risky(); }
            fn main(): int throws Boom { return try f(); }
            """);
        var f = Fn(module, "f");
        var branch = Assert.Single(Terminators(f).OfType<ErrorBranch>());
        Assert.IsType<Propagate>(f.Blocks[branch.OnError.Value].Terminator);
    }

    [Fact]
    public void A_throw_carries_an_interface_value_to_its_landing()
    {
        var module = Lowered("""
            fn f(): int throws Boom { throw Boom { }; }
            fn main(): int throws Boom { return try f(); }
            """);
        var f = Fn(module, "f");
        var thrown = Assert.Single(Terminators(f).OfType<Throw>());
        Assert.IsType<IrInterfaceType>(f.Temps[thrown.Value.Value].Type);
        Assert.IsType<Propagate>(f.Blocks[thrown.Landing.Value].Terminator);
    }

    [Fact]
    public void Sites_with_the_same_pending_defers_share_their_landing()
    {
        var module = Lowered("""
            fn f(): int throws Boom {
                defer note();
                let a = try risky();
                let b = try risky();
                return a + b;
            }
            fn main(): int throws Boom { return try f(); }
            """);
        var branches = Terminators(Fn(module, "f")).OfType<ErrorBranch>().ToList();
        Assert.Equal(2, branches.Count);
        Assert.Equal(branches[0].OnError, branches[1].OnError);
    }

    [Fact]
    public void A_later_defer_lengthens_the_chain_into_the_earlier_landing()
    {
        var module = Lowered("""
            fn f(): int throws Boom {
                defer note();
                let a = try risky();
                defer note();
                let b = try risky();
                return a + b;
            }
            fn main(): int throws Boom { return try f(); }
            """);
        var f = Fn(module, "f");
        var branches = Terminators(f).OfType<ErrorBranch>().ToList();
        Assert.Equal(2, branches.Count);
        Assert.NotEqual(branches[0].OnError, branches[1].OnError);
        // The second landing runs the later defer, then goes on into the first one.
        var second = f.Blocks[branches[1].OnError.Value];
        Assert.Equal(branches[0].OnError, Assert.IsType<Branch>(second.Terminator).Target);
    }

    [Fact]
    public void A_clause_tests_the_type_and_a_miss_ends_at_an_unreachable_bottom()
    {
        var module = Lowered("""
            fn f(): int {
                try { return risky(); } catch (_: Boom) { return 0; }
            }
            fn main(): int { return f(); }
            """);
        var f = Fn(module, "f");
        Assert.False(f.Throws);
        Assert.Equal(Dispatch(f).Id, Assert.Single(Terminators(f).OfType<ErrorBranch>()).OnError);

        var test = Assert.IsType<CondBranch>(Dispatch(f).Terminator);
        Assert.IsType<ClearError>(f.Blocks[test.IfTrue.Value].Insts[0]);
        // No clause took it, and the function throws nothing: the sema proved the clause covers
        // every site, so the edge onward ends where control never arrives.
        var onward = Assert.IsType<Branch>(f.Blocks[test.IfFalse.Value].Terminator);
        Assert.IsType<Unreachable>(f.Blocks[onward.Target.Value].Terminator);
        Assert.DoesNotContain(Terminators(f), t => t is Propagate);
    }

    [Fact]
    public void A_catch_all_takes_the_error_without_a_test()
    {
        var module = Lowered("""
            fn f(): int {
                try { return risky(); } catch (e) { return 0; }
            }
            fn main(): int { return f(); }
            """);
        var f = Fn(module, "f");
        var dispatch = Dispatch(f);
        Assert.Contains(dispatch.Insts, op => op is ClearError);
        Assert.DoesNotContain(f.Blocks, b => b.Insts.Any(op => op is TypeTest));
        Assert.IsType<Return>(dispatch.Terminator);
    }

    [Fact]
    public void Try_bang_ends_its_dispatch_in_a_panic()
    {
        var module = Lowered("""
            fn f(): int { return try! risky(); }
            fn main(): int { return f(); }
            """);
        var f = Fn(module, "f");
        Assert.False(f.Throws);
        var branch = Assert.Single(Terminators(f).OfType<ErrorBranch>());
        Assert.IsType<PanicError>(f.Blocks[branch.OnError.Value].Terminator);
    }

    [Fact]
    public void Try_question_clears_the_error_and_gives_null()
    {
        var module = Lowered("""
            fn f(): ?int { return try? risky(); }
            fn main(): int { return f() ?? 0; }
            """);
        var f = Fn(module, "f");
        var dispatch = f.Blocks[Assert.Single(Terminators(f).OfType<ErrorBranch>()).OnError.Value];
        Assert.IsType<ClearError>(dispatch.Insts[0]);
        Assert.Contains(dispatch.Insts, op => op is OptNone);
        // Nothing asks what the error was.
        Assert.DoesNotContain(f.Blocks, b => b.Insts.Any(op => op is CurrentError));
    }

    [Fact]
    public void The_expression_form_dispatches_like_the_block()
    {
        var module = Lowered("""
            fn f(): int { return try risky() catch (_: Boom) 0; }
            fn main(): int { return f(); }
            """);
        var f = Fn(module, "f");
        var test = Assert.IsType<CondBranch>(Dispatch(f).Terminator);
        Assert.IsType<ClearError>(f.Blocks[test.IfTrue.Value].Insts[0]);
        var onward = Assert.IsType<Branch>(f.Blocks[test.IfFalse.Value].Terminator);
        Assert.IsType<Unreachable>(f.Blocks[onward.Target.Value].Terminator);
    }

    [Fact]
    public void A_set_clause_tests_each_of_its_types()
    {
        var module = Lowered("""
            class Other :: [Error] { fn message(): string { return "other"; } }
            fn both(): int throws [Boom, Other] { return 1; }
            fn f(): int { return try both() catch (e in [Boom, Other]) 0; }
            fn main(): int { return f(); }
            """);
        var f = Fn(module, "f");
        var first = Assert.IsType<CondBranch>(Dispatch(f).Terminator);
        // Either test takes it, into the same block.
        var second = Assert.IsType<CondBranch>(f.Blocks[first.IfFalse.Value].Terminator);
        Assert.Equal(first.IfTrue, second.IfTrue);
        Assert.Equal(2, f.Blocks.Sum(b => b.Insts.Count(op => op is TypeTest)));
        // The binding is the Error value itself: no downcast.
        Assert.DoesNotContain(f.Blocks, b => b.Insts.Any(op => op is Downcast));
    }

    // --- a defer that throws (05 E7) ---

    private static IEnumerable<IrOp> Ops(IrFunction function) => function.Blocks.SelectMany(b => b.Insts);

    [Fact]
    public void A_defer_that_cannot_fail_runs_on_the_error_path_as_it_stands()
    {
        var module = Lowered("""
            fn f(): int throws Boom {
                defer note();
                return try risky();
            }
            fn main(): int throws Boom { return try f(); }
            """);
        Assert.DoesNotContain(Ops(Fn(module, "f")), op => op is StashError or RestoreError or SuppressError);
    }

    [Fact]
    public void A_defer_that_can_fail_stashes_the_error_in_flight()
    {
        var module = Lowered("""
            fn f(): int throws Boom {
                defer try risky();
                return try risky();
            }
            fn main(): int throws Boom { return try f(); }
            """);
        var f = Fn(module, "f");
        // The error path's step: stashed first, restored where the body ran clean, merged where it
        // failed — the first error winning.
        var step = Assert.Single(f.Blocks, b => b.Insts.FirstOrDefault() is StashError);
        var stash = ((StashError)step.Insts[0]).Stash;
        Assert.Contains(Ops(f), op => op is RestoreError r && r.Stash == stash);
        Assert.Contains(Ops(f), op => op is SuppressError s && s.Stash == stash);
    }

    [Fact]
    public void A_defer_that_throws_on_a_normal_exit_runs_the_earlier_ones()
    {
        var module = Lowered("""
            fn f(): int throws Boom {
                defer note();
                defer try risky();
                return 1;
            }
            fn main(): int throws Boom { return try f(); }
            """);
        var f = Fn(module, "f");
        // The normal exit runs the later defer first; its failure lands on a step that runs the
        // earlier one — a call of 'note' — and goes on to the bottom.
        var branch = Assert.Single(Terminators(f).OfType<ErrorBranch>());
        var landing = f.Blocks[branch.OnError.Value];
        Assert.Contains(landing.Insts, op => op is Call);
        Assert.IsType<Propagate>(f.Blocks[Assert.IsType<Branch>(landing.Terminator).Target.Value].Terminator);
    }

    [Fact]
    public void A_defer_outside_a_try_is_not_the_trys_to_catch()
    {
        var module = Lowered("""
            fn f(): int throws Boom {
                defer try risky();
                try { let a = try risky(); return a; } catch (_) { return 0; }
            }
            fn main(): int throws Boom { return try f(); }
            """);
        var f = Fn(module, "f");
        // The defer runs at the return inside the try, but it was registered outside: its failure
        // leaves the function rather than reaching the try's dispatch.
        var dispatch = Dispatch(f).Id;
        var branches = Terminators(f).OfType<ErrorBranch>().ToList();
        Assert.Contains(branches, b => b.OnError == dispatch);                                   // the call in the try
        Assert.Contains(branches, b => f.Blocks[b.OnError.Value].Terminator is Propagate);        // the defer's
    }

    [Fact]
    public void A_try_whose_body_cannot_fail_has_no_dispatch()
    {
        var module = Lowered("""
            fn f(): int {
                try { note(); return 1; } catch (_: Boom) { return 0; }
            }
            fn main(): int { return f(); }
            """);
        Assert.DoesNotContain(Fn(module, "f").Blocks, b => b.Insts.Any(op => op is CurrentError));
    }

    [Fact]
    public void A_rethrown_binding_puts_its_record_back_into_flight()
    {
        var module = Lowered("""
            fn f(): int throws Boom {
                try { return try risky(); } catch (e) { note(); throw e; }
            }
            fn main(): int throws Boom { return try f(); }
            """);
        var f = Fn(module, "f");
        // The clause keeps the record aside instead of dropping it, and the rethrow restores it on
        // its way to the landing: the same error, no new record (05 E6 O3).
        var stash = Assert.Single(Ops(f).OfType<StashError>()).Stash;
        Assert.DoesNotContain(Ops(f), op => op is ClearError);
        Assert.Contains(Ops(f), op => op is RestoreError r && r.Stash == stash);
        Assert.Empty(Terminators(f).OfType<Throw>());
    }

    [Fact]
    public void Only_the_binding_itself_thrown_again_keeps_the_record()
    {
        var module = Lowered("""
            fn f(): int throws Boom {
                try { return try risky(); } catch (e: Boom) { let other = e; throw other; }
            }
            fn main(): int throws Boom { return try f(); }
            """);
        var f = Fn(module, "f");
        // Under another name the value starts an error of its own: the clause drops the record,
        // and the throw makes a new one.
        Assert.DoesNotContain(Ops(f), op => op is StashError or RestoreError);
        Assert.Contains(Ops(f), op => op is ClearError);
        Assert.Single(Terminators(f).OfType<Throw>());
    }
}
