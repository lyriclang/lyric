// Measurement point 3, 'maps': the C# twin of maps.lyr — Dictionary<long, long>.
var m = new System.Collections.Generic.Dictionary<long, long>();
ulong x = 88172645463325252;
for (long i = 0; i < 1000000; i++)
{
    x ^= x << 13;
    x ^= x >> 7;
    x ^= x << 17;
    m[(long)(x % 4000000)] = i;
}
long hits = 0, total = 0;
for (long k = 0; k < 4000000; k += 2)
{
    if (m.TryGetValue(k, out var v)) { hits++; total += v; }
}
System.Console.WriteLine($"{m.Count} {hits} {total}");
