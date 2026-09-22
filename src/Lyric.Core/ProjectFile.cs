using System.Text.Json;

namespace Lyric.Core;

/// <summary>A <c>lyric.json</c> that could not be understood, or one this toolchain cannot serve.
/// Carries the path, because the file a tool complains about is rarely the one it was pointed
/// at, and the code, because "edit the file" and "upgrade the toolchain" are different
/// advice.</summary>
public sealed class ProjectFileException(string path, string message,
    string code = CliDiagnostics.BadProjectFile) : Exception(message)
{
    public string Path { get; } = path;

    /// <summary>The <c>LYR-CLI</c> code a tool reports this under.</summary>
    public string Code { get; } = code;
}

/// <summary>
/// What a project says about itself: read by every tool, executed by none.
///
/// <para>The counterpart of a build script. A script answers "what should be built" and only a build
/// can ask it; this answers "what is this project" — where the modules live, which segments belong
/// to a host, which other projects it stands on — and a language server has to know that without
/// running anything.</para>
///
/// <para>Everything here is OPTIONAL, and a project without the file behaves exactly as one did
/// before it existed: the directory of the entry file is the module root and there are no native
/// roots and no dependencies. That is what makes this an addition rather than a new requirement.</para>
///
/// <para>It lives in <c>Lyric.Core</c> rather than beside the compiler because the driver reads
/// it too, and the driver references no compiler. The runtime never reads it: a runtime consumes
/// bytecode.</para>
/// </summary>
public sealed record ProjectFile
{
    /// <summary>The file name searched for, upwards from the entry file.</summary>
    public const string FileName = "lyric.json";

    /// <summary>Where the file was found. Every path it names is relative to this.</summary>
    public required string Directory { get; init; }

    /// <summary>The project's name: the <c>name</c> key, or the directory's name when the file
    /// names none. What an implicit build calls its artifact and what a library's module file is
    /// called.</summary>
    public required string Name { get; init; }

    /// <summary>Where the program's own modules live, absolute. Defaults to
    /// <see cref="Directory"/>.</summary>
    public required string SourceRoot { get; init; }

    /// <summary>Where the tests live, absolute — the directory only <c>lyric test</c> compiles.
    /// <c>null</c> when the file names none; the runner then tries <c>tests/</c> under
    /// <see cref="Directory"/> and treats its absence as "no tests".</summary>
    public string? TestRoot { get; init; }

    /// <summary>The minimum toolchain version the file names, as written, or <c>null</c>. A
    /// file whose minimum this toolchain does not reach is refused at the read, so the value here
    /// is always one this toolchain satisfies.</summary>
    public string? Toolchain { get; init; }

    /// <summary>Module path segment to directory, absolute: the project's own native roots and
    /// those every dependency brings. Empty when nobody names one.</summary>
    public required IReadOnlyDictionary<string, string> NativeRoots { get; init; }

    /// <summary>
    /// Module path segment to the SOURCE ROOT of the project that provides it, absolute — the
    /// closure over every dependency's own dependencies, flattened.
    ///
    /// <para>A dependency owns its first segment the way a native root does: <c>geometry</c> and
    /// everything under it come from that project, and the segment is taken out of this project's
    /// own root. The value is the dependency's source root, so <c>import geometry.shapes</c> reads
    /// <c>&lt;root&gt;/geometry/shapes.lyr</c>, the same derivation as for <c>std</c>.</para>
    /// </summary>
    public required IReadOnlyDictionary<string, string> Dependencies { get; init; }

    /// <summary>What was tolerated rather than rejected — an unknown key, most of the time. A caller
    /// prints these; ignoring them silently is how a typo becomes an afternoon.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Looks for <see cref="FileName"/> in <paramref name="startDirectory"/> and upwards, and reads
    /// the first one found. <c>null</c> when there is none.
    /// </summary>
    /// <exception cref="ProjectFileException">A file was found and could not be understood. Not
    /// finding one is normal; finding a broken one is not.</exception>
    public static ProjectFile? Discover(string startDirectory)
    {
        var directory = new DirectoryInfo(System.IO.Path.GetFullPath(startDirectory));

        while (directory is not null)
        {
            var candidate = System.IO.Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate)) return Read(candidate);
            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Reads one file, without searching, and resolves the projects it depends on.</summary>
    public static ProjectFile Read(string path)
    {
        var root = Manifest.Read(System.IO.Path.GetFullPath(path));
        return Resolve(root);
    }

    // ------------------------------------------------------------------------ the closure

