using System.Runtime.InteropServices;

namespace Lyric5.Toolchain;

public enum TargetOs { Linux, Windows, MacOs }

/// <summary>
/// A compilation target named by its zig triple — <c>x86_64-linux-gnu</c>, <c>aarch64-macos</c>,
/// <c>x86_64-windows-gnu</c> (design/v5/spec/11 P4). The Tier 1 set is <see cref="Tier1"/>.
/// </summary>
public sealed record Target(string Triple, string Arch, TargetOs Os)
{
    public static readonly IReadOnlyList<Target> Tier1 =
    [
        Parse("x86_64-linux-gnu"),
        Parse("aarch64-linux-gnu"),
        Parse("x86_64-windows-gnu"),
        Parse("aarch64-macos"),
        Parse("x86_64-macos"),
    ];

    public static Target Parse(string triple)
    {
        var parts = triple.Split('-');
        if (parts.Length < 2) throw new ArgumentException($"'{triple}' is not a target triple (arch-os[-abi])", nameof(triple));
        var os = parts[1] switch
        {
            "linux" => TargetOs.Linux,
            "windows" => TargetOs.Windows,
            "macos" => TargetOs.MacOs,
            _ => throw new ArgumentException($"'{triple}': unsupported operating system '{parts[1]}'", nameof(triple)),
        };
        if (parts[0] is not ("x86_64" or "aarch64"))
        {
            throw new ArgumentException($"'{triple}': unsupported architecture '{parts[0]}'", nameof(triple));
        }
        return new Target(triple, parts[0], os);
    }

    /// <summary>The machine this process runs on, as a triple.</summary>
    public static Target Host
    {
        get
        {
            var arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "aarch64",
                var other => throw new PlatformNotSupportedException($"unsupported host architecture {other}"),
            };
            if (OperatingSystem.IsWindows()) return Parse($"{arch}-windows-gnu");
            if (OperatingSystem.IsMacOS()) return Parse($"{arch}-macos");
            return Parse($"{arch}-linux-gnu");
        }
    }

    public bool IsHost => Triple == Host.Triple;

    public string ExecutableSuffix => Os == TargetOs.Windows ? ".exe" : "";

    public override string ToString() => Triple;
}

/// <summary>The four build shapes of design/v5/spec/01 C7 and L12: the built-in profiles, and the
/// base every profile of a manifest comes from (<see cref="BuildProfile"/>).</summary>
public enum Profile { Debug, Release, Asan, Tsan }

public static class ProfileFlags
{
    /// <summary>ASan and TSan runtimes ship with clang, not with zig cc (measured, 01 C7).</summary>
    public static bool RequiresClang(this Profile profile) => profile is Profile.Asan or Profile.Tsan;

    /// <summary>Optimization and debug flags every unit of the profile gets.</summary>
    public static IReadOnlyList<string> Codegen(this Profile profile) => profile switch
    {
        Profile.Debug => ["-O0", "-g"],
        Profile.Release => ["-O2", "-g", "-DNDEBUG"],
        _ => ["-O1", "-g", "-fno-omit-frame-pointer"],
    };

    /// <summary>
    /// Instrumentation. A unit that opts out (the conservative collector reads memory it does not
    /// own on purpose) gets the codegen flags without these; the link still carries them so the
    /// sanitizer runtime is present. UBSan does not recover: undefined behaviour ends the program
    /// like an ASan report does, instead of a line on stderr that a test could miss.
    /// </summary>
    public static IReadOnlyList<string> Instrumentation(this Profile profile) => profile switch
    {
        Profile.Asan => ["-fsanitize=address,undefined", "-fno-sanitize-recover=undefined"],
        Profile.Tsan => ["-fsanitize=thread"],
        _ => [],
    };

    public static string Name(this Profile profile) => profile.ToString().ToLowerInvariant();
}
