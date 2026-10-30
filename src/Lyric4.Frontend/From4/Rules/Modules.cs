using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lyric4.AST;

namespace Lyric4.From4.Rules;

/// <summary>K01: <c>module a.b;</c> goes — Lyric 5 names a module by its path. Where a project's
/// header named it otherwise, the new name is said.</summary>
internal sealed class ModuleHeader : IRule
{
    public string Id => "K01";

    public void Apply(RuleContext context)
    {
        foreach (var module in context.Modules)
        {
            if (module.Header is not { } header) continue;
            var text = context.TextOf(module);
            var start = header.Span.Start;
            // back to the keyword (where the span does not start at it), forward over the ';' and the end of the line
            start = text.LastIndexOf("module", Math.Min(text.Length - 1, start + "module".Length - 1), StringComparison.Ordinal);
            if (start < 0) continue;
            var end = text.IndexOf(';', start);
            if (end < 0) continue;
            end++;
            if (end < text.Length && text[end] == '\r') end++;
            if (end < text.Length && text[end] == '\n') end++;
            if (start == 0 || text[start - 1] == '\n')
                while (end < text.Length && text[end] == '\n') end++; // the blank line under it
            context.Replace(Id, module.Span.File, start, end, "");

            if (context.LyricJson is not null && Root(context) is { } root)
            {
                var named = string.Join('.', header.Segments);
                var byPath = From4.ModuleNameUnder(root, context.PathOf(module));
                if (named != byPath)
                    context.Note(Id, "LYR-MIG0101", NoteKind.Review, header.Span,
                        $"the module was named '{named}' by its header; Lyric 5 names it by its path, '{byPath}'",
                        "the package's own imports of it are rewritten; another package that imports it names it anew");
            }
        }
    }

    private static string? Root(RuleContext context) => context.LyricJson is { } json ? Manifest.SourceRoot(json) : null;
}

/// <summary>
/// K02, K08: <c>lyric.json</c> becomes <c>lyric.toml</c> — the name, a version, the dependencies by
/// path. What has no counterpart is said: native roots become <c>[native]</c> by hand (review), a
/// source root other than <c>src</c> is the layout to move to, a test root becomes the package's
/// <c>tests/</c>, and the toolchain line of 4.x goes.
/// </summary>
internal sealed class Manifest : IRule
{
    public string Id => "K02";

    public void Apply(RuleContext context)
    {
        if (context.LyricJson is not { } file) return;
        var directory = Path.GetDirectoryName(file)!;
        using var json = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = json.RootElement;
        var name = root.TryGetProperty("name", out var n) && n.GetString() is { } given ? given : Path.GetFileName(directory);

        var toml = new StringBuilder();
        toml.Append("[package]\n").Append($"name = \"{name}\"\n").Append("version = \"0.1.0\"\n");
        if (root.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind == JsonValueKind.Object)
        {
            toml.Append("\n[dependencies]\n");
            foreach (var dependency in dependencies.EnumerateObject())
                toml.Append($"{dependency.Name} = {{ path = \"{dependency.Value.GetString()!.Replace('\\', '/')}\" }}\n");
        }
        context.Write(Path.Combine(directory, "lyric.toml"), toml.ToString());
        context.Remove(file);

        if (root.TryGetProperty("nativeRoots", out var native) && native.ValueKind == JsonValueKind.Object)
            foreach (var entry in native.EnumerateObject())
                context.Note(Id, "LYR-MIG0102", NoteKind.Review, file,
                    $"the native root '{entry.Name}' ({entry.Value.GetString()}) has no counterpart in lyric.toml",
                    "a library of C goes under [native]; Lyric 5 calls C through extern \"C\" declarations");
        var sourceRoot = SourceRoot(file);
        if (Path.GetRelativePath(directory, sourceRoot) is var relative && relative != "src")
            context.Note(Id, "LYR-MIG0103", NoteKind.Review, file,
                $"the modules lie under '{(relative == "." ? "the package's directory" : relative)}'; a Lyric 5 package keeps them under src/",
                relative == "." ? "move the .lyr files into src/" : $"rename '{relative}' to src/");
        if (root.TryGetProperty("testRoot", out var tests))
            context.Note("K08", "LYR-MIG0108", NoteKind.Hand, file,
                $"the tests under '{tests.GetString()}' are compiled apart in Lyric 4; Lyric 5 runs a package's tests/ with lyric test",
                "move them to tests/ and make each test an @Test function");
        if (root.TryGetProperty("toolchain", out var toolchain))
            context.Note(Id, "LYR-MIG0104", NoteKind.Hand, file,
                $"the toolchain line '{toolchain.GetString()}' names a version of Lyric 4",
                "[package] toolchain names the least Lyric 5 a package needs, where it needs one");
    }

