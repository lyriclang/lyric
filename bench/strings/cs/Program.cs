// Measurement point 3, 'strings': the C# twin of strings.lyr — StringBuilder, Split, int.Parse.
var sb = new System.Text.StringBuilder();
for (long i = 0; i < 300000; i++)
{
    sb.Append("item").Append(i).Append(',').Append((i * 7919) % 100000).Append('\n');
}
var text = sb.ToString();
long lines = 0, named = 0, total = 0;
foreach (var line in text.AsSpan().TrimEnd('\n').ToString().Split('\n'))
{
    var comma = line.IndexOf(',');
    if (comma >= 0)
    {
        if (line.AsSpan(0, comma).Contains("99", System.StringComparison.Ordinal)) named++;
        if (int.TryParse(line.AsSpan(comma + 1), out var v)) total += v;
    }
    lines++;
}
System.Console.WriteLine($"{lines} {named} {total}");
