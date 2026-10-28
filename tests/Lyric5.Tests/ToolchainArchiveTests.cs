using System.Diagnostics;
using Lyric5.Build;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>What a toolchain's archive carries beside the binary: the runtime prebuilt for its own
/// target, the manifest an installation checks, the file that loads Lyric's values into lldb.</summary>
public class ToolchainArchiveTests
{
    private static readonly Target Linux = Target.Parse("x86_64-linux-gnu");
    private static readonly CCompiler Packaged = new(CCompilerKind.Zig, "/runner/zig/zig", "0.15.1");

    private static string FakeToolchain(BuildProfile profile)
    {
        var root = Directory.CreateTempSubdirectory("lyric-prebuilt-").FullName;
        var dir = RuntimeArchive.PrebuiltDir(root, Linux, profile);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "liblyr.a"), "!<arch>\n");
        File.WriteAllText(Path.Combine(dir, RuntimeArchive.PrebuiltKeyFile), RuntimeArchive.PrebuiltKey(Packaged, Linux, profile) + "\n");
        return root;
    }

    [Fact]
    public void A_prebuilt_runtime_stands_for_a_build_with_the_same_compiler_version_wherever_it_lies()
    {
        var debug = BuildProfile.Named("debug")!;
        var root = FakeToolchain(debug);
        var elsewhere = Packaged with { Path = "/home/someone/.lyric/zig/0.15.1/zig" };
        Assert.Equal(Path.Combine(RuntimeArchive.PrebuiltDir(root, Linux, debug), "liblyr.a"), RuntimeArchive.Prebuilt(root, elsewhere, Linux, debug));
    }

    [Fact]
    public void A_prebuilt_runtime_built_another_way_stands_for_nothing()
    {
        var debug = BuildProfile.Named("debug")!;
        var root = FakeToolchain(debug);
        Assert.Null(RuntimeArchive.Prebuilt(root, Packaged with { Version = "0.16.0" }, Linux, debug));
        Assert.Null(RuntimeArchive.Prebuilt(root, Packaged with { Kind = CCompilerKind.Clang }, Linux, debug));
        Assert.Null(RuntimeArchive.Prebuilt(root, Packaged, Linux, debug with { Gc = Collector.Boehm }));
        Assert.Null(RuntimeArchive.Prebuilt(root, Packaged, Linux, BuildProfile.Named("release")!));
        Assert.Null(RuntimeArchive.Prebuilt(root, Packaged, Target.Parse("aarch64-linux-gnu"), debug));
        File.Delete(Path.Combine(RuntimeArchive.PrebuiltDir(root, Linux, debug), RuntimeArchive.PrebuiltKeyFile));
        Assert.Null(RuntimeArchive.Prebuilt(root, Packaged, Linux, debug));
    }

    [Fact]
    public void The_manifest_names_every_file_and_finds_what_changed()
    {
        var root = Directory.CreateTempSubdirectory("lyric-manifest-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "stdlib5", "std"));
        File.WriteAllText(Path.Combine(root, "lyric5"), "binary");
        File.WriteAllText(Path.Combine(root, "stdlib5", "std", "io.lyr"), "module");
        ToolchainManifest.Write(root, "5.0.0-dev.20261028+abcdef0", "x86_64-linux-gnu");

        var (version, triple, files) = ToolchainManifest.Read(root);
        Assert.Equal("5.0.0-dev.20261028+abcdef0", version);
        Assert.Equal("x86_64-linux-gnu", triple);
        Assert.Equal(["lyric5", "stdlib5/std/io.lyr"], files.Select(f => f.Path));
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData("binary"u8)), files[0].Sha256);
        Assert.Equal(6, files[0].Size);
        Assert.Empty(ToolchainManifest.Verify(root));

        File.WriteAllText(Path.Combine(root, "lyric5"), "binarx");
        File.Delete(Path.Combine(root, "stdlib5", "std", "io.lyr"));
        File.WriteAllText(Path.Combine(root, "extra"), "");
        Assert.Equal(["changed: lyric5", "missing: stdlib5/std/io.lyr", "not in the manifest: extra"], ToolchainManifest.Verify(root));
    }

    [Fact]
    public void A_directory_without_a_manifest_is_refused()
    {
        var root = Directory.CreateTempSubdirectory("lyric-manifest-").FullName;
        Assert.Throws<InvalidDataException>(() => ToolchainManifest.Read(root));
        File.WriteAllText(Path.Combine(root, ToolchainManifest.FileName), "{\"version\": \"5.0.0\"}");
        Assert.Throws<InvalidDataException>(() => ToolchainManifest.Read(root));
    }

    /// <summary>zig without -target builds for its own architecture: the one in its binary's header
    /// is read, and a script or a file too short for a header says none.</summary>
    [Fact]
    public void A_compiler_binary_s_architecture_is_read_from_its_header()
    {
        var process = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64";
        Assert.Equal(process, CCompiler.ExecutableArchitecture(Environment.ProcessPath!));
        var script = Path.Combine(Directory.CreateTempSubdirectory("lyric-arch-").FullName, "zig");
        File.WriteAllText(script, "#!/bin/sh\nexec /opt/zig/zig \"$@\"\n" + new string('#', 80));
        Assert.Null(CCompiler.ExecutableArchitecture(script));
        File.WriteAllText(script, "MZ");
        Assert.Null(CCompiler.ExecutableArchitecture(script));
        Assert.Null(CCompiler.ExecutableArchitecture(script + ".missing"));
    }

    /// <summary>lldb sources the file from wherever the toolchain lies and finds the script beside
    /// it. Where no lldb is on the PATH there is nothing to load it into.</summary>
    [Fact]
    public void Lldb_loads_Lyrics_values_through_the_init_file()
    {
        if (OperatingSystem.IsWindows() || CCompiler.FindOnPath("lldb") is not { } lldb) return;
        var init = Path.Combine(AppContext.BaseDirectory, "lldb", "lyric.lldbinit");
        Assert.True(File.Exists(init), $"{init} is not beside the binary");
        var info = new ProcessStartInfo(lldb)
        {
            ArgumentList = { "--batch", "--no-lldbinit", "-o", $"command source '{init}'", "-o", "type category list lyric" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.Contains("Category: lyric (enabled)", output);
    }
}
