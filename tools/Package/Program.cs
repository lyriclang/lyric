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
//
// package index --channel <channel> --version <version> --date <yyyy-mm-dd> --sequence <n>
//               --archives <directory> --out <file> [--previous <file>] [--keep <n>]
//
// index writes a channel's index: the archives in the directory as the newest version, then what the
// previous index offers, its signature checked first, <n> versions at most. It signs nothing.

if (args is ["index", .. var indexing]) return WriteIndex(indexing);

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

static int WriteIndex(string[] options)
{
    const string Usage = "usage: package index --channel <channel> --version <version> --date <yyyy-mm-dd> --sequence <n> --archives <directory> --out <file> [--previous <file>] [--keep <n>]";
    var named = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 0; i < options.Length; i += 2)
    {
        if (i + 1 >= options.Length || options[i] is not ("--channel" or "--version" or "--date" or "--sequence" or "--archives" or "--out" or "--previous" or "--keep"))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        named[options[i]] = options[i + 1];
    }
    if (new[] { "--channel", "--version", "--date", "--sequence", "--archives", "--out" }.Any(o => !named.ContainsKey(o)) || !long.TryParse(named["--sequence"], out var sequence))
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
    var keep = 1;
    if (named.TryGetValue("--keep", out var kept) && (!int.TryParse(kept, out keep) || keep < 1))
    {
        Console.Error.WriteLine(Usage);
        return 2;
    }
    try
    {
        var channel = named["--channel"];
        ChannelIndex? previous = null;
        if (named.TryGetValue("--previous", out var before))
            previous = ChannelIndex.Read(File.ReadAllBytes(before), File.ReadAllText(before + ".minisig"), ToolchainFetch.KeysOf(channel), channel);
        var index = ChannelIndex.Next(previous, channel, ChannelIndex.ReleaseFrom(named["--archives"], named["--version"], named["--date"]), sequence, keep);
        File.WriteAllBytes(named["--out"], index.ToJson());
        Console.WriteLine($"{named["--out"]}: number {sequence}, {index.Releases.Count} version(s), the newest {index.Newest!.Version} with {index.Newest.Archives.Count} archives");
        return 0;
    }
    catch (Exception e) when (e is InvalidDataException or SignatureException or IOException)
    {
        Console.Error.WriteLine($"package: {e.Message}");
        return 1;
    }
}
