namespace Lyric.Ir.Interp;

/// <summary>
/// A value of the compile-time interpreter: a scalar's bits, or
/// one of the heap objects of this file in <see cref="Ref"/>.
/// <para>The bits hold an integer in two's complement, sign-extended to 64 for a signed type and
/// zero-extended for an unsigned one; a <c>float</c> as its 64 bits, a <c>float32</c> as its 32 in
/// the low half; a <c>bool</c> 0 or 1; a <c>char</c> its scalar value.</para>
/// <para>An AGGREGATE — a struct, an enum, an inline array, an optional — is a mutable object, and
/// a temp ALIASES it, as a C temp of such a type points at its storage (<c>CEmitter.IsAggregate</c>):
/// a store copies INTO the storage that is there (<see cref="Heap.CopyInto"/>), so an alias taken
/// before the store reads the new value, as in C.</para>
/// </summary>
public readonly record struct Value(ulong Bits, object? Ref)
{
    public static Value Int(long v) => new(unchecked((ulong)v), null);
    public static Value Uint(ulong v) => new(v, null);
    public static Value Bool(bool v) => new(v ? 1UL : 0UL, null);
    public static Value Double(double v) => new(unchecked((ulong)BitConverter.DoubleToInt64Bits(v)), null);
    public static Value Single(float v) => new(BitConverter.SingleToUInt32Bits(v), null);
    public static Value Of(object o) => new(0, o);

    public long AsInt => unchecked((long)Bits);
    public bool AsBool => Bits != 0;
    public double AsDouble => BitConverter.Int64BitsToDouble(unchecked((long)Bits));
    public float AsSingle => BitConverter.UInt32BitsToSingle(unchecked((uint)Bits));
}

/// <summary>A string: its UTF-8 bytes, which nothing changes.</summary>
public sealed class StrObj(byte[] bytes)
{
    public byte[] Bytes { get; } = bytes;
    public static StrObj Of(string text) => new(System.Text.Encoding.UTF8.GetBytes(text));
    public override string ToString() => System.Text.Encoding.UTF8.GetString(Bytes);
}

/// <summary><c>T[]</c>: a reference; its slots are storage, an aggregate element an object of its
/// own that a load aliases.</summary>
public sealed class ArrayObj(Value[] items)
{
    public Value[] Items { get; } = items;
}

/// <summary><c>Slice&lt;T&gt;</c> and <c>StringView</c>: a view of an array's slots or of a
/// string's bytes, <see cref="Length"/> of them from <see cref="Offset"/> — two words that share
/// what they show, copied as a scalar is.</summary>
public sealed class SliceObj(object source, int offset, int length)
{
    /// <summary>An <see cref="ArrayObj"/>, or a <see cref="StrObj"/> for a view of bytes.</summary>
    public object Source { get; } = source;
    public int Offset { get; } = offset;
    public int Length { get; } = length;
}

/// <summary>A class object, a closure's environment, a cell: a reference with fields.</summary>
public sealed class ClassObj(TypeId type, Value[] fields)
{
    public TypeId Type { get; } = type;
    public Value[] Fields { get; } = fields;
}

/// <summary>An aggregate: a struct (its fields), an enum (slot 0 its tag, then the variant's
/// payload), an inline array (its elements). Mutable storage, aliased by temps.</summary>
public sealed class AggObj(AggKind kind, TypeId type, Value[] slots)
{
    public AggKind Kind { get; } = kind;
    /// <summary>The struct's entry, or the enum's (not the variant's); unused for an inline array.</summary>
    public TypeId Type { get; } = type;
    /// <summary>Replaced whole when an enum's storage takes another variant.</summary>
    public Value[] Slots { get; set; } = slots;
}

public enum AggKind { Struct, Enum, Inline }

/// <summary>An optional, present or not — one form for the niche and the flag, whose
/// difference is C's layout, not the language's.</summary>
public sealed class OptObj(bool has, Value inner)
{
    public bool Has { get; set; } = has;
    public Value Inner { get; set; } = inner;
}

/// <summary>An interface value: the object of a class, or a box holding a value's copy,
/// with the concrete type the table row is found by.</summary>
public sealed class IfaceObj(Value data, TypeId concrete, TypeId iface)
{
    /// <summary>A <see cref="ClassObj"/>, or a <see cref="BoxObj"/>.</summary>
    public Value Data { get; } = data;
    public TypeId Concrete { get; } = concrete;
    public TypeId Interface { get; } = iface;
}

/// <summary>A value's box behind an interface: the payload is its own storage.</summary>
public sealed class BoxObj(Value payload)
{
    public Value Payload { get; set; } = payload;
}

/// <summary>A function value: the lifted function and its environment, or none.</summary>
public sealed class ClosureObj(FunctionId target, Value environment)
{
    public FunctionId Target { get; } = target;
    public Value Environment { get; } = environment;
}

/// <summary>A place: what a place parameter holds — a slot to read and write.</summary>
public sealed class PlaceObj(Func<Value> get, Action<Value> set)
{
    public Value Get() => get();
    public void Set(Value v) => set(v);
}

/// <summary>An error in flight: the thrown value as an <c>Error</c> interface value,
/// with the errors suppressed into it.</summary>
public sealed class ErrorRecord(Value value)
{
    public Value Value { get; } = value;
    public List<ErrorRecord> Suppressed { get; } = [];
}

/// <summary>The copies the IR's value semantics ask for.</summary>
public static class Heap
{
    public static bool IsAggregate(Value v) => v.Ref is AggObj or OptObj;

    /// <summary>A fresh copy of an aggregate, deep through the aggregates it holds; anything else
    /// as it is — a reference is shared, a scalar has no identity.</summary>
    public static Value Copy(Value v) => v.Ref switch
    {
        AggObj a => Value.Of(new AggObj(a.Kind, a.Type, CopySlots(a.Slots))),
        OptObj o => Value.Of(new OptObj(o.Has, Copy(o.Inner))),
        _ => v,
    };

    private static Value[] CopySlots(Value[] slots)
    {
        var copy = new Value[slots.Length];
        for (var i = 0; i < slots.Length; i++) copy[i] = Copy(slots[i]);
        return copy;
    }

    /// <summary>A store: <paramref name="source"/> written into the storage <paramref name="target"/>
    /// names — in place where it is an aggregate, so aliases of it see the store. What the slot
    /// holds afterwards comes back.</summary>
    public static Value Store(Value target, Value source)
    {
        switch (target.Ref, source.Ref)
        {
            case (AggObj t, AggObj s):
                if (t.Kind == AggKind.Enum || t.Slots.Length != s.Slots.Length)
                {
                    t.Slots = CopySlots(s.Slots); // another variant, another payload
                    return target;
                }
                for (var i = 0; i < s.Slots.Length; i++) t.Slots[i] = Store(t.Slots[i], s.Slots[i]);
                return target;
            case (OptObj t, OptObj s):
                t.Inner = s.Has && t.Has ? Store(t.Inner, s.Inner) : Copy(s.Inner);
                t.Has = s.Has;
                return target;
            default:
                return Copy(source);
        }
    }
}
