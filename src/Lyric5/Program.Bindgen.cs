using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5;

public static partial class Program
{
    // --- bindgen -------------------------------------------------------------------------------

    /// <summary>
    /// <c>lyric bindgen &lt;header&gt; [--target &lt;triple&gt;]</c> (spec 14 §2 rule 6): the module a
    /// <c>bindings</c> entry makes of the header, on standard output, to be kept under <c>src/</c>.
    /// Within a package the header is compiled with the package's <c>include</c> and <c>defines</c>
    /// for the target, and <c>@name/…</c> names a file of its source tree; outside one, with none.
    /// </summary>
    private static int BindgenVerb(string[] args)
    {
        if (Verbs.Read(Verbs.Find("bindgen")!, args, Unknown, out var refused) is not { } parsed) return refused;
        if (parsed.Words.Count != 1) return Unknown("'bindgen' takes one header: lyric5 bindgen native/geo.h");
        var values = parsed.Values;
        var given = parsed.Words[0];
        Target target;
        try { target = values.TryGetValue("--target", out var triple) ? Target.Parse(triple) : Target.Host; }
        catch (ArgumentException)
        {
            return Unknown($"unknown target '{values["--target"]}': {string.Join(", ", Target.Tier1.Select(t => t.Triple))}");
        }
        var start = Path.GetFullPath(values.GetValueOrDefault("-C") ?? Directory.GetCurrentDirectory());
        // zig cc reads a header for every target; clang for the host alone
        if ((CCompiler.Locate(CCompilerKind.Zig) ?? CCompiler.Locate()) is not { } compiler)
        {
            Say.Error(Console.Error, "LYR-BLD0001", "no C compiler: install zig, or set LYRIC_CC");
            return 2;
        }
        try
        {
            string header;
            List<string> include = [];
            IReadOnlyList<string> defines = [];
            if (Project.FindManifest(start) is { } file)
            {
                var manifest = Manifest.Read(file);
                var graph = Project.Resolve(manifest, parsed.Flags.Contains("--offline"));
                header = NativePart.TreePath(given) is not null
                    ? Bindings.Full(graph, manifest, given)
                    : Path.GetFullPath(given, start);
                include = Bindings.Include(graph, manifest, target);
                defines = manifest.NativeFor(target).Defines;
            }
            else if (NativePart.TreePath(given) is not null)
                return Unknown($"'{given}' names a source tree, and there is no lyric.toml here or above to hold it");
            else header = Path.GetFullPath(given, start);
            if (!File.Exists(header))
            {
                Say.Error(Console.Error, "LYR-CLI0001", $"no such header '{given}'");
                return 2;
            }
            var bound = Lyric5.Build.Bindgen.Make(compiler, target, header, include, defines, given, DisplayVersion());
            Console.Out.Write(bound.Text);
            return 0;
        }
        catch (BindgenException failed)
        {
            Say.Error(Console.Error, "LYR-BLD0005", $"the bindings of '{given}' for {target.Triple} cannot be made: {failed.Message}");
            return 1;
        }
        catch (ManifestException manifestRefused)
        {
            Say.Manifest(Console.Error, manifestRefused);
            return manifestRefused.Exit;
        }
    }
}
