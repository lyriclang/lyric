using Lyric.Bytecode;

namespace Lyric.Vm.Jit;

/// <summary>
/// Everything compiled code needs that is not on its own stack: the program around it.
///
/// <para>One per <see cref="LoadedProgram"/>, made when compilation is switched on and handed to
/// every compiled function as its first argument. It carries the module's tables so the emitter
/// can read types at compile time, and the globals array so compiled and interpreted code write
/// to the SAME state — a copy here would pass every single-engine test and quietly lose a game's
/// progress.</para>
///
/// <para><b>Calls go through here rather than being bound at compile time</b>, and that is what
/// makes recursion work without any cycle analysis. A function being compiled cannot yet hand out
/// its own delegate; a late-bound call asks for it when it runs, by which time it exists. The
/// price is one indirection per call, which sits next to a native's own cost and is invisible
/// beside it.</para>
/// </summary>
internal sealed class JitContext
{
    public required Interpreter.Prepared[] Prepared { get; init; }

    public required NativeRegistry.BoundNative[] Natives { get; init; }

    public required LyrValue[] Globals { get; init; }

    public required IReadOnlyList<BytecodeType> GlobalTypes { get; init; }

    public required IReadOnlyList<BytecodeTypeDef> Types { get; init; }

    public required IReadOnlyList<BytecodeImport> Imports { get; init; }

    public required IReadOnlyList<string> Strings { get; init; }

    public required DispatchTable Dispatch { get; init; }

    public required ArgumentPool Arguments { get; init; }

    public BytecodeSourceMap? SourceMap { get; init; }

    /// <summary>
    /// This function's compiled form, compiling it the first time it is asked for.
    ///
    /// <para>A refusal is cached as firmly as a success: most functions in a game touch something
    /// the compiler declines, and re-analysing them on every call would cost more than compiling
    /// the rest saves.</para>
    ///
    /// <para><see cref="Interpreter.Prepared.JitTried"/> is set BEFORE the attempt, so a function
    /// that calls itself finds "asked, no answer yet" rather than recursing forever. It reaches
    /// the same place a refusal does — a late-bound call — and that call finds the finished
    /// delegate once compilation returns.</para>
    /// </summary>
    public Compiled? CodeFor(Interpreter.Prepared function)
    {
        if (function.JitTried) return function.Compiled;

        function.JitTried = true;
        function.Compiled = JitCompiler.TryCompile(function, this);
        return function.Compiled;
    }

    /// <summary>A buffer for a call's arguments, from the same pool the interpreter uses.
    /// </summary>
    public LyrValue[] RentArgs(int arity) => Arguments.Rent(arity);

    /// <summary>
    /// Calls anything the shared index space names: an import runs in the host, a function runs
    /// compiled if it can and interpreted if it cannot.
    ///
    /// <para>The buffer is recycled on the way out and abandoned on the way through an exception —
    /// the interpreter's rule, and for the interpreter's reason: losing a pool entry costs the
    /// next allocation and never correctness.</para>
    /// </summary>
    public LyrValue Call(int index, LyrValue[] args)
    {
        if (index < Natives.Length)
        {
            var native = Natives[index];
            var produced = native.Implementation(args);
            Arguments.Recycle(args);
            return produced;
        }

        var callee = Prepared[index - Natives.Length];
        var code = CodeFor(callee);

        // The compiler only emits a call to a function it has already compiled, so the fallback
        // is unreachable in a well-formed program. It is here anyway, and it interprets rather
        // than throwing, because "slower than intended" is a better failure than "stopped".
        var result = code is not null
            ? code(this, args)
            : Interpreter.Execute(
                Prepared, index - Natives.Length, Strings, Types, Dispatch, Natives, Globals,
                GlobalTypes, Arguments, args, SourceMap);

        Arguments.Recycle(args);
        return result;
    }
}
