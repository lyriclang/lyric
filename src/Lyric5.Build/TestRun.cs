using System.Text;
using Lyric.AST;
using Lyric.Compiler;
using Lyric.Ir;
using Lyric5.Toolchain;

namespace Lyric5.Build;

/// <summary>One test <c>lyric test</c> runs: the function, by its module's path and its name,
/// whether it says it throws, why it is skipped, if it is, how many seconds it may take, and
/// whether it runs beside the others that say so (<c>@Test { skip = "…", timeout = 10, parallel =
/// true }</c>; 60 unless it says, not parallel).</summary>
public sealed record TestCase(string Module, string Function, bool Throws, string Skip, long Timeout = 60, bool Parallel = false)
{
    /// <summary>How the run names it: <c>app.tests.math.adds</c>.</summary>
    public string Name => $"{Module}.{Function}";
}

/// <summary>
/// <c>lyric test</c> (design/v5/spec/10 B12, 07 V4, 11 C2): every function the package marks
/// <c>@Test</c> — in its modules under <c>src/</c>, and under <c>tests/</c> — built into one
/// program and run. The program is ordinary Lyric: a module the toolchain writes, which hands the
/// tests to <c>std.test.run</c>; that runs each in a task of its own and answers the exit.
/// </summary>
public static class TestRun
{
    /// <summary>The module the toolchain writes the program into, in the package's namespace: the
    /// tests it imports need not be <c>pub</c>, only not <c>private</c>.</summary>
    public const string DriverModule = "__tests__";

    /// <summary>Builds the package's tests — those whose name holds <paramref name="filter"/>, where
    /// one is given — and runs them, what they print passed on as it comes. The answer is the
    /// program's exit: 0 when every test held, 1 when one failed (11 C5); a refused program is 1
    /// as well, a build that could not run 2.</summary>
    public static int Run(Project project, BuildProfile profile, Target target, CCompiler compiler, string version, string? filter,
        TextWriter output, TextWriter error)
    {
        // the scripts first, so that a test may import what they generate (11 W3 BS4, BS6)
        var (ran, _) = BuildScripts.Run(project, Target.Host, profile, compiler, version, error);
        if (ran != 0) return ran;
        var tests = Discover(project, error);
        if (tests is null) return 1;
        var chosen = filter is null ? tests : tests.Where(t => t.Name.Contains(filter, StringComparison.Ordinal)).ToList();
        if (chosen.Count == 0)
        {
            output.WriteLine(tests.Count == 0 ? "no tests" : $"no test's name holds '{filter}'");
            return 0;
        }
        var program = project with { EntryText = Driver(chosen) };
        return BuildAndRun(program, profile, target, compiler, version, output, error);
    }

    /// <summary>The program built for <paramref name="target"/> and run — on this machine only; for
    /// another, the binary is built and named, as <c>run</c> does (14 §1.1), exit 2.</summary>
    internal static int BuildAndRun(Project program, BuildProfile profile, Target target, CCompiler compiler, string version,
        TextWriter output, TextWriter error)
    {
        var (exit, built) = Pipeline.Build(new BuildRequest(program, profile, target, compiler, version), error);
        if (exit != 0 || built is null) return exit;
        if (!target.IsHost)
        {
            error.WriteLine($"error[LYR-CLI0003]: '{target.Triple}' is not this machine; the binary is at {built}");
            return 2;
        }
        return ProcessRunner.Stream(built, [], output, error);
    }

    /// <summary>
    /// The package's tests in the order they run — module by module in the order of their paths,
    /// each module's in the order they are written —, after one compilation of every module under
    /// <c>src/</c> and <c>tests/</c>. <c>null</c> after reporting a program the compiler refuses.
    /// </summary>
    public static IReadOnlyList<TestCase>? Discover(Project project, TextWriter error) =>
        Marked(project, "Test", error)?.Select(m =>
        {
            // the attribute's value, evaluated while the package compiled (09 F4): written or defaulted
            var skip = m.Fields.GetValueOrDefault("skip") is StringConst { Value: var why } ? why : "";
            var timeout = m.Fields.GetValueOrDefault("timeout") is IntConst { Value: var seconds } ? (long)seconds : 60;
            var parallel = m.Fields.GetValueOrDefault("parallel") is BoolConst { Value: true };
            return new TestCase(m.Module, m.Function.Name, m.Function.Throws is not null, skip, timeout, parallel);
        }).ToList();

