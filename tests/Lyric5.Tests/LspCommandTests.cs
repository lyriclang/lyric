using System.Diagnostics;
using System.Text;
using System.Text.Json;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric lsp</c>: the language server as the binary an editor starts — a process speaking LSP on
/// its standard input and output, with the standard library beside it. The server's answers are
/// tested in <c>Lyric.Tests.Lsp</c>; these test that the verb is that server.
/// </summary>
public class LspCommandTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    /// <summary>The binary under test, through the host that runs this test: <c>lyric5.dll</c>
    /// beside it, started as an editor starts it.</summary>
    private static Process Start()
    {
        var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(host)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "lyric5.dll"));
        start.ArgumentList.Add("lsp");
        start.ArgumentList.Add("--stdio");
        return Process.Start(start)!;
    }

    private static void Send(Process server, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var input = server.StandardInput.BaseStream;
        input.Write(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
        input.Write(body);
        input.Flush();
    }

    /// <summary>The next message, read by its <c>Content-Length</c>.</summary>
    private static JsonElement Receive(Process server)
    {
        var output = server.StandardOutput.BaseStream;
        var header = new StringBuilder();
        while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
        {
            var b = output.ReadByte();
            Assert.True(b >= 0, "the server closed its output");
            header.Append((char)b);
        }
        var length = int.Parse(header.ToString().Split("\r\n")
            .First(l => l.StartsWith("Content-Length:", StringComparison.Ordinal))["Content-Length:".Length..].Trim());
        var body = new byte[length];
        for (var read = 0; read < length;) read += output.Read(body, read, length - read);
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>The next message that carries <paramref name="method"/>, or answers the request with
    /// <paramref name="id"/>.</summary>
    private static JsonElement ReceiveUntil(Process server, Func<JsonElement, bool> wanted)
    {
        var task = Task.Run(() =>
        {
            while (true)
            {
                var message = Receive(server);
                if (wanted(message)) return message;
            }
        });
        Assert.True(task.Wait(Timeout), "the server did not answer");
        return task.Result;
    }

    private static bool Answers(JsonElement message, int id) =>
        message.TryGetProperty("id", out var got) && got.ValueKind == JsonValueKind.Number && got.GetInt32() == id;

    private static int End(Process server)
    {
        Assert.True(server.WaitForExit(Timeout), "the server did not end");
        return server.ExitCode;
    }

    [Fact]
    public void The_server_answers_the_editor_and_ends_with_zero_after_shutdown()
    {
        using var server = Start();
        Send(server, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""");
        var initialized = ReceiveUntil(server, m => Answers(m, 1));
        Assert.True(initialized.GetProperty("result").GetProperty("capabilities").TryGetProperty("hoverProvider", out _));

        Send(server, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        Send(server, """{"jsonrpc":"2.0","id":2,"method":"shutdown"}""");
        ReceiveUntil(server, m => Answers(m, 2));
        Send(server, """{"jsonrpc":"2.0","method":"exit"}""");
        Assert.Equal(0, End(server));
    }

    [Fact]
    public void Without_a_shutdown_the_exit_is_one()
    {
        using var server = Start();
        Send(server, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""");
        ReceiveUntil(server, m => Answers(m, 1));
        Send(server, """{"jsonrpc":"2.0","method":"exit"}""");
        Assert.Equal(1, End(server));
    }

    [Fact]
    public void A_module_of_a_package_gets_the_findings_of_its_package()
    {
        // The type error stands in a module the open file imports: only a server that analyses the
        // package — with the standard library beside the binary — says it, under the other file.
        var root = Package(("lyric.toml", AppManifest),
            ("src/util.lyr", "pub fn value(): int {\n    return \"one\";\n}\n"),
            ("src/main.lyr", "import app.util { value };\n\nfn main(): int {\n    return value();\n}\n"));
        var main = Path.Combine(root, "src", "main.lyr");

        using var server = Start();
        Send(server, """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}""");
        ReceiveUntil(server, m => Answers(m, 1));
        Send(server, """{"jsonrpc":"2.0","method":"initialized","params":{}}""");
        Send(server, JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", method = "textDocument/didOpen",
            @params = new { textDocument = new { uri = new Uri(main).AbsoluteUri, languageId = "lyric", version = 1, text = File.ReadAllText(main) } },
        }));

        var published = ReceiveUntil(server, m =>
            m.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics"
            // the server writes the URI of a path in its own form; the file's name is enough here
            && m.GetProperty("params").GetProperty("uri").GetString()!.EndsWith("/src/util.lyr", StringComparison.Ordinal));
        var finding = Assert.Single(published.GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        Assert.StartsWith("LYR-SEM", finding.GetProperty("code").GetString());

        Send(server, """{"jsonrpc":"2.0","id":2,"method":"shutdown"}""");
        ReceiveUntil(server, m => Answers(m, 2));
        Send(server, """{"jsonrpc":"2.0","method":"exit"}""");
        Assert.Equal(0, End(server));
    }
}
