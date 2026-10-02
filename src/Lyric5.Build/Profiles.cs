using System.Text.RegularExpressions;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>A <c>[profile.&lt;name&gt;]</c> table (design/v5/spec/11 W2 P3): the fields it sets —
/// <c>null</c> where it sets none — and, for a profile of the manifest's own, the one it inherits
/// from.</summary>
public sealed record ProfileSpec(string Name, string? Inherits, int? Opt, bool? DebugInfo, bool? Lto,
    bool? DenyWarnings, bool? OverflowChecks, bool? FastMath, int Line);

/// <summary>
/// The profiles of a program (design/v5/spec/11 W2 P3): the four built in, changed by the root
/// manifest's <c>[profile.debug]</c> and the like, and the ones the root manifest names, each over
/// the profile it inherits from. A dependency's profiles are not read: the program is built one way.
/// </summary>
public static partial class Profiles
{
    /// <summary>The fields a profile table may set.</summary>
    public static readonly string[] Fields = ["opt", "debugInfo", "lto", "denyWarnings", "overflowChecks", "fastMath"];

    /// <summary>The profile named <paramref name="name"/>: built in, or the manifest's.</summary>
    /// <exception cref="ArgumentException">No profile has that name.</exception>
    public static BuildProfile Resolve(Manifest? manifest, string name)
    {
        var specs = manifest?.Profiles ?? new Dictionary<string, ProfileSpec>();
        var builtIn = BuildProfile.Named(name);
        if (builtIn is null && !specs.ContainsKey(name))
            throw new ArgumentException($"unknown profile '{name}': {string.Join(", ", Known(manifest))}");
        if (!specs.TryGetValue(name, out var spec)) return builtIn!;
        // A built-in changed by the manifest, or the manifest's own over what it inherits from
        // (the manifest's check refused a chain that does not end in a built-in).
        var over = builtIn ?? (Resolve(manifest, spec.Inherits!) with { Name = name });
        return over with
        {
            Opt = spec.Opt ?? over.Opt,
            DebugInfo = spec.DebugInfo ?? over.DebugInfo,
            Lto = spec.Lto ?? over.Lto,
            DenyWarnings = spec.DenyWarnings ?? over.DenyWarnings,
            OverflowChecks = spec.OverflowChecks ?? over.OverflowChecks,
            FastMath = spec.FastMath ?? over.FastMath,
        };
    }

    /// <summary>Every profile name the program knows, the built-in ones first.</summary>
    public static IEnumerable<string> Known(Manifest? manifest) =>
        BuildProfile.BuiltIn.Concat((manifest?.Profiles.Keys ?? Enumerable.Empty<string>())
            .Where(n => !BuildProfile.BuiltIn.Contains(n)).Order(StringComparer.Ordinal));

    /// <summary><c>[profile]</c>'s tables, checked: a name, the fields with their types, a manifest's
    /// own profile inheriting from one that exists, no built-in inheriting, no circle.</summary>
    /// <exception cref="ManifestException">A table is not a profile.</exception>
    public static Dictionary<string, ProfileSpec> Read(TomlTable document, string file)
    {
        var specs = new Dictionary<string, ProfileSpec>(StringComparer.Ordinal);
        if (!document.TryGet("profile", out var found)) return specs;
        if (found is not TomlTable profiles)
            throw new ManifestException("LYR-PKG0002", file, 1, 1, "[profile] is a table of profiles: [profile.release]");
        foreach (var name in profiles.Keys)
        {
            profiles.TryGet(name, out var value);
            if (value is not TomlTable table)
                throw new ManifestException("LYR-PKG0002", file, profiles.Line, 1, $"'{name}' is no profile: [profile.{name}] is a table");
            if (!NamePattern().IsMatch(name))
                throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'{name}' is no profile name: a lowercase letter, then letters, digits, '_' and '-'");
            foreach (var key in table.Keys)
                if (!Fields.Contains(key) && key != "inherits")
                    throw new ManifestException("LYR-PKG0003", file, table.Line, 1, $"[profile.{name}] has no key '{key}'");
            int? opt = null;
            if (table.TryGet("opt", out var level))
            {
                if (level is not long n || n is < 0 or > 3)
                    throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'opt' of [profile.{name}] is a level from 0 to 3");
                opt = (int)n;
            }
            bool? Flag(string key)
            {
                if (!table.TryGet(key, out var given)) return null;
                return given is bool b
                    ? b
                    : throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'{key}' of [profile.{name}] is true or false");
            }
            string? inherits = null;
            if (table.TryGet("inherits", out var parent))
                inherits = parent as string
                    ?? throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"'inherits' of [profile.{name}] names a profile");
            var builtIn = BuildProfile.BuiltIn.Contains(name);
            if (builtIn && inherits is not null)
                throw new ManifestException("LYR-PKG0002", file, table.Line, 1, $"[profile.{name}] is built in and inherits from nothing");
            if (!builtIn && inherits is null)
                throw new ManifestException("LYR-PKG0002", file, table.Line, 1,
                    $"[profile.{name}] is the manifest's own and inherits from a profile: inherits = \"release\"");
            specs[name] = new ProfileSpec(name, inherits, opt, Flag("debugInfo"), Flag("lto"), Flag("denyWarnings"),
                Flag("overflowChecks"), Flag("fastMath"), table.Line);
        }
        // Every chain ends in a built-in profile.
        foreach (var spec in specs.Values.Where(s => s.Inherits is not null))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal) { spec.Name };
            for (var at = spec; at.Inherits is { } next; at = specs[next])
            {
                if (BuildProfile.BuiltIn.Contains(next)) break;
                if (!specs.ContainsKey(next))
                    throw new ManifestException("LYR-PKG0002", file, at.Line, 1, $"[profile.{at.Name}] inherits from '{next}', which is no profile");
                if (!seen.Add(next))
                    throw new ManifestException("LYR-PKG0002", file, spec.Line, 1, $"[profile.{spec.Name}] inherits from itself, through '{next}'");
            }
        }
        return specs;
    }

    [GeneratedRegex("^[a-z][a-z0-9_-]*$")]
    private static partial Regex NamePattern();
}
