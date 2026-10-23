using static Lyric.Ir.Interp.LibmTables;

namespace Lyric.Ir.Interp;

/// <summary>
/// musl 1.2.5's double math functions, ported line for line (MIT,
/// runtime/third_party/musl-math/COPYRIGHT): <c>exp</c>, <c>log</c>, <c>log2</c>, <c>log10</c>,
/// <c>pow</c>, <c>sin</c>, <c>cos</c>, <c>tan</c>, <c>asin</c>, <c>acos</c>, <c>atan</c>,
/// <c>atan2</c>, <c>sinh</c>, <c>cosh</c>, <c>tanh</c>, <c>cbrt</c>, <c>hypot</c>. The runtime
/// carries the same C under <c>lyr_</c> names, compiled without fused multiply-add and without
/// contraction, so a value computed at compile time has the bits the program computes at run
/// time, on every tier-1 target. The fp_barrier and FORCE_EVAL of the C only shape the exceptions
/// raised, not the value: they are the identity here. The tables are generated from musl's data
/// files (<see cref="LibmTables"/>).
/// </summary>
public static class Libm
{
    private static double AsDouble(ulong i) => BitConverter.UInt64BitsToDouble(i);
    private static ulong AsUint64(double f) => BitConverter.DoubleToUInt64Bits(f);
    private static uint HighWord(double d) => (uint)(AsUint64(d) >> 32);
    private static uint LowWord(double d) => (uint)AsUint64(d);
    private static double InsertWords(uint hi, uint lo) => AsDouble(((ulong)hi << 32) | lo);
    private static double SetLowWord(double d, uint lo) => InsertWords((uint)(AsUint64(d) >> 32), lo);

    /// <summary>2^e for a normal exponent.</summary>
    private static double P(int e) => AsDouble((ulong)(1023 + e) << 52);

    private static readonly double Infinity = double.PositiveInfinity;

    // ---------------------------------------------------------------- the error helpers

    private static double MathXflow(uint sign, double y) => (sign != 0 ? -y : y) * y;
    private static double MathOflow(uint sign) => MathXflow(sign, P(769));
    private static double MathUflow(uint sign) => MathXflow(sign, P(-767));
    private static double MathDivzero(uint sign) => (sign != 0 ? -1.0 : 1.0) / 0.0;
    private static double MathInvalid(double x) => (x - x) / (x - x);

    // ---------------------------------------------------------------- scalbn

    public static double Scalbn(double x, int n)
    {
        double y = x;
        if (n > 1023)
        {
            y *= P(1023);
            n -= 1023;
            if (n > 1023)
            {
                y *= P(1023);
                n -= 1023;
                if (n > 1023) n = 1023;
            }
        }
        else if (n < -1022)
        {
            y *= P(-1022) * P(53);
            n += 1022 - 53;
            if (n < -1022)
            {
                y *= P(-1022) * P(53);
                n += 1022 - 53;
                if (n < -1022) n = -1022;
            }
        }
        return y * AsDouble((ulong)(0x3ff + n) << 52);
    }

    // ---------------------------------------------------------------- exp

    private const int ExpTableBits = 7;
    private const int ExpN = 1 << ExpTableBits;

    private static uint Top12(double x) => (uint)(AsUint64(x) >> 52);

    private static double ExpSpecialcase(double tmp, ulong sbits, ulong ki)
    {
        double scale, y;
        if ((ki & 0x80000000) == 0)
        {
            sbits -= 1009UL << 52;
            scale = AsDouble(sbits);
            y = P(1009) * (scale + scale * tmp);
            return y;
        }
        sbits += 1022UL << 52;
        scale = AsDouble(sbits);
        y = scale + scale * tmp;
        if (y < 1.0)
        {
            double hi, lo;
            lo = scale - y + scale * tmp;
            hi = 1.0 + y;
            lo = 1.0 - hi + y + lo;
            y = (hi + lo) - 1.0;
            if (y == 0.0) y = 0.0;
        }
        y = P(-1022) * y;
        return y;
    }

    public static double Exp(double x)
    {
        uint abstop;
        ulong ki, idx, top, sbits;
        double kd, z, r, r2, scale, tail, tmp;
        abstop = Top12(x) & 0x7ff;
        if (abstop - Top12(P(-54)) >= Top12(512.0) - Top12(P(-54)))
        {
            if (abstop - Top12(P(-54)) >= 0x80000000)
                return 1.0 + x;
            if (abstop >= Top12(1024.0))
            {
                if (AsUint64(x) == AsUint64(-Infinity)) return 0.0;
                if (abstop >= Top12(Infinity)) return 1.0 + x;
                if (AsUint64(x) >> 63 != 0) return MathUflow(0);
                return MathOflow(0);
            }
            abstop = 0;
        }
        z = Exp_invln2N * x;
        kd = z + Exp_shift;
        ki = AsUint64(kd);
        kd -= Exp_shift;
        r = x + kd * Exp_negln2hiN + kd * Exp_negln2loN;
        idx = 2 * (ki % ExpN);
        top = ki << (52 - ExpTableBits);
        tail = AsDouble(Exp_tab[idx]);
        sbits = Exp_tab[idx + 1] + top;
        r2 = r * r;
        tmp = tail + r + r2 * (Exp_poly[0] + r * Exp_poly[1]) + r2 * r2 * (Exp_poly[2] + r * Exp_poly[3]);
        if (abstop == 0) return ExpSpecialcase(tmp, sbits, ki);
        scale = AsDouble(sbits);
        return scale + scale * tmp;
    }

    // ---------------------------------------------------------------- log

    private const int LogTableBits = 7;
    private const int LogN = 1 << LogTableBits;
    private const ulong LogOff = 0x3fe6000000000000;

    private static uint Top16(double x) => (uint)(AsUint64(x) >> 48);

