using System.Security.Cryptography;
using System.Text.Json;

namespace Lyric5.Toolchain;

/// <summary>
/// <c>toolchain.json</c> at the root of a toolchain's archive: its version, its target and every
/// other file in it with its size and SHA-256. An installation checks what it unpacked against it,
/// so a file the archive did not hold, or one that changed on the way, is found before the
/// toolchain runs.
/// </summary>
public static class ToolchainManifest
{
    public const string FileName = "toolchain.json";

    /// <summary>A file the manifest lists: its path with <c>/</c>, its size and its hash in hex.</summary>
    public sealed record Entry(string Path, long Size, string Sha256);

    /// <summary>Writes the manifest of the files under <paramref name="root"/> into it, the
    /// manifest itself left out, in the order of their paths.</summary>
    public static void Write(string root, string version, string triple)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("version", version);
            json.WriteString("triple", triple);
            json.WriteStartArray("files");
            foreach (var entry in Scan(root))
            {
                json.WriteStartObject();
                json.WriteString("path", entry.Path);
                json.WriteNumber("size", entry.Size);
                json.WriteString("sha256", entry.Sha256);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        File.WriteAllBytes(Path.Combine(root, FileName), stream.ToArray());
    }

    /// <summary>The manifest's version, target and files, read from <paramref name="root"/>.</summary>
    /// <exception cref="InvalidDataException">There is none, or it is not a manifest.</exception>
    public static (string Version, string Triple, IReadOnlyList<Entry> Files) Read(string root)
    {
        var path = Path.Combine(root, FileName);
        if (!File.Exists(path)) throw new InvalidDataException($"no {FileName} in {root}");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var top = document.RootElement;
            var files = new List<Entry>();
            foreach (var file in top.GetProperty("files").EnumerateArray())
                files.Add(new Entry(file.GetProperty("path").GetString()!, file.GetProperty("size").GetInt64(), file.GetProperty("sha256").GetString()!));
            return (top.GetProperty("version").GetString()!, top.GetProperty("triple").GetString()!, files);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException($"{path} is not a toolchain manifest: {e.Message}");
        }
    }

    /// <summary>What differs between the files under <paramref name="root"/> and its manifest, one
    /// line each — a file missing, one changed, one the manifest does not name; empty where they agree.</summary>
    public static IReadOnlyList<string> Verify(string root)
    {
        var (_, _, listed) = Read(root);
        var found = Scan(root).ToDictionary(e => e.Path, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var entry in listed)
        {
            if (!found.Remove(entry.Path, out var actual)) problems.Add($"missing: {entry.Path}");
            else if (actual.Size != entry.Size || actual.Sha256 != entry.Sha256) problems.Add($"changed: {entry.Path}");
        }
        foreach (var path in found.Keys.Order(StringComparer.Ordinal)) problems.Add($"not in the manifest: {path}");
        return problems;
    }

    private static IEnumerable<Entry> Scan(string root)
    {
        var full = Path.GetFullPath(root);
        return Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(full, file).Replace('\\', '/'))
            .Where(path => path != FileName)
            .Order(StringComparer.Ordinal)
            .Select(path =>
            {
                using var stream = File.OpenRead(Path.Combine(full, path));
                var hash = SHA256.HashData(stream);
                return new Entry(path, stream.Length, Convert.ToHexStringLower(hash));
            });
    }
}
