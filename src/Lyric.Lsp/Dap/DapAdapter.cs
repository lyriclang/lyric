using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lyric.Lsp.Protocol;

namespace Lyric.Lsp.Dap;

/// <summary>A program built to debug: the binary, or <c>null</c>; what the build said; and the
/// names its debug information writes for the places its sources lie, with the places.</summary>
public sealed record DebugBuild(string? Executable, string Said, IReadOnlyList<(string Name, string Place)> Places);

/// <summary>How the adapter builds the program it launches.</summary>
/// <param name="Build">Builds what a <c>launch</c> names — a package's directory or a file — in the
/// debug profile; with a test's name, the package's tests whose names hold it.</param>
public sealed record DapAdapterOptions(Func<string, string?, DebugBuild> Build)
{
    /// <summary>The lldb-dap to run; found where it usually is without it.</summary>
    public string? LldbDap { get; init; }

    /// <summary>The Python lldb imports at the start: the toolchain's <c>lldb/lyric_lldb.py</c>,
    /// which shows Lyric's values as Lyric writes them.</summary>
    public string? LldbScript { get; init; }
}

/// <summary>
/// <c>lyric dap</c>: the Debug Adapter Protocol spoken with the editor, passed to lldb-dap and
/// back, changed where Lyric has something to say — the program a <c>launch</c> names is built
/// first, a panic is a breakpoint the editor turns on and off (<c>lyric-panic</c>), and a frame is
/// shown by its Lyric name. Everything else — breakpoints with conditions, logpoints, stepping,
/// threads, attaching — is lldb's, unchanged.
/// </summary>
public sealed class DapAdapter(Stream clientInput, Stream clientOutput, DapAdapterOptions options)
{
    /// <summary>The filter a panic stops under, on unless the editor turns it off.</summary>
    public const string PanicFilter = "lyric-panic";

    /// <summary>The runtime's one door every panic goes through.</summary>
    public const string PanicFunction = "lyr_panic";

    /// <summary>The breakpoint's name in lldb, which takes no dash.</summary>
    private const string PanicBreakpoint = "lyric_panic";

    /// <summary>The sequence numbers of what the adapter itself sends: far above both sides', so
    /// that nothing it says is taken for something either side said.</summary>
    private int _ownSeq = 2_000_000;

    private readonly LspConnection _client = new(clientInput, clientOutput);
    private LspConnection? _lldb;
    private Process? _process;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _globals = new(StringComparer.Ordinal);
    private bool _panics = true;
    private bool _launched;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var first = await ReadAsync(_client, cancellationToken).ConfigureAwait(false);
        if (first is null) return 1;
        var lldbDap = options.LldbDap ?? FindLldbDap();
        if (lldbDap is null)
        {
            await RefuseAsync(first, "lldb-dap was not found: install LLVM's lldb, or set LYRIC_LLDB_DAP to it", cancellationToken).ConfigureAwait(false);
            return 1;
        }

