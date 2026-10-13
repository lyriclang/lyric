using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace Lyric.Ir.Interp;

/// <summary>
/// The standard library's natives at compile time (design/v5/spec/09 Q3, Q7; 13 M9a P2): each
/// pure one as the runtime computes it — the C beside each case is <c>runtime/src</c>'s —, each
/// one that touches the world (the console, files, the network, processes, the clock, threads and
/// tasks, the system) refused where it is called (<c>LYR-CT0003</c>).
/// </summary>
public sealed class Natives(Interpreter interpreter)
{
    private delegate Value Native(Value[] args, IrType[] types, IrType result);

    /// <summary>The natives that change or read the world, by the module that owns them.</summary>
    private static readonly string[] WorldModules = ["std.io.", "std.fs.", "std.net.", "std.process.", "std.os.", "std.thread."];

    private static readonly HashSet<string> WorldNatives = new(StringComparer.Ordinal)
    {
        "std.core.benchClock", "std.core.threadAllocations", "std.core.threadAllocatedBytes",
        "std.task.startContext", "std.task.park", "std.task.deadlocked", "std.task.poisonedBy",
        "std.task.setCurrentScheduler", "std.task.waitOnPoller", "std.task.monotonicNanos",
        "std.task.resumeContext", "std.task.panicCode", "std.task.panicMessage", "std.task.panicTrace",
        "std.task.repanic", "std.task.startThread", "std.task.currentPoller", "std.task.wakePoller",
        "std.task.armOnPoller", "std.task.forgetOnPoller", "std.task.takeFromPoller",
    };

    /// <summary>The C library's functions that each libm rounds its own way: no answer at compile
    /// time is the same on every platform (Q7) until one implementation serves the compiler and
    /// the runtime alike (13 M9a S4).</summary>
    private static readonly HashSet<string> RoundedByEachLibm = new(StringComparer.Ordinal)
    {
        "C:cbrt", "C:exp", "C:log", "C:log2", "C:log10", "C:sin", "C:cos", "C:tan", "C:asin", "C:acos",
        "C:atan", "C:sinh", "C:cosh", "C:tanh", "C:atan2", "C:hypot", "C:pow",
    };

    private readonly ConditionalWeakTable<object, StrongBox<long>> _identities = new();
    private long _nextIdentity = 1;

    /// <summary>What <c>embed</c> reads through (09 Q4); <c>null</c>, it reads nothing.</summary>
    public EmbedReader? Embed { get; init; }

    /// <summary>What the build is for and as (09 B4), which <c>std.core</c>'s <c>target</c> and
    /// <c>profile</c> read.</summary>
    public BuildFacts Facts { get; init; } = BuildFacts.Host;

    /// <summary>Where the native at hand is called: <c>embed</c>'s path is the calling module's.</summary>
    private Core.Span _at;

    public Value Call(IrImport import, Value[] args, IrType[] types, Core.Span at)
    {
        _at = at;
        var name = import.Name;
        if (Table.TryGetValue(name, out var native)) return native(args, types, import.ReturnType);
        if (RoundedByEachLibm.Contains(name))
            throw ComptimeFailure.NotYet($"'{Short(name)}' rounds as each C library does, and compile time has no answer the same on every platform yet");
        if (WorldNatives.Contains(name) || WorldModules.Any(m => name.StartsWith(m, StringComparison.Ordinal)) || name.StartsWith("C:", StringComparison.Ordinal))
            throw ComptimeFailure.World($"'{Short(name)}' reaches outside the program — {What(name)} — and code run at compile time does not (09 Q3)");
        throw ComptimeFailure.NotYet($"'{Short(name)}' does not run at compile time yet");
    }

    // --- the Unicode Character Database (unicode.c over unicode_tables.h): the same lookups over
    // the same tables, which tooling/unicode/gen.py writes for both — not .NET's, which are of
    // another Unicode version.

