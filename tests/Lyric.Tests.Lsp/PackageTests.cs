using System.Text.Json;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// The reason <c>lyric.toml</c> is data: an editor has to learn a package's modules and
/// dependencies without running anything from it.
///
/// <para>These use a REAL DIRECTORY rather than the buffer-path trick the other tests use. The
/// manifest is found by walking up from the document, so where the document claims to be is the
/// whole question.</para>
/// </summary>
public sealed class PackageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(),
        "lyric-lsp-package-" + Guid.NewGuid().ToString("N")[..8]);

    private const string AppManifest = "[package]\nname = \"app\"\nversion = \"0.1.0\"\n";

    public PackageTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "app", "src"));
        Directory.CreateDirectory(Path.Combine(_dir, "engine", "src"));

        File.WriteAllText(Path.Combine(_dir, "engine", "lyric.toml"),
            "[package]\nname = \"engine\"\nversion = \"1.0.0\"\n");
        File.WriteAllText(Path.Combine(_dir, "engine", "src", "input.lyr"), """
            pub fn keyDown(key: int): bool { return key == 32; }
            """);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private void WriteManifest(string text) => File.WriteAllText(Path.Combine(_dir, "app", "lyric.toml"), text);

    private string EntryPath => Path.Combine(_dir, "app", "src", "main.lyr");

    private const string ImportsTheEngine = """
        import engine.input { keyDown };

        fn main(): int { return if (keyDown(32)) 1 else 0; }
        """;

    private static string DidOpen(string uri, string text) =>
        JsonSerializer.Serialize(new
        {
            textDocument = new { uri, languageId = "lyric", version = 1, text },
        });

    private static JsonElement Diagnostics(JsonElement notification) =>
        notification.GetProperty("params").GetProperty("diagnostics");

    [Fact]
    public async Task A_dependency_from_the_manifest_resolves()
    {
        WriteManifest(AppManifest + "\n[dependencies]\nengine = { path = \"../engine\" }\n");

        var uri = DocumentUri.FromFilePath(EntryPath);
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, ImportsTheEngine));
        var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);

        Assert.Empty(Diagnostics(published).EnumerateArray());
    }

    [Fact]
    public async Task Without_the_dependency_the_import_is_unknown()
    {
        // The counter-check. Without it the test above would pass on a server that resolves the
        // import for some other reason, and the file would be proving nothing.
        WriteManifest(AppManifest);

        var uri = DocumentUri.FromFilePath(EntryPath);
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, ImportsTheEngine));
        var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);

        Assert.NotEmpty(Diagnostics(published).EnumerateArray());
    }

    [Fact]
    public async Task A_broken_manifest_still_leaves_the_editor_with_diagnostics()
    {
        // The one that matters for a tool someone is looking at. Checking the file alone gives
        // resolution that may be wrong; publishing nothing would leave the editor showing an
        // earlier state with no hint that anything happened.
        WriteManifest("[package]\nname = ");

        var uri = DocumentUri.FromFilePath(EntryPath);
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, ImportsTheEngine));

        // The import is unresolved, because the manifest that would have named the dependency is
        // unusable — but an answer arrives, which is the point, and the log says why.
        var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);
        Assert.NotEmpty(Diagnostics(published).EnumerateArray());
    }

    [Fact]
    public async Task A_module_of_the_package_finds_its_neighbour()
    {
        WriteManifest(AppManifest);
        File.WriteAllText(Path.Combine(_dir, "app", "src", "area.lyr"), """
            pub fn square(n: int): int { return n * n; }
            """);

        var uri = DocumentUri.FromFilePath(EntryPath);
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, """
            import app.area { square };

            fn main(): int { return square(7); }
            """));
        var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);

        Assert.Empty(Diagnostics(published).EnumerateArray());
    }

    [Fact]
    public async Task A_module_opened_after_its_neighbour_keeps_its_analysis()
    {
        // The server publishes a closed module under the URI it builds itself, and the same file,
        // once open, under the editor's — 'file:///c:/…' against 'file:///c%3A/…' on Windows, a
        // letter escaped here. Compared as text, the second analysis took the first spelling for a
        // file gone from the package, dropped the file's fresh analysis, and a hover in it waited
        // for one that never came.
        WriteManifest(AppManifest);
        var utilPath = Path.Combine(_dir, "app", "src", "util.lyr");
        const string util = "pub fn twice(n: int): int {\n    let doubled = n * 2;\n    return doubled;\n}\n";
        File.WriteAllText(utilPath, util);
        File.WriteAllText(EntryPath, "import app.util { twice };\n\nfn main(): int { return twice(1); }\n");

        var serverSpelling = DocumentUri.FromFilePath(utilPath);
        var editorSpelling = serverSpelling.Replace("/util.lyr", "/%75til.lyr", StringComparison.Ordinal);
        Assert.NotEqual(serverSpelling, editorSpelling);

        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(DocumentUri.FromFilePath(EntryPath), File.ReadAllText(EntryPath)));
        while (UriOf(await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics)) != serverSpelling) { }

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(editorSpelling, util));
        while (UriOf(await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics)) != editorSpelling) { }

        var id = await harness.RequestAsync(LspMethods.Hover, JsonSerializer.Serialize(new
        {
            textDocument = new { uri = editorSpelling },
            position = new { line = 2, character = 12 },
        }));
        while (true)
        {
            var message = await harness.ReceiveAsync();
            if (message.TryGetProperty("method", out var method))
            {
                // The analysis of the open module must not be followed by a withdrawal of the same
                // file under the other spelling.
                if (method.GetString() == LspMethods.PublishDiagnostics)
                    Assert.NotEqual(serverSpelling, UriOf(message));
                continue;
            }
            if (!message.TryGetProperty("id", out var answered) || answered.ValueKind != JsonValueKind.Number
                || answered.GetInt32() != id)
                continue;

            Assert.Contains("doubled", message.GetProperty("result").GetProperty("contents").GetProperty("value").GetString());
            return;
        }
    }

    private static string? UriOf(JsonElement notification) =>
        notification.GetProperty("params").GetProperty("uri").GetString();

    [Fact]
    public async Task The_editor_writes_no_lock()
    {
        // A keystroke reads the graph and writes nothing: lyric.lock is the build's to write. The
        // lock here is valid but not in the form a build writes, which a build would rewrite.
        WriteManifest(AppManifest + "\n[dependencies]\nengine = { path = \"../engine\" }\n");
        var lockFile = Path.Combine(_dir, "app", "lyric.lock");
        const string written = "# kept by hand\nversion = 1\n";
        File.WriteAllText(lockFile, written);

        var uri = DocumentUri.FromFilePath(EntryPath);
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, ImportsTheEngine));
        Assert.Empty(Diagnostics(await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics)).EnumerateArray());

        Assert.Equal(written, File.ReadAllText(lockFile));
    }

    [Fact]
    public async Task A_module_under_tests_is_one_of_the_package()
    {
        // tests/ is the package's as lyric test compiles it: a test module imports the package's
        // modules by their paths, and the editor checks it in the same compilation.
        WriteManifest(AppManifest);
        File.WriteAllText(Path.Combine(_dir, "app", "src", "area.lyr"), """
            pub fn square(n: int): int { return n * n; }
            """);
        Directory.CreateDirectory(Path.Combine(_dir, "app", "tests"));
        var test = Path.Combine(_dir, "app", "tests", "squares.lyr");

        var uri = DocumentUri.FromFilePath(test);
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();

        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(uri, """
            import app.area { square };

            @Test
            fn squares(): void {
                assert(square(3) == 9);
            }
            """));

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);
            if (published.GetProperty("params").GetProperty("uri").GetString() != uri) continue;
            Assert.Empty(Diagnostics(published).EnumerateArray());
            return;
        }
        Assert.Fail("no diagnostics arrived for the test module");
    }
}