    public static double Log(double x)
    {
        double w, z, r, r2, r3, y, invc, logc, kd, hi, lo;
        ulong ix, iz, tmp;
        uint top;
        int k, i;
        ix = AsUint64(x);
        top = Top16(x);
        ulong LO = AsUint64(1.0 - P(-4));
        ulong HI = AsUint64(1.0 + AsDouble(0x3fb0900000000000)); // 0x1.09p-4
        if (ix - LO < HI - LO)
        {
            if (ix == AsUint64(1.0)) return 0;
            r = x - 1.0;
            r2 = r * r;
            r3 = r * r2;
            var B = Log_poly1;
            y = r3 *
                (B[1] + r * B[2] + r2 * B[3] +
                 r3 * (B[4] + r * B[5] + r2 * B[6] +
                       r3 * (B[7] + r * B[8] + r2 * B[9] + r3 * B[10])));
            w = r * P(27);
            double rhi = r + w - w;
            double rlo = r - rhi;
            w = rhi * rhi * B[0];
            hi = r + w;
            lo = r - hi + w;
            lo += B[0] * rlo * (rhi + r);
            y += lo;
            y += hi;
            return y;
        }
        if (top - 0x0010 >= 0x7ff0 - 0x0010)
        {
            if (ix * 2 == 0) return MathDivzero(1);
            if (ix == AsUint64(Infinity)) return x;
            if ((top & 0x8000) != 0 || (top & 0x7ff0) == 0x7ff0) return MathInvalid(x);
            ix = AsUint64(x * P(52));
            ix -= 52UL << 52;
        }
        tmp = ix - LogOff;
        i = (int)((tmp >> (52 - LogTableBits)) % LogN);
        k = (int)((long)tmp >> 52);
        iz = ix - (tmp & (0xfffUL << 52));
        invc = Log_tab[2 * i];
        logc = Log_tab[2 * i + 1];
        z = AsDouble(iz);
        r = (z - Log_tab2[2 * i] - Log_tab2[2 * i + 1]) * invc;
        kd = k;
        w = kd * Log_ln2hi + logc;
        hi = w + r;
        lo = w - hi + r + kd * Log_ln2lo;
        r2 = r * r;
        var A = Log_poly;
        y = lo + r2 * A[0] +
            r * r2 * (A[1] + r * A[2] + r2 * (A[3] + r * A[4])) + hi;
        return y;
    }

    // ---------------------------------------------------------------- log2

    private const int Log2TableBits = 6;
    private const int Log2N = 1 << Log2TableBits;

    public static double Log2(double x)
    {
        double z, r, r2, r4, y, invc, logc, kd, hi, lo, t1, t2, t3, p;
        ulong ix, iz, tmp;
        uint top;
        int k, i;
        ix = AsUint64(x);
        top = Top16(x);
        ulong LO = AsUint64(1.0 - AsDouble(0x3fa5b51000000000)); // 0x1.5b51p-5
        ulong HI = AsUint64(1.0 + AsDouble(0x3fa6ab2000000000)); // 0x1.6ab2p-5
        if (ix - LO < HI - LO)
        {
            if (ix == AsUint64(1.0)) return 0;
            r = x - 1.0;
            double rhi0, rlo0;
            rhi0 = AsDouble(AsUint64(r) & (ulong.MaxValue << 32));
            rlo0 = r - rhi0;
            hi = rhi0 * Log2_invln2hi;
            lo = rlo0 * Log2_invln2hi + r * Log2_invln2lo;
            r2 = r * r;
            r4 = r2 * r2;
            var B = Log2_poly1;
            p = r2 * (B[0] + r * B[1]);
            y = hi + p;
            lo += hi - y + p;
            lo += r4 * (B[2] + r * B[3] + r2 * (B[4] + r * B[5]) +
                        r4 * (B[6] + r * B[7] + r2 * (B[8] + r * B[9])));
            y += lo;
            return y;
        }
        if (top - 0x0010 >= 0x7ff0 - 0x0010)
        {
            if (ix * 2 == 0) return MathDivzero(1);
            if (ix == AsUint64(Infinity)) return x;
            if ((top & 0x8000) != 0 || (top & 0x7ff0) == 0x7ff0) return MathInvalid(x);
            ix = AsUint64(x * P(52));
            ix -= 52UL << 52;
        }
        tmp = ix - LogOff;
        i = (int)((tmp >> (52 - Log2TableBits)) % Log2N);
        k = (int)((long)tmp >> 52);
        iz = ix - (tmp & (0xfffUL << 52));
        invc = Log2_tab[2 * i];
        logc = Log2_tab[2 * i + 1];
        z = AsDouble(iz);
        kd = k;
        double rhi, rlo;
        r = (z - Log2_tab2[2 * i] - Log2_tab2[2 * i + 1]) * invc;
        rhi = AsDouble(AsUint64(r) & (ulong.MaxValue << 32));
        rlo = r - rhi;
        t1 = rhi * Log2_invln2hi;
        t2 = rlo * Log2_invln2hi + r * Log2_invln2lo;
        t3 = kd + logc;
        hi = t3 + t1;
        lo = t3 - hi + t1 + t2;
        r2 = r * r;
        r4 = r2 * r2;
        var A = Log2_poly;
        p = A[0] + r * A[1] + r2 * (A[2] + r * A[3]) + r4 * (A[4] + r * A[5]);
        y = lo + r2 * p + hi;
        return y;
    }

    // ---------------------------------------------------------------- log10

    private const double Ivln10hi = 4.34294481878168880939e-01;
    private const double Ivln10lo = 2.50829467116452752298e-11;
    private const double Log10_2hi = 3.01029995663611771306e-01;
    private const double Log10_2lo = 3.69423907715893078616e-13;
    private const double Lg1 = 6.666666666666735130e-01;
    private const double Lg2 = 3.999999999940941908e-01;
    private const double Lg3 = 2.857142874366239149e-01;
    private const double Lg4 = 2.222219843214978396e-01;
    private const double Lg5 = 1.818357216161805012e-01;
    private const double Lg6 = 1.531383769920937332e-01;
    private const double Lg7 = 1.479819860511658591e-01;

    public static double Log10(double x)
    {
        ulong ui = AsUint64(x);
        double hfsq, f, s, z, R, w, t1, t2, dk, y, hi, lo, val_hi, val_lo;
        uint hx;
        int k;
        hx = (uint)(ui >> 32);
        k = 0;
        if (hx < 0x00100000 || (hx >> 31) != 0)
        {
            if (ui << 1 == 0) return -1 / (x * x);
            if ((hx >> 31) != 0) return (x - x) / 0.0;
            k -= 54;
            x *= P(54);
            ui = AsUint64(x);
            hx = (uint)(ui >> 32);
        }
        else if (hx >= 0x7ff00000)
        {
            return x;
        }
        else if (hx == 0x3ff00000 && ui << 32 == 0)
        {
            return 0;
        }
        hx += 0x3ff00000 - 0x3fe6a09e;
        k += (int)(hx >> 20) - 0x3ff;
        hx = (hx & 0x000fffff) + 0x3fe6a09e;
        ui = ((ulong)hx << 32) | (ui & 0xffffffff);
        x = AsDouble(ui);
        f = x - 1.0;
        hfsq = 0.5 * f * f;
        s = f / (2.0 + f);
        z = s * s;
        w = z * z;
        t1 = w * (Lg2 + w * (Lg4 + w * Lg6));
        t2 = z * (Lg1 + w * (Lg3 + w * (Lg5 + w * Lg7)));
        R = t2 + t1;
        hi = f - hfsq;
        hi = AsDouble(AsUint64(hi) & (ulong.MaxValue << 32));
        lo = f - hi - hfsq + s * (hfsq + R);
        val_hi = hi * Ivln10hi;
        dk = k;
        y = dk * Log10_2hi;
        val_lo = dk * Log10_2lo + (lo + hi) * Ivln10lo + lo * Ivln10hi;
        w = y + val_hi;
        val_lo += (y - w) + val_hi;
        val_hi = w;
        return val_lo + val_hi;
    }

