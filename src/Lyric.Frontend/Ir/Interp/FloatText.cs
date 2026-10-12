using System.Globalization;
using System.Numerics;
using System.Text;

namespace Lyric.Ir.Interp;

/// <summary>
/// A float's text as the runtime writes it (runtime/src/string.c): the shortest digits that read
/// back — found by the same trials, <c>%e</c> at one precision after another until the text reads
/// back, which .NET's correctly rounded <c>E</c> format and parser answer as glibc does — laid out
/// as <c>laid_out</c> lays them; and the text under a precision, rounded half away from zero on
/// the exact decimal expansion (<c>lyr_str_float_text</c>). Bit for bit (09 Q7).
/// </summary>
public static class FloatText
{
    public static string Shortest(double value)
    {
        if (double.IsNaN(value)) return "nan";
        if (double.IsPositiveInfinity(value)) return "inf";
        if (double.IsNegativeInfinity(value)) return "-inf";
        var sci = "";
        for (var precision = 0; precision <= 16; precision++)
        {
            sci = value.ToString("E" + precision, CultureInfo.InvariantCulture);
            if (double.Parse(sci, CultureInfo.InvariantCulture) == value) break;
        }
        return LaidOut(sci);
    }

    public static string Shortest(float value)
    {
        if (float.IsNaN(value)) return "nan";
        if (float.IsPositiveInfinity(value)) return "inf";
        if (float.IsNegativeInfinity(value)) return "-inf";
        var sci = "";
        for (var precision = 0; precision <= 8; precision++)
        {
            sci = ((double)value).ToString("E" + precision, CultureInfo.InvariantCulture);
            if (float.Parse(sci, CultureInfo.InvariantCulture) == value) break;
        }
        return LaidOut(sci);
    }

    /// <summary><c>laid_out</c>: plain for a decimal exponent from -4 up to below 16, with `.0` on an
    /// integral value; an exponent beyond, signed and two digits at least.</summary>
    private static string LaidOut(string sci)
    {
        var negative = sci.StartsWith('-');
        var body = negative ? sci[1..] : sci;
        var e = body.IndexOf('E');
        var digits = new StringBuilder();
        foreach (var c in body[..e])
            if (c != '.') digits.Append(c);
        while (digits.Length > 1 && digits[^1] == '0') digits.Length--;
        var exponent = int.Parse(body[(e + 1)..], CultureInfo.InvariantCulture);
        var count = digits.Length;

        var text = new StringBuilder();
        if (negative) text.Append('-');
        if (exponent < -4 || exponent >= 16)
        {
            text.Append(digits[0]);
            if (count > 1) text.Append('.').Append(digits.ToString(1, count - 1));
            text.Append('e').Append(exponent < 0 ? '-' : '+').Append(Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture));
        }
        else if (exponent < 0)
        {
            text.Append("0.").Append('0', -exponent - 1).Append(digits);
        }
        else
        {
            for (var i = 0; i <= exponent; i++) text.Append(i < count ? digits[i] : '0');
            text.Append('.');
            if (count > exponent + 1) text.Append(digits.ToString(exponent + 1, count - exponent - 1));
            else text.Append('0');
        }
        return text.ToString();
    }

    /// <summary><c>exact_of</c>: the exact decimal digits of a double, and the point's place.</summary>
    private static (string Digits, int Point, bool Negative) Exact(double value)
    {
        var bits = (ulong)BitConverter.DoubleToInt64Bits(value);
        var negative = (bits >> 63) != 0;
        var biased = (int)((bits >> 52) & 0x7FF);
        var m = bits & ((1UL << 52) - 1);
        int e;
        if (biased == 0) e = -1074;
        else
        {
            m |= 1UL << 52;
            e = biased - 1075;
        }
        if (m == 0) return ("0", 1, negative);
        BigInteger b = m;
        var scale = 0;
        if (e >= 0) b <<= e;
        else
        {
            scale = -e;
            b *= BigInteger.Pow(5, scale);
        }
        var digits = b.ToString(CultureInfo.InvariantCulture);
        return (digits, digits.Length - scale, negative);
    }

    private static char DigitAt(string digits, int i) => i >= 0 && i < digits.Length ? digits[i] : '0';

    /// <summary><c>rounded_digits</c>: <paramref name="keep"/> digits from the first, rounded half away
    /// from zero on the next; a carry out of the first digit gives one more.</summary>
    private static (char[] Kept, bool Carried) Rounded(string digits, int keep)
    {
        var kept = new char[keep];
        for (var i = 0; i < keep; i++) kept[i] = DigitAt(digits, i);
        if (DigitAt(digits, keep) < '5') return (kept, false);
        var j = keep - 1;
        for (; j >= 0; j--)
        {
            if (kept[j] == '9') kept[j] = '0';
            else
            {
                kept[j]++;
                break;
            }
        }
        if (j >= 0) return (kept, false);
        return (['1', .. kept], true);
    }

    /// <summary><c>lyr_str_float_text</c>: <paramref name="form"/> 0 fixed, 1 and 2 with an exponent
    /// (<c>e</c>, <c>E</c>), 3 fixed with the point moved two places right.</summary>
    public static string WithPrecision(double value, long precision, long form)
    {
        if (double.IsNaN(value)) return form == 2 ? "NAN" : "nan";
        if (double.IsPositiveInfinity(value)) return form == 2 ? "INF" : "inf";
        if (double.IsNegativeInfinity(value)) return form == 2 ? "-INF" : "-inf";
        var places = precision < 0 ? 0 : precision > 100000 ? 100000 : (int)precision;
        var (digits, point, negative) = Exact(value);
        var text = new StringBuilder();
        if (negative) text.Append('-');
        if (form is 1 or 2)
        {
            var zero = digits == "0";
            var exponent = zero ? 0 : point - 1;
            var (kept, carried) = Rounded(digits, places + 1);
            if (carried) exponent++;
            text.Append(kept[0]);
            if (places > 0) text.Append('.').Append(kept, 1, places);
            text.Append(form == 2 ? 'E' : 'e').Append(exponent < 0 ? '-' : '+')
                .Append(Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture));
            return text.ToString();
        }
        var at = point + (form == 3 ? 2 : 0);
        var keep = at + places;
        char[] digitsKept = [];
        if (keep > 0)
        {
            var (kept, carried) = Rounded(digits, keep);
            digitsKept = kept;
            if (carried) at++;
        }
        else if (keep == 0 && DigitAt(digits, 0) >= '5')
        {
            digitsKept = ['1'];
            at++;
        }
        if (at <= 0) text.Append('0');
        else for (var i = 0; i < at; i++) text.Append(i < digitsKept.Length ? digitsKept[i] : '0');
        if (places > 0)
        {
            text.Append('.');
            for (var i = 0; i < places; i++)
            {
                var index = at + i;
                text.Append(index >= 0 && index < digitsKept.Length ? digitsKept[index] : '0');
            }
        }
        return text.ToString();
    }

    /// <summary>strtod over a text the library has validated, '_' left out (<c>float_text</c>).</summary>
    public static double Parse(string text)
    {
        var plain = text.Replace("_", "", StringComparison.Ordinal);
        if (double.TryParse(plain, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return value;
        throw ComptimeFailure.NotYet($"the text \"{text}\" is read as a float only at run time");
    }
}