    /// <summary>
    /// The flat table every tool works from: this project's roots plus what its dependencies
    /// bring, transitively.
    ///
    /// <para>FLAT, because the module namespace is flat: an import names a segment and nothing
    /// says which project asked. So one segment maps to one directory for the whole program,
    /// and two projects that want it to mean different things is a conflict named with both
    /// requesters — except that THIS project's own entry overrides whatever a dependency says,
    /// which is how a root pins a version everybody under it then shares (Go's <c>replace</c>,
    /// Cargo's <c>[patch]</c>).</para>
    ///
    /// <para>A dependency's own <c>nativeRoots</c> join the table too: an SDK that ships as a
    /// project brings its native segments along. Nothing a dependency says about its tests
    /// travels; those are its own.</para>
    /// </summary>
    private static ProjectFile Resolve(Manifest root)
    {
        // Segment -> (directory, who said so). The root's entries go in first and are pinned.
        var dependencies = new Dictionary<string, (string Root, string By)>(StringComparer.Ordinal);
        var natives = new Dictionary<string, (string Root, string By)>(StringComparer.Ordinal);

        foreach (var (segment, directory) in root.NativeRoots)
            natives[segment] = (directory, root.Path);

        var pending = new Queue<(string Segment, string Directory, string By)>();
        foreach (var (segment, directory) in root.Dependencies)
            pending.Enqueue((segment, directory, root.Path));

        // Projects already read, by directory: a diamond is read once, and a cycle ends.
        var visited = new HashSet<string>(PathComparer) { root.Directory };

        while (pending.Count > 0)
        {
            var (segment, directory, by) = pending.Dequeue();
            var manifest = Manifest.ReadOrDefault(directory);

            if (natives.ContainsKey(segment))
                throw new ProjectFileException(by,
                    $"'{segment}' is a dependency here and a native root in {natives[segment].By}; "
                    + "a segment belongs to one root");

            if (dependencies.TryGetValue(segment, out var existing))
            {
                // Pinned by the root, or the same project reached twice: nothing to decide.
                if (existing.By == root.Path || PathComparer.Equals(existing.Root, manifest.SourceRoot))
                    goto Descend;

                throw new ProjectFileException(by,
                    $"'{segment}' is required at {manifest.SourceRoot} by {by} and at "
                    + $"{existing.Root} by {existing.By}; one segment, one root — name it in the "
                    + "project's own lyric.json to decide");
            }

            dependencies[segment] = (manifest.SourceRoot, by);

            Descend:
            if (!visited.Add(manifest.Directory)) continue;

            foreach (var (nativeSegment, nativeDirectory) in manifest.NativeRoots)
            {
                if (dependencies.ContainsKey(nativeSegment))
                    throw new ProjectFileException(manifest.Path,
                        $"'{nativeSegment}' is a native root here and a dependency in "
                        + $"{dependencies[nativeSegment].By}; a segment belongs to one root");
                if (natives.TryGetValue(nativeSegment, out var other)
                    && !PathComparer.Equals(other.Root, nativeDirectory))
                    throw new ProjectFileException(manifest.Path,
                        $"'{nativeSegment}' is a native root at {nativeDirectory} here and at "
                        + $"{other.Root} in {other.By}; a segment belongs to one root");
                natives.TryAdd(nativeSegment, (nativeDirectory, manifest.Path));
            }

            foreach (var (innerSegment, innerDirectory) in manifest.Dependencies)
                pending.Enqueue((innerSegment, innerDirectory, manifest.Path));
        }

        return new ProjectFile
        {
            Directory = root.Directory,
            Name = root.Name,
            SourceRoot = root.SourceRoot,
            TestRoot = root.TestRoot,
            Toolchain = root.Toolchain,
            NativeRoots = natives.ToDictionary(e => e.Key, e => e.Value.Root, StringComparer.Ordinal),
            Dependencies = dependencies.ToDictionary(e => e.Key, e => e.Value.Root, StringComparer.Ordinal),
            Warnings = root.Warnings,
        };
    }

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // ------------------------------------------------------------------------ one file

    /// <summary>One <c>lyric.json</c> as written, before the closure: its own roots, its own
    /// dependencies as segment to PROJECT directory, its own warnings.</summary>
    private sealed record Manifest(
        string Path,
        string Directory,
        string Name,
        string SourceRoot,
        string? TestRoot,
        string? Toolchain,
        IReadOnlyDictionary<string, string> NativeRoots,
        IReadOnlyDictionary<string, string> Dependencies,
        IReadOnlyList<string> Warnings)
    {
        /// <summary>The project at <paramref name="directory"/>: its file when it has one, and
        /// the bare defaults when it has none — a directory of modules is a project with its
        /// directory as the root, which is what every project was before the file existed.</summary>
        public static Manifest ReadOrDefault(string directory)
        {
            var file = System.IO.Path.Combine(directory, FileName);
            if (File.Exists(file)) return Read(file);

            var empty = new Dictionary<string, string>(StringComparer.Ordinal);
            return new Manifest(file, directory, System.IO.Path.GetFileName(directory), directory,
                null, null, empty, empty, []);
        }

        public static Manifest Read(string full)
        {
            var directory = System.IO.Path.GetDirectoryName(full)!;

            string text;
            try
            {
                text = File.ReadAllText(full);
            }
            catch (IOException io)
            {
                throw new ProjectFileException(full, io.Message);
            }

            // Comments and trailing commas are allowed: this is a file people edit by hand, and the
            // usual objection to JSON as a project format is exactly those two.
            var options = new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text, options);
            }
            catch (JsonException json)
            {
                throw new ProjectFileException(full, json.Message);
            }