    // ---------------------------------------------------------------- pow

    private const int PowLogTableBits = 7;
    private const int PowLogN = 1 << PowLogTableBits;
    private const ulong PowOff = 0x3fe6955500000000;
    private const uint SignBias = 0x800 << ExpTableBits;

    private static double PowLogInline(ulong ix, out double tail)
    {
        double z, r, y, invc, logc, logctail, kd, hi, t1, t2, lo, lo1, lo2, p;
        ulong iz, tmp;
        int k, i;
        tmp = ix - PowOff;
        i = (int)((tmp >> (52 - PowLogTableBits)) % PowLogN);
        k = (int)((long)tmp >> 52);
        iz = ix - (tmp & (0xfffUL << 52));
        z = AsDouble(iz);
        kd = k;
        invc = Pow_tab[4 * i];
        logc = Pow_tab[4 * i + 2];
        logctail = Pow_tab[4 * i + 3];
        double zhi = AsDouble((iz + (1UL << 31)) & (ulong.MaxValue << 32));
        double zlo = z - zhi;
        double rhi = zhi * invc - 1.0;
        double rlo = zlo * invc;
        r = rhi + rlo;
        t1 = kd * Pow_ln2hi + logc;
        t2 = t1 + r;
        lo1 = kd * Pow_ln2lo + logctail;
        lo2 = t1 - t2 + r;
        var A = Pow_poly;
        double ar, ar2, ar3, lo3, lo4;
        ar = A[0] * r;
        ar2 = r * ar;
        ar3 = r * ar2;
        double arhi = A[0] * rhi;
        double arhi2 = rhi * arhi;
        hi = t2 + arhi2;
        lo3 = rlo * (ar + arhi);
        lo4 = t2 - hi + arhi2;
        p = (ar3 * (A[1] + r * A[2] +
                    ar2 * (A[3] + r * A[4] + ar2 * (A[5] + r * A[6]))));
        lo = lo1 + lo2 + lo3 + lo4 + p;
        y = hi + lo;
        tail = hi - y + lo;
        return y;
    }

    private static double PowSpecialcase(double tmp, ulong sbits, ulong ki)
    {
        double scale, y;
        if ((ki & 0x80000000) == 0)
        {
            sbits -= 1009UL << 52;
            scale = AsDouble(sbits);
            y = P(1009) * (scale + scale * tmp);
            return y;
        }
        sbits += 1022UL << 52;
        scale = AsDouble(sbits);
        y = scale + scale * tmp;
        if (Math.Abs(y) < 1.0)
        {
            double hi, lo, one = 1.0;
            if (y < 0.0) one = -1.0;
            lo = scale - y + scale * tmp;
            hi = one + y;
            lo = one - hi + y + lo;
            y = (hi + lo) - one;
            if (y == 0.0) y = AsDouble(sbits & 0x8000000000000000);
        }
        y = P(-1022) * y;
        return y;
    }

    private static double PowExpInline(double x, double xtail, uint sign_bias)
    {
        uint abstop;
        ulong ki, idx, top, sbits;
        double kd, z, r, r2, scale, tail, tmp;
        abstop = Top12(x) & 0x7ff;
        if (abstop - Top12(P(-54)) >= Top12(512.0) - Top12(P(-54)))
        {
            if (abstop - Top12(P(-54)) >= 0x80000000)
            {
                double one = 1.0 + x;
                return sign_bias != 0 ? -one : one;
            }
            if (abstop >= Top12(1024.0))
            {
                if (AsUint64(x) >> 63 != 0) return MathUflow(sign_bias);
                return MathOflow(sign_bias);
            }
            abstop = 0;
        }
        z = Exp_invln2N * x;
        kd = z + Exp_shift;
        ki = AsUint64(kd);
        kd -= Exp_shift;
        r = x + kd * Exp_negln2hiN + kd * Exp_negln2loN;
        r += xtail;
        idx = 2 * (ki % ExpN);
        top = (ki + sign_bias) << (52 - ExpTableBits);
        tail = AsDouble(Exp_tab[idx]);
        sbits = Exp_tab[idx + 1] + top;
        r2 = r * r;
        tmp = tail + r + r2 * (Exp_poly[0] + r * Exp_poly[1]) + r2 * r2 * (Exp_poly[2] + r * Exp_poly[3]);
        if (abstop == 0) return PowSpecialcase(tmp, sbits, ki);
        scale = AsDouble(sbits);
        return scale + scale * tmp;
    }

    private static int Checkint(ulong iy)
    {
        int e = (int)(iy >> 52 & 0x7ff);
        if (e < 0x3ff) return 0;
        if (e > 0x3ff + 52) return 2;
        if ((iy & ((1UL << (0x3ff + 52 - e)) - 1)) != 0) return 0;
        if ((iy & (1UL << (0x3ff + 52 - e))) != 0) return 1;
        return 2;
    }

    private static bool Zeroinfnan(ulong i) => 2 * i - 1 >= 2 * AsUint64(Infinity) - 1;

    public static double Pow(double x, double y)
    {
        uint sign_bias = 0;
        ulong ix, iy;
        uint topx, topy;
        ix = AsUint64(x);
        iy = AsUint64(y);
        topx = Top12(x);
        topy = Top12(y);
        if (topx - 0x001 >= 0x7ff - 0x001 || (topy & 0x7ff) - 0x3be >= 0x43e - 0x3be)
        {
            if (Zeroinfnan(iy))
            {
                if (2 * iy == 0) return 1.0;
                if (ix == AsUint64(1.0)) return 1.0;
                if (2 * ix > 2 * AsUint64(Infinity) || 2 * iy > 2 * AsUint64(Infinity)) return x + y;
                if (2 * ix == 2 * AsUint64(1.0)) return 1.0;
                if ((2 * ix < 2 * AsUint64(1.0)) == (iy >> 63 == 0)) return 0.0;
                return y * y;
            }
            if (Zeroinfnan(ix))
            {
                double x2 = x * x;
                if (ix >> 63 != 0 && Checkint(iy) == 1) x2 = -x2;
                return iy >> 63 != 0 ? 1 / x2 : x2;
            }
            if (ix >> 63 != 0)
            {
                int yint = Checkint(iy);
                if (yint == 0) return MathInvalid(x);
                if (yint == 1) sign_bias = SignBias;
                ix &= 0x7fffffffffffffff;
                topx &= 0x7ff;
            }
            if ((topy & 0x7ff) - 0x3be >= 0x43e - 0x3be)
            {
                if (ix == AsUint64(1.0)) return 1.0;
                if ((topy & 0x7ff) < 0x3be)
                    return ix > AsUint64(1.0) ? 1.0 + y : 1.0 - y;
                return (ix > AsUint64(1.0)) == (topy < 0x800) ? MathOflow(0) : MathUflow(0);
            }
            if (topx == 0)
            {
                ix = AsUint64(x * P(52));
                ix &= 0x7fffffffffffffff;
                ix -= 52UL << 52;
            }
        }
        double hi = PowLogInline(ix, out double lo);
        double ehi, elo;
        double yhi = AsDouble(iy & (ulong.MaxValue << 27));
        double ylo = y - yhi;
        double lhi = AsDouble(AsUint64(hi) & (ulong.MaxValue << 27));
        double llo = hi - lhi + lo;
        ehi = yhi * lhi;
        elo = ylo * lhi + y * llo;
        return PowExpInline(ehi, elo, sign_bias);
    }

