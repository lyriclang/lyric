// 04 Vergleich (Stufe a): C# — Definite Assignment (ECMA-334 §9.4.4.x): nach einem
// try-Statement ist v definitely assigned, wenn v am Ende des try-Blocks UND am Ende
// jedes catch-Blocks definitely assigned ist — und ein catch-Block, der mit `return`
// endet, hat einen unerreichbaren Endpunkt, an dem ALLES als zugewiesen gilt.
using System;

class UsageError : Exception { public UsageError(string what) : base("usage: " + what) { } }
record struct Options(int Limit, bool Verbose);

static class Program
{
    static Options ParseArgs(string[] args)
    {
        if (args.Length == 0) throw new UsageError("need at least one argument");
        return new Options(args.Length, args[0] == "-v");
    }

    static int RunA(string[] args)
    {
        Options opts;
        try { opts = ParseArgs(args); }
        catch (UsageError e) { Console.WriteLine(e.Message); return 2; }
        Console.WriteLine($"A: limit {opts.Limit} verbose {opts.Verbose}");   // ok: opts ist zugewiesen
        return 0;
    }

    static int Main() => RunA(new[] { "-v", "x" }) + RunA(Array.Empty<string>());
}
// Ausgabe:
// A: limit 2 verbose True
// usage: need at least one argument
