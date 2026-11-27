using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>The overall sema entry point: type checking and the flow analyses through the
/// TypeChecker, plus the structural rules. The resolver runs before it and supplies the
/// <see cref="BindingResult"/>.</summary>
public static class Semantics
{
    /// <param name="singleProgram">Whether this compilation is one executable. The entry contract
    /// allows exactly one 'main' per executable; a workspace compilation holds several programs at
    /// once, and there a second 'main' is another program rather than a duplicate. The shape rule
    /// for 'main' applies either way.</param>
    public static TypeResult Analyze(Compilation compilation, BindingResult binding,
        DiagnosticEngine de, bool singleProgram = true, bool ideQuery = false,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var checker = new TypeChecker(compilation, binding, de, cancellation);
        var types = checker.Check();

        // A NESTING BOUND STOPS THE WHOLE PIPELINE, not just the checker. Both walkers below
        // recurse over the same tree the checker gave up on, and 'SemaRules.WalkExpr' proved it:
        // the checker reported LYR-SEM0098 as it should and the process died three lines later,
        // in the walk that followed. Whatever cannot be checked cannot be walked either.
        if (types.NestingExceeded || ideQuery) return types;

        cancellation.ThrowIfCancellationRequested();
        new SemaRules(compilation, binding, types, de, singleProgram).Run();
        cancellation.ThrowIfCancellationRequested();
        // Marked propagation and coverage; whether an element covers a thrown type is
        // the checker's conformance question, asked where the site stands.
        new ExceptionAnalyzer(compilation, types, de, checker.ThrownCoveredBy, checker.ErrorRoot, checker.CancelledType).Run();
        cancellation.ThrowIfCancellationRequested();

        // Warnings describe a program that compiles. Over a broken one the reference tables are
        // partial, and a warning computed from half a table is a guess with a confident tone.
        compilation.DocLinks = new DocLinks(compilation, binding, types);
        if (!de.HasErrors) new WarningAnalyzer(compilation, binding, types, de).Run();
        return types;
    }
}