    // ---------------------------------------------------------------- the trigonometric kernels

    private const double S1 = -1.66666666666666324348e-01;
    private const double S2 = 8.33333333332248946124e-03;
    private const double S3 = -1.98412698298579493134e-04;
    private const double S4 = 2.75573137070700676789e-06;
    private const double S5 = -2.50507602534068634195e-08;
    private const double S6 = 1.58969099521155010221e-10;

    private static double KernelSin(double x, double y, int iy)
    {
        double z, r, v, w;
        z = x * x;
        w = z * z;
        r = S2 + z * (S3 + z * S4) + z * w * (S5 + z * S6);
        v = z * x;
        if (iy == 0) return x + v * (S1 + z * r);
        return x - ((z * (0.5 * y - v * r) - y) - v * S1);
    }

    private const double C1 = 4.16666666666666019037e-02;
    private const double C2 = -1.38888888888741095749e-03;
    private const double C3 = 2.48015872894767294178e-05;
    private const double C4 = -2.75573143513906633035e-07;
    private const double C5 = 2.08757232129817482790e-09;
    private const double C6 = -1.13596475577881948265e-11;

    private static double KernelCos(double x, double y)
    {
        double hz, z, r, w;
        z = x * x;
        w = z * z;
        r = z * (C1 + z * (C2 + z * C3)) + w * w * (C4 + z * (C5 + z * C6));
        hz = 0.5 * z;
        w = 1.0 - hz;
        return w + (((1.0 - w) - hz) + (z * r - x * y));
    }

    private static readonly double[] TanT =
    [
        3.33333333333334091986e-01,
        1.33333333333201242699e-01,
        5.39682539762260521377e-02,
        2.18694882948595424599e-02,
        8.86323982359930005737e-03,
        3.59207910759131235356e-03,
        1.45620945432529025516e-03,
        5.88041240820264096874e-04,
        2.46463134818469906812e-04,
        7.81794442939557092300e-05,
        7.14072491382608190305e-05,
        -1.85586374855275456654e-05,
        2.59073051863633712884e-05,
    ];
    private const double TanPio4 = 7.85398163397448278999e-01;
    private const double TanPio4lo = 3.06161699786838301793e-17;

    private static double KernelTan(double x, double y, int odd)
    {
        var T = TanT;
        double z, r, v, w, s, a;
        double w0, a0;
        uint hx;
        bool big;
        int sign = 0;
        hx = HighWord(x);
        big = (hx & 0x7fffffff) >= 0x3FE59428;
        if (big)
        {
            sign = (int)(hx >> 31);
            if (sign != 0)
            {
                x = -x;
                y = -y;
            }
            x = (TanPio4 - x) + (TanPio4lo - y);
            y = 0.0;
        }
        z = x * x;
        w = z * z;
        r = T[1] + w * (T[3] + w * (T[5] + w * (T[7] + w * (T[9] + w * T[11]))));
        v = z * (T[2] + w * (T[4] + w * (T[6] + w * (T[8] + w * (T[10] + w * T[12])))));
        s = z * x;
        r = y + z * (s * (r + v) + y) + s * T[0];
        w = x + r;
        if (big)
        {
            s = 1 - 2 * odd;
            v = s - 2.0 * (x + (r - w * w / (w + s)));
            return sign != 0 ? -v : v;
        }
        if (odd == 0) return w;
        w0 = w;
        w0 = SetLowWord(w0, 0);
        v = r - (w0 - x);
        a0 = a = -1.0 / w;
        a0 = SetLowWord(a0, 0);
        return a0 + a * (1.0 + a0 * w0 + a0 * v);
    }

    // ---------------------------------------------------------------- the argument reduction

    private const double RemToint = 1.5 / 2.220446049250313e-16; // 1.5/DBL_EPSILON
    private static readonly double RemPio4 = AsDouble(0x3fe921fb54442d18); // 0x1.921fb54442d18p-1
    private const double RemInvpio2 = 6.36619772367581382433e-01;
    private const double RemPio2_1 = 1.57079632673412561417e+00;
    private const double RemPio2_1t = 6.07710050650619224932e-11;
    private const double RemPio2_2 = 6.07710050630396597660e-11;
    private const double RemPio2_2t = 2.02226624879595063154e-21;
    private const double RemPio2_3 = 2.02226624871116645580e-21;
    private const double RemPio2_3t = 8.47842766036889956997e-32;

