using Lyric5.Build;

namespace Lyric5;

public static partial class Program
{
    // --- toolchain -----------------------------------------------------------------------------

    /// <summary>
    /// <c>lyric toolchain install|list|default|remove|update</c>: the toolchains installed side by
    /// side under <see cref="ToolchainHome"/>, fetched and checked by <see cref="ToolchainFetch"/>.
    /// Each refusal is the environment's, exit 2.
    /// </summary>
    private static int Toolchain(string[] args)
    {
        if (Verbs.Read(Verbs.Find("toolchain")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Rest is not null) return Unknown("unknown option '--' for 'toolchain'");
        var words = parsed.Words;
        var available = parsed.Flags.Contains("--available");
        if (words.Count == 0) return Unknown("'toolchain' takes an action: install, list, default, remove or update");
        if (available && words[0] != "list") return Unknown("'--available' goes with 'toolchain list'");
        var home = ToolchainHome.ForUser();
        try
        {
            return (words[0], words.Count) switch
            {
                ("install", 2) => ToolchainInstall(home, words[1]),
                ("list", 1) => available ? ToolchainAvailable(home) : ToolchainList(home),
                ("default", 1) => ToolchainDefault(home, null),
                ("default", 2) => ToolchainDefault(home, words[1]),
                ("remove", 2) => ToolchainRemove(home, words[1]),
                ("update", 1) => ToolchainUpdate(home),
                ("install" or "remove", _) => Unknown($"'toolchain {words[0]}' takes one version"),
                ("default", _) => Unknown("'toolchain default' takes one version, or none to say the default"),
                ("list" or "update", _) => Unknown($"'toolchain {words[0]}' takes no version"),
                _ => Unknown($"no action '{words[0]}' for 'toolchain': install, list, default, remove or update"),
            };
        }
        catch (ToolchainException refused)
        {
            Say.Error(Console.Error, refused.Code, refused.Message, refused.Help);
            return 2;
        }
    }

    private static ToolchainFetch Fetcher() =>
        ToolchainFetch.ForUser(DisplayVersion(), Console.IsErrorRedirected ? null : Console.Error);

    private static int ToolchainInstall(ToolchainHome home, string asked)
    {
        var fetch = Fetcher();
        var (release, channel) = fetch.Release(asked);
        if (!fetch.Install(home, release, channel))
        {
            Console.Out.WriteLine($"lyric {release.Version} is installed already");
            return 0;
        }
        Console.Out.WriteLine($"installed lyric {release.Version} in {home.DirectoryOf(release.Version)}");
        if (home.Default() is null) MakeDefault(home, release.Version);
        return 0;
    }

    /// <summary>The default set and said, with the advice to put <c>bin</c> on the PATH where it
    /// is not.</summary>
    private static void MakeDefault(ToolchainHome home, string version)
    {
        home.SetDefault(version);
        Console.Out.WriteLine($"lyric {version} is the default");
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.Any(entry => string.Equals(Path.TrimEndingDirectorySeparator(entry), home.Bin, comparison)))
            Console.Out.WriteLine($"  = help: {home.Bin} on the PATH runs it as {ToolchainHome.BinaryName}");
    }

    private static int ToolchainList(ToolchainHome home)
    {
        var installed = home.Installed();
        if (installed.Count == 0)
        {
            Console.Out.WriteLine($"no toolchain is installed in {home.Toolchains}");
            return 0;
        }
        var chosen = home.Default();
        foreach (var version in installed) Console.Out.WriteLine(version == chosen ? $"{version} (default)" : version);
        return 0;
    }

    /// <summary>The versions both channels offer, the newest first; a channel that cannot be
    /// fetched is said and skipped, unless neither can.</summary>
    private static int ToolchainAvailable(ToolchainHome home)
    {
        var fetch = Fetcher();
        var installed = home.Installed().ToHashSet(StringComparer.Ordinal);
        ToolchainException? missed = null;
        var any = false;
        foreach (var channel in new[] { ChannelIndex.Stable, ChannelIndex.Dev })
        {
            ChannelIndex index;
            try { index = fetch.Index(channel); }
            catch (ToolchainException unreachable) when (unreachable.Code == "LYR-CLI0023")
            {
                missed = unreachable;
                Console.Error.WriteLine($"the channel '{channel}': {unreachable.Message}");
                continue;
            }
            any = true;
            foreach (var release in index.Releases.OrderByDescending(r => r.Number))
                Console.Out.WriteLine($"{release.Version,-34} {channel,-6} {release.Date}{(installed.Contains(release.Version) ? "  installed" : "")}");
        }
        if (!any && missed is not null) throw missed;
        return 0;
    }

    private static int ToolchainDefault(ToolchainHome home, string? asked)
    {
        if (asked is null)
        {
            Console.Out.WriteLine(home.Default() is { } chosen ? chosen : $"no default: {home.Config} names none");
            return 0;
        }
        MakeDefault(home, Installed(home, asked));
        return 0;
    }

    private static int ToolchainRemove(ToolchainHome home, string asked)
    {
        var version = Installed(home, asked);
        home.Remove(version, ToolchainHome.Running(AppContext.BaseDirectory));
        Console.Out.WriteLine($"removed lyric {version}");
        return 0;
    }

    /// <summary>The newest of the default's channel installed beside it and made the default; the
    /// old one stays until it is removed.</summary>
    private static int ToolchainUpdate(ToolchainHome home)
    {
        var current = home.Default()
                      ?? throw new ToolchainException("LYR-CLI0025", "there is no default to update", "lyric5 toolchain install stable — or dev — installs one");
        var channel = ToolchainHome.ChannelOf(current);
        var fetch = Fetcher();
        var newest = fetch.Index(channel).Newest;
        if (newest is null || SemVer.Parse(current) is { } number && newest.Number.CompareTo(number) <= 0)
        {
            Console.Out.WriteLine($"lyric {current} is the newest of the channel '{channel}'");
            return 0;
        }
        if (fetch.Install(home, newest, channel))
            Console.Out.WriteLine($"installed lyric {newest.Version} in {home.DirectoryOf(newest.Version)}");
        MakeDefault(home, newest.Version);
        Console.Out.WriteLine($"  = note: lyric {current} stays until 'lyric5 toolchain remove {current}'");
        return 0;
    }

    /// <summary>The installed version <paramref name="asked"/> names.</summary>
    private static string Installed(ToolchainHome home, string asked) =>
        home.Resolve(asked) ?? throw new ToolchainException("LYR-CLI0025", $"lyric {asked} is not installed in {home.Toolchains}",
            $"lyric5 toolchain install {asked}");
}