    private static long UnicodeCategory(uint c)
    {
        if (c >= 0x110000) return 29; // Cn: no character has a scalar value beyond 0x10FFFF
        var block = UnicodeTables.CategoryIndex[(int)(c >> UnicodeTables.CategoryShift)];
        return UnicodeTables.CategoryBlocks[(block << UnicodeTables.CategoryShift) | (int)(c & ((1u << UnicodeTables.CategoryShift) - 1))];
    }

    /// <summary>The mapping of <paramref name="c"/> in a table of pairs sorted by the character
    /// mapped, or <paramref name="c"/> itself.</summary>
    private static uint Mapped(ReadOnlySpan<uint> pairs, uint c)
    {
        int lo = 0, hi = pairs.Length / 2;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (pairs[2 * mid] < c) lo = mid + 1;
            else hi = mid;
        }
        return lo < pairs.Length / 2 && pairs[2 * lo] == c ? pairs[2 * lo + 1] : c;
    }

    private static bool UnicodeWhiteSpace(uint c)
    {
        var ranges = UnicodeTables.WhiteSpace;
        for (var i = 0; i < ranges.Length; i += 2)
        {
            if (c < ranges[i]) return false;
            if (c <= ranges[i + 1]) return true;
        }
        return false;
    }

    private static string Short(string name) => name.StartsWith("C:", StringComparison.Ordinal) ? name[2..] : name;

    private static string What(string name) => name switch
    {
        // the runtime's own C calls, which std declares 'extern "C"', by their area
        _ when name.StartsWith("C:lyr_os_", StringComparison.Ordinal) => "the system",
        _ when name.StartsWith("C:lyr_fs_", StringComparison.Ordinal) => "a file",
        _ when name.StartsWith("C:lyr_net_", StringComparison.Ordinal) => "the network",
        _ when name.StartsWith("C:lyr_process_", StringComparison.Ordinal) => "a process",
        _ when name.StartsWith("C:lyr_random", StringComparison.Ordinal) => "the system's randomness",
        _ when name.StartsWith("C:", StringComparison.Ordinal) => "a C function",
        _ when name.StartsWith("std.io.", StringComparison.Ordinal) => "the console",
        _ when name.StartsWith("std.fs.", StringComparison.Ordinal) => "a file",
        _ when name.StartsWith("std.net.", StringComparison.Ordinal) => "the network",
        _ when name.StartsWith("std.process.", StringComparison.Ordinal) => "a process",
        _ when name.StartsWith("std.os.", StringComparison.Ordinal) => "the system",
        _ when name.Contains("Clock", StringComparison.Ordinal) || name.Contains("Nanos", StringComparison.Ordinal) => "the clock",
        _ when name.Contains("Alloc", StringComparison.Ordinal) => "the allocator",
        _ => "a thread or a task",
    };

    private Dictionary<string, Native>? _table;
    private Dictionary<string, Native> Table => _table ??= Build();

    private static StrObj S(Value v) => (StrObj)v.Ref!;
    private static Value Str(string text) => Value.Of(StrObj.Of(text));
    private static Value Str(byte[] bytes) => Value.Of(new StrObj(bytes));
    private static IrScalar K(IrType t) => t is IrScalarType s ? s.Kind : IrScalar.Void;
    private static long I(Value v) => v.AsInt;

    private Dictionary<string, Native> Build() => new(StringComparer.Ordinal)
    {
        // --- strings (string.c)
        ["std.string.concat"] = (a, _, _) => Str([.. S(a[0]).Bytes, .. S(a[1]).Bytes]),
        ["std.string.fromInt"] = (a, _, _) => Str(I(a[0]).ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["std.string.fromUint"] = (a, _, _) => Str(a[0].Bits.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["std.string.fromBool"] = (a, _, _) => Str(a[0].AsBool ? "true" : "false"),
        ["std.string.fromFloat"] = (a, _, _) => Str(FloatText.Shortest(a[0].AsDouble)),
        ["std.core.textOfFloat"] = (a, _, _) => Str(FloatText.Shortest(a[0].AsDouble)),
        ["std.string.fromFloat32"] = (a, _, _) => Str(FloatText.Shortest(a[0].AsSingle)),
        ["std.core.shortestFloat32"] = (a, _, _) => Str(FloatText.Shortest(a[0].AsSingle)),
        ["std.core.textOfFloat32"] = (a, _, _) => Str(FloatText.Shortest(a[0].AsSingle)),
        ["std.string.fromChar"] = (a, _, _) => Str(Utf8((uint)a[0].Bits)),
        ["std.core.floatText"] = (a, _, _) => Str(FloatText.WithPrecision(a[0].AsDouble, I(a[1]), I(a[2]))),
        ["std.core.floatOfText"] = (a, _, _) => Value.Double(FloatText.Parse(S(a[0]).ToString())),
        ["std.core.float32OfText"] = (a, _, _) => Value.Single((float)FloatText.Parse(S(a[0]).ToString())),
        ["std.core.compareBytes"] = (a, _, _) => Value.Int(Order(S(a[0]).Bytes, S(a[1]).Bytes)),
        ["std.core.byteSlice"] = (a, _, _) =>
        {
            var view = (SliceObj)Interpreter.Slice(a[0], I(a[1]), I(a[2]), chars: true).Ref!;
            return Str(Bytes.Of(view));
        },
        ["std.core.stringOfBytes"] = (a, _, _) =>
        {
            var array = (ArrayObj)a[0].Ref!;
            var count = I(a[1]);
            if (count < 0 || count > array.Items.Length)
                throw ComptimeFailure.Panic("LYR-RT0003", $"range 0..{count} out of bounds for length {array.Items.Length}");
            return Str([.. array.Items.Take((int)count).Select(v => (byte)v.Bits)]);
        },

        // --- views of bytes (string.h)
        ["std.core.byteCount"] = (a, _, _) => Value.Int(Interpreter.View(a[0]).Length),
        ["std.core.byteAt"] = (a, _, _) =>
        {
            var bytes = Bytes.Of(a[0]);
            return Value.Int(bytes[Interpreter.Index(a[1], bytes.Length)]);
        },
        ["std.core.viewMatchesAt"] = (a, _, _) =>
        {
            var s = Bytes.Of(a[0]);
            var p = Bytes.Of(a[1]);
            var at = I(a[2]);
            return Value.Bool(at >= 0 && at <= s.Length - p.Length && s.AsSpan((int)at, p.Length).SequenceEqual(p));
        },
        ["std.core.viewsHoldTheSameBytes"] = (a, _, _) => Value.Bool(Bytes.Of(a[0]).AsSpan().SequenceEqual(Bytes.Of(a[1]))),
        ["std.core.compareViewBytes"] = (a, _, _) => Value.Int(Order(Bytes.Of(a[0]), Bytes.Of(a[1]))),
        ["std.core.viewToString"] = (a, _, _) => Str(Bytes.Of(a[0])),
        ["std.core.stringOfSlice"] = (a, _, _) => Str(Bytes.Of(a[0])),
        ["std.core.charOfView"] = (a, _, _) => Value.Uint(CharAt(Bytes.Of(a[0]), I(a[1]))),
        ["std.core.viewBytes"] = (a, _, _) => a[0],
        ["std.core.utf8Invalid"] = (a, _, _) => Value.Int(InvalidAt(Bytes.Of(a[0]))),
        ["std.core.stringOfUtf8Lossy"] = (a, _, _) => Str(Lossy(Bytes.Of(a[0]))),

        // --- byte arrays (string.c)
        ["std.core.putBytes"] = (a, _, _) =>
        {
            var into = (ArrayObj)a[0].Ref!;
            Put(into, I(a[1]), Bytes.Of(a[2]));
            return default;
        },
        ["std.core.putIntDigits"] = (a, _, _) =>
        {
            var digits = Encoding.ASCII.GetBytes(I(a[2]).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Put((ArrayObj)a[0].Ref!, I(a[1]), digits);
            return Value.Int(digits.Length);
        },
        ["std.core.putUintDigits"] = (a, _, _) =>
        {
            var digits = Encoding.ASCII.GetBytes(a[2].Bits.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Put((ArrayObj)a[0].Ref!, I(a[1]), digits);
            return Value.Int(digits.Length);
        },
        ["std.core.copyBytes"] = (a, _, _) =>
        {
            var into = (ArrayObj)a[0].Ref!;
            var from = (ArrayObj)a[1].Ref!;
            var count = I(a[2]);
            var room = Math.Min(into.Items.Length, from.Items.Length);
            if (count < 0 || count > room)
                throw ComptimeFailure.Panic("LYR-RT0003", $"range 0..{count} out of bounds for length {room}");
            Array.Copy(from.Items, into.Items, count);
            return default;
        },
        ["std.core.moveElements"] = (a, _, _) =>
        {
            var into = Interpreter.View(a[0]);
            var from = Interpreter.View(a[1]);
            var count = (int)I(a[2]);
            var moved = new Value[count];
            for (var i = 0; i < count; i++) moved[i] = Heap.Copy(from.Items![from.Offset + i]);
            for (var i = 0; i < count; i++) into.Items![into.Offset + i] = Heap.Store(into.Items[into.Offset + i], moved[i]);
            return default;
        },

        // --- panics (panic.c)
        ["std.core.panic"] = (a, _, _) => throw ComptimeFailure.Panic("LYR-RT0008", Clip(S(a[0]).ToString(), 1000)),
        ["std.core.assertFailed"] = (a, _, _) =>
        {
            var written = S(a[0]).ToString();
            var message = S(a[1]).ToString();
            throw ComptimeFailure.Panic("LYR-RT0011",
                "assertion failed" + (written.Length > 0 ? ": " + written : "") + (message.Length > 0 ? ": " + message : ""));
        },
        ["std.core.unreachable"] = (a, _, _) => throw ComptimeFailure.Panic("LYR-RT0012", Suffixed("unreachable", S(a[0]).ToString())),
        ["std.core.todo"] = (a, _, _) => throw ComptimeFailure.Panic("LYR-RT0013", Suffixed("not implemented", S(a[0]).ToString())),
        ["std.collections.changedWhileWalked"] = (a, _, _) =>
            throw ComptimeFailure.Panic("LYR-RT0016", $"{Clip(S(a[0]).ToString(), 100)}: changed while it was walked"),
        ["std.core.panicRange"] = (a, _, _) =>
            throw ComptimeFailure.Panic("LYR-RT0003", $"range {I(a[0])}..{I(a[1])} out of bounds for length {I(a[2])}"),
        ["std.core.traceText"] = (_, _, _) => Str(""),

        // --- the number tower (numeric.h)
        ["std.core.addOverflows"] = (a, t, _) => Value.Bool(Arith.Exact(IrBinKind.Add, K(t[0]), a[0].Bits, a[1].Bits).Overflows),
        ["std.core.subOverflows"] = (a, t, _) => Value.Bool(Arith.Exact(IrBinKind.Sub, K(t[0]), a[0].Bits, a[1].Bits).Overflows),
        ["std.core.mulOverflows"] = (a, t, _) => Value.Bool(Arith.Exact(IrBinKind.Mul, K(t[0]), a[0].Bits, a[1].Bits).Overflows),
        ["std.core.addWrapping"] = (a, t, _) => Value.Uint(Arith.Exact(IrBinKind.Add, K(t[0]), a[0].Bits, a[1].Bits).Wrapped),
        ["std.core.subWrapping"] = (a, t, _) => Value.Uint(Arith.Exact(IrBinKind.Sub, K(t[0]), a[0].Bits, a[1].Bits).Wrapped),
        ["std.core.mulWrapping"] = (a, t, _) => Value.Uint(Arith.Exact(IrBinKind.Mul, K(t[0]), a[0].Bits, a[1].Bits).Wrapped),
        ["std.core.countLeadingZeros"] = (a, t, _) =>
        {
            var w = Arith.Width(K(t[0]));
            var bits = Masked(a[0].Bits, w);
            return Value.Int(bits == 0 ? w : BitOperations.LeadingZeroCount(bits) - (64 - w));
        },
        ["std.core.countTrailingZeros"] = (a, t, _) =>
        {
            var w = Arith.Width(K(t[0]));
            var bits = Masked(a[0].Bits, w);
            return Value.Int(bits == 0 ? w : BitOperations.TrailingZeroCount(bits));
        },
        ["std.core.countOnes"] = (a, t, _) => Value.Int(BitOperations.PopCount(Masked(a[0].Bits, Arith.Width(K(t[0]))))),
        ["std.core.rotateBitsLeft"] = (a, t, _) =>
        {
            var k = K(t[0]);
            var w = Arith.Width(k);
            var s = (int)(((I(a[1]) % w) + w) % w);
            var bits = Masked(a[0].Bits, w);
            if (s == 0) return a[0];
            return Value.Uint(Arith.Norm(Masked((bits << s) | (bits >> (w - s)), w), k));
        },
        ["std.core.asTypeOf"] = (a, t, _) => Arith.Convert(K(t[0]), K(t[1]), a[0]),
        ["std.core.floatBits"] = (a, t, _) => Value.Uint(K(t[0]) == IrScalar.F32 ? a[0].Bits & 0xFFFF_FFFF : a[0].Bits),
        ["std.core.floatFromBits"] = (a, t, _) => Value.Uint(K(t[1]) == IrScalar.F32 ? a[0].Bits & 0xFFFF_FFFF : a[0].Bits),
        ["std.core.totalOrderKey"] = (a, t, _) =>
        {
            var narrow = K(t[0]) == IrScalar.F32;
            var key = narrow ? a[0].Bits & 0xFFFF_FFFF : a[0].Bits;
            var sign = narrow ? 0x8000_0000UL : 0x8000_0000_0000_0000UL;
            var all = narrow ? 0xFFFF_FFFFUL : ulong.MaxValue;
            return Value.Uint((key & sign) != 0 ? ~key & all : key | sign);
        },
        // An object's address at run time; at compile time a number per object, in the order asked —
        // the same each run (Q7), which an address is not.
        ["std.core.identityOf"] = (a, _, _) => Value.Int(_identities.GetValue(a[0].Ref!, _ => new StrongBox<long>(_nextIdentity++)).Value!),

        // --- a file of the package, the one thing compile time reads (09 Q4)
        ["std.core.embed"] = (a, _, _) =>
        {
            var bytes = (Embed ?? throw ComptimeFailure.Embed("'embed' reads no file in this compilation")).Bytes(_at, S(a[0]).ToString());
            return Value.Of(new ArrayObj([.. bytes.Select(b => Value.Uint(b))]));
        },

        // --- the build's target and profile (09 A6 B4): compile time's alone, nothing at run time asks
        ["std.core.buildOs"] = (_, _, _) => Value.Int(Facts.Os),
        ["std.core.buildArch"] = (_, _, _) => Value.Int(Facts.Arch),
        ["std.core.buildPointerWidth"] = (_, _, _) => Value.Int(Facts.PointerWidth),
        ["std.core.buildDebug"] = (_, _, _) => Value.Bool(Facts.Debug),

        // --- the Unicode Character Database (unicode.c)
        ["std.core.unicodeCategory"] = (a, _, _) => Value.Int(UnicodeCategory((uint)a[0].Bits)),
        ["std.core.unicodeUpper"] = (a, _, _) => Value.Uint(Mapped(UnicodeTables.Upper, (uint)a[0].Bits)),
        ["std.core.unicodeLower"] = (a, _, _) => Value.Uint(Mapped(UnicodeTables.Lower, (uint)a[0].Bits)),
        ["std.core.unicodeWhiteSpace"] = (a, _, _) => Value.Bool(UnicodeWhiteSpace((uint)a[0].Bits)),

        // --- the C library's exactly rounded functions (math.h, which std declares 'extern "C"'):
        // IEEE 754 fixes each result to the bit, on every platform.
        ["C:floor"] = (a, _, _) => Value.Double(Math.Floor(a[0].AsDouble)),
        ["C:ceil"] = (a, _, _) => Value.Double(Math.Ceiling(a[0].AsDouble)),
        ["C:trunc"] = (a, _, _) => Value.Double(Math.Truncate(a[0].AsDouble)),
        ["C:sqrt"] = (a, _, _) => Value.Double(Math.Sqrt(a[0].AsDouble)),
        ["C:round"] = (a, _, _) =>
        {
            // a half away from zero: the part after the point is exact below 2^52, and above
            // every double is whole; -0.3 stays -0.0, NaN and the infinities stay
            var x = a[0].AsDouble;
            var whole = Math.Truncate(x);
            return Value.Double(Math.Abs(x - whole) >= 0.5 ? whole + Math.CopySign(1.0, x) : whole);
        },

        // --- std.hash's key (10 K2): drawn from the system per process at run time, fixed at compile
        // time (13 M9a P2) — a Map walked in a 'comptime' site walks the same way in every build.
        ["C:lyr_hash_key"] = (_, _, _) => Value.Uint(0),

        // --- the scheduler and the atomics on one thread (task.h, atomic.h): no scheduler runs at
        // compile time, and an atomic operation is the plain one.
        ["std.task.schedulerRuns"] = (_, _, _) => Value.Bool(false),
        ["std.task.currentScheduler"] = (_, _, result) => interpreter.Zero(result),
        ["std.task.spin"] = (_, _, _) => default,
        ["std.task.atomicLoad"] = (a, _, _) => ((PlaceObj)a[0].Ref!).Get(),
        ["std.task.atomicStore"] = (a, _, _) =>
        {
            ((PlaceObj)a[0].Ref!).Set(a[1]);
            return default;
        },
        ["std.task.atomicExchange"] = (a, _, _) =>
        {
            var place = (PlaceObj)a[0].Ref!;
            var old = Heap.Copy(place.Get());
            place.Set(a[1]);
            return old;
        },
        ["std.task.atomicCompareAndSet"] = (a, _, _) =>
        {
            var place = (PlaceObj)a[0].Ref!;
            if (!Arith.SameValue(place.Get(), a[1])) return Value.Bool(false);
            place.Set(a[2]);
            return Value.Bool(true);
        },
        ["std.task.atomicFetchAndAdd"] = (a, _, _) =>
        {
            var place = (PlaceObj)a[0].Ref!;
            var old = place.Get();
            place.Set(Value.Int(unchecked(old.AsInt + I(a[1]))));
            return old;
        },
    };

    private static ulong Masked(ulong bits, int width) => width == 64 ? bits : bits & ((1UL << width) - 1);

    private static string Clip(string text, int most) => text.Length <= most ? text : text[..most];

    private static string Suffixed(string head, string message) => message.Length > 0 ? $"{head}: {message}" : head;

    /// <summary><c>lyr_bytes_compare</c>: byte order, then the length.</summary>
    private static int Order(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var c = a.SequenceCompareTo(b);
        return c < 0 ? -1 : c > 0 ? 1 : 0;
    }

    /// <summary><c>lyr_bytes_put</c>: the bytes into the array from <paramref name="at"/>.</summary>
    private static void Put(ArrayObj into, long at, byte[] bytes)
    {
        if (at < 0 || at > into.Items.Length - bytes.Length)
            throw ComptimeFailure.Panic("LYR-RT0003", $"range {at}..{at + bytes.Length} out of bounds for length {into.Items.Length}");
        for (var i = 0; i < bytes.Length; i++) into.Items[at + i] = Value.Uint(bytes[i]);
    }

    private static byte[] Utf8(uint c) => c switch
    {
        < 0x80 => [(byte)c],
        < 0x800 => [(byte)(0xC0 | (c >> 6)), (byte)(0x80 | (c & 0x3F))],
        < 0x10000 => [(byte)(0xE0 | (c >> 12)), (byte)(0x80 | ((c >> 6) & 0x3F)), (byte)(0x80 | (c & 0x3F))],
        _ => [(byte)(0xF0 | (c >> 18)), (byte)(0x80 | ((c >> 12) & 0x3F)), (byte)(0x80 | ((c >> 6) & 0x3F)), (byte)(0x80 | (c & 0x3F))],
    };

    /// <summary><c>lyr_char_at</c>: the character that begins at a byte of well-formed UTF-8.</summary>
    private static uint CharAt(byte[] bytes, long at)
    {
        var i = Interpreter.Index(Value.Int(at), bytes.Length);
        var b0 = bytes[i];
        if (b0 < 0x80) return b0;
        if ((b0 & 0xC0) == 0x80) throw ComptimeFailure.Panic("LYR-RT0003", $"byte {at} does not begin a character");
        if (b0 < 0xE0) return ((uint)(b0 & 0x1F) << 6) | (uint)(bytes[i + 1] & 0x3F);
        if (b0 < 0xF0) return ((uint)(b0 & 0x0F) << 12) | ((uint)(bytes[i + 1] & 0x3F) << 6) | (uint)(bytes[i + 2] & 0x3F);
        return ((uint)(b0 & 0x07) << 18) | ((uint)(bytes[i + 1] & 0x3F) << 12) | ((uint)(bytes[i + 2] & 0x3F) << 6) | (uint)(bytes[i + 3] & 0x3F);
    }

    /// <summary>One step of a decode (<c>utf8_step</c>, Unicode table 3-7): the length of the
    /// well-formed sequence at <paramref name="i"/>, or of the maximal part of one that is not.</summary>
    private static (int Length, bool Ok) Step(byte[] b, int i)
    {
        var c = b[i];
        if (c < 0x80) return (1, true);
        int need;
        byte lo = 0x80, hi = 0xBF;
        if (c >= 0xC2 && c <= 0xDF) need = 1;
        else if (c == 0xE0) { need = 2; lo = 0xA0; }
        else if ((c >= 0xE1 && c <= 0xEC) || c == 0xEE || c == 0xEF) need = 2;
        else if (c == 0xED) { need = 2; hi = 0x9F; }
        else if (c == 0xF0) { need = 3; lo = 0x90; }
        else if (c >= 0xF1 && c <= 0xF3) need = 3;
        else if (c == 0xF4) { need = 3; hi = 0x8F; }
        else return (1, false);
        for (var k = 1; k <= need; k++)
        {
            if (i + k >= b.Length) return (k, false);
            var d = b[i + k];
            if (d < (k == 1 ? lo : 0x80) || d > (k == 1 ? hi : 0xBF)) return (k, false);
        }
        return (need + 1, true);
    }

    /// <summary><c>lyr_utf8_invalid_at</c>: the offset of the first byte that is no well-formed
    /// UTF-8, or -1.</summary>
    private static long InvalidAt(byte[] bytes)
    {
        for (var i = 0; i < bytes.Length;)
        {
            var (length, ok) = Step(bytes, i);
            if (!ok) return i;
            i += length;
        }
        return -1;
    }

    /// <summary><c>lyr_str_from_utf8_lossy</c>: each maximal part of a sequence that is not
    /// well-formed replaced by U+FFFD.</summary>
    private static byte[] Lossy(byte[] bytes)
    {
        var text = new List<byte>(bytes.Length);
        for (var i = 0; i < bytes.Length;)
        {
            var (length, ok) = Step(bytes, i);
            if (ok) text.AddRange(bytes.AsSpan(i, length).ToArray());
            else text.AddRange([0xEF, 0xBF, 0xBD]);
            i += length;
        }
        return [.. text];
    }
}
