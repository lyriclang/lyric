namespace Lyric.Core.Rendering;

/// <summary>
/// How the text renderer draws (design/v5/spec/11 G5, G6, C7; M12a S2): in colour or not, the paths
/// relative to the package or absolute, how many errors before it stops, how wide a source line may
/// be. The driver sets it once per command from its options; a library caller gets
/// <see cref="Plain"/>.
/// </summary>
public sealed record RenderStyle
{
    /// <summary><c>--max-errors</c> unless said (11 G6, D8).</summary>
    public const int DefaultMaxErrors = 50;

    /// <summary>The columns a quoted source line may take before it is cut around its marks.</summary>
    public const int DefaultWidth = 140;

    /// <summary>ANSI colour in the output (C7: <c>--color</c>, <c>NO_COLOR</c>, <c>TERM=dumb</c>).</summary>
    public bool Color { get; init; }

    /// <summary><c>--absolute-paths</c>: every path as the file system has it, none made relative.</summary>
    public bool AbsolutePaths { get; init; }

    /// <summary>How many errors are drawn before the rest is counted only; 0 draws all.</summary>
    public int MaxErrors { get; init; } = DefaultMaxErrors;

    /// <summary>The directory paths are said relative to — the package's root; <c>null</c> leaves
    /// them as they were read.</summary>
    public string? BaseDirectory { get; init; }

    /// <summary>See <see cref="DefaultWidth"/>.</summary>
    public int Width { get; init; } = DefaultWidth;

    /// <summary>No colour, paths as read, the default limit.</summary>
    public static RenderStyle Plain { get; } = new();

    private static readonly AsyncLocal<RenderStyle?> CurrentStyle = new();

    /// <summary>The style of the command running on this flow — set by the driver before it compiles,
    /// read where the engine renders without one. Per flow, not per process: the driver's tests run
    /// commands side by side in one process.</summary>
    public static RenderStyle Current
    {
        get => CurrentStyle.Value ?? Plain;
        set => CurrentStyle.Value = value;
    }

    /// <summary>
    /// Whether to colour (C7): <c>always</c> and <c>never</c> say it; <c>auto</c> colours a terminal
    /// unless <c>NO_COLOR</c> is set to anything or <c>TERM</c> is <c>dumb</c>. <c>null</c> for a
    /// mode that is none of the three.
    /// </summary>
    public static bool? ColorFor(string mode, bool isTerminal, Func<string, string?> environment) => mode switch
    {
        "always" => true,
        "never" => false,
        "auto" => isTerminal
                  && string.IsNullOrEmpty(environment("NO_COLOR"))
                  && environment("TERM") != "dumb",
        _ => null,
    };

    /// <summary>A path as the output says it: relative to <see cref="BaseDirectory"/> where the file
    /// lies under it, else as read. A name that is no path (a virtual file's, a synthesized one's)
    /// stays what it is.</summary>
    public string Display(string path)
    {
        if (!Path.IsPathRooted(path)) return path;
        if (AbsolutePaths || BaseDirectory is null) return path;
        var relative = Path.GetRelativePath(BaseDirectory, path);
        return relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? path
            : relative;
    }
}
