using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>A measurement, not a test: what zig cc does with a macOS framework on a macOS host — for the
/// host, and for each macOS triple named. Fails with its findings, to be read in the run's log.
/// Removed once the slice has its decisions.</summary>
public class FrameworkProbe
{
    [Fact]
    public void Measure_a_framework_on_the_host()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var zig = CCompiler.Locate(CCompilerKind.Zig) ?? throw new InvalidOperationException("no zig");
        var dir = TestDirectories.Fresh("lyric5-probe-");
        var source = Path.Combine(dir, "main.c");
        File.WriteAllText(source, "#include <CoreFoundation/CoreFoundation.h>\n#include <stdio.h>\n"
                                  + "int main(void) { CFStringRef s = CFSTR(\"hello\"); printf(\"%ld\\n\", (long)CFStringGetLength(s)); return 0; }\n");
        var report = new List<string>();
        var sdk = ProcessRunner.TryRun("xcrun", ["--show-sdk-path"], TimeSpan.FromMinutes(1));
        report.Add($"xcrun: {sdk?.ExitCode} {sdk?.Stdout.Trim()} {sdk?.Stderr.Trim()}");
        report.Add($"zig: {zig.Path}, host {Target.Host.Triple}");
        foreach (var target in new string?[] { null, "aarch64-macos", "x86_64-macos" })
        {
            var output = Path.Combine(dir, "main-" + (target ?? "host"));
            List<string> args = ["cc"];
            if (target is not null) args.AddRange(["-target", target]);
            args.AddRange(["-framework", "CoreFoundation", source, "-o", output]);
            var built = ProcessRunner.Run(zig.Path, args, TimeSpan.FromMinutes(3), dir);
            report.Add($"[{target ?? "host"}] exit {built.ExitCode}: {Short(built.Stderr)}");
            if (built.ExitCode == 0 && (target is null || target.StartsWith(Target.Host.Arch)))
            {
                var ran = ProcessRunner.Run(output, [], TimeSpan.FromMinutes(1));
                report.Add($"   ran: exit {ran.ExitCode} out '{ran.Stdout.Trim()}' {Short(ran.Stderr)}");
            }
            // with the SDK named as the sysroot
            if (sdk is { ExitCode: 0 } && target is not null)
            {
                var withSdk = ProcessRunner.Run(zig.Path, [.. args.Take(3), "--sysroot", sdk.Stdout.Trim(), .. args.Skip(3)], TimeSpan.FromMinutes(3), dir);
                report.Add($"   with --sysroot: exit {withSdk.ExitCode}: {Short(withSdk.Stderr)}");
            }
        }
        Assert.Fail("framework probe:\n" + string.Join("\n", report));
    }

    private static string Short(string text) =>
        string.Join(" | ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(4)).Trim() is var s && s.Length > 400 ? s[..400] : s.Trim();
}
