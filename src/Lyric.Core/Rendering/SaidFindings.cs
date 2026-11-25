namespace Lyric.Core.Rendering;

/// <summary>
/// What one command has said, across the compilations it runs. The programs of a package share
/// modules, and each program is compiled with an engine of its own: a finding in a shared module was
/// said once per program, 2,247 lines for 361 places in a package of eight. A finding is known here
/// by its code, its file's path, its span and its message, which two compilations of one file agree
/// on; the errors drawn are counted here too, so that <c>--max-errors</c> is the command's.
/// </summary>
public sealed class SaidFindings
{
    private readonly HashSet<string> _said = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _shownErrors;

    /// <summary>Whether <paramref name="key"/> is said for the first time, and notes it.</summary>
    public bool First(string key)
    {
        lock (_gate) return _said.Add(key);
    }

    /// <summary>Whether <paramref name="key"/> was said already.</summary>
    public bool Said(string key)
    {
        lock (_gate) return _said.Contains(key);
    }

    /// <summary>The errors drawn so far by the command.</summary>
    public int ShownErrors
    {
        get { lock (_gate) return _shownErrors; }
    }

    /// <summary>One more error drawn.</summary>
    public void Shown()
    {
        lock (_gate) _shownErrors++;
    }
}
