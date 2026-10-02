using System.Text.Json;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>
/// <c>lyric metadata</c> (design/v5/spec/11 W2 P10): a package as one JSON object for the tools
/// around it — its graph, its programs, its profiles, the targets, where <c>out/</c> is. Its
/// <c>"version"</c> is the schema's (C10): a field is added within a version, none is changed or
/// removed.
/// </summary>
public static class Metadata
{
    public const int SchemaVersion = 1;

    /// <summary>The object, on one line.</summary>
    /// <param name="programs">The package's programs; none for a library.</param>
    public static string Json(PackageGraph graph, IReadOnlyList<Project> programs, string toolchain)
    {
        var root = graph.Root;
        using var output = new MemoryStream();
        using (var json = new Utf8JsonWriter(output))
        {
            json.WriteStartObject();
            json.WriteNumber("version", SchemaVersion);
            json.WriteString("toolchain", toolchain);
            json.WriteString("root", root.Name);

            json.WriteStartArray("packages");
            foreach (var (name, manifest) in graph.Packages.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                json.WriteStartObject();
                json.WriteString("name", name);
                json.WriteString("version", manifest.Version);
                json.WriteString("edition", manifest.Edition);
                json.WriteString("manifest", manifest.File);
                if (manifest.Toolchain is { } pin) json.WriteString("toolchain", pin.Minimum.ToString());
                if (graph.Revisions.TryGetValue(name, out var revision))
                {
                    json.WriteStartObject("git");
                    json.WriteString("url", revision.Git.Url);
                    var key = revision.Git.Kind switch { GitRefKind.Tag => "tag", GitRefKind.Branch => "branch", GitRefKind.Rev => "rev", _ => null };
                    if (key is not null) json.WriteString(key, revision.Git.Ref);
                    json.WriteString("commit", revision.Commit);
                    json.WriteEndObject();
                }
                else json.WriteString("path", manifest.Root);
                json.WriteStartArray("dependencies");
                foreach (var dependency in manifest.Dependencies.Select(d => d.Name).Order(StringComparer.Ordinal))
                    json.WriteStringValue(dependency);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("programs");
            foreach (var program in programs)
            {
                json.WriteStartObject();
                json.WriteString("name", program.BinaryName);
                json.WriteString("entry", program.Source);
                json.WriteString("module", program.Module);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("profiles");
            foreach (var profile in Profiles.Known(root).Select(name => Profiles.Resolve(root, name)))
            {
                json.WriteStartObject();
                json.WriteString("name", profile.Name);
                json.WriteString("base", profile.Base.Name());
                json.WriteNumber("opt", profile.Opt);
                json.WriteBoolean("debugInfo", profile.DebugInfo);
                json.WriteBoolean("lto", profile.Lto);
                json.WriteBoolean("denyWarnings", profile.DenyWarnings);
                json.WriteBoolean("overflowChecks", profile.OverflowChecks);
                json.WriteBoolean("fastMath", profile.FastMath);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteStartArray("targets");
            foreach (var target in Target.Tier1) json.WriteStringValue(target.Triple);
            json.WriteEndArray();
            json.WriteString("host", Target.Host.Triple);
            json.WriteString("out", Path.Combine(root.Root, "out"));
            json.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }
}
