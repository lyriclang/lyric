// Measurement point 3, 'sorting': the C# twin of sorting.lyr — Array.Sort (introsort).
const int n = 1000000;
var xs = new long[n];
ulong x = 88172645463325252;
for (int i = 0; i < n; i++)
{
    x ^= x << 13;
    x ^= x >> 7;
    x ^= x << 17;
    xs[i] = (long)(x % 1000000000);
}
System.Array.Sort(xs);
long check = 0;
for (int i = 0; i < n; i++) check = (check * 31 + xs[i]) % 1000000007;
System.Console.WriteLine($"{xs[0]} {xs[n / 2]} {xs[n - 1]} {check}");