    private static int RemPio2(double x, double[] y)
    {
        ulong ui = AsUint64(x);
        double z, w, t, r, fn;
        var tx = new double[3];
        var ty = new double[2];
        uint ix;
        int sign, n, ex, ey, i;
        sign = (int)(ui >> 63);
        ix = (uint)(ui >> 32) & 0x7fffffff;
        // The C's `goto medium` cannot jump into a block here: the cases that would are left to
        // fall through to the medium case below, which every one of them satisfies.
        if (ix <= 0x400f6a7a)
        {
            if ((ix & 0xfffff) != 0x921fb)
            {
            if (ix <= 0x4002d97c)
            {
                if (sign == 0)
                {
                    z = x - RemPio2_1;
                    y[0] = z - RemPio2_1t;
                    y[1] = (z - y[0]) - RemPio2_1t;
                    return 1;
                }
                else
                {
                    z = x + RemPio2_1;
                    y[0] = z + RemPio2_1t;
                    y[1] = (z - y[0]) + RemPio2_1t;
                    return -1;
                }
            }
            else
            {
                if (sign == 0)
                {
                    z = x - 2 * RemPio2_1;
                    y[0] = z - 2 * RemPio2_1t;
                    y[1] = (z - y[0]) - 2 * RemPio2_1t;
                    return 2;
                }
                else
                {
                    z = x + 2 * RemPio2_1;
                    y[0] = z + 2 * RemPio2_1t;
                    y[1] = (z - y[0]) + 2 * RemPio2_1t;
                    return -2;
                }
            }
            }
        }
        else if (ix <= 0x401c463b)
        {
            if (ix <= 0x4015fdbc)
            {
                if (ix != 0x4012d97c)
                {
                if (sign == 0)
                {
                    z = x - 3 * RemPio2_1;
                    y[0] = z - 3 * RemPio2_1t;
                    y[1] = (z - y[0]) - 3 * RemPio2_1t;
                    return 3;
                }
                else
                {
                    z = x + 3 * RemPio2_1;
                    y[0] = z + 3 * RemPio2_1t;
                    y[1] = (z - y[0]) + 3 * RemPio2_1t;
                    return -3;
                }
                }
            }
            else
            {
                if (ix != 0x401921fb)
                {
                if (sign == 0)
                {
                    z = x - 4 * RemPio2_1;
                    y[0] = z - 4 * RemPio2_1t;
                    y[1] = (z - y[0]) - 4 * RemPio2_1t;
                    return 4;
                }
                else
                {
                    z = x + 4 * RemPio2_1;
                    y[0] = z + 4 * RemPio2_1t;
                    y[1] = (z - y[0]) + 4 * RemPio2_1t;
                    return -4;
                }
                }
            }
        }
        if (ix < 0x413921fb)
        {
            fn = x * RemInvpio2 + RemToint - RemToint;
            n = (int)fn;
            r = x - fn * RemPio2_1;
            w = fn * RemPio2_1t;
            if (r - w < -RemPio4)
            {
                n--;
                fn--;
                r = x - fn * RemPio2_1;
                w = fn * RemPio2_1t;
            }
            else if (r - w > RemPio4)
            {
                n++;
                fn++;
                r = x - fn * RemPio2_1;
                w = fn * RemPio2_1t;
            }
            y[0] = r - w;
            ey = (int)(AsUint64(y[0]) >> 52 & 0x7ff);
            ex = (int)(ix >> 20);
            if (ex - ey > 16)
            {
                t = r;
                w = fn * RemPio2_2;
                r = t - w;
                w = fn * RemPio2_2t - ((t - r) - w);
                y[0] = r - w;
                ey = (int)(AsUint64(y[0]) >> 52 & 0x7ff);
                if (ex - ey > 49)
                {
                    t = r;
                    w = fn * RemPio2_3;
                    r = t - w;
                    w = fn * RemPio2_3t - ((t - r) - w);
                    y[0] = r - w;
                }
            }
            y[1] = (r - y[0]) - w;
            return n;
        }
        if (ix >= 0x7ff00000)
        {
            y[0] = y[1] = x - x;
            return 0;
        }
        ui = AsUint64(x);
        ui &= ulong.MaxValue >> 12;
        ui |= (ulong)(0x3ff + 23) << 52;
        z = AsDouble(ui);
        for (i = 0; i < 2; i++)
        {
            tx[i] = (int)z;
            z = (z - tx[i]) * P(24);
        }
        tx[i] = z;
        while (tx[i] == 0.0) i--;
        n = RemPio2Large(tx, ty, (int)(ix >> 20) - (0x3ff + 23), i + 1, 1);
        if (sign != 0)
        {
            y[0] = -ty[0];
            y[1] = -ty[1];
            return -n;
        }
        y[0] = ty[0];
        y[1] = ty[1];
        return n;
    }

    private static int RemPio2Large(double[] x, double[] y, int e0, int nx, int prec)
    {
        int jz, jx, jv, jp, jk, carry, n, i, j, k, m, q0, ih;
        var iq = new int[20];
        double z, fw;
        var f = new double[20];
        var fq = new double[20];
        var q = new double[20];
        var ipio2 = Rem_ipio2;
        var PIo2 = Rem_PIo2;
        jk = Rem_init_jk[prec];
        jp = jk;
        jx = nx - 1;
        jv = (e0 - 3) / 24; if (jv < 0) jv = 0;
        q0 = e0 - 24 * (jv + 1);
        j = jv - jx; m = jx + jk;
        for (i = 0; i <= m; i++, j++)
            f[i] = j < 0 ? 0.0 : (double)ipio2[j];
        for (i = 0; i <= jk; i++)
        {
            for (j = 0, fw = 0.0; j <= jx; j++)
                fw += x[j] * f[jx + i - j];
            q[i] = fw;
        }
        jz = jk;
    recompute:
        for (i = 0, j = jz, z = q[jz]; j > 0; i++, j--)
        {
            fw = (double)(int)(P(-24) * z);
            iq[i] = (int)(z - P(24) * fw);
            z = q[j - 1] + fw;
        }
        z = Scalbn(z, q0);
        z -= 8.0 * Math.Floor(z * 0.125);
        n = (int)z;
        z -= (double)n;
        ih = 0;
        if (q0 > 0)
        {
            i = iq[jz - 1] >> (24 - q0); n += i;
            iq[jz - 1] -= i << (24 - q0);
            ih = iq[jz - 1] >> (23 - q0);
        }
        else if (q0 == 0) ih = iq[jz - 1] >> 23;
        else if (z >= 0.5) ih = 2;
        if (ih > 0)
        {
            n += 1; carry = 0;
            for (i = 0; i < jz; i++)
            {
                j = iq[i];
                if (carry == 0)
                {
                    if (j != 0)
                    {
                        carry = 1;
                        iq[i] = 0x1000000 - j;
                    }
                }
                else
                    iq[i] = 0xffffff - j;
            }
            if (q0 > 0)
            {
                switch (q0)
                {
                    case 1:
                        iq[jz - 1] &= 0x7fffff; break;
                    case 2:
                        iq[jz - 1] &= 0x3fffff; break;
                }
            }
            if (ih == 2)
            {
                z = 1.0 - z;
                if (carry != 0)
                    z -= Scalbn(1.0, q0);
            }
        }
        if (z == 0.0)
        {
            j = 0;
            for (i = jz - 1; i >= jk; i--) j |= iq[i];
            if (j == 0)
            {
                for (k = 1; iq[jk - k] == 0; k++) ;
                for (i = jz + 1; i <= jz + k; i++)
                {
                    f[jx + i] = (double)ipio2[jv + i];
                    for (j = 0, fw = 0.0; j <= jx; j++)
                        fw += x[j] * f[jx + i - j];
                    q[i] = fw;
                }
                jz += k;
                goto recompute;
            }
        }
        if (z == 0.0)
        {
            jz -= 1;
            q0 -= 24;
            while (iq[jz] == 0)
            {
                jz--;
                q0 -= 24;
            }
        }
        else
        {
            z = Scalbn(z, -q0);
            if (z >= P(24))
            {
                fw = (double)(int)(P(-24) * z);
                iq[jz] = (int)(z - P(24) * fw);
                jz += 1;
                q0 += 24;
                iq[jz] = (int)fw;
            }
            else
                iq[jz] = (int)z;
        }
        fw = Scalbn(1.0, q0);
        for (i = jz; i >= 0; i--)
        {
            q[i] = fw * (double)iq[i];
            fw *= P(-24);
        }
        for (i = jz; i >= 0; i--)
        {
            for (fw = 0.0, k = 0; k <= jp && k <= jz - i; k++)
                fw += PIo2[k] * q[i + k];
            fq[jz - i] = fw;
        }
        switch (prec)
        {
            case 0:
                fw = 0.0;
                for (i = jz; i >= 0; i--)
                    fw += fq[i];
                y[0] = ih == 0 ? fw : -fw;
                break;
            case 1:
            case 2:
                fw = 0.0;
                for (i = jz; i >= 0; i--)
                    fw += fq[i];
                y[0] = ih == 0 ? fw : -fw;
                fw = fq[0] - fw;
                for (i = 1; i <= jz; i++)
                    fw += fq[i];
                y[1] = ih == 0 ? fw : -fw;
                break;
            case 3:
                for (i = jz; i > 0; i--)
                {
                    fw = fq[i - 1] + fq[i];
                    fq[i] += fq[i - 1] - fw;
                    fq[i - 1] = fw;
                }
                for (i = jz; i > 1; i--)
                {
                    fw = fq[i - 1] + fq[i];
                    fq[i] += fq[i - 1] - fw;
                    fq[i - 1] = fw;
                }
                for (fw = 0.0, i = jz; i >= 2; i--)
                    fw += fq[i];
                if (ih == 0)
                {
                    y[0] = fq[0]; y[1] = fq[1]; y[2] = fw;
                }
                else
                {
                    y[0] = -fq[0]; y[1] = -fq[1]; y[2] = -fw;
                }
                break;
        }
        return n & 7;
    }

