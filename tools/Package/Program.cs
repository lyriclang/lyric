using Lyric5.Build;
using Lyric5.Toolchain;

// package <published toolchain> --version <version> [--triple <triple>]
// package verify <unpacked toolchain>
//
// Builds the runtime for the toolchain's target — the host's unless --triple names another — in the
// debug and release profiles with the default collector, as a build would build it, and lays each
// archive under runtime/lib/<triple>/<profile>/ with the key a build compares (RuntimeArchive.Prebuilt).
// The C compiler is zig, the one a user installs beside the toolchain; another compiler's archive
// would stand for nobody's build. Then writes toolchain.json over everything in the directory.
//
// verify checks an unpacked archive against its toolchain.json and names what differs; exit 1 on a finding.

if (args is ["verify", var unpacked])
{
    var problems = ToolchainManifest.Verify(unpacked);
    foreach (var problem in problems) Console.Error.WriteLine($"package: {problem}");
    if (problems.Count == 0) Console.WriteLine($"{ToolchainManifest.FileName}: every file as listed");
    return problems.Count == 0 ? 0 : 1;
}

const string Usage = "usage: package <published toolchain> --version <version> [--triple <triple>]";
string? dir = null, version = null, triple = null;
for (var i = 0; i < args.Length; i++)
{
    var value = i + 1 < args.Length ? args[i + 1] : null;
    switch (args[i])
    {
        case "--version" when value is not null: version = value; i++; break;
        case "--triple" when value is not null: triple = value; i++; break;
        case var word when !word.StartsWith('-') && dir is null: dir = word; break;
        default: Console.Error.WriteLine(Usage); return 2;
    }
}
if (dir is null || version is null) { Console.Error.WriteLine(Usage); return 2; }

var root = Path.GetFullPath(dir);
if (!Directory.Exists(Path.Combine(root, "runtime", "include", "lyr")))
{
    Console.Error.WriteLine($"package: {root} carries no runtime source");
    return 2;
}
if (CCompiler.Locate() is not { Kind: CCompilerKind.Zig } compiler)
{
    Console.Error.WriteLine("package: the prebuilt runtime is built with zig, and none was found (LYRIC_CC, PATH)");
    return 2;
}
var target = triple is null ? Target.Host : Target.Parse(triple);
var scratch = Path.Combine(Path.GetTempPath(), $"lyric-package-{Environment.ProcessId}");
try
{
    foreach (var name in new[] { "debug", "release" })
    {
        var profile = BuildProfile.Named(name)!;
        // portable: named as a target, zig builds for the architecture's baseline, not for the CPU of
        // the machine that packages
        var built = RuntimeLayout.BuildArchive(new CBuild(compiler, target, profile, scratch, portable: true), root, Path.Combine(scratch, "lib"));
        var into = RuntimeArchive.PrebuiltDir(root, target, profile);
        Directory.CreateDirectory(into);
        File.Copy(built, Path.Combine(into, "liblyr.a"), overwrite: true);
        File.WriteAllText(Path.Combine(into, RuntimeArchive.PrebuiltKeyFile), RuntimeArchive.PrebuiltKey(compiler, target, profile) + "\n");
        Console.WriteLine($"runtime/lib/{target.Triple}/{name}/liblyr.a ({new FileInfo(built).Length} bytes, zig {compiler.Version})");
    }
}
catch (CBuildException e)
{
    Console.Error.WriteLine($"package: {e.Message}");
    return 1;
}
finally
{
    try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
}

ToolchainManifest.Write(root, version, target.Triple);
Console.WriteLine($"{ToolchainManifest.FileName}: {ToolchainManifest.Read(root).Files.Count} files");
return 0;
