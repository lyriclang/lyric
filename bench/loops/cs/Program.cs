// Measurement point 2, 'loops': the C# twin of loops.lyr (unchecked arithmetic, the default).
long acc = 0;
for (long i = 0; i < 20000; i++)
    for (long j = 0; j < 10000; j++)
        acc = (acc + i * j) % 1000000007;
System.Console.WriteLine(acc);