    // ---------------------------------------------------------------- sin, cos, tan

    public static double Sin(double x)
    {
        var y = new double[2];
        uint ix;
        uint n;
        ix = HighWord(x);
        ix &= 0x7fffffff;
        if (ix <= 0x3fe921fb)
        {
            if (ix < 0x3e500000) return x;
            return KernelSin(x, 0.0, 0);
        }
        if (ix >= 0x7ff00000) return x - x;
        n = (uint)RemPio2(x, y);
        switch (n & 3)
        {
            case 0: return KernelSin(y[0], y[1], 1);
            case 1: return KernelCos(y[0], y[1]);
            case 2: return -KernelSin(y[0], y[1], 1);
            default: return -KernelCos(y[0], y[1]);
        }
    }

    public static double Cos(double x)
    {
        var y = new double[2];
        uint ix;
        uint n;
        ix = HighWord(x);
        ix &= 0x7fffffff;
        if (ix <= 0x3fe921fb)
        {
            if (ix < 0x3e46a09e) return 1.0;
            return KernelCos(x, 0);
        }
        if (ix >= 0x7ff00000) return x - x;
        n = (uint)RemPio2(x, y);
        switch (n & 3)
        {
            case 0: return KernelCos(y[0], y[1]);
            case 1: return -KernelSin(y[0], y[1], 1);
            case 2: return -KernelCos(y[0], y[1]);
            default: return KernelSin(y[0], y[1], 1);
        }
    }

    public static double Tan(double x)
    {
        var y = new double[2];
        uint ix;
        uint n;
        ix = HighWord(x);
        ix &= 0x7fffffff;
        if (ix <= 0x3fe921fb)
        {
            if (ix < 0x3e400000) return x;
            return KernelTan(x, 0.0, 0);
        }
        if (ix >= 0x7ff00000) return x - x;
        n = (uint)RemPio2(x, y);
        return KernelTan(y[0], y[1], (int)(n & 1));
    }

    // ---------------------------------------------------------------- asin, acos, atan, atan2

    private const double Pio2Hi = 1.57079632679489655800e+00;
    private const double Pio2Lo = 6.12323399573676603587e-17;
    private const double PS0 = 1.66666666666666657415e-01;
    private const double PS1 = -3.25565818622400915405e-01;
    private const double PS2 = 2.01212532134862925881e-01;
    private const double PS3 = -4.00555345006794114027e-02;
    private const double PS4 = 7.91534994289814532176e-04;
    private const double PS5 = 3.47933107596021167570e-05;
    private const double QS1 = -2.40339491173441421878e+00;
    private const double QS2 = 2.02094576023350569471e+00;
    private const double QS3 = -6.88283971605453293030e-01;
    private const double QS4 = 7.70381505559019352791e-02;

    private static double AsinR(double z)
    {
        double p, q;
        p = z * (PS0 + z * (PS1 + z * (PS2 + z * (PS3 + z * (PS4 + z * PS5)))));
        q = 1.0 + z * (QS1 + z * (QS2 + z * (QS3 + z * QS4)));
        return p / q;
    }

    public static double Asin(double x)
    {
        double z, r, s;
        uint hx, ix;
        hx = HighWord(x);
        ix = hx & 0x7fffffff;
        if (ix >= 0x3ff00000)
        {
            uint lx = LowWord(x);
            if (((ix - 0x3ff00000) | lx) == 0) return x * Pio2Hi + P(-120);
            return 0 / (x - x);
        }
        if (ix < 0x3fe00000)
        {
            if (ix < 0x3e500000 && ix >= 0x00100000) return x;
            return x + x * AsinR(x * x);
        }
        z = (1 - Math.Abs(x)) * 0.5;
        s = Math.Sqrt(z);
        r = AsinR(z);
        if (ix >= 0x3fef3333)
        {
            x = Pio2Hi - (2 * (s + s * r) - Pio2Lo);
        }
        else
        {
            double f, c;
            f = s;
            f = SetLowWord(f, 0);
            c = (z - f * f) / (s + f);
            x = 0.5 * Pio2Hi - (2 * s * r - (Pio2Lo - 2 * c) - (0.5 * Pio2Hi - 2 * f));
        }
        if (hx >> 31 != 0) return -x;
        return x;
    }

    public static double Acos(double x)
    {
        double z, w, s, c, df;
        uint hx, ix;
        hx = HighWord(x);
        ix = hx & 0x7fffffff;
        if (ix >= 0x3ff00000)
        {
            uint lx = LowWord(x);
            if (((ix - 0x3ff00000) | lx) == 0)
            {
                if (hx >> 31 != 0) return 2 * Pio2Hi + P(-120);
                return 0;
            }
            return 0 / (x - x);
        }
        if (ix < 0x3fe00000)
        {
            if (ix <= 0x3c600000) return Pio2Hi + P(-120);
            return Pio2Hi - (x - (Pio2Lo - x * AsinR(x * x)));
        }
        if (hx >> 31 != 0)
        {
            z = (1.0 + x) * 0.5;
            s = Math.Sqrt(z);
            w = AsinR(z) * s - Pio2Lo;
            return 2 * (Pio2Hi - (s + w));
        }
        z = (1.0 - x) * 0.5;
        s = Math.Sqrt(z);
        df = s;
        df = SetLowWord(df, 0);
        c = (z - df * df) / (s + df);
        w = AsinR(z) * s + c;
        return 2 * (df + w);
    }

