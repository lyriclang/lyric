using System.Text;
using Lyric.Ir.Interp;
using Profile = Lyric5.Toolchain.Profile;

namespace Lyric5.Tests;

/// <summary>
/// The runtime's libm and the compiler's are the same code: musl's double functions, in C under <c>runtime/third_party/musl-math/</c> and in C# as
/// <see cref="Libm"/>. Bit for bit over a million inputs per function, on every tier-1 target the
/// tests run on — which is what makes a <c>comptime</c> answer the program's own.
/// </summary>
public class LibmTests
{
    /// <summary><c>programs/libm_bits.lyr</c> hashes the results of the seventeen functions over
    /// its xorshift stream; the same stream through the port gives the same hashes, and its last
    /// line says the compile-time answers were the run-time bits.</summary>
    [Theory]
    [InlineData(Profile.Debug)]
    [InlineData(Profile.Release)]
    public void The_runtime_and_the_compiler_compute_the_same_bits(Profile profile)
    {
        var result = RuntimeBuildTests.RunEmitted(CEmitterTests.EmitC("libm_bits"), "libm_bits", profile);
        Assert.True(result.ExitCode == 0, $"exit {result.ExitCode}\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}");
        var expected = Expected().Split('\n');
        var actual = result.Stdout.Replace("\r\n", "\n").Split('\n');
        var differ = new List<string>();
        for (var k = 0; k < Math.Max(expected.Length, actual.Length); k++)
        {
            var e = k < expected.Length ? expected[k] : "(nothing)";
            var a = k < actual.Length ? actual[k] : "(nothing)";
            if (e != a) differ.Add($"program: {a}\n   port: {e}");
        }
        Assert.True(differ.Count == 0, "the program and the port differ:\n" + string.Join("\n", differ));
    }

    private static readonly string[] Names =
    [
        "exp", "ln", "log2", "log10", "pow", "sin", "cos", "tan", "asin", "acos", "atan", "atan2",
        "sinh", "cosh", "tanh", "cbrt", "hypot",
    ];

    /// <summary>What the program prints, computed as it computes it: the same stream, the same
    /// ranging, the same hash — with the port in place of the runtime.</summary>
    private static string Expected()
    {
        var seed = 0x1E3779B97F4A7C15UL;
        ulong Next()
        {
            var x = seed;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            seed = x;
            return unchecked(x * 0x2545F4914F6CDD1DUL);
        }
        static double Unit(ulong u) => (u >> 11) / 9007199254740992.0;
        var h = new ulong[Names.Length];
        Array.Fill(h, 0x4bf29ce484222325UL);
        void Add(int k, double v) => h[k] = unchecked((h[k] ^ BitConverter.DoubleToUInt64Bits(v)) * 0x100000001b3UL);
        for (var i = 0; i < 500000; i++)
        {
            var u1 = Next();
            var u2 = Next();
            var x = BitConverter.UInt64BitsToDouble(u1);
            var y = BitConverter.UInt64BitsToDouble(u2);
            var r = Unit(u1);
            var s = Unit(u2);
            var wide = (r - 0.5) * 1500.0;
            var twenty = (r - 0.5) * 40.0;
            var one = (s - 0.5) * 2.0;
            var big = r * 1000000.0;
            var near = (s - 0.5) / 8.0 + 1.0;
            var medium = (s - 0.5) * 2000000.0;
            Add(0, Libm.Exp(x)); Add(0, Libm.Exp(wide));
            Add(1, Libm.Log(x)); Add(1, Libm.Log(big)); Add(1, Libm.Log(near));
            Add(2, Libm.Log2(x)); Add(2, Libm.Log2(big)); Add(2, Libm.Log2(near));
            Add(3, Libm.Log10(x)); Add(3, Libm.Log10(big)); Add(3, Libm.Log10(near));
            Add(4, Libm.Pow(x, y)); Add(4, Libm.Pow(r * 100.0, twenty)); Add(4, Libm.Pow(-(r * 4.0), Math.Floor(s * 10.0)));
            Add(5, Libm.Sin(x)); Add(5, Libm.Sin(twenty)); Add(5, Libm.Sin(medium));
            Add(6, Libm.Cos(x)); Add(6, Libm.Cos(twenty)); Add(6, Libm.Cos(medium));
            Add(7, Libm.Tan(x)); Add(7, Libm.Tan(twenty)); Add(7, Libm.Tan(medium));
            Add(8, Libm.Asin(x)); Add(8, Libm.Asin(one));
            Add(9, Libm.Acos(x)); Add(9, Libm.Acos(one));
            Add(10, Libm.Atan(x)); Add(10, Libm.Atan(twenty));
            Add(11, Libm.Atan2(x, y)); Add(11, Libm.Atan2(twenty, one));
            Add(12, Libm.Sinh(x)); Add(12, Libm.Sinh(twenty)); Add(12, Libm.Sinh(one));
            Add(13, Libm.Cosh(x)); Add(13, Libm.Cosh(twenty)); Add(13, Libm.Cosh(one));
            Add(14, Libm.Tanh(x)); Add(14, Libm.Tanh(twenty)); Add(14, Libm.Tanh(one));
            Add(15, Libm.Cbrt(x)); Add(15, Libm.Cbrt(medium));
            Add(16, Libm.Hypot(x, y)); Add(16, Libm.Hypot(big, medium));
        }
        var text = new StringBuilder();
        for (var k = 0; k < Names.Length; k++) text.Append(Names[k]).Append(' ').Append(h[k]).Append('\n');
        text.Append("same true true true true true true\n");
        return text.ToString();
    }
}