    /// <summary>The package's name: the <c>name</c> a <c>lyric.json</c> gives, else its directory's.</summary>
    internal static string PackageName(string lyricJson)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(lyricJson), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return json.RootElement.TryGetProperty("name", out var n) && n.GetString() is { } given
            ? given
            : Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(lyricJson)))!;
    }

    /// <summary>The source root a <c>lyric.json</c> names, absolute; its directory where it names none.</summary>
    internal static string SourceRoot(string lyricJson)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(lyricJson))!;
        using var json = JsonDocument.Parse(File.ReadAllText(lyricJson), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return json.RootElement.TryGetProperty("sourceRoot", out var root) && root.GetString() is { } given
            ? Path.GetFullPath(Path.Combine(directory, given))
            : directory;
    }
}

/// <summary>K03, K04, K05: the imports. <c>std.io.console</c> and the other 4.x modules of the
/// table are named by their Lyric 5 module; what the prelude holds is no longer imported, and an
/// import left with nothing goes.</summary>
internal sealed partial class Imports : IRule
{
    public string Id => "K04";

    /// <summary>4.x module → Lyric 5 module (K03, K04).</summary>
    private static readonly Dictionary<string, string> Modules = new(StringComparer.Ordinal)
    {
        ["std.io.console"] = "std.io",
        ["std.io.file"] = "std.fs",
        ["std.io.stream"] = "std.io",
        ["std.io.net"] = "std.net",
        ["std.io.path"] = "std.path",
        ["std.io.error"] = "std.io",
        ["std.result"] = "std.core",
        ["std.option"] = "std.core",
        ["std.bytes"] = "std.encoding",
    };

    /// <summary>What Lyric 5's prelude (<c>stdlib5/std/prelude.lyr</c>) holds, by the 4.x module a
    /// program imported it from (K05).</summary>
    private static readonly Dictionary<string, HashSet<string>> Prelude = new(StringComparer.Ordinal)
    {
        ["std.core"] =
        [
            "panic", "assert", "unreachable", "todo", "Error", "Exception", "Result", "Box", "Ordering", "Range",
            "RangeInclusive", "RangeFrom", "RangeTo", "RangeFull", "Equatable", "Hashable", "Identity", "Ordered",
            "TotalOrder", "Display", "Debug", "Default", "Clone", "VariantName", "Variants", "Closeable", "Num",
            "Integer", "Float", "Iterator", "Iterable", "FromIterator", "Into", "From", "Index", "IndexSet", "Test",
            "Bench", "Deprecated", "AlwaysInline", "NoInline", "Cold", "MustUse", "NonExhaustive", "Layout", "Export",
            "Allow", "Warn", "Deny", "When", "Shared",
        ],
        ["std.test"] = ["Test"],
        ["std.collections"] = ["List", "Map", "Set"],
    };

    [GeneratedRegex(@"\Gimport\s+([A-Za-z_][A-Za-z0-9_]*(?:\s*\.\s*[A-Za-z_][A-Za-z0-9_]*)*)")]
    private static partial Regex PathOfImport();

