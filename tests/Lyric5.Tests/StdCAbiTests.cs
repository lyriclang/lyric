using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Lyric5.Toolchain;

namespace Lyric5.Tests;

/// <summary>
/// std calls its runtime as any C function, under a name of the program's own bound to the
/// symbol — so the C compiler never holds std's declaration against the runtime header's. A
/// result narrower in C than in Lyric is read with bits the function never set (an 'int' for an
/// 'int' of Lyric's left the upper half of the register to chance). Here the C compiler holds the
/// two together: one assertion per function, on every target zig builds for.
/// </summary>
public partial class StdCAbiTests
{
    [GeneratedRegex("""extern "C" fn (\w+)\(([^)]*)\)(?:: ([^=]+?))? = "(\w+)";""")]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^#define \w+ (lyr_\w+)$", RegexOptions.Multiline)]
    private static partial Regex MuslName();

    /// <summary>The bytes of a Lyric result as C holds it; <c>null</c> where none is checked —
    /// nothing comes back, or a struct, which C returns as its own.</summary>
    private static string? Width(string type) => type.Trim() switch
    {
        "void" or "never" => null,
        "int" or "uint" or "int64" or "uint64" or "float" => "8",
        "int32" or "uint32" or "char" or "float32" => "4",
        "int16" or "uint16" => "2",
        "int8" or "uint8" or "bool" => "1",
        var t when t is "string" || t.StartsWith('?') || t.EndsWith("[]") || t.StartsWith("Coroutine<")
                   || t.StartsWith("Ptr<") || t == "CStr" || char.IsUpper(t[0]) => "sizeof(void *)",
        _ => null,
    };

    private static int Arity(string parameters)
    {
        if (parameters.Trim().Length == 0) return 0;
        int depth = 0, count = 1;
        foreach (var c in parameters)
        {
            if (c is '<' or '(') depth++;
            else if (c is '>' or ')') depth--;
            else if (c == ',' && depth == 0) count++;
        }
        return count;
    }

    [Fact]
    public void Each_C_function_of_std_returns_what_its_declaration_says()
    {
        var root = RuntimeLayout.FindRoot(AppContext.BaseDirectory);
        var musl = MuslName().Matches(File.ReadAllText(Path.Combine(root, "runtime", "third_party", "musl-math", "lyr_names.h")))
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var unit = new StringBuilder("#include \"lyr/lyr.h\"\n#include <math.h>\n\n");
        var checkedCount = 0;
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "stdlib5"), "*.lyr", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            foreach (Match m in Declaration().Matches(File.ReadAllText(file)))
            {
                var (name, parameters, result, symbol) = (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, m.Groups[4].Value);
                // musl's functions under the runtime's prefix are declared in its own directory only
                if (musl.Contains(symbol) || Width(result.Length == 0 ? "void" : result) is not { } width) continue;
                var zeros = string.Join(", ", Enumerable.Repeat("0", Arity(parameters)));
                unit.Append($"_Static_assert(sizeof({symbol}({zeros})) == {width}, \"{symbol}: {Path.GetFileName(file)}'s {name} returns {result.Trim()}\");\n");
                checkedCount++;
            }
        // every converted module holds such functions: far more than a handful are checked
        Assert.True(checkedCount > 100, $"only {checkedCount} functions checked");

        var dir = Directory.CreateTempSubdirectory("lyr-std-c-abi-").FullName;
        var source = Path.Combine(dir, "std_c_abi.c");
        File.WriteAllText(source, unit.ToString());
        var compiler = CCompiler.Locate() ?? throw new InvalidOperationException("no C compiler");
        string?[] targets = compiler.CanCrossCompile
            ? ["x86_64-linux-gnu", "aarch64-linux-gnu", "x86_64-windows-gnu", "aarch64-macos"]
            : [null];
        var failures = new List<string>();
        foreach (var target in targets)
        {
            var start = new ProcessStartInfo(compiler.Path) { RedirectStandardError = true, RedirectStandardOutput = true };
            if (compiler.Kind == CCompilerKind.Zig) start.ArgumentList.Add("cc");
            if (target is not null) { start.ArgumentList.Add("-target"); start.ArgumentList.Add(target); }
            // an object, not -fsyntax-only, which zig cc does not take
            foreach (var arg in new[] { "-c", "-o", Path.Combine(dir, "std_c_abi.o"), "-std=c11", "-I", RuntimeLayout.IncludeDir(root), source })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stderr = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) failures.Add($"{target ?? "host"}:\n{stderr.Result}");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures));
        Directory.Delete(dir, recursive: true);
    }
}
