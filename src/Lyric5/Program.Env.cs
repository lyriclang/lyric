using System.Text.Json;
using LintSettings = Lyric.Core.LintSettings;
using DiagnosticCatalog = Lyric.Core.DiagnosticCatalog;
using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

public static partial class Program
{
    // --- env -----------------------------------------------------------------------------------

    /// <summary>A setting a build reads: its name, its value, and where the value comes from —
    /// <c>command line</c>, an environment variable by its name, <c>lyric.toml</c>, or <c>default</c>.</summary>
    private sealed record Setting(string Name, string Value, string Origin);

    /// <summary>
    /// <c>lyric env</c> (spec 14 §6): every setting a build of the package here would read, with its
    /// value and the rung of the ladder it comes from — the command line over a <c>LYRIC_*</c>
    /// variable over the manifest over the default. It takes the options that change one —
    /// <c>--profile</c>, the fields, <c>--target</c> — so that what it says is what that build reads.
    /// </summary>
    private static int Env(string[] args, LintArgs lints)
    {
        if (Verbs.Read(Verbs.Find("env")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Words.Count > 0 || parsed.Rest is not null)
            return Unknown($"'env' takes no words, got '{(parsed.Words.Count > 0 ? parsed.Words[0] : "--")}'");
        var values = parsed.Values;
        var start = Path.GetFullPath(values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        if (!Directory.Exists(start))
        {
            Say.Error(Console.Error, "LYR-CLI0001", $"no such directory '{start}'");
            return 2;
        }
        Manifest? manifest = null;
        if (Project.FindManifest(start) is { } file)
        {
            try { manifest = Manifest.Read(file); }
            catch (ManifestException refused)
            {
                Say.Manifest(Console.Error, refused);
                return refused.Exit;
            }
        }

        var settings = new List<Setting>
        {
            new("toolchain", DisplayVersion(), "default"),
            new("edition", "5", "default"),
            new("std", Pipeline.StdlibRoot, "default"),
            new("manifest", manifest?.File ?? "(none: a file is a package of its own)", manifest is null ? "default" : "lyric.toml"),
        };

        // the C compiler, and zig apart from it: the one installed under the home, else the PATH's
        var cc = Environment.GetEnvironmentVariable("LYRIC_CC");
        var home = LyricHome.Root;
        var compiler = CCompiler.Locate(cc, home);
        var installed = CCompiler.InstalledZig(home);
        string Where(CCompiler found) => installed is not null && found.Path == installed.Path ? "installed" : "PATH";
        settings.Add(new("cc", compiler?.ToString() ?? "none found", cc is { Length: > 0 } ? "LYRIC_CC" : compiler is null ? "default" : Where(compiler)));
        var zig = installed ?? CCompiler.Locate(CCompilerKind.Zig);
        settings.Add(new("zig", zig?.ToString() ?? $"none found (lyric5 toolchain install zig installs {LyricHome.ZigVersion})", zig is null ? "default" : Where(zig)));
        var cache = Environment.GetEnvironmentVariable("LYRIC_CACHE");
        settings.Add(new("cache", UserCache.Root, cache is { Length: > 0 } ? "LYRIC_CACHE" : "default"));

        // the profile: its name, then each field from the rung that set it
        var fromEnvironment = Environment.GetEnvironmentVariable("LYRIC_PROFILE");
        var (name, nameOrigin) = values.TryGetValue("--profile", out var named) ? (named, "command line")
            : fromEnvironment is { Length: > 0 } ? (fromEnvironment, "LYRIC_PROFILE")
            : ("debug", "default");
        if (ProfileOf(values, parsed.Flags, manifest) is not { } profile) return 2;
        settings.Add(new("profile", profile.Name, nameOrigin));
        var builtIn = BuildProfile.BuiltIn.Contains(name) ? Profiles.Resolve(null, name) : null;
        var written = Profiles.Resolve(manifest, name);
        void Field(string field, string flag, Func<BuildProfile, string> read)
        {
            var origin = values.ContainsKey(flag) || parsed.Flags.Contains(flag) || parsed.Flags.Contains("--no-" + flag[2..]) ? "command line"
                : builtIn is null || read(written) != read(builtIn) ? "lyric.toml"
                : "default";
            settings.Add(new($"profile.{field}", read(profile), origin));
        }
        static string Bool(bool on) => on ? "true" : "false";
        Field("opt", "--opt", p => p.Opt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Field("gc", "--gc", p => p.Gc.ToString().ToLowerInvariant());
        Field("debugInfo", "--debug-info", p => Bool(p.DebugInfo));
        Field("lto", "--lto", p => Bool(p.Lto));
        Field("denyWarnings", "--deny-warnings", p => Bool(p.DenyWarnings));
        Field("overflowChecks", "--overflow-checks", p => Bool(p.OverflowChecks));
        Field("fastMath", "--fast-math", p => Bool(p.FastMath));

        Target target;
        try { target = values.TryGetValue("--target", out var triple) ? Target.Parse(triple) : Target.Host; }
        catch (ArgumentException)
        {
            return Unknown($"unknown target '{values["--target"]}': {string.Join(", ", Target.Tier1.Select(t => t.Triple))}");
        }
        settings.Add(new("target", target.Triple, values.ContainsKey("--target") ? "command line" : "default"));

        // each lint at the level a build of the package gives it: -A/-W/-D over [lints] over its own
        var manifestLints = manifest?.Lints ?? [];
        var levels = new LintSettings(lints.Rules, manifestLints);
        foreach (var (lintName, lint) in DiagnosticCatalog.Lints.OrderBy(l => l.Key, StringComparer.Ordinal))
        {
            var origin = new LintSettings(lints.Rules, []).LevelOf(lint).Origin is not null ? "command line"
                : new LintSettings([], manifestLints).LevelOf(lint).Origin is not null ? "lyric.toml"
                : "default";
            settings.Add(new($"lint.{lintName}", levels.LevelOf(lint).Level.ToString().ToLowerInvariant(), origin));
        }
        settings.Add(new("lint.warnDeps", lints.WarnDeps ? "true" : "false", lints.WarnDeps ? "command line" : "default"));

        // the variables of the family that are set, those read above among them
        foreach (System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
            if (variable.Key is string key && key.StartsWith("LYRIC_", StringComparison.Ordinal))
                settings.Add(new($"env.{key}", variable.Value as string ?? "", key));
        var variables = settings.Where(s => s.Name.StartsWith("env.", StringComparison.Ordinal)).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();
        settings = [.. settings.Where(s => !s.Name.StartsWith("env.", StringComparison.Ordinal)), .. variables];

        if (parsed.Flags.Contains("--json"))
        {
            using var stream = new MemoryStream();
            using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                w.WriteStartObject();
                w.WriteNumber("version", 1);
                w.WriteStartArray("settings");
                foreach (var s in settings)
                {
                    w.WriteStartObject();
                    w.WriteString("name", s.Name);
                    w.WriteString("value", s.Value);
                    w.WriteString("origin", s.Origin);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            Console.Out.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
            return 0;
        }
        var nameWidth = settings.Max(s => s.Name.Length) + 2;
        foreach (var s in settings)
            Console.Out.WriteLine($"{s.Name.PadRight(nameWidth)}{s.Value}  ({s.Origin})");
        return 0;
    }
}
