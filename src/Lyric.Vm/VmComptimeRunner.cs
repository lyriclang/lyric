using Lyric.Bytecode;
using Lyric.Core;

namespace Lyric.Vm;

/// <summary>
/// The runtime as the compiler's <c>comptime</c> evaluator.
///
/// <para>The evaluation module is loaded like any foreign module — with NO capability and an
/// instruction budget — and each site's function is invoked once. That is the whole promise of
/// <c>comptime</c> in one place: a site computes what the language computes, it cannot reach
/// the file system, the network, the clock or the environment, and it cannot take the build
/// down with it. A site that tries is an outcome with a reason, which the compiler reports at
/// the site; nothing here writes to a console.</para>
///
/// <para>Determinism follows from the same facts. The language's arithmetic is specified to the
/// bit, the only non-deterministic draws are capability-gated, and the budget counts instructions
/// rather than time — so the same source yields the same literal on every machine, which is what
/// lets a build be reproduced.</para>
/// </summary>
public sealed class VmComptimeRunner : IComptimeRunner
{
    /// <summary>Instructions per site and for the module initializer. Generous, because a table
    /// built at compile time is exactly the thing that deserves the cycles — and bounded, because
    /// a build that never finishes is worse than one that says why.</summary>
    public long Budget { get; init; } = 100_000_000;

    /// <summary>
    /// The natives a compile-time evaluation refuses although no capability gates them: the
    /// draws whose whole point is that a run cannot be repeated. A site reaching one would
    /// compile to a different literal on every build, which is the one thing <c>comptime</c>
    /// must never do. Refused by import name — the name is symbolic in the module (§11) — rather
    /// than by a capability the rest of the language would then have to carry.
    /// </summary>
    private static readonly string[] NonDeterministic = ["std.random.secureRandom"];

    public IReadOnlyList<ComptimeOutcome> Evaluate(byte[] bytes, IReadOnlyList<string> functions)
    {
        var module = BytecodeReader.ReadOrThrow(bytes);

        foreach (var import in module.Imports)
            if (NonDeterministic.Contains(import.Name, StringComparer.Ordinal))
                return functions.Select(_ => ComptimeOutcome.Failed(
                    $"it reaches '{import.Name}', whose result differs from run to run — a value "
                    + "computed at compile time has to be the same on every build")).ToArray();

        using var natives = NativeRegistry.CreateDefault(TextWriter.Null, TextWriter.Null);

        LoadedProgram program;
        try
        {
            program = LoadedProgram.Load(module, natives, Capability.None, new ExecutionBudget(Budget));
        }
        catch (LyricRuntimeException refused)
        {
            // A capability the sites' closure would need, or an import nobody binds: every site
            // shares the answer, because the module is loaded once for all of them.
            return functions.Select(_ => ComptimeOutcome.Failed(
                refused.Code == VmDiagnostics.CapabilityDenied
                    ? "it reaches something that needs a capability, and compile-time evaluation grants none"
                      + $" ({refused.Message})"
                    : refused.Message)).ToArray();
        }
        catch (LyricPanic panic)
        {
            return functions.Select(_ => ComptimeOutcome.Failed(
                $"the module's constant initializer {Describe(panic)}")).ToArray();
        }

        var outcomes = new ComptimeOutcome[functions.Count];
        for (var i = 0; i < functions.Count; i++)
        {
            var index = program.IndexOfFunction(functions[i]);
            if (index < 0)
            {
                outcomes[i] = ComptimeOutcome.Failed($"the evaluation module has no '{functions[i]}'");
                continue;
            }

            try
            {
                var value = program.Invoke(index, new ExecutionBudget(Budget));
                outcomes[i] = ComptimeOutcome.Of(Box(value, module.Functions[index].ReturnType.Tag));
            }
            catch (LyricPanic panic)
            {
                outcomes[i] = ComptimeOutcome.Failed(Describe(panic));
            }
        }

        return outcomes;
    }

    private static string Describe(LyricPanic panic) => panic.Code == VmDiagnostics.BudgetExhausted
        ? "did not finish within the compile-time budget"
        : $"panicked [{panic.Code}]: {panic.Message}";

    /// <summary>The value as the compiler's boxed form, by the function's declared return tag —
    /// the one place the bit pattern of a <see cref="LyrValue"/> is given a meaning.</summary>
    private static object Box(LyrValue value, TypeTag tag) => tag switch
    {
        TypeTag.String => value.AsString,
        TypeTag.Bool => value.AsBool,
        TypeTag.F64 => value.AsF64,
        TypeTag.F32 => (double)value.AsF32,
        TypeTag.Char => (int)value.AsI64,
        TypeTag.U64 => value.AsU64,
        _ => value.AsI64,
    };
}