            using (document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                    throw new ProjectFileException(full, "the file has to hold an object");

                var warnings = new List<string>();
                var name = System.IO.Path.GetFileName(directory);
                var sourceRoot = directory;
                string? testRoot = null;
                string? toolchain = null;
                var nativeRoots = new Dictionary<string, string>(StringComparer.Ordinal);
                var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    switch (property.Name)
                    {
                        case "name":
                            name = ReadName(full, property.Value);
                            break;

                        case "sourceRoot":
                            sourceRoot = Resolve(full, directory, "sourceRoot", property.Value);
                            break;

                        case "nativeRoots":
                            ReadRoots(full, directory, property.Value, nativeRoots, "nativeRoots",
                                "a native root owns one segment, and the compiler looks it up by "
                                + "the first segment of an import");
                            break;

                        case "dependencies":
                            ReadRoots(full, directory, property.Value, dependencies, "dependencies",
                                "a dependency owns one segment, and the compiler looks it up by "
                                + "the first segment of an import");
                            break;

                        case "testRoot":
                            testRoot = Resolve(full, directory, "testRoot", property.Value);
                            break;

                        case "toolchain":
                            toolchain = ReadToolchain(full, property.Value);
                            break;

                        // Tolerated rather than rejected, for the same reason the bytecode reader skips
                        // a section it does not know: a file written for a later version has to stay
                        // readable. The warning is what keeps a typo from being silent.
                        default:
                            warnings.Add($"unknown key '{property.Name}'");
                            break;
                    }
                }

                foreach (var segment in dependencies.Keys)
                    if (nativeRoots.ContainsKey(segment))
                        throw new ProjectFileException(full,
                            $"'{segment}' is named under both 'dependencies' and 'nativeRoots'; a "
                            + "segment belongs to one root");

                return new Manifest(full, directory, name, sourceRoot, testRoot, toolchain,
                    nativeRoots, dependencies, warnings);
            }
        }

        private static string ReadName(string file, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String)
                throw new ProjectFileException(file, "'name' has to be a string");

            var name = value.GetString()!;
            // It becomes a module name and a file name: the rule 'lyric new' applies to a project
            // name, applied to the one a file writes down.
            if (name.Length == 0 || char.IsAsciiDigit(name[0])
                || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                throw new ProjectFileException(file,
                    $"'name' is '{name}', which is not a module name: letters, digits and '_', "
                    + "not starting with a digit");
            return name;
        }

        /// <summary>The minimum toolchain, checked at the read: a project this toolchain cannot
        /// serve is refused with the two numbers, not compiled against a language it was not
        /// written for.</summary>
        private static string ReadToolchain(string file, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String)
                throw new ProjectFileException(file, "'toolchain' has to be a string like \"4.5\"");

            var minimum = value.GetString()!;
            if (!ToolchainVersion.TryParse(minimum, out _))
                throw new ProjectFileException(file,
                    $"'toolchain' is '{minimum}', which is not a version like 4.5 or 4.5.0");

            if (!ToolchainVersion.Satisfies(minimum))
                throw new ProjectFileException(file,
                    $"this project needs toolchain {minimum}, and this is {ToolchainVersion.Value}",
                    CliDiagnostics.ToolchainTooOld);

            return minimum;
        }

        /// <summary>A segment-to-directory table: the shape <c>nativeRoots</c> has had since 1.2,
        /// and <c>dependencies</c> shares.</summary>
        private static void ReadRoots(string file, string directory, JsonElement element,
            Dictionary<string, string> into, string key, string why)
        {
            if (element.ValueKind != JsonValueKind.Object)
                throw new ProjectFileException(file,
                    $"'{key}' has to be an object of segment to directory");

            foreach (var entry in element.EnumerateObject())
            {
                // The loader keys roots by the FIRST segment of a module path, so a key with a
                // dot in it would name something that can never be looked up.
                if (entry.Name.Length == 0 || entry.Name.Contains('.'))
                    throw new ProjectFileException(file,
                        $"'{entry.Name}' is not a module path segment: {why}");

                // 'std' belongs to the standard library, and nothing in a project may take its
                // place — the rule guide 12 states for a directory, applied to a key.
                if (entry.Name == "std")
                    throw new ProjectFileException(file,
                        $"'std' cannot be a {key} segment: it belongs to the standard library");

                if (!into.TryAdd(entry.Name, Resolve(file, directory, $"{key}.{entry.Name}", entry.Value)))
                    throw new ProjectFileException(file, $"'{entry.Name}' is named twice");
            }
        }

        /// <summary>A path from the file, made absolute against the file's own directory and checked
        /// to exist. A root that is not there is a mistake worth naming now rather than as "cannot
        /// find module" later.</summary>
        private static string Resolve(string file, string directory, string key, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String)
                throw new ProjectFileException(file, $"'{key}' has to be a string");

            var raw = value.GetString()!;
            string resolved;
            try
            {
                resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(directory, raw));
            }
            catch (ArgumentException)
            {
                throw new ProjectFileException(file, $"'{key}' is not a usable path: '{raw}'");
            }

            if (!System.IO.Directory.Exists(resolved))
                throw new ProjectFileException(file, $"'{key}' names '{raw}', which is not a directory");

            return resolved;
        }
    }
}