    private static readonly double[] Atanhi =
    [
        4.63647609000806093515e-01,
        7.85398163397448278999e-01,
        9.82793723247329054082e-01,
        1.57079632679489655800e+00,
    ];
    private static readonly double[] Atanlo =
    [
        2.26987774529616870924e-17,
        3.06161699786838301793e-17,
        1.39033110312309984516e-17,
        6.12323399573676603587e-17,
    ];
    private static readonly double[] AT =
    [
        3.33333333333329318027e-01,
        -1.99999999998764832476e-01,
        1.42857142725034663711e-01,
        -1.11111104054623557880e-01,
        9.09088713343650656196e-02,
        -7.69187620504482999495e-02,
        6.66107313738753120669e-02,
        -5.83357013379057348645e-02,
        4.97687799461593236017e-02,
        -3.65315727442169155270e-02,
        1.62858201153657823623e-02,
    ];

    public static double Atan(double x)
    {
        double w, s1, s2, z;
        uint ix, sign;
        int id;
        ix = HighWord(x);
        sign = ix >> 31;
        ix &= 0x7fffffff;
        if (ix >= 0x44100000)
        {
            if (double.IsNaN(x)) return x;
            z = Atanhi[3] + P(-120);
            return sign != 0 ? -z : z;
        }
        if (ix < 0x3fdc0000)
        {
            if (ix < 0x3e400000) return x;
            id = -1;
        }
        else
        {
            x = Math.Abs(x);
            if (ix < 0x3ff30000)
            {
                if (ix < 0x3fe60000)
                {
                    id = 0;
                    x = (2.0 * x - 1.0) / (2.0 + x);
                }
                else
                {
                    id = 1;
                    x = (x - 1.0) / (x + 1.0);
                }
            }
            else
            {
                if (ix < 0x40038000)
                {
                    id = 2;
                    x = (x - 1.5) / (1.0 + 1.5 * x);
                }
                else
                {
                    id = 3;
                    x = -1.0 / x;
                }
            }
        }
        z = x * x;
        w = z * z;
        var aT = AT;
        s1 = z * (aT[0] + w * (aT[2] + w * (aT[4] + w * (aT[6] + w * (aT[8] + w * aT[10])))));
        s2 = w * (aT[1] + w * (aT[3] + w * (aT[5] + w * (aT[7] + w * aT[9]))));
        if (id < 0) return x - x * (s1 + s2);
        z = Atanhi[id] - (x * (s1 + s2) - Atanlo[id] - x);
        return sign != 0 ? -z : z;
    }

    private const double Pi = 3.1415926535897931160E+00;
    private const double PiLo = 1.2246467991473531772E-16;

    public static double Atan2(double y, double x)
    {
        double z;
        uint m, lx, ly, ix, iy;
        if (double.IsNaN(x) || double.IsNaN(y)) return x + y;
        ix = HighWord(x); lx = LowWord(x);
        iy = HighWord(y); ly = LowWord(y);
        if (((ix - 0x3ff00000) | lx) == 0) return Atan(y);
        m = ((iy >> 31) & 1) | ((ix >> 30) & 2);
        ix = ix & 0x7fffffff;
        iy = iy & 0x7fffffff;
        if ((iy | ly) == 0)
        {
            switch (m)
            {
                case 0:
                case 1: return y;
                case 2: return Pi;
                case 3: return -Pi;
            }
        }
        if ((ix | lx) == 0) return (m & 1) != 0 ? -Pi / 2 : Pi / 2;
        if (ix == 0x7ff00000)
        {
            if (iy == 0x7ff00000)
            {
                switch (m)
                {
                    case 0: return Pi / 4;
                    case 1: return -Pi / 4;
                    case 2: return 3 * Pi / 4;
                    case 3: return -3 * Pi / 4;
                }
            }
            else
            {
                switch (m)
                {
                    case 0: return 0.0;
                    case 1: return -0.0;
                    case 2: return Pi;
                    case 3: return -Pi;
                }
            }
        }
        if (ix + (64 << 20) < iy || iy == 0x7ff00000) return (m & 1) != 0 ? -Pi / 2 : Pi / 2;
        if ((m & 2) != 0 && iy + (64 << 20) < ix)
            z = 0;
        else
            z = Atan(Math.Abs(y / x));
        switch (m)
        {
            case 0: return z;
            case 1: return -z;
            case 2: return Pi - (z - PiLo);
            default: return (z - PiLo) - Pi;
        }
    }

    // ---------------------------------------------------------------- expm1, the hyperbolic ones

    private const double OThreshold = 7.09782712893383973096e+02;
    private const double Ln2Hi = 6.93147180369123816490e-01;
    private const double Ln2Lo = 1.90821492927058770002e-10;
    private const double Invln2 = 1.44269504088896338700e+00;
    private const double Q1 = -3.33333333333331316428e-02;
    private const double Q2 = 1.58730158725481460165e-03;
    private const double Q3 = -7.93650757867487942473e-05;
    private const double Q4 = 4.00821782732936239552e-06;
    private const double Q5 = -2.01099218183624371326e-07;

    public static double Expm1(double x)
    {
        double y, hi, lo, c, t, e, hxs, hfx, r1, twopk;
        ulong ui = AsUint64(x);
        uint hx = (uint)(ui >> 32) & 0x7fffffff;
        int k, sign = (int)(ui >> 63);
        if (hx >= 0x4043687A)
        {
            if (double.IsNaN(x)) return x;
            if (sign != 0) return -1;
            if (x > OThreshold)
            {
                x *= P(1023);
                return x;
            }
        }
        c = 0;
        if (hx > 0x3fd62e42)
        {
            if (hx < 0x3FF0A2B2)
            {
                if (sign == 0)
                {
                    hi = x - Ln2Hi;
                    lo = Ln2Lo;
                    k = 1;
                }
                else
                {
                    hi = x + Ln2Hi;
                    lo = -Ln2Lo;
                    k = -1;
                }
            }
            else
            {
                k = (int)(Invln2 * x + (sign != 0 ? -0.5 : 0.5));
                t = k;
                hi = x - t * Ln2Hi;
                lo = t * Ln2Lo;
            }
            x = hi - lo;
            c = (hi - x) - lo;
        }
        else if (hx < 0x3c900000)
        {
            return x;
        }
        else
            k = 0;
        hfx = 0.5 * x;
        hxs = x * hfx;
        r1 = 1.0 + hxs * (Q1 + hxs * (Q2 + hxs * (Q3 + hxs * (Q4 + hxs * Q5))));
        t = 3.0 - r1 * hfx;
        e = hxs * ((r1 - t) / (6.0 - x * t));
        if (k == 0) return x - (x * e - hxs);
        e = x * (e - c) - c;
        e -= hxs;
        if (k == -1) return 0.5 * (x - e) - 0.5;
        if (k == 1)
        {
            if (x < -0.25) return -2.0 * (e - (x + 0.5));
            return 1.0 + 2.0 * (x - e);
        }
        twopk = AsDouble((ulong)(0x3ff + k) << 52);
        if (k < 0 || k > 56)
        {
            y = x - e + 1.0;
            if (k == 1024)
                y = y * 2.0 * P(1023);
            else
                y = y * twopk;
            return y - 1.0;
        }
        double uf = AsDouble((ulong)(0x3ff - k) << 52);
        if (k < 20)
            y = (x - e + (1 - uf)) * twopk;
        else
            y = (x - (e + uf) + 1) * twopk;
        return y;
    }

