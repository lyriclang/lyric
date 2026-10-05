using Lyric5.Toolchain;

namespace Lyric5.Tests;

public class ToolchainTests
{
    [Theory]
    [InlineData("x86_64-linux-gnu", "x86_64", TargetOs.Linux, "")]
    [InlineData("aarch64-linux-gnu", "aarch64", TargetOs.Linux, "")]
    [InlineData("x86_64-windows-gnu", "x86_64", TargetOs.Windows, ".exe")]
    [InlineData("aarch64-macos", "aarch64", TargetOs.MacOs, "")]
    public void A_triple_names_architecture_and_system(string triple, string arch, TargetOs os, string suffix)
    {
        var target = Target.Parse(triple);
        Assert.Equal(arch, target.Arch);
        Assert.Equal(os, target.Os);
        Assert.Equal(suffix, target.ExecutableSuffix);
    }

    [Theory]
    [InlineData("x86_64")]
    [InlineData("riscv64-linux-gnu")]
    [InlineData("x86_64-freebsd")]
    public void Anything_else_is_refused(string triple) =>
        Assert.Throws<ArgumentException>(() => Target.Parse(triple));

    [Fact]
    public void Tier_1_is_the_five_targets_of_the_plan()
    {
        Assert.Equal(
            ["x86_64-linux-gnu", "aarch64-linux-gnu", "x86_64-windows-gnu", "aarch64-macos", "x86_64-macos"],
            Target.Tier1.Select(t => t.Triple));
    }

    [Fact]
    public void The_sanitizer_profiles_refuse_a_compiler_without_their_runtime()
    {
        var zig = new CCompiler(CCompilerKind.Zig, "/usr/bin/zig", "0.16.0");
        var cache = TestDirectories.Fresh("lyric5-cbuild-");
        var refusal = Assert.Throws<CBuildException>(() => new CBuild(zig, Target.Host, Profile.Asan, cache));
        Assert.Contains("needs clang", refusal.Message);
    }

    [Fact]
    public void Only_zig_crosses_without_a_sysroot()
    {
        var gcc = new CCompiler(CCompilerKind.Gcc, "/usr/bin/gcc", "15");
        var other = Target.Tier1.First(t => t.Triple != Target.Host.Triple);
        var cache = TestDirectories.Fresh("lyric5-cbuild-");
        Assert.Throws<CBuildException>(() => new CBuild(gcc, other, Profile.Debug, cache));
    }

    [Fact]
    public void An_uninstrumented_unit_keeps_the_codegen_flags_and_loses_the_sanitizers()
    {
        var clang = new CCompiler(CCompilerKind.Clang, "/usr/bin/clang", "22");
        var cache = TestDirectories.Fresh("lyric5-cbuild-");
        var build = new CBuild(clang, Target.Host, Profile.Asan, cache);

        var instrumented = build.FlagsFor(new CUnit("a.c", [], []));
        var plain = build.FlagsFor(new CUnit("b.c", [], [], Instrument: false));

        Assert.Contains("-fsanitize=address,undefined", instrumented);
        Assert.DoesNotContain(plain, f => f.StartsWith("-fsanitize", StringComparison.Ordinal));
        Assert.Contains("-fno-omit-frame-pointer", plain);
    }

    [Fact]
    public void The_cache_key_moves_with_a_header_and_stays_without_a_change()
    {
        var dir = TestDirectories.Fresh("lyric5-key-");
        var include = Directory.CreateDirectory(Path.Combine(dir, "include")).FullName;
        File.WriteAllText(Path.Combine(dir, "a.c"), "#include \"h.h\"\nint f(void) { return H; }\n");
        File.WriteAllText(Path.Combine(include, "h.h"), "#define H 1\n");
        var build = new CBuild(new CCompiler(CCompilerKind.Zig, "/usr/bin/zig", "0.16.0"), Target.Host, Profile.Debug,
            Path.Combine(dir, "cache"));
        var unit = new CUnit(Path.Combine(dir, "a.c"), [], [include]);

        var first = build.CacheKey(unit);
        Assert.Equal(first, build.CacheKey(unit));

        File.WriteAllText(Path.Combine(include, "h.h"), "#define H 2\n");
        Assert.NotEqual(first, build.CacheKey(unit));
    }
}
