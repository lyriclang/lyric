using Lyric.AST;
using Lyric.Core;

namespace Lyric.Ir.Lowering;

/// <summary>
/// Builds the FACTORY of a coroutine: the function that stands under the written name and yields
/// the suspended chain.
///
/// <para>Since format 4.0 it is one instruction of substance: <c>mkcoro</c> captures the
/// arguments — <c>this</c> first when the coroutine is a method — and builds the chain object,
/// not-yet-started; the first pull hands them to the body's frame as its parameters. The
/// state-machine era allocated a field object and closed a state machine over it here.</para>
///
/// <para>A file of its own rather than a mode in the FunctionLowerer, because the factory lowers no
/// written code. It has no body, no expressions and no control flow; housing it in the big lowerer
/// would mean a branch there that uses none of its machinery.</para>
/// </summary>
internal static class CoroutineFactory
{
    public static IrFunction Build(FunctionDecl decl, string name, IrType yieldType,
        FunctionId body, IrType[] parameterTypes, bool hasReceiver, IrType? receiverType,
        Span span)
    {
        var slots = new SlotAllocator();
        var blocks = new List<IrBlock>();
        var builder = new BlockBuilder(blocks); // creates bb0 and points the cursor at it

        // The factory's parameters are the coroutine's, in the same order, so a caller does nothing
        // different from any other function.
        if (hasReceiver && receiverType is not null) slots.Declare("this", receiverType);
        for (var i = 0; i < decl.Parameters.Length; i++)
            slots.Declare(decl.Parameters[i].Name, parameterTypes[i]);

        var args = new List<TempId>();
        var slot = 0;
        if (hasReceiver && receiverType is not null)
        {
            var value = slots.NewTemp(receiverType);
            builder.Emit(new LoadLocal(value, new LocalId(slot++), receiverType, span));
            args.Add(value);
        }
        for (var i = 0; i < decl.Parameters.Length; i++, slot++)
        {
            var value = slots.NewTemp(parameterTypes[i]);
            builder.Emit(new LoadLocal(value, new LocalId(slot), parameterTypes[i], span));
            args.Add(value);
        }

        // The chain value keeps the coroutine signature the closure era gave it, so nothing about
        // assignability, fields or the format's type tags moves with the mechanism.
        var signature = TypeTable.CoroutineSignature(yieldType);
        var chain = slots.NewTemp(signature);
        builder.Emit(new MakeCoroutine(chain, body, args.ToArray(), signature, span));
        builder.Seal(new Return(chain, span));

        return new IrFunction(name, signature,
            decl.Parameters.Length + (hasReceiver ? 1 : 0), slots.Locals, slots.Temps, blocks)
        {
            Entry = new BlockId(0),
        };
    }
}
