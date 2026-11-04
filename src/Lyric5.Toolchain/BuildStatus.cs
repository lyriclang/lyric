using System.Diagnostics;
using System.Globalization;

namespace Lyric5.Toolchain;

/// <summary>
/// The build's status lines on the standard error (spec 14 §1.1): each step that does work says one
/// line as it starts — a word right-aligned in twelve columns, then what it works on —, and what is
/// up to date says nothing. A timed step (<see cref="Begin"/>) ends with its time on its own line
/// where the standard error is a terminal, its count of the C units drawn over it meanwhile; a log
/// reads a whole line as the step starts, and the times together at the end. The driver's, for one
/// command: set before the build and cleared after it; <c>-q</c> and <c>--json</c> leave it unset.
/// </summary>
public static class BuildStatus
{
    /// <summary>Where the lines go; <c>null</c>: nowhere.</summary>
    public static TextWriter? Writer { get; set; }

    /// <summary>Whether the word is drawn in colour, as the diagnostics are.</summary>
    public static bool Color { get; set; }

    /// <summary>Whether a line is drawn over: a terminal, and no <c>-v</c> whose commands would land
    /// in the middle of it.</summary>
    public static bool Live { get; set; }

    /// <summary><c>-v</c>: each package and each C unit gets a line of its own.</summary>
    public static bool Verbose { get; set; }

    private static readonly object Gate = new();
    private static readonly List<(string Word, TimeSpan Time)> Times = [];
    private static Step? _open;
    private static bool _drawn;
    private static int _warnings, _units, _cached;

    /// <summary>One step: <c>   Compiling app v0.1.0 (src/main.lyr)</c>.</summary>
    public static void Say(string word, string what)
    {
        if (Writer is not { } writer) return;
        lock (Gate)
        {
            CloseOpen(writer);
            EraseCount(writer);
            writer.WriteLine($"{Word(word)} {what}");
        }
    }

    /// <summary>A line <c>-v</c> alone says.</summary>
    public static void SayVerbose(string word, string what)
    {
        if (Verbose) Say(word, what);
    }

    /// <summary>A step that ends with its time, when the result is disposed. Nothing else is written
    /// to the standard error between the two but other status lines.</summary>
    public static Step Begin(string word, string what)
    {
        var step = new Step(word, what);
        if (Writer is not { } writer) return step;
        lock (Gate)
        {
            CloseOpen(writer);
            EraseCount(writer);
            if (Live)
            {
                writer.Write($"{Word(word)} {what}");
                writer.Flush();
                _open = step;
            }
            else writer.WriteLine($"{Word(word)} {what}");
        }
        return step;
    }

    /// <summary>The count of the units compiled so far, drawn over the last one: over the step
    /// that compiles them.</summary>
    public static void Count(int done, int total, string unit)
    {
        if (!Live || Writer is not { } writer) return;
        lock (Gate)
        {
            writer.Write($"\r{Word(_open?.Word ?? "Building")} [{done}/{total}] {unit}\u001b[K");
            writer.Flush();
            _drawn = _open is null;
        }
    }

    /// <summary>The count erased, where one is drawn outside a step: before a line of another kind,
    /// and when the units are done.</summary>
    public static void Done()
    {
        if (Writer is not { } writer) return;
        lock (Gate) EraseCount(writer);
    }

    /// <summary>What the front end found, for the last line.</summary>
    public static void Warned(int warnings)
    {
        lock (Gate) _warnings += warnings;
    }

    /// <summary>The C units of a program and how many came from the cache, for the last line.</summary>
    public static void Units(int units, int cached)
    {
        lock (Gate)
        {
            _units += units;
            _cached += cached;
        }
    }

    /// <summary>What a finished build leaves to read after <c>Finished</c>: the warnings and the
    /// cache, where there is something to say of them, and in a log, whose lines carry no time, the
    /// steps' times.</summary>
    public static void Summary()
    {
        var parts = new List<string>();
        if (_warnings > 0) parts.Add(_warnings == 1 ? "1 warning" : $"{_warnings} warnings");
        if (_cached > 0) parts.Add($"{_cached} of {_units} C units from the cache");
        if (parts.Count > 0) Say("Summary", string.Join("; ", parts));
        if (Live || Times.Count == 0) return;
        var times = Times.GroupBy(t => t.Word, StringComparer.Ordinal)
            .Select(g => $"{g.Key.ToLowerInvariant()} {Seconds(g.Aggregate(TimeSpan.Zero, (sum, t) => sum + t.Time))}");
        Say("Timings", string.Join(", ", times));
    }

    /// <summary>A file's size as a reader takes it: <c>1.2 MB</c>.</summary>
    public static string Size(long bytes) =>
        bytes < 1024 ? $"{bytes} B"
        : bytes < 1024 * 1024 ? ((double)bytes / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " KB"
        : ((double)bytes / (1024 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    /// <summary>A time as the lines say it: <c>0.42s</c>.</summary>
    public static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture) + "s";

    /// <summary>Cleared after a command.</summary>
    public static void Reset()
    {
        if (Writer is { } writer)
            lock (Gate)
            {
                CloseOpen(writer);
                EraseCount(writer);
            }
        lock (Gate)
        {
            Times.Clear();
            _warnings = _units = _cached = 0;
        }
        Writer = null;
        Color = false;
        Live = false;
        Verbose = false;
    }

    /// <summary>A step begun by <see cref="Begin"/>; disposing it ends the step.</summary>
    public sealed class Step : IDisposable
    {
        private readonly long _started = Stopwatch.GetTimestamp();
        private bool _ended;

        internal Step(string word, string what)
        {
            Word = word;
            What = what;
        }

        internal string Word { get; }

        /// <summary>What it works on: known better at its end, the modules once they are loaded.</summary>
        public string What { get; set; }

        public void Dispose()
        {
            if (_ended) return;
            _ended = true;
            var time = Stopwatch.GetElapsedTime(_started);
            if (Writer is not { } writer) return;
            lock (Gate)
            {
                Times.Add((Word, time));
                if (!ReferenceEquals(_open, this)) return;
                _open = null;
                writer.WriteLine($"\r{BuildStatus.Word(Word)} {What} {Seconds(time)}\u001b[K");
            }
        }
    }

    /// <summary>A step still open when another line comes: its line ends without a time.</summary>
    private static void CloseOpen(TextWriter writer)
    {
        if (_open is not { } open) return;
        _open = null;
        writer.WriteLine($"\r{Word(open.Word)} {open.What}\u001b[K");
    }

    private static void EraseCount(TextWriter writer)
    {
        if (!_drawn) return;
        writer.Write("\r\u001b[K");
        _drawn = false;
    }

    private static string Word(string word) => Color ? $"\u001b[1;32m{word,12}\u001b[0m" : $"{word,12}";
}
