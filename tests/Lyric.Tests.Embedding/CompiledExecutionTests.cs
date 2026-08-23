using System.Runtime.CompilerServices;
using Lyric.Core;
using Lyric.Embedding;
using Lyric.Vm;

namespace Lyric.Tests.Embedding;

/// <summary>
/// <see cref="HostOptions.Compile"/>: the same scripts, run as machine code instead of
/// interpreted.
///
/// <para>What these tests hold is not that compiling is faster — a test cannot say that honestly
/// — but that it changes NOTHING else. Same answers, and the two guarantees a host was given
/// before the option existed still hold with it set: a metered call is counted to the
/// instruction, and a refusal is a quiet fallback rather than an error.</para>
/// </summary>
public class CompiledExecutionTests
{
    private static string RepoRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", ".."));

    private static ScriptInstance Instance(string source, bool compile)
    {
        var vm = new LangVm(new HostOptions
        {
            StdlibRoot = Path.Combine(RepoRoot(), "stdlib"),
            Capabilities = Capability.None,
            Compile = compile,
        });
        return vm.Instantiate(vm.Compile(source, "mod"));
    }

    private const string Arithmetic = """
        pub fn total(n: int): int {
            var sum = 0;
            var i = 0;
            while (i < n) {
                sum = sum + i * 2;
                i = i + 1;
            }
            return sum;
        }
        """;

    [Fact]
    public void A_compiled_function_answers_what_the_interpreter_answers()
    {
        var interpreted = Instance(Arithmetic, compile: false).Call<long>("total", 1000);
        var compiled = Instance(Arithmetic, compile: true).Call<long>("total", 1000);

        Assert.Equal(interpreted, compiled);
    }

    [Fact]
    public void The_option_is_what_decides()
    {
        // The control for the test above: without it, "the answers agree" would also hold if
        // nothing were ever compiled.
        var off = Instance(Arithmetic, compile: false);
        off.Call<long>("total", 10);

        // LYRIC_JIT=1 forces compilation for every program in the process — it is how the CI runs
        // this suite a second time on the other engine — and the negative half cannot hold under
        // it. The metered test below needs no such guard: no switch makes a metered call compile.
        if (Environment.GetEnvironmentVariable("LYRIC_JIT") != "1")
            Assert.Equal(0, off.CompiledFunctions);

        var on = Instance(Arithmetic, compile: true);
        on.Call<long>("total", 10);
        Assert.True(on.CompiledFunctions > 0,
            "nothing was compiled with Compile = true; refusals: "
            + string.Join(", ", on.Refusals.Select(r => $"{r.Function}: {r.Reason}")));
    }

    [Fact]
    public void A_metered_call_stays_interpreted()
    {
        // The promise the budget makes — the same script stops at the same instruction — is one
        // compiled code cannot keep, so it is not used where the promise was made. This is what
        // lets a host set the option for a whole VM and still meter the foreign code in it.
        var instance = Instance(Arithmetic, compile: true);
        var budget = new ExecutionBudget(1_000_000);

        instance.Call<long>("total", budget, 100);

        Assert.Equal(0, instance.CompiledFunctions);
        Assert.True(budget.Consumed > 0, "a metered call counted nothing");
    }

    [Fact]
    public void A_refusal_is_a_fallback_and_not_a_failure()
    {
        // Recursion is declined today. The function still runs, with the right answer, and the
        // refusal is readable — which is the whole safety story: it costs speed, never
        // correctness.
        var instance = Instance("""
            pub fn fib(n: int): int {
                if (n < 2) { return n; }
                return fib(n - 1) + fib(n - 2);
            }
            """, compile: true);

        Assert.Equal(55L, instance.Call<long>("fib", 10));
        Assert.Contains(instance.Refusals, r => r.Function.EndsWith(".fib", StringComparison.Ordinal));
    }
}
