using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyric.Lsp.Dap;
using static Lyric5.Tests.PackageFixture;

namespace Lyric5.Tests;

/// <summary>
/// <c>lyric dap</c>: the debug adapter as the binary an editor starts, lldb-dap behind it. A
/// launch builds the package; the panic filter stops the program where it panics, and the frames
/// are shown by their Lyric names at their Lyric lines. Where no lldb-dap is installed the test
/// says so — on CI's Linux and macOS one is; Windows' is measured on its own.
/// </summary>
public class DapCommandTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private sealed class Session : IDisposable
    {
        private readonly Process _process;
        private int _seq;
        private readonly BlockingQueue _inbox = new();

        public Session()
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
            var start = new ProcessStartInfo(host)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "lyric5.dll"));
            start.ArgumentList.Add("dap");
            _process = Process.Start(start)!;
            _ = Task.Run(Read);
        }

        public int Send(string command, JsonObject? arguments = null)
        {
            var seq = Interlocked.Increment(ref _seq);
            var message = new JsonObject { ["seq"] = seq, ["type"] = "request", ["command"] = command };
            if (arguments is not null) message["arguments"] = arguments;
            var body = Encoding.UTF8.GetBytes(message.ToJsonString());
            var input = _process.StandardInput.BaseStream;
            input.Write(Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"));
            input.Write(body);
            input.Flush();
            return seq;
        }

        private void Read()
        {
            var output = _process.StandardOutput.BaseStream;
            while (true)
            {
                var header = new StringBuilder();
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    var b = output.ReadByte();
                    if (b < 0) { _inbox.Add(null); return; }
                    header.Append((char)b);
                }
                var length = int.Parse(header.ToString().Split("\r\n")
                    .First(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))["Content-Length:".Length..].Trim());
                var body = new byte[length];
                for (var read = 0; read < length;) read += output.Read(body, read, length - read);
                _inbox.Add(JsonNode.Parse(body)!.AsObject());
            }
        }

        /// <summary>What arrived and was passed over, the newest last: said where a wait fails.</summary>
        private readonly List<string> _passed = [];

        public JsonObject Until(Func<JsonObject, bool> wanted, string what)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (true)
            {
                JsonObject? message;
                try { message = _inbox.Take(deadline - DateTime.UtcNow); }
                catch (TimeoutException) { throw new TimeoutException($"no {what}; passed over:\n{string.Join('\n', _passed.TakeLast(30))}"); }
                if (message is null) throw new InvalidOperationException($"the adapter ended before {what}; passed over:\n{string.Join('\n', _passed.TakeLast(30))}");
                if (wanted(message)) return message;
                var shown = message.ToJsonString();
                _passed.Add(shown.Length > 300 ? shown[..300] : shown);
            }
        }

        public JsonObject Response(int seq) =>
            Until(m => (string?)m["type"] == "response" && (int?)m["request_seq"] == seq, $"the answer to {seq}");

        public JsonObject Event(string name) => Until(m => (string?)m["event"] == name, $"the event '{name}'");

        public void Dispose()
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            _process.Dispose();
        }
    }

    /// <summary>A queue a reader thread fills and the test takes from, under a deadline.</summary>
    private sealed class BlockingQueue
    {
        private readonly System.Collections.Concurrent.BlockingCollection<JsonObject?> _items = new();
        public void Add(JsonObject? item) => _items.Add(item);
        public JsonObject? Take(TimeSpan within) =>
            _items.TryTake(out var item, within > TimeSpan.Zero ? within : TimeSpan.Zero) ? item : throw new TimeoutException("the adapter did not answer");
    }

    /// <summary>
    /// Whether there is no lldb-dap to test against: a machine without one, and Windows unless
    /// <c>LYRIC_LLDB_DAP</c> names one — Windows is measured by a job of its own, not gated. CI's
    /// Linux and macOS have one, and fail where they do not.
    /// </summary>
    private static bool Unavailable()
    {
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("LYRIC_LLDB_DAP") is not { Length: > 0 }) return true;
        if (DapAdapter.FindLldbDap() is not null) return false;
        Assert.True(Environment.GetEnvironmentVariable("CI") is null, "CI's Linux and macOS have lldb-dap; this one does not");
        return true;
    }

    private const string Panics = "fn deep(xs: int[], i: int): int {\n    return xs[i];\n}\n\nfn main(): int {\n    let xs = [1, 2, 3];\n    return deep(xs, 5);\n}\n";

    [Fact]
    public void A_panic_stops_the_program_and_its_frames_are_lyrics()
    {
        if (Unavailable()) return;
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Panics));
        using var dap = new Session();

        var initialized = dap.Response(dap.Send("initialize", new JsonObject { ["adapterID"] = "lyric", ["linesStartAt1"] = true }));
        Assert.Equal(["lyric-panic"], initialized["body"]!["exceptionBreakpointFilters"]!.AsArray().Select(f => (string?)f!["filter"]));

        var launch = dap.Send("launch", new JsonObject { ["program"] = root });
        dap.Event("initialized");
        dap.Response(dap.Send("setExceptionBreakpoints", new JsonObject { ["filters"] = new JsonArray(JsonValue.Create("lyric-panic")) }));
        dap.Send("configurationDone");
        Assert.True((bool?)dap.Response(launch)["success"]);

        var stopped = dap.Event("stopped");
        var trace = dap.Response(dap.Send("stackTrace", new JsonObject { ["threadId"] = (int)stopped["body"]!["threadId"]!, ["levels"] = 8 }));
        var frames = trace["body"]!["stackFrames"]!.AsArray().Select(f => f!.AsObject()).ToList();
        var deep = Assert.Single(frames, f => (string?)f["name"] == "app.main.deep");
        Assert.Equal("main.lyr", (string?)deep["source"]?["name"]);
        Assert.Equal(2, (int?)deep["line"]);
        Assert.Contains(frames, f => (string?)f["name"] == "app.main.main");
        // the runtime's frame is shown, set back
        Assert.Equal("subtle", (string?)frames[0]["presentationHint"]);

        dap.Response(dap.Send("disconnect", new JsonObject { ["terminateDebuggee"] = true }));
    }

    [Fact]
    public void Without_the_filter_the_panic_ends_the_program()
    {
        // The counter-check: the stop above is the filter's.
        if (Unavailable()) return;
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", Panics));
        using var dap = new Session();

        dap.Response(dap.Send("initialize", new JsonObject { ["adapterID"] = "lyric" }));
        dap.Send("launch", new JsonObject { ["program"] = root });
        dap.Event("initialized");
        dap.Response(dap.Send("setExceptionBreakpoints", new JsonObject { ["filters"] = new JsonArray() }));
        dap.Send("configurationDone");

        var ended = dap.Until(m => (string?)m["event"] is "stopped" or "exited", "the end");
        Assert.Equal("exited", (string?)ended["event"]);
        Assert.Equal(101, (int?)ended["body"]!["exitCode"]);
    }

    [Fact]
    public void A_program_that_does_not_build_is_refused_with_what_the_build_said()
    {
        if (Unavailable()) return;
        var root = Package(("lyric.toml", AppManifest), ("src/main.lyr", "fn main(): int {\n    return \"no\";\n}\n"));
        using var dap = new Session();

        dap.Response(dap.Send("initialize", new JsonObject { ["adapterID"] = "lyric" }));
        var launch = dap.Send("launch", new JsonObject { ["program"] = root });
        var said = dap.Until(m => (string?)m["event"] == "output" && ((string?)m["body"]!["output"])!.Contains("LYR-SEM0001"), "the build's finding");
        Assert.NotNull(said);
        var answer = dap.Response(launch);
        Assert.False((bool?)answer["success"]);
        Assert.Contains("did not build", (string?)answer["message"]);
        dap.Send("disconnect");
    }
}
