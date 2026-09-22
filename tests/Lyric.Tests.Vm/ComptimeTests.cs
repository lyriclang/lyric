using System.Runtime.CompilerServices;
using Lyric.Bytecode;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Vm;

namespace Lyric.Tests.Vm;

/// <summary>
/// <c>comptime e</c> — the compiler evaluates <c>e</c> through the VM in a sandbox and writes
/// the value back as a literal. The pipeline runs twice: once with every site hoisted into a
/// function of its own (the evaluation module, pruned to what the sites reach), once with the
/// values in hand. What these tests pin: the value is the one run time would compute, the
/// computation is gone from the module, and a site that reaches for a capability, panics or
/// loops fails at the SITE with the reason — never at run time and never as a hang.
/// </summary>
public class ComptimeTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static CompileResult Compile(string source, bool withRunner = true) =>
        SourceCompiler.Compile(ScriptSource.FromText("test", source), new CompilerOptions
        {
            StdlibRoot = Path.Combine(RepoRoot(), "stdlib"),
            ComptimeRunner = withRunner ? new VmComptimeRunner { Budget = 5_000_000 } : null,
        });

    private static BytecodeModule Ok(string source)
    {
        var result = Compile(source);
        var writer = new StringWriter();
        result.Diagnostics.RenderText(writer);
        Assert.True(result.Ok && result.Bytes is not null, "source did not compile: " + writer);
        return BytecodeReader.ReadOrThrow(result.Bytes!);
    }

    private static long Run(BytecodeModule module) =>
        Interpreter.Run(module, [], NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null),
            Capability.None).AsI64;

    private const string Fib = """
        fn fib(n: int): int {
            var a = 0;
            var b = 1;
            for (_ in 0..n) { let next = a + b; a = b; b = next; }
            return a;
        }
        let F = comptime fib(30);
        fn main(): int { return F % 1000; }
        """;

    [Fact]
    public void The_value_is_the_one_run_time_computes() =>
        Assert.Equal(832040 % 1000, Run(Ok(Fib)));

    [Fact]
    public void The_computation_is_gone_from_the_module()
    {
        // The site became a literal, so nothing calls 'fib' and the pruning removes it. That is
        // the observable half of "computed by the compiler".
        var module = Ok(Fib);
        Assert.DoesNotContain(module.Functions, f => f.Name.EndsWith(".fib", StringComparison.Ordinal));
    }

    [Fact]
    public void Strings_bools_and_chars_come_back_as_literals() =>
        Assert.Equal(3, Run(Ok("""
            fn tag(n: int): string { return f"v{n}"; }
            let S = comptime tag(4);
            let B = comptime S == "v4";
            let C = comptime 'x';
            fn main(): int { return (if (B) 1 else 0) + (if (C == 'x') 2 else 0); }
            """)));

    [Fact]
    public void A_site_inside_a_function_body_works_too() =>
        Assert.Equal(31536000 % 1000, Run(Ok("""
            fn main(): int { return comptime (60 * 60 * 24 * 365) % 1000; }
            """)));

    [Fact]
    public void A_local_cannot_be_used()
    {
        var result = Compile("""
            fn main(): int { let n = 3; return comptime n + 1; }
            """);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Code == "LYR-SEM0100");
    }

    [Fact]
    public void A_non_literal_type_is_refused()
    {
        var result = Compile("""
            fn main(): int { let xs = comptime [1, 2, 3]; return xs.length; }
            """);
        Assert.Contains(result.Diagnostics.Diagnostics, d => d.Code == "LYR-SEM0100");
    }

    [Fact]
    public void A_panic_is_reported_at_the_site()
    {
        var result = Compile("""
            fn checked(x: int): int { if (x > 10) { panic("too big"); } return x; }
            fn main(): int { return comptime checked(99); }
            """);
        var error = Assert.Single(result.Diagnostics.Diagnostics, d => d.Code == "LYR-CT0002");
        Assert.Contains("too big", error.Message);
        Assert.Null(result.Bytes);
    }

    [Fact]
    public void A_loop_that_never_ends_is_stopped_by_the_budget()
    {
        var result = Compile("""
            fn spin(): int { var i = 0; while (true) { i = i + 1; } return i; }
            fn main(): int { return comptime spin(); }
            """);
        var error = Assert.Single(result.Diagnostics.Diagnostics, d => d.Code == "LYR-CT0002");
        Assert.Contains("budget", error.Message);
    }

    [Fact]
    public void A_capability_is_never_granted_at_compile_time()
    {
        var result = Compile("""
            import std.io.file { exists };
            fn main(): int { return if (comptime exists("/")) 1 else 0; }
            """);
        var error = Assert.Single(result.Diagnostics.Diagnostics, d => d.Code == "LYR-CT0002");
        Assert.Contains("fileAccess", error.Message);
    }

    [Fact]
    public void A_draw_that_cannot_be_repeated_is_refused_while_a_seeded_one_evaluates()
    {
        // 'secureRandom' needs no capability and is refused all the same: a literal that
        // differs from build to build is the one thing comptime must not produce. A seeded
        // generator is ordinary arithmetic and evaluates.
        var refused = Compile("""
            import std.random { secureRandom };
            fn main(): int { return comptime (secureRandom(1)[0] as int); }
            """);
        var error = Assert.Single(refused.Diagnostics.Diagnostics, d => d.Code == "LYR-CT0002");
        Assert.Contains("secureRandom", error.Message);

        var seeded = Run(Ok("""
            import std.random { Random };
            fn draw(): int { var r = Random.seeded(7); return r.nextIntRange(0, 100); }
            fn main(): int { return comptime draw(); }
            """));
        Assert.InRange(seeded, 0, 99);
    }

    [Fact]
    public void A_pure_site_evaluates_in_a_program_that_reads_files_elsewhere()
    {
        // The evaluation module is pruned to what the sites reach, so the program's own
        // fileAccess never enters it. The real module still requires the bit.
        var module = Ok("""
            import std.io.file { exists };
            let N = comptime 6 * 7;
            fn main(): int { return if (exists("/nowhere") ) 0 else N; }
            """);
        Assert.Equal((ulong)Capability.FileAccess, module.Capabilities);
    }

    [Fact]
    public void Without_an_evaluator_a_build_says_so_and_a_check_passes()
    {
        var build = Compile("fn main(): int { return comptime (1 + 2); }", withRunner: false);
        Assert.Contains(build.Diagnostics.Diagnostics, d => d.Code == "LYR-CT0001");

        var check = SourceCompiler.Check(ScriptSource.FromText("test", "fn main(): int { return comptime (1 + 2); }"),
            new CompilerOptions { StdlibRoot = Path.Combine(RepoRoot(), "stdlib") });
        Assert.True(check.Ok);
    }

    [Fact]
    public void The_word_stays_a_name_before_an_operator() =>
        // 'comptime' opens the prefix only before something that begins an expression.
        Assert.Equal(5, Run(Ok("""
            let comptime = 2;
            fn main(): int { return comptime + 3; }
            """)));
}
