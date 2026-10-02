using Lyric.Core;

namespace Lyric.Ir.Lowering;

/// <summary>
/// Builds the FACTORY of a coroutine: the function that stands under the written name and returns
/// the coroutine, not started.
///
/// <para>One instruction of substance: <c>mkcoro</c> takes the arguments — <c>this</c> first when
/// the coroutine is a method — and builds the coroutine over them; the first pull hands them to
/// the body as its parameters (06 A1). The factory's parameters are the body's, read off its
/// lowerer before the body runs, so the two cannot disagree — a generic instance's included.</para>
///
/// <para>A file of its own rather than a mode in the FunctionLowerer, because the factory lowers no
/// written code. It has no body, no expressions and no control flow; housing it in the big lowerer
/// would mean a branch there that uses none of its machinery.</para>
/// </summary>
internal static class CoroutineFactory
{
    public static IrFunction Build(string name, IrCoroutineType type, FunctionId body,
        IReadOnlyList<IrLocal> parameters, Span span)
    {
        var slots = new SlotAllocator();
        var blocks = new List<IrBlock>();
        var builder = new BlockBuilder(blocks); // creates bb0 and points the cursor at it

        // The factory's parameters are the coroutine's, in the same order, so a caller does nothing
        // different from any other function.
        foreach (var parameter in parameters) slots.Declare(parameter.Name, parameter.Type);

        var args = new TempId[parameters.Count];
        for (var i = 0; i < parameters.Count; i++)
        {
            args[i] = slots.NewTemp(parameters[i].Type);
            builder.Emit(new LoadLocal(args[i], new LocalId(i), parameters[i].Type, span));
        }

        var coroutine = slots.NewTemp(type);
        builder.Emit(new MakeCoroutine(coroutine, body, args, type, span));
        builder.Seal(new Return(coroutine, span));

        return new IrFunction(name, type, parameters.Count, slots.Locals, slots.Temps, blocks)
        {
            Entry = new BlockId(0),
        };
    }
}