        var start = new ProcessStartInfo(lldbDap) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false };
        _process = Process.Start(start);
        if (_process is null)
        {
            await RefuseAsync(first, $"lldb-dap at '{lldbDap}' did not start", cancellationToken).ConfigureAwait(false);
            return 1;
        }
        _lldb = new LspConnection(_process.StandardOutput.BaseStream, _process.StandardInput.BaseStream);

        var back = Task.Run(() => FromLldbAsync(cancellationToken), cancellationToken);
        await FromClientAsync(first, cancellationToken).ConfigureAwait(false);
        await back.ConfigureAwait(false);
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    /// <summary>The editor's messages, the first one already read.</summary>
    private async Task FromClientAsync(JsonObject first, CancellationToken cancellationToken)
    {
        for (JsonObject? message = first; message is not null; message = await ReadAsync(_client, cancellationToken).ConfigureAwait(false))
        {
            switch ((string?)message["command"])
            {
                case "initialize":
                    WithDefaults(message);
                    break;
                case "launch":
                    if (await LaunchAsync(message, cancellationToken).ConfigureAwait(false) is not { } launch) continue;
                    message = launch;
                    break;
                case "setExceptionBreakpoints":
                    _panics = message["arguments"]?["filters"] is JsonArray filters && filters.Any(f => (string?)f == PanicFilter);
                    await AnswerAsync(message, new JsonObject { ["breakpoints"] = new JsonArray() }, cancellationToken).ConfigureAwait(false);
                    if (_launched) await LldbCommandAsync($"breakpoint {(_panics ? "enable" : "disable")} {PanicBreakpoint}", cancellationToken).ConfigureAwait(false);
                    continue;
            }
            await WriteAsync(_lldb!, message, cancellationToken).ConfigureAwait(false);
            if ((string?)message["command"] == "disconnect") break;
        }
        try { _process!.StandardInput.Close(); } catch (IOException) { }
    }

    /// <summary>
    /// An <c>initialize</c> with what the protocol takes for granted written out: the paths are paths,
    /// lines and columns count from 1. lldb-dap 21 refuses one without <c>pathFormat</c> — and its
    /// debugger is never made, so every request after it fails — where 23 reads the default.
    /// </summary>
    private static void WithDefaults(JsonObject initialize)
    {
        var arguments = initialize["arguments"] as JsonObject ?? [];
        arguments["pathFormat"] ??= "path";
        arguments["linesStartAt1"] ??= true;
        arguments["columnsStartAt1"] ??= true;
        initialize["arguments"] = arguments;
    }

    /// <summary>lldb-dap's messages, on their way to the editor.</summary>
    private async Task FromLldbAsync(CancellationToken cancellationToken)
    {
        while (await ReadAsync(_lldb!, cancellationToken).ConfigureAwait(false) is { } message)
        {
            // the answer to what the adapter asked itself: the editor did not ask it
            if ((string?)message["type"] == "response" && (int?)message["request_seq"] >= 2_000_000) continue;
            if ((string?)message["type"] == "response") Rewrite(message);
            await WriteAsync(_client, message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>What lldb-dap answers, as the editor sees it: the panic filter beside lldb's own,
    /// a frame by its Lyric name.</summary>
    private void Rewrite(JsonObject response)
    {
        var command = (string?)response["command"];
        if (command == "initialize" && response["body"] is JsonObject body)
        {
            // lldb's own filters are C++'s and Objective-C's: a Lyric program throws neither
            var filters = new JsonArray();
            filters.Add((JsonNode)new JsonObject { ["filter"] = PanicFilter, ["label"] = "Panic", ["default"] = true });
            body["exceptionBreakpointFilters"] = filters;
        }
        else if (command == "variables" && response["body"] is JsonObject scope && scope["variables"] is JsonArray variables)
        {
            // a local by its Lyric name, l3_count is count; what the compiler made — a temporary,
            // its storage, the error in flight, the poll's count, a local of its own (l7__match1) — left out
            foreach (var variable in variables.OfType<JsonObject>().ToList())
            {
                if ((string?)variable["name"] is not { } name) continue;
                if (LocalName(name) is { } lyric) variable["name"] = lyric;
                else if (_globals.TryGetValue(name, out var global)) variable["name"] = global;
                else if (IsCompilers(name) || name.StartsWith("lyr_", StringComparison.Ordinal)) { variables.Remove(variable); continue; }
                // a reference shows what it holds, not where: '0x7fbe… "hello"' is '"hello"'
                if ((string?)variable["value"] is { } value && value.StartsWith("0x", StringComparison.Ordinal)
                    && value.IndexOf(' ') is > 2 and var space)
                    variable["value"] = value[(space + 1)..];
            }
        }
        else if (command == "stackTrace" && response["body"] is JsonObject trace && trace["stackFrames"] is JsonArray frames)
        {
            foreach (var frame in frames.OfType<JsonObject>())
            {
                if ((string?)frame["name"] is not { } name) continue;
                var lyric = LyricName(name);
                frame["name"] = lyric;
                // a frame without a Lyric name is the runtime's — the panic's own, the entry, C's
                // main —: shown, set back
                if (lyric == name) frame["presentationHint"] = "subtle";
            }
        }
    }

    /// <summary>A local's Lyric name: <c>l3_count</c> is <c>count</c>; <c>null</c> for any other
    /// name, and for a local the compiler made (<c>l7__match1</c>).</summary>
    public static string? LocalName(string name)
    {
        if (name.Length < 3 || name[0] != 'l' || !char.IsAsciiDigit(name[1])) return null;
        var at = 1;
        while (at < name.Length && char.IsAsciiDigit(name[at])) at++;
        if (at >= name.Length - 1 || name[at] != '_' || name[at + 1] == '_') return null;
        return name[(at + 1)..];
    }

    /// <summary>Whether a variable is the compiler's: a temporary (<c>t4</c>, <c>t4_s</c>), a local
    /// it introduced (<c>l7__match1</c>), the error in flight and the stashes, the poll's count.</summary>
    public static bool IsCompilers(string name) =>
        (name.Length > 1 && name[0] == 't' && name[1..].Split('_')[0].All(char.IsAsciiDigit) && name[1..].Split('_')[0].Length > 0)
        || (name.Length > 1 && name[0] == 'l' && char.IsAsciiDigit(name[1]) && name.Contains("__", StringComparison.Ordinal))
        || name is "lyr_e" or "lyr_poll" or "lyr_env" or "lyr_err" || (name.StartsWith("lyr_s", StringComparison.Ordinal) && name[5..].All(char.IsAsciiDigit));

    /// <summary>A frame's name as Lyric writes it: <c>lyr_app_main_main</c> is <c>app.main.main</c>,
    /// by the names the build wrote; a name lldb adds to — an argument list, an offset — keeps its
    /// rest.</summary>
    public string LyricName(string name)
    {
        var end = 0;
        while (end < name.Length && (char.IsAsciiLetterOrDigit(name[end]) || name[end] == '_')) end++;
        return _names.TryGetValue(name[..end], out var lyric) ? lyric + name[end..] : name;
    }

    /// <summary>
    /// A <c>launch</c> as lldb-dap takes it: the program built first — a package's directory or a
    /// file in <c>program</c> —, its binary in its place, the toolchain's lldb commands read, and
    /// the panic breakpoint set before it runs. <c>null</c> after answering a build that failed.
    /// </summary>
    private async Task<JsonObject?> LaunchAsync(JsonObject launch, CancellationToken cancellationToken)
    {
        var arguments = launch["arguments"] as JsonObject ?? [];
        if ((string?)arguments["program"] is not { } program)
        {
            await AnswerAsync(launch, null, cancellationToken, "the launch names no program: a package's directory or a .lyr file").ConfigureAwait(false);
            return null;
        }
        // 'test': the package's tests whose names hold it, the program 'lyric test' runs
        var test = (string?)arguments["test"];
        arguments.Remove("test");
        var (executable, said, places) = options.Build(program, test);
        if (said.Length > 0) await OutputAsync(said, cancellationToken).ConfigureAwait(false);
        if (executable is null)
        {
            await AnswerAsync(launch, null, cancellationToken, $"'{program}' did not build").ConfigureAwait(false);
            return null;
        }

        _names.Clear();
        _globals.Clear();
        var namesFile = executable + ".lyric-names";
        if (File.Exists(namesFile))
            foreach (var line in File.ReadAllLines(namesFile))
                switch (line.Split('\t'))
                {
                    case [var c, var lyric]: _names[c] = lyric; break;
                    case ["global", var c, var lyric]: _globals[c] = lyric; break;
                }

        arguments["program"] = executable;
        arguments["cwd"] ??= Directory.Exists(program) ? program : Path.GetDirectoryName(Path.GetFullPath(program));
        var init = arguments["initCommands"] as JsonArray ?? [];
        if (options.LldbScript is { } script && File.Exists(script)) init.Add((JsonNode)JsonValue.Create($"command script import '{script}'"));
        // the binary names its sources by their packages' names (app/src/main.lyr), the same
        // wherever it was built: lldb finds them where they lie
        if (places.Count > 0)
            init.Add((JsonNode)JsonValue.Create("settings set target.source-map " + string.Join(' ', places.Select(p => $"'{p.Name}' '{p.Place}'"))));
        arguments["initCommands"] = init;
        var preRun = arguments["preRunCommands"] as JsonArray ?? [];
        preRun.Add((JsonNode)JsonValue.Create($"breakpoint set -n {PanicFunction} -N {PanicBreakpoint}"));
        if (!_panics) preRun.Add((JsonNode)JsonValue.Create($"breakpoint disable {PanicBreakpoint}"));
        arguments["preRunCommands"] = preRun;
        launch["arguments"] = arguments;
        _launched = true;
        return launch;
    }

    /// <summary>An lldb command the adapter runs itself, through the evaluation of the REPL.</summary>
    private Task LldbCommandAsync(string command, CancellationToken cancellationToken) =>
        WriteAsync(_lldb!, new JsonObject
        {
            ["seq"] = Interlocked.Increment(ref _ownSeq),
            ["type"] = "request",
            ["command"] = "evaluate",
            ["arguments"] = new JsonObject { ["expression"] = "`" + command, ["context"] = "repl" },
        }, cancellationToken);

    private Task AnswerAsync(JsonObject request, JsonObject? body, CancellationToken cancellationToken, string? refused = null)
    {
        var response = new JsonObject
        {
            ["seq"] = Interlocked.Increment(ref _ownSeq),
            ["type"] = "response",
            ["request_seq"] = (int?)request["seq"] ?? 0,
            ["command"] = (string?)request["command"],
            ["success"] = refused is null,
        };
        if (refused is not null) response["message"] = refused;
        if (body is not null) response["body"] = body;
        return WriteAsync(_client, response, cancellationToken);
    }

    private Task OutputAsync(string text, CancellationToken cancellationToken) =>
        WriteAsync(_client, new JsonObject
        {
            ["seq"] = Interlocked.Increment(ref _ownSeq),
            ["type"] = "event",
            ["event"] = "output",
            ["body"] = new JsonObject { ["category"] = "console", ["output"] = text },
        }, cancellationToken);

    /// <summary>An editor that cannot be served: its first request refused, then its messages read
    /// until it lets go.</summary>
    private async Task RefuseAsync(JsonObject first, string why, CancellationToken cancellationToken)
    {
        for (JsonObject? message = first; message is not null; message = await ReadAsync(_client, cancellationToken).ConfigureAwait(false))
        {
            if ((string?)message["type"] != "request") continue;
            await AnswerAsync(message, null, cancellationToken, why).ConfigureAwait(false);
            if ((string?)message["command"] == "disconnect") return;
        }
    }

    private static async Task<JsonObject?> ReadAsync(LspConnection connection, CancellationToken cancellationToken)
    {
        while (true)
        {
            var payload = await connection.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (payload is null) return null;
            if (JsonNode.Parse(payload) is JsonObject message) return message;
        }
    }

    private static Task WriteAsync(LspConnection connection, JsonObject message, CancellationToken cancellationToken) =>
        connection.WriteAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), cancellationToken);

    /// <summary>
    /// lldb-dap where it usually is: <c>LYRIC_LLDB_DAP</c>, then the path — <c>lldb-dap</c>, its
    /// older name <c>lldb-vscode</c>, and the versioned names a Linux distribution installs them
    /// under (<c>lldb-dap-18</c>), the newest first —, then Xcode's on macOS.
    /// </summary>
    public static string? FindLldbDap()
    {
        if (Environment.GetEnvironmentVariable("LYRIC_LLDB_DAP") is { Length: > 0 } configured) return configured;
        var suffix = OperatingSystem.IsWindows() ? ".exe" : "";
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var dir in path)
            foreach (var name in new[] { "lldb-dap", "lldb-vscode" })
                if (File.Exists(Path.Combine(dir, name + suffix))) return Path.Combine(dir, name + suffix);
        foreach (var dir in path.Where(Directory.Exists))
            if (Directory.EnumerateFiles(dir, "lldb-dap-*")
                    .Select(f => (File: f, Version: int.TryParse(Path.GetFileName(f)["lldb-dap-".Length..], out var v) ? v : -1))
                    .Where(f => f.Version >= 0).OrderByDescending(f => f.Version).FirstOrDefault() is { File: { } versioned })
                return versioned;
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                using var xcrun = Process.Start(new ProcessStartInfo("xcrun", "-f lldb-dap") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
                var found = xcrun?.StandardOutput.ReadToEnd().Trim();
                xcrun?.WaitForExit();
                if (xcrun?.ExitCode == 0 && File.Exists(found)) return found;
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return null;
    }
}