    public void Apply(RuleContext context)
    {
        var own = OwnModules(context);
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            foreach (var import in module.Declarations.OfType<ImportDecl>())
            {
                var path = string.Join('.', import.Path);
                // K09: a module of the package itself is named with the package in front
                if (own.TryGetValue(path, out var renamed) && PathOfImport().Match(text, import.Span.Start) is { Success: true } local)
                {
                    var group = local.Groups[1];
                    context.Replace("K09", import.Span.File, group.Index, group.Index + group.Length, renamed);
                    continue;
                }
                if (Prelude.TryGetValue(path, out var prelude) && import.Clause is ImportSelective selective)
                {
                    var kept = selective.Names.Where(name => !prelude.Contains(name)).ToList();
                    if (kept.Count == 0)
                    {
                        context.Replace("K05", import.Span.File, import.Span.Start, LineEnd(text, import.Span.End), "");
                        continue;
                    }
                    if (kept.Count < selective.Names.Length)
                        context.Replace("K05", selective.Span, "{ " + string.Join(", ", kept) + " }");
                }
                if (Modules.TryGetValue(path, out var to) && PathOfImport().Match(text, import.Span.Start) is { Success: true } written)
                {
                    var group = written.Groups[1];
                    context.Replace(path == "std.io.console" ? "K03" : "K04", import.Span.File, group.Index, group.Index + group.Length, to);
                }
            }
        }
    }

    /// <summary>
    /// K09: the package's own modules, by the name 4.x registered them under — the header's, else
    /// the path's — to the name Lyric 5 gives them: the package's name, then the path under the
    /// source root (<c>app.util</c>). Empty for a single file, which is a package of its own.
    /// </summary>
    private static Dictionary<string, string> OwnModules(RuleContext context)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (context.LyricJson is not { } json) return names;
        var root = Manifest.SourceRoot(json);
        var package = Manifest.PackageName(json);
        foreach (var module in context.Modules)
        {
            var byPath = From4.ModuleNameUnder(root, context.PathOf(module));
            var registered = module.Header is { } header ? string.Join('.', header.Segments) : byPath;
            names[registered] = $"{package}.{byPath}";
        }
        return names;
    }

    /// <summary>The end of the import's line, its line break included.</summary>
    private static int LineEnd(string text, int end)
    {
        if (end < text.Length && text[end] == '\r') end++;
        if (end < text.Length && text[end] == '\n') end++;
        return end;
    }
}

/// <summary>K06: a declaration without <c>pub</c> was private to its module in Lyric 4 and is seen by
/// the whole package in Lyric 5 — said once a module, for review.</summary>
internal sealed class Visibility : IRule
{
    public string Id => "K06";

    public void Apply(RuleContext context)
    {
        // a single file is a package of its own: what it does not export, nothing else sees either way
        if (context.LyricJson is null) return;
        foreach (var module in context.Modules)
        {
            // 'main' seen by the whole package changes nothing
            var hidden = module.Declarations.Where(d => IsPublic(d) == false && d is not FunctionDecl { Name: "main" }).ToList();
            if (hidden.Count == 0) continue;
            context.Note(Id, "LYR-MIG0106", NoteKind.Review, hidden[0].Span,
                $"{hidden.Count} declaration{(hidden.Count == 1 ? "" : "s")} without 'pub' were private to the module in Lyric 4; in Lyric 5 the whole package sees them",
                "write 'private' where only the module may see one");
        }
    }

    /// <summary>Whether a declaration is <c>pub</c>; <c>null</c> for one that has no visibility.</summary>
    private static bool? IsPublic(Decl declaration) => declaration switch
    {
        FunctionDecl d => d.IsPublic,
        StaticBindingDecl d => d.IsPublic,
        StructDecl d => d.IsPublic,
        ClassDecl d => d.IsPublic,
        EnumDecl d => d.IsPublic,
        InterfaceDecl d => d.IsPublic,
        ExtendDecl d => d.IsPublic,
        GlobalBindingDecl d => d.IsPublic,
        TypeAliasDecl d => d.IsPublic,
        _ => null,
    };
}

/// <summary>K07: a <c>build.lyr</c> of Lyric 4 defined the build; Lyric 5 reads targets and profiles
/// from <c>lyric.toml</c>, and a build script only prepares.</summary>
internal sealed class BuildScript : IRule
{
    public string Id => "K07";

    public void Apply(RuleContext context)
    {
        if (context.LyricJson is not { } file) return;
        var script = Path.Combine(Path.GetDirectoryName(file)!, "build.lyr");
        if (File.Exists(script))
            context.Note(Id, "LYR-MIG0107", NoteKind.Review, script,
                "a Lyric 4 build.lyr defines the build; in Lyric 5 lyric.toml does ([[bin]], [profile.*]), and build.lyr only prepares sources",
                "move executable(…) and the profiles into lyric.toml; packed goes");
    }
}
