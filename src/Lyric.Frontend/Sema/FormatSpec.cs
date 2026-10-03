namespace Lyric.Sema;

/// <summary>
/// A format spec as design/v5/spec/08 Y7 writes it, read (spec 12 §2 rule 1):
/// <c>[[fill]align][sign][#][0][width][grouping][.precision][type]</c>. Which parts a value takes is
/// asked here, by kind of value; std.core reads the same text at run time.
/// </summary>
internal sealed record FormatSpec(int? Fill, char? Align, char? Sign, bool Alternate, bool Zero,
    int? Width, char? Grouping, int? Precision, char? Type)
{
    /// <summary>The spec <paramref name="text"/> writes, or <c>null</c> with <paramref name="error"/>
    /// naming what is no part of one.</summary>
    public static FormatSpec? Read(string text, out string error)
    {
        error = "";
        var cs = text.EnumerateRunes().Select(r => r.Value).ToArray();
        bool At(int i, string any) => i < cs.Length && cs[i] < 128 && any.Contains((char)cs[i]);

        var at = 0;
        int? fill = null;
        char? align = null;
        if (At(1, "<>^")) { fill = cs[0]; align = (char)cs[1]; at = 2; }
        else if (At(0, "<>^")) { align = (char)cs[0]; at = 1; }
        char? sign = At(at, "+-") ? (char)cs[at++] : null;
        var alternate = At(at, "#");
        if (alternate) at++;
        var zero = At(at, "0");
        if (zero) at++;
        var width = Digits(cs, ref at);
        char? grouping = At(at, ",_") ? (char)cs[at++] : null;
        int? precision = null;
        if (At(at, "."))
        {
            at++;
            precision = Digits(cs, ref at);
            if (precision is null)
            {
                error = "'.' needs the digits of a precision";
                return null;
            }
        }
        char? type = At(at, "boxXeEf%?") ? (char)cs[at++] : null;
        if (at < cs.Length)
        {
            error = $"'{char.ConvertFromUtf32(cs[at])}' is no part of a format there — the form is "
                + "[[fill]align][sign][#][0][width][grouping][.precision][type]";
            return null;
        }
        if (width < 0 || precision < 0)
        {
            error = "a width or a precision beyond 'int'";
            return null;
        }
        return new FormatSpec(fill, align, sign, alternate, zero, width, grouping, precision, type);
    }

    /// <summary>The decimal digits from <paramref name="at"/> on: <c>null</c> without one, -1 beyond
    /// <c>int</c>.</summary>
    private static int? Digits(int[] cs, ref int at)
    {
        var start = at;
        long value = 0;
        while (at < cs.Length && cs[at] >= 48 && cs[at] <= 57)
        {
            value = Math.Min(value * 10 + (cs[at] - 48), (long)int.MaxValue + 1);
            at++;
        }
        if (at == start) return null;
        return value > int.MaxValue ? -1 : (int)value;
    }

    private static bool IsRadix(char? type) => type is 'b' or 'o' or 'x' or 'X';

    /// <summary>What an integer does not take (12 §2 rule 2), or <c>null</c>.</summary>
    public string? MisfitForInteger() =>
        Precision is not null ? "an integer has no precision"
        : Type is 'e' or 'E' or 'f' or '%' ? $"'{Type}' writes a float — an integer takes 'b', 'o', 'x' and 'X'"
        : Alternate && !IsRadix(Type) ? "'#' writes a radix's prefix and needs 'b', 'o', 'x' or 'X'"
        : Grouping == ',' && IsRadix(Type) ? "',' groups decimal digits — '_' groups another radix's"
        : null;

    /// <summary>What a float does not take.</summary>
    public string? MisfitForFloat() =>
        IsRadix(Type) ? $"'{Type}' writes an integer's radix — a float takes 'e', 'E', 'f' and '%'"
        : Alternate ? "'#' writes a radix's prefix, which a float has none of"
        : null;

    /// <summary>What a string does not take: it has fill, alignment, width and precision.</summary>
    public string? MisfitForString() =>
        Sign is not null || Alternate || Zero || Grouping is not null || Type is not null
            ? "a string takes fill, alignment, width and a precision"
            : null;

    /// <summary>Fill, alignment and width only: what a <c>char</c>, a <c>bool</c>, a
    /// <c>Display</c> type and <c>?</c> take.</summary>
    public string? MisfitForPadding(string what) =>
        Sign is not null || Alternate || Zero || Grouping is not null || Precision is not null || Type is not (null or '?')
            ? $"{what} takes fill, alignment and width"
            : null;
}
