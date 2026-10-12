using System.Runtime.InteropServices;

namespace Lyric.Ir.Interp;

/// <summary>
/// What a build is for and as (design/v5/spec/09 A6 B4, Q7; 13 M9a P9): the target's system, its
/// machine and the width of its pointers, and whether the profile descends from the built-in
/// <c>debug</c> — what <c>std.core</c>'s <c>target</c> and <c>profile</c> hold. The interpreter
/// answers them; nothing at run time asks. The codes are <c>std.core</c>'s: system 1 Linux,
/// 2 macOS, 3 Windows, 0 another; machine 1 x86-64, 2 AArch64, 0 another.
/// </summary>
public sealed record BuildFacts(int Os, int Arch, int PointerWidth, bool Debug)
{
    /// <summary>The machine the compiler runs on, in the default profile (debug) — a compilation
    /// that names no target builds for its host.</summary>
    public static BuildFacts Host { get; } = new(
        OperatingSystem.IsLinux() ? 1 : OperatingSystem.IsMacOS() ? 2 : OperatingSystem.IsWindows() ? 3 : 0,
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => 1,
            Architecture.Arm64 => 2,
            _ => 0,
        },
        IntPtr.Size * 8,
        Debug: true);

    /// <summary>The facts of a target named by its system and machine, as a triple names them
    /// (<c>x86_64</c>, <c>aarch64</c>), in a profile that is or is not a debug one.</summary>
    public static BuildFacts Of(string os, string arch, bool debug) => new(
        os switch { "linux" => 1, "macos" => 2, "windows" => 3, _ => 0 },
        arch switch { "x86_64" => 1, "aarch64" => 2, _ => 0 },
        arch is "x86_64" or "aarch64" ? 64 : 32,
        debug);
}
