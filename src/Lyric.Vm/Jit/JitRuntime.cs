namespace Lyric.Vm.Jit;

/// <summary>
/// The handful of operations compiled code calls rather than inlines.
///
/// <para>Everything here exists because it either allocates or can fail, and both are things that
/// want one implementation rather than one per emit site. The failures matter most: an index out
/// of range and a null reference have to produce the SAME exception, with the same message, as
/// the interpreter — a host that develops interpreted and ships compiled would otherwise get a
/// different error in the field than the one it debugged.</para>
///
/// <para>The function name comes in as a literal string, baked into the call. It is the one thing
/// compiled code cannot recover for itself: the interpreter reads it off the frame, and there is
/// no frame here.</para>
/// </summary>
internal static class JitRuntime
{
    /// <summary>The slots behind a reference, or the interpreter's exception when there is none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The reference is null — matching
    /// <see cref="LyrValue.AsObject"/> in both type and message.</exception>
    public static LyrValue[] Slots(LyrValue[]? reference) =>
        reference ?? throw new InvalidOperationException("null object reference");

    public static LyrValue Element(LyrValue[]? array, long index, string function)
    {
        var slots = Slots(array);
        return slots[Checked(index, slots.Length, function)];
    }

    public static void SetElement(LyrValue[]? array, long index, LyrValue value, string function)
    {
        var slots = Slots(array);
        slots[Checked(index, slots.Length, function)] = value;
    }

    public static long Length(LyrValue[]? array) => Slots(array).Length;

    public static LyrValue[] Field(LyrValue[]? reference) => Slots(reference);

    public static LyrValue[] Concat(LyrValue[]? left, LyrValue[]? right)
    {
        var a = Slots(left);
        var b = Slots(right);

        var joined = new LyrValue[a.Length + b.Length];
        a.CopyTo(joined, 0);
        b.CopyTo(joined, a.Length);
        return joined;
    }

    public static LyrValue[] Repeat(LyrValue[]? source, long count)
    {
        var slots = Slots(source);

        if (count < 0)
            throw new LyricPanic(VmDiagnostics.IndexOutOfRange,
                $"array repetition count {count} is negative");

        var repeated = new LyrValue[slots.Length * count];
        for (var i = 0; i < count; i++) slots.CopyTo(repeated, i * slots.Length);
        return repeated;
    }

    /// <summary>A reference as a value, tolerating null — a slot that has never been written
    /// holds one, and packing it must not throw.</summary>
    public static LyrValue Reference(object? value) =>
        value is null ? default : LyrValue.FromHostObject(value);

    /// <summary>The array behind a value, without the null check: an untouched slot legitimately
    /// holds nothing, and whether that is an error is decided where it is USED, exactly as in the
    /// interpreter.</summary>
    public static LyrValue[]? AsArray(LyrValue value) => value.Ref as LyrValue[];

    public static string AsText(LyrValue value) => value.Ref as string ?? string.Empty;

    /// <summary>The value inside an optional, or the interpreter's panic when there is none —
    /// same code, same wording, and the function name baked in because compiled code has no frame
    /// to read it off.</summary>
    public static LyrValue Unwrap(LyrValue option, string function)
    {
        if (option.IsSome) return option.Unwrap();

        throw new LyricPanic(VmDiagnostics.NullDereference,
            $"force-unwrapped a '?T' that had no value in '{function}'");
    }

    /// <summary>
    /// A numeric conversion, done by the INTERPRETER's own routine rather than by IL.
    ///
    /// <para>Not laziness -- a matter of not being able to. Float to integer is defined as
    /// WASM trunc_sat: truncate towards zero, clamp at the ends, NaN to zero. IL's conv leaves
    /// an out-of-range float to the platform, so emitting one would be a wrong answer on the
    /// values that are hardest to notice and easiest to hit.</para>
    ///
    /// <para>The tags are compile-time constants baked into the call, so what remains at run time
    /// is one call rather than a decision.</para>
    /// </summary>
    public static LyrValue Convert(LyrValue value, int from, int to) =>
        Interpreter.Convert((Lyric.Bytecode.TypeTag)from, (Lyric.Bytecode.TypeTag)to, value);

    /// <summary>Ordinal string equality, as the interpreter compares them — and as the 0-or-1 a
    /// Lyric bool is here.</summary>
    public static long TextEquals(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal)
            ? 1L
            : 0L;

    /// <summary>Whether an optional holds anything, as the 0-or-1 a Lyric bool is here.</summary>
    public static long HasValue(LyrValue option) => option.IsSome ? 1L : 0L;

    // The scalar unpackers exist as STATIC methods rather than as the properties on LyrValue,
    // because a property on a struct needs a managed pointer -- and a value that has just come
    // back from a call sits on the stack, where there is none to take.
    public static long ToI64(LyrValue value) => value.AsI64;

    public static double ToF64(LyrValue value) => value.AsF64;

    public static float ToF32(LyrValue value) => value.AsF32;

    private static int Checked(long index, int length, string function)
    {
        if (index >= 0 && index < length) return (int)index;

        throw new LyricPanic(VmDiagnostics.IndexOutOfRange,
            $"index {index} is outside an array of length {length} in '{function}'");
    }
}
