using System.Text.Json;
using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Lsp.Analysis;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// The package as ONE compilation: every module under its <c>src/</c> in one symbol world.
///
/// <para>The first half drives <see cref="SourceCompiler.CheckProject"/> over the
/// <see cref="PackageUnit"/> directly and pins what a workspace compilation is. The second half drives the server and pins what an editor sees of it:
/// answers that cross file boundaries in BOTH directions, and diagnostics for files nobody has
/// open.</para>
/// </summary>
public sealed class WorkspaceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "lyric-lsp-workspace-" + Guid.NewGuid().ToString("N")[..8]);

    public WorkspaceTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        File.WriteAllText(Path.Combine(_dir, "lyric.toml"), "[package]\nname = \"app\"\nversion = \"0.1.0\"\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string PathOf(string name) => Path.Combine(_dir, "src", name);

    private string Write(string name, string text)
    {
        var path = PathOf(name);
        File.WriteAllText(path, text);
        return path;
    }

    private const string UtilSource = "pub fn value(): int { return 1; }\n";

    private const string AppSource =
        "import app.util { value };\n\nfn main(): int { return value(); }\n";

    /// <summary>The package the server would analyse for these files, as it would.</summary>
    private PackageUnit Unit(string path) =>
        PackageUnit.Find(path, out var refused) ?? throw new InvalidOperationException(refused ?? "no package");

    private CompileResult CheckProject(params string[] paths)
    {
        var unit = Unit(paths[0]);
        return SourceCompiler.CheckProject(
            paths.Select(p => ScriptSource.FromDisk(p, unit.ModuleOf(p)!)).ToArray(), unit.Options);
    }

    /// <summary>The module AST a file was parsed into — what a cursor question walks.</summary>
    private static Module RootOf(SemanticModel model, FileId file)
    {
        foreach (var module in model.Compilation.Modules)
        {
            var ast = model.Compilation.AstOf(module);
            if (ast.Span.File == file) return ast;
        }

        throw new InvalidOperationException("no module was parsed from this file");
    }

    // ------------------------------------------------------------------ the compilation

    [Fact]
    public void Two_roots_are_one_symbol_world()
    {
        var app = Write("app.lyr", AppSource);
        var util = Write("util.lyr", UtilSource);

        var result = CheckProject(app, util);
        Assert.True(result.Ok);
        Assert.NotNull(result.Model);

        // The question that was impossible per-buffer: standing ON THE DECLARATION in util, the
        // uses in app are found — the use sites are bound to the same symbol object the
        // declaration produced, across the file boundary.
        var utilFile = DiagnosticMapper.FindFile(result.Sources, util);
        var appFile = DiagnosticMapper.FindFile(result.Sources, app);
        var offset = UtilSource.IndexOf("value", StringComparison.Ordinal) + 1;

        var sites = ReferenceProvider.At(result.Model, RootOf(result.Model, utilFile), utilFile,
            offset, includeDeclaration: false);

        Assert.NotNull(sites);
        Assert.Contains(sites, site => site.File == appFile);
    }

    [Fact]
    public void Two_programs_may_both_declare_main()
    {
        // A workspace is not an executable: each 'main' is the entry point of its own script, and
        // the duplicate rule of a single program does not apply across them.
        var one = Write("one.lyr", "fn main(): int { return 1; }\n");
        var two = Write("two.lyr", "fn main(): int { return 2; }\n");

        Assert.True(CheckProject(one, two).Ok);
    }

    [Fact]
    public void A_root_imported_by_another_root_is_read_once()
    {
        // The import finds the module already registered under the name the path derives, so the
        // file is not parsed a second time — one file id, one module, one set of diagnostics.
        var app = Write("app.lyr", AppSource);
        var util = Write("util.lyr", UtilSource);

        var result = CheckProject(app, util);

        var copies = 0;
        for (var i = 1; i <= result.Sources.FileCount; i++)
            if (DocumentUri.PathComparer.Equals(result.Sources.GetPath(new FileId(i)), util))
                copies++;

        Assert.Equal(1, copies);
    }

    [Fact]
    public void Roots_importing_each_other_still_compile()
    {
        // The cycle guard of the loader applies to roots as well: registration happens before
        // anything resolves, so the mutual import terminates and both modules get analysed.
        var a = Write("a.lyr", "import app.b;\n\npub fn fromA(): int { return 1; }\n");
        var b = Write("b.lyr", "import app.a;\n\npub fn fromB(): int { return 2; }\n");

        var result = CheckProject(a, b);
        Assert.NotNull(result.Model);
        Assert.Equal(2, result.Model.Compilation.Modules
            .Count(m => m.FullName is "app.a" or "app.b"));
    }

    // ------------------------------------------------------------------ the server

    private static string DidOpen(string uri, string text) =>
        JsonSerializer.Serialize(new
        {
            textDocument = new { uri, languageId = "lyric", version = 1, text },
        });

    /// <summary>Reads published diagnostics until a batch arrives for this URI.</summary>
    private static async Task<JsonElement> DiagnosticsFor(ServerHarness harness, string uri)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);
            if (published.GetProperty("params").GetProperty("uri").GetString() == uri)
                return published.GetProperty("params").GetProperty("diagnostics");
        }

        throw new InvalidOperationException($"no diagnostics arrived for {uri}");
    }

    [Fact]
    public async Task References_reach_the_file_that_imports_this_one()
    {
        // The headline of the workspace: the OTHER direction. The buffer-scoped server answered
        // "who uses my function" only for uses the open buffer could reach; here the use stands in
        // a file that imports the open one, and is found because both are in one compilation.
        Write("app.lyr", AppSource);
        var util = Write("util.lyr", UtilSource);
        var utilUri = DocumentUri.FromFilePath(util);
        var appUri = DocumentUri.FromFilePath(PathOf("app.lyr"));

        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(utilUri, UtilSource));

        // The analysis is done when the buffer's own batch arrives; asking earlier races it.
        await DiagnosticsFor(harness, utilUri);

        // The cursor on 'value' in its DECLARATION: line 0, inside the name.
        var id = await harness.RequestAsync(LspMethods.References, JsonSerializer.Serialize(new
        {
            textDocument = new { uri = utilUri },
            position = new { line = 0, character = 8 },
            context = new { includeDeclaration = false },
        }));

        var response = await harness.ReceiveResponseAsync(id);
        var locations = response.GetProperty("result").EnumerateArray().ToArray();

        Assert.Contains(locations,
            location => location.GetProperty("uri").GetString() == appUri);
    }

    [Fact]
    public async Task A_closed_project_file_gets_diagnostics_too()
    {
        // util holds a type error and is never opened. The project compile reads it from disk, and
        // its diagnostics are published under a URI the server built itself.
        Write("app.lyr", AppSource);
        var util = Write("util.lyr",
            "pub fn value(): int { return \"nope\"; }\n");
        var utilUri = DocumentUri.FromFilePath(util);
        var appUri = DocumentUri.FromFilePath(PathOf("app.lyr"));

        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(appUri, AppSource));

        Assert.NotEmpty((await DiagnosticsFor(harness, utilUri)).EnumerateArray());
    }

    [Fact]
    public async Task A_disk_change_refreshes_a_closed_file()
    {
        // The watch notification: util is fixed by something that is not this editor — another
        // program, a branch switch — and the closed file's stale squiggles must go.
        Write("app.lyr", AppSource);
        var util = Write("util.lyr",
            "pub fn value(): int { return \"nope\"; }\n");
        var utilUri = DocumentUri.FromFilePath(util);
        var appUri = DocumentUri.FromFilePath(PathOf("app.lyr"));

        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(appUri, AppSource));
        Assert.NotEmpty((await DiagnosticsFor(harness, utilUri)).EnumerateArray());

        File.WriteAllText(util, UtilSource);
        await harness.NotifyAsync(LspMethods.DidChangeWatchedFiles, JsonSerializer.Serialize(new
        {
            changes = new[] { new { uri = utilUri, type = 2 } },
        }));

        Assert.Empty((await DiagnosticsFor(harness, utilUri)).EnumerateArray());
    }

    [Fact]
    public async Task A_deleted_file_takes_its_diagnostics_with_it()
    {
        // Diagnostics are state the server owns in the client: a file that leaves the project must
        // have them withdrawn, or the squiggles outlive the file.
        Write("app.lyr", AppSource);
        Write("util.lyr", UtilSource);
        var bad = Write("bad.lyr", "fn broken(): int { return \"nope\"; }\n");
        var badUri = DocumentUri.FromFilePath(bad);
        var appUri = DocumentUri.FromFilePath(PathOf("app.lyr"));

        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(appUri, AppSource));
        Assert.NotEmpty((await DiagnosticsFor(harness, badUri)).EnumerateArray());

        File.Delete(bad);
        await harness.NotifyAsync(LspMethods.DidChangeWatchedFiles, JsonSerializer.Serialize(new
        {
            changes = new[] { new { uri = badUri, type = 3 } },
        }));

        Assert.Empty((await DiagnosticsFor(harness, badUri)).EnumerateArray());
    }

    [Fact]
    public async Task A_never_saved_buffer_joins_the_project()
    {
        // The importing file lies on disk, the imported module exists only as a buffer that was
        // never saved. The package collects open buffers under src/ as well, so the import
        // resolves.
        Write("app.lyr", "import app.scratch { fresh };\n\nfn main(): int { return fresh(); }\n");
        var appUri = DocumentUri.FromFilePath(PathOf("app.lyr"));
        var scratchUri = DocumentUri.FromFilePath(PathOf("scratch.lyr"));

        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(scratchUri,
            "pub fn fresh(): int { return 7; }\n"));

        Assert.Empty((await DiagnosticsFor(harness, appUri)).EnumerateArray());
    }

    [Fact]
    public async Task The_server_asks_for_file_watches_when_the_client_can()
    {
        await using var harness = new ServerHarness();
        await harness.InitializeAsync(
            """{"workspace":{"didChangeWatchedFiles":{"dynamicRegistration":true}}}""");

        var request = await harness.ReceiveNotificationAsync(LspMethods.RegisterCapability);
        var registration = request.GetProperty("params").GetProperty("registrations")[0];

        Assert.Equal(LspMethods.DidChangeWatchedFiles,
            registration.GetProperty("method").GetString());

        var patterns = registration.GetProperty("registerOptions").GetProperty("watchers")
            .EnumerateArray()
            .Select(watcher => watcher.GetProperty("globPattern").GetString())
            .ToArray();

        Assert.Contains("**/*.lyr", patterns);
        Assert.Contains("**/lyric.toml", patterns);
    }
}
