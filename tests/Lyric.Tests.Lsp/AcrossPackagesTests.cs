using System.Text.Json;
using Lyric.Lsp.Analysis;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Tests.Lsp;

/// <summary>
/// A package's declaration used by another: the library `geo` and the program `app` that depends on
/// it by path, both open in the editor — two compilations. References and rename asked in the
/// library reach the program's uses, which only the program's compilation knows.
/// </summary>
public sealed class AcrossPackagesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "lyric-lsp-across-" + Guid.NewGuid().ToString("N")[..8]);

    private const string Lib = "pub fn distance(a: int, b: int): int {\n    return b - a;\n}\n";
    private const string Main = "import geo { distance };\n\nfn main(): int {\n    return distance(1, 3);\n}\n";

    public AcrossPackagesTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "geo", "src"));
        Directory.CreateDirectory(Path.Combine(_dir, "app", "src"));
        File.WriteAllText(Path.Combine(_dir, "geo", "lyric.toml"), "[package]\nname = \"geo\"\nversion = \"1.0.0\"\n");
        File.WriteAllText(Path.Combine(_dir, "app", "lyric.toml"),
            "[package]\nname = \"app\"\nversion = \"0.1.0\"\n\n[dependencies]\ngeo = { path = \"../geo\" }\n");
        File.WriteAllText(LibPath, Lib);
        File.WriteAllText(MainPath, Main);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        GC.SuppressFinalize(this);
    }

    private string LibPath => Path.Combine(_dir, "geo", "src", "lib.lyr");
    private string MainPath => Path.Combine(_dir, "app", "src", "main.lyr");

    private static string DidOpen(string uri, string text) =>
        JsonSerializer.Serialize(new { textDocument = new { uri, languageId = "lyric", version = 1, text } });

    private static async Task Analysed(ServerHarness harness, string uri)
    {
        for (var attempt = 0; attempt < 16; attempt++)
        {
            var published = await harness.ReceiveNotificationAsync(LspMethods.PublishDiagnostics);
            if (published.GetProperty("params").GetProperty("uri").GetString() == uri) return;
        }
        throw new InvalidOperationException($"no diagnostics arrived for {uri}");
    }

    /// <summary>Both packages open and analysed: the library first, then the program.</summary>
    private async Task<(ServerHarness Harness, string Lib, string Main)> BothOpen()
    {
        var lib = DocumentUri.FromFilePath(LibPath);
        var main = DocumentUri.FromFilePath(MainPath);
        var harness = new ServerHarness();
        await harness.InitializeAsync();
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(lib, Lib));
        await Analysed(harness, lib);
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(main, Main));
        await Analysed(harness, main);
        return (harness, lib, main);
    }

    [Fact]
    public async Task References_asked_in_the_library_reach_the_program_that_uses_it()
    {
        var (harness, lib, main) = await BothOpen();
        await using var _ = harness;

        var id = await harness.RequestAsync(LspMethods.References, JsonSerializer.Serialize(new
        {
            textDocument = new { uri = lib },
            position = new { line = 0, character = 9 },
            context = new { includeDeclaration = true },
        }));
        var uris = (await harness.ReceiveResponseAsync(id)).GetProperty("result").EnumerateArray()
            .Select(l => l.GetProperty("uri").GetString()).ToList();

        Assert.Contains(lib, uris);
        Assert.Equal(1, uris.Count(u => u == main)); // the call; the import clause binds, and is renamed below
    }

    [Fact]
    public async Task A_rename_in_the_library_renames_the_programs_uses()
    {
        var (harness, lib, main) = await BothOpen();
        await using var _ = harness;

        var id = await harness.RequestAsync(LspMethods.Rename, JsonSerializer.Serialize(new
        {
            textDocument = new { uri = lib },
            position = new { line = 0, character = 9 },
            newName = "gap",
        }));
        var changes = (await harness.ReceiveResponseAsync(id)).GetProperty("result").GetProperty("changes");

        Assert.Equal(1, changes.GetProperty(lib).GetArrayLength());
        Assert.Equal(2, changes.GetProperty(main).GetArrayLength());
    }

    [Fact]
    public async Task Without_the_program_open_the_library_answers_for_itself()
    {
        // The counter-check: the program's uses come from the program's compilation, which only
        // an open program has.
        var lib = DocumentUri.FromFilePath(LibPath);
        await using var harness = new ServerHarness();
        await harness.InitializeAsync();
        await harness.NotifyAsync(LspMethods.DidOpen, DidOpen(lib, Lib));
        await Analysed(harness, lib);

        var id = await harness.RequestAsync(LspMethods.References, JsonSerializer.Serialize(new
        {
            textDocument = new { uri = lib },
            position = new { line = 0, character = 9 },
            context = new { includeDeclaration = true },
        }));
        var uris = (await harness.ReceiveResponseAsync(id)).GetProperty("result").EnumerateArray()
            .Select(l => l.GetProperty("uri").GetString()).ToList();
        Assert.All(uris, u => Assert.Equal(lib, u));
    }

    [Fact]
    public void The_standard_library_and_the_git_cache_are_not_the_editors_to_change()
    {
        Assert.True(PackageUnit.IsReadOnly(Path.Combine(Lyric5.Build.Pipeline.StdlibRoot, "std", "io.lyr")));
        Assert.True(PackageUnit.IsReadOnly(Path.Combine(Lyric5.Toolchain.UserCache.Root, "git", "checkouts", "geo-1", "src", "lib.lyr")));
        Assert.False(PackageUnit.IsReadOnly(Path.Combine(_dir, "geo", "src", "lib.lyr")));
    }
}
