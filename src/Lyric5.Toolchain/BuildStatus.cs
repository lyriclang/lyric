namespace Lyric5.Toolchain;

/// <summary>
/// The build's status lines on the standard error (spec 14 §1.1): each step that does work says one
/// line as it starts — a word right-aligned in twelve columns, then what it works on —, and what is
/// up to date says nothing. Where the standard error is a terminal, the C units being compiled are
/// counted on a line of their own, drawn over and erased when they are done. The driver's, for one
/// command: set before the build and cleared after it; <c>-q</c> and <c>--json</c> leave it unset.
/// </summary>
public static class BuildStatus
{
    /// <summary>Where the lines go; <c>null</c>: nowhere.</summary>
    public static TextWriter? Writer { get; set; }

    /// <summary>Whether the word is drawn in colour, as the diagnostics are.</summary>
    public static bool Color { get; set; }

    /// <summary>Whether the count of the C units is drawn: a terminal, and no <c>-v</c> whose
    /// commands would land in the middle of it.</summary>
    public static bool Live { get; set; }

    private static readonly object Gate = new();
    private static bool _drawn;

    /// <summary>One step: <c>   Compiling app v0.1.0 (src/main.lyr)</c>.</summary>
    public static void Say(string word, string what)
    {
        if (Writer is not { } writer) return;
        lock (Gate)
        {
            EraseCount(writer);
            writer.WriteLine($"{Word(word)} {what}");
        }
    }

    /// <summary>The count of the units compiled so far, drawn over the last one.</summary>
    public static void Count(int done, int total, string unit)
    {
        if (!Live || Writer is not { } writer) return;
        lock (Gate)
        {
            writer.Write($"\r{Word("Building")} [{done}/{total}] {unit}\u001b[K");
            writer.Flush();
            _drawn = true;
        }
    }

    /// <summary>The count erased, where one is drawn: before a line of another kind, and when the
    /// units are done.</summary>
    public static void Done()
    {
        if (Writer is not { } writer) return;
        lock (Gate) EraseCount(writer);
    }

    /// <summary>Cleared after a command.</summary>
    public static void Reset()
    {
        Done();
        Writer = null;
        Color = false;
        Live = false;
    }

    private static void EraseCount(TextWriter writer)
    {
        if (!_drawn) return;
        writer.Write("\r\u001b[K");
        _drawn = false;
    }

    private static string Word(string word) => Color ? $"\u001b[1;32m{word,12}\u001b[0m" : $"{word,12}";
}