    /// <summary>
    /// The package's functions marked with std.core's <paramref name="attribute"/> — <c>Test</c>,
    /// <c>Bench</c> — module by module in the order of their paths, each module's in the order they
    /// are written, with the module's path, the mark and the mark's fields by name as the compilation
    /// evaluated them (09 F4). One compilation of every module under <c>src/</c> and <c>tests/</c>;
    /// <c>null</c> after reporting a program the compiler refuses.
    /// </summary>
    internal static IReadOnlyList<(string Module, FunctionDecl Function, AttributeNode Attribute, IReadOnlyDictionary<string, IrConstValue> Fields)>? Marked(
        Project project, string attribute, TextWriter error)
    {
        var manifest = project.Manifest!;
        var testRoot = project.TestRoot!;
        var roots = new List<ScriptSource>();
        foreach (var file in Files(manifest.SourceRoot))
            roots.Add(ScriptSource.FromDisk(file, Project.ModulePathOf(manifest, file)!));
        foreach (var file in Files(testRoot))
        {
            var relative = Path.GetRelativePath(testRoot, file);
            var module = $"{manifest.Name}.tests.{ScriptSource.ModuleNameUnder(testRoot, file)}";
            // 'app.tests.x' is one module: src/tests/x.lyr or tests/x.lyr, not both.
            if (File.Exists(Path.Combine(manifest.SourceRoot, "tests", relative)))
            {
                error.WriteLine($"error[LYR-RES0015]: the module '{module}' lies under src/tests/ and under tests/");
                error.WriteLine("  = help: rename one of the two files");
                return null;
            }
            roots.Add(ScriptSource.FromDisk(file, module));
        }

        var options = new CompilerOptions
        {
            StdlibRoot = Pipeline.StdlibRoot, PackageRoots = project.PackageRoots,
            PackageDependencies = project.DeclaredDependencies,
            PackageTestRoots = new Dictionary<string, string> { [manifest.Name] = testRoot },
            PackageGenRoots = Pipeline.GenRoots(project),
        };
        var result = SourceCompiler.CheckProject(roots, options);
        // The warnings are the build's to report, which compiles the program again: once is enough.
        if (!result.Ok || result.Model is not { } model)
        {
            result.Render(error);
            return null;
        }

        var mark = model.Compilation.FindModule(["std", "core"])?.Members.LookupLocal(attribute);
        var found = new List<(string, FunctionDecl, AttributeNode, IReadOnlyDictionary<string, IrConstValue>)>();
        // the struct's fields in their order, which is the order of the value's (its layout)
        var fieldNames = ((mark as Lyric.Resolver.TypeSymbol)?.Declaration as StructDecl)?.Members.OfType<FieldDecl>()
            .Select(f => f.Name).ToArray() ?? [];
        foreach (var module in model.Compilation.Modules
                     .Where(m => m.Path.Length > 1 && m.Path[0] == manifest.Name)
                     .OrderBy(m => string.Join('.', m.Path), StringComparer.Ordinal))
        {
            foreach (var fn in model.Compilation.AstOf(module).Declarations.OfType<FunctionDecl>())
            {
                if (fn.Attributes.FirstOrDefault(a => ReferenceEquals(model.Types.RefOf(a), mark)) is not { } marked) continue;
                var fields = new Dictionary<string, IrConstValue>(StringComparer.Ordinal);
                if (model.Types.AttributeValueOf(marked) is { } site
                    && result.ComptimeValues?.GetValueOrDefault(model.Types.ComptimeSites.IndexOf(site)) is StructConst value)
                    for (var i = 0; i < Math.Min(fieldNames.Length, value.Fields.Length); i++) fields[fieldNames[i]] = value.Fields[i];
                found.Add((string.Join('.', module.Path), fn, marked, fields));
            }
        }
        return found;
    }

    /// <summary>The program: each test imported under a name of its own and called from a lambda —
    /// with <c>try</c> where it throws —, the list handed to <c>std.test.run</c>, whose answer is
    /// the exit. A lambda rather than the function itself: an imported function is no value the
    /// lowering takes yet.</summary>
    public static string Driver(IReadOnlyList<TestCase> tests)
    {
        var text = new StringBuilder();
        text.Append("// The test program lyric test writes.\n");
        text.Append("import std.test { TestCase, run };\n");
        var named = new List<(TestCase Test, string Alias)>();
        foreach (var module in tests.GroupBy(t => t.Module))
        {
            var imports = new List<string>();
            foreach (var test in module)
            {
                var alias = $"test{named.Count}";
                named.Add((test, alias));
                imports.Add($"{test.Function} as {alias}");
            }
            text.Append($"import {module.Key} {{ {string.Join(", ", imports)} }};\n");
        }
        text.Append("\nfn main(): int {\n    return run([\n");
        foreach (var (test, alias) in named)
        {
            var skip = test.Skip.Length > 0 ? $", skip = {Quote(test.Skip)}" : "";
            if (test.Timeout != 60) skip += $", timeout = {test.Timeout}";
            if (test.Parallel) skip += ", parallel = true";
            var call = test.Throws ? $"try {alias}()" : $"{alias}()";
            text.Append($"        TestCase {{ name = {Quote(test.Name)}, body = () => {call}{skip} }},\n");
        }
        text.Append("    ]);\n}\n");
        return text.ToString();
    }

    /// <summary>A Lyric string literal holding <paramref name="text"/>.</summary>
    internal static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";

    private static IEnumerable<string> Files(string root) =>
        Directory.Exists(root)
            ? Directory.EnumerateFiles(root, "*.lyr", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            : [];
}