    private const int Expo2K = 2043;
    private static readonly double Expo2Kln2 = AsDouble(0x40962066151add8b); // 0x1.62066151add8bp+10

    private static double Expo2(double x, double sign)
    {
        double scale = InsertWords((uint)(0x3ff + Expo2K / 2) << 20, 0);
        return Exp(x - Expo2Kln2) * (sign * scale) * scale;
    }

    public static double Sinh(double x)
    {
        ulong ui = AsUint64(x);
        uint w;
        double t, h, absx;
        h = 0.5;
        if (ui >> 63 != 0) h = -h;
        ui &= ulong.MaxValue / 2;
        absx = AsDouble(ui);
        w = (uint)(ui >> 32);
        if (w < 0x40862e42)
        {
            t = Expm1(absx);
            if (w < 0x3ff00000)
            {
                if (w < 0x3ff00000 - (26 << 20)) return x;
                return h * (2 * t - t * t / (t + 1));
            }
            return h * (t + t / (t + 1));
        }
        t = Expo2(absx, 2 * h);
        return t;
    }

    public static double Cosh(double x)
    {
        ulong ui = AsUint64(x);
        uint w;
        double t;
        ui &= ulong.MaxValue / 2;
        x = AsDouble(ui);
        w = (uint)(ui >> 32);
        if (w < 0x3fe62e42)
        {
            if (w < 0x3ff00000 - (26 << 20)) return 1;
            t = Expm1(x);
            return 1 + t * t / (2 * (1 + t));
        }
        if (w < 0x40862e42)
        {
            t = Exp(x);
            return 0.5 * (t + 1 / t);
        }
        t = Expo2(x, 1.0);
        return t;
    }

    public static double Tanh(double x)
    {
        ulong ui = AsUint64(x);
        uint w;
        int sign;
        double t;
        sign = (int)(ui >> 63);
        ui &= ulong.MaxValue / 2;
        x = AsDouble(ui);
        w = (uint)(ui >> 32);
        if (w > 0x3fe193ea)
        {
            if (w > 0x40340000)
            {
                t = 1 - 0 / x;
            }
            else
            {
                t = Expm1(2 * x);
                t = 1 - 2 / (t + 2);
            }
        }
        else if (w > 0x3fd058ae)
        {
            t = Expm1(2 * x);
            t = t / (t + 2);
        }
        else if (w >= 0x00100000)
        {
            t = Expm1(-2 * x);
            t = -t / (t + 2);
        }
        else
        {
            t = x;
        }
        return sign != 0 ? -t : t;
    }

    // ---------------------------------------------------------------- cbrt, hypot

    private const uint CbrtB1 = 715094163;
    private const uint CbrtB2 = 696219795;
    private const double CbrtP0 = 1.87595182427177009643;
    private const double CbrtP1 = -1.88497979543377169875;
    private const double CbrtP2 = 1.621429720105354466140;
    private const double CbrtP3 = -0.758397934778766047437;
    private const double CbrtP4 = 0.145996192886612446982;

    public static double Cbrt(double x)
    {
        ulong ui = AsUint64(x);
        double r, s, t, w;
        uint hx = (uint)(ui >> 32) & 0x7fffffff;
        if (hx >= 0x7ff00000) return x + x;
        if (hx < 0x00100000)
        {
            ui = AsUint64(x * P(54));
            hx = (uint)(ui >> 32) & 0x7fffffff;
            if (hx == 0) return x;
            hx = hx / 3 + CbrtB2;
        }
        else
            hx = hx / 3 + CbrtB1;
        ui &= 1UL << 63;
        ui |= (ulong)hx << 32;
        t = AsDouble(ui);
        r = (t * t) * (t / x);
        t = t * ((CbrtP0 + r * (CbrtP1 + r * CbrtP2)) + ((r * r) * r) * (CbrtP3 + r * CbrtP4));
        ui = AsUint64(t);
        ui = (ui + 0x80000000) & 0xffffffffc0000000UL;
        t = AsDouble(ui);
        s = t * t;
        r = x / s;
        w = t + t;
        r = (r - t) / (w + r);
        t = t + t * r;
        return t;
    }

    private static readonly double HypotSplit = P(27) + 1;

    private static void HypotSq(out double hi, out double lo, double x)
    {
        double xh, xl, xc;
        xc = x * HypotSplit;
        xh = x - xc + xc;
        xl = x - xh;
        hi = x * x;
        lo = xh * xh - hi + 2 * xh * xl + xl * xl;
    }

    public static double Hypot(double x, double y)
    {
        ulong ux = AsUint64(x), uy = AsUint64(y), ut;
        int ex, ey;
        double hx, lx, hy, ly, z;
        ux &= ulong.MaxValue >> 1;
        uy &= ulong.MaxValue >> 1;
        if (ux < uy)
        {
            ut = ux;
            ux = uy;
            uy = ut;
        }
        ex = (int)(ux >> 52);
        ey = (int)(uy >> 52);
        x = AsDouble(ux);
        y = AsDouble(uy);
        if (ey == 0x7ff) return y;
        if (ex == 0x7ff || uy == 0) return x;
        if (ex - ey > 64) return x + y;
        z = 1;
        if (ex > 0x3ff + 510)
        {
            z = P(700);
            x *= P(-700);
            y *= P(-700);
        }
        else if (ey < 0x3ff - 450)
        {
            z = P(-700);
            x *= P(700);
            y *= P(700);
        }
        HypotSq(out hx, out lx, x);
        HypotSq(out hy, out ly, y);
        return z * Math.Sqrt(ly + lx + hy + hx);
    }
}
