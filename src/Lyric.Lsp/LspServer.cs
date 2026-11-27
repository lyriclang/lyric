using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Lyric.Core;
using Lyric.Lsp.Analysis;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;

namespace Lyric.Lsp;

/// <summary>What a server run needs besides its two streams.</summary>
public sealed record LspServerOptions
{
    /// <summary>
    /// How long a document must be quiet before it is compiled.
    ///
    /// <para>Its purpose is to skip versions, not to delay the answer, and it is set to coalesce a
    /// burst of keystrokes and nothing more: the compile that follows checks the standard library
    /// with the package, a few hundred milliseconds in a server that has warmed up, and every
    /// millisecond waited here is added to that.</para>
    /// </summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(50);

    /// <summary>How long an analysis after a file's first runs before the client is shown its
    /// progress: one that ends sooner is not worth a bar that comes and goes.</summary>
    public TimeSpan ProgressDelay { get; init; } = TimeSpan.FromMilliseconds(750);

    /// <summary>The version the server says it is in <c>initialize</c>: the toolchain's.</summary>
    public string Version { get; init; } = ToolchainVersion.Value;

    /// <summary>As in <see cref="Compiler.CompilerOptions.StdlibRoot"/>: <c>null</c> is the
    /// standard library beside the binary, the one <c>lyric build</c> compiles against.</summary>
    public string? StdlibRoot { get; init; }
}

/// <summary>
/// The server: one read loop, a lifecycle, and a dispatcher.
///
/// <para>The loop NEVER WAITS ON AN ANALYSIS. Everything a message triggers is either immediate or
/// handed to <see cref="AnalysisService"/>, because the message that makes the current analysis
/// pointless is the next one in the stream, and a loop parked on a compile cannot read it.</para>
///
/// <para>The lifecycle is a real state machine and not a pair of flags. A client is entitled to an
/// error rather than an answer before <c>initialize</c> and after <c>shutdown</c>, and the exit
/// code says which of the two ways the session ended — the one part of this protocol a process
/// supervisor can see.</para>
/// </summary>
public sealed class LspServer : IDisposable
{
    private enum State
    {
        /// <summary>Nothing but <c>initialize</c> is answered.</summary>
        Starting,

        Running,

        /// <summary><c>shutdown</c> has been answered; only <c>exit</c> is still expected.</summary>
        ShuttingDown,
    }

    private readonly LspConnection _connection;
    private readonly LspServerOptions _options;
    private readonly DocumentStore _documents = new();
    private readonly AnalysisService _analysis;

    private State _state = State.Starting;
    private bool _exitRequested;

    /// <summary>Whether the client reads <see cref="LocationLink"/>. Read once from
    /// <c>initialize</c>, because a client's capabilities do not change while it runs.</summary>
    private bool _definitionLinkSupport;

    private bool _completionSnippetSupport;

    /// <summary>Whether the client reads nested document symbols. The flat alternative is not
    /// produced, so a client without this gets no outline rather than a wrong one.</summary>
    private bool _hierarchicalSymbols;

    /// <summary>Whether the client accepts a file-watch registration. Without it the server never
    /// hears about changes behind the editor and the diagnostics of closed files go stale.</summary>
    private bool _watchedFilesSupport;

    /// <summary>The id of the last request THIS side issued. Distinct from the client's ids by
    /// construction: each side numbers its own requests. Taken with <see cref="Interlocked"/>,
    /// because a progress token is asked for from an analysis, off the read loop.</summary>
    private int _lastRequestId;

    /// <summary>Whether the client draws progress the server reports (E20).</summary>
    private bool _workDoneProgress;

    /// <summary>The number of the last progress token: each analysis reports on a token of its own.
    /// </summary>
    private int _progressCount;

    /// <summary>The requests of this side that wait for their answer, by id: what a progress token
    /// needs before it is used. Completed with whether the client accepted.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, TaskCompletionSource<bool>> _awaitingAnswer = new();

    /// <summary>All in-flight client requests dispatched off the read loop, by raw id text: what
    /// <c>$/cancelRequest</c> withdraws.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _inFlight =
        new(StringComparer.Ordinal);

    /// <summary>Latest interactive completion/signatureHelp request per method+URI so rapid typing
    /// cancels superseded requests immediately.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _interactiveRequests =
        new(StringComparer.Ordinal);

    private readonly CancellationTokenSource _shutdownCts = new();

    /// <summary>Whether the client asks for an open document's diagnostics itself
    /// (<c>textDocument/diagnostic</c>), and whether it asks again when told to.</summary>
    private bool _pullDiagnostics;
    private bool _diagnosticRefresh;
    private bool _semanticTokensRefresh;
    private bool _inlayHintRefresh;
    private bool _codeLensRefresh;

    /// <summary>The newest diagnostics of each file, by URI: what a pull answers.</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PublishDiagnosticsParams> _lastDiagnostics =
        new(StringComparer.Ordinal);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _diagnosticResultIds =
        new(StringComparer.Ordinal);
    private int _diagnosticVersion;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string ResultId, IReadOnlyList<int> Data)> _lastSemanticTokens =
        new(StringComparer.Ordinal);
    private int _semanticTokenVersion;

    public LspServer(Stream input, Stream output, LspServerOptions? options = null)
    {
        _options = options ?? new LspServerOptions();
        _connection = new LspConnection(input, output);
        _analysis = new AnalysisService(
            _documents,
            PublishDiagnosticsAsync,
            LogAsync,
            _options.Debounce,
            _options.StdlibRoot,
            _options.Version,
            BeginProgress,
            _options.ProgressDelay,
            OnAnalysisCompleteAsync);
    }

    /// <summary>
    /// Serves until <c>exit</c> or end of stream, and returns the process exit code.
    ///
    /// <para><see cref="ExitCodes.Success"/> only after a <c>shutdown</c> was received.
    /// An <c>exit</c> without one, or a stream that simply stops, means the client went away
    /// unexpectedly and is <see cref="ExitCodes.Failure"/> — the specification asks for exactly
    /// this distinction, and it is what tells a crashed editor from a closed one.</para>
    /// </summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            while (!_exitRequested)
            {
                var payload = await _connection.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (payload is null) break; // the client closed the stream

                await HandlePayloadAsync(payload, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is shutting us down. Not a protocol fault. A cancellation from anywhere else
            // is a fault of the server and leaves as one, rather than as a client that went away.
        }
        catch (LspProtocolException exception)
        {
            // The framing is gone, so no further message can be located in the stream. Reporting
            // it over that same stream would be guesswork; the exit code carries it instead.
            await SafeLogAsync($"protocol error: {exception.Message}").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        return _state == State.ShuttingDown ? ExitCodes.Success : ExitCodes.Failure;
    }

    private async Task HandlePayloadAsync(byte[] payload, CancellationToken cancellationToken)
    {
        JsonRpcMessage? message;
        try
        {
            message = JsonSerializer.Deserialize(payload, LspJson.Default.JsonRpcMessage);
        }
        catch (JsonException)
        {
            // The frame was intact, so the stream is still usable and the next message will be
            // read normally. Only this one is lost, and the client is told which error it was.
            await SendErrorAsync(null, JsonRpcErrorCodes.ParseError, "message is not valid JSON",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message is not null && message.IsResponse)
        {
            // The answer to a request this side sent. The watch registration and the refresh wait
            // for nothing; a progress token's reports wait for its answer. A refusal is only worth a
            // line in the log: the server works without any of them.
            if (message.Id is { ValueKind: JsonValueKind.Number } answered && answered.TryGetInt32(out var number)
                && _awaitingAnswer.TryRemove(number, out var waiting))
                waiting.TrySetResult(message.Error is null);
            if (message.Error is { } error)
                await SafeLogAsync($"the client refused a request: {error.Message}")
                    .ConfigureAwait(false);
            return;
        }

        if (message is null || message.Method is null)
        {
            await SendErrorAsync(message?.Id, JsonRpcErrorCodes.InvalidRequest,
                "expected a request or a notification", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (message.IsRequest)
            await HandleRequestAsync(message, message.Id!.Value, cancellationToken).ConfigureAwait(false);
        else
            await HandleNotificationAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleRequestAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        switch (message.Method)
        {
            case LspMethods.Initialize when _state == State.Starting:
                _state = State.Running;

                // Unreadable parameters are not an error here: everything this server takes from
                // them has a default, and refusing to initialize over an unknown capability would
                // be a server that only talks to the clients it was tested against.
                var capabilities = LspJson.ReadParams(
                    message.Params, LspJson.Default.InitializeParams)?.Capabilities;
                var client = capabilities?.TextDocument;

                _definitionLinkSupport = client?.Definition?.LinkSupport ?? false;
                _completionSnippetSupport = client?.Completion?.CompletionItem?.SnippetSupport ?? false;
                _hierarchicalSymbols =
                    client?.DocumentSymbol?.HierarchicalDocumentSymbolSupport ?? false;
                _watchedFilesSupport =
                    capabilities?.Workspace?.DidChangeWatchedFiles?.DynamicRegistration ?? false;
                _pullDiagnostics = client?.Diagnostic is { ValueKind: JsonValueKind.Object };
                _diagnosticRefresh = capabilities?.Workspace?.Diagnostics?.RefreshSupport ?? false;
                _semanticTokensRefresh = capabilities?.Workspace?.SemanticTokens?.RefreshSupport ?? false;
                _inlayHintRefresh = capabilities?.Workspace?.InlayHint?.RefreshSupport ?? false;
                _codeLensRefresh = capabilities?.Workspace?.CodeLens?.RefreshSupport ?? false;
                _workDoneProgress = capabilities?.Window?.WorkDoneProgress ?? false;

                await SendResultAsync(id, BuildInitializeResult(),
                    LspJson.Default.InitializeResult, cancellationToken).ConfigureAwait(false);
                return;

            case LspMethods.Initialize:
                await SendErrorAsync(id, JsonRpcErrorCodes.InvalidRequest,
                    "the server is already initialized", cancellationToken).ConfigureAwait(false);
                return;

            case LspMethods.Hover when _state == State.Running:
                DispatchRequest(id, LspMethods.Hover, ct => SendHoverAsync(message, id, ct));
                return;

            case LspMethods.Definition when _state == State.Running:
                DispatchRequest(id, LspMethods.Definition, ct => SendDefinitionAsync(message, id, ct));
                return;

            case LspMethods.TypeDefinition when _state == State.Running:
                DispatchRequest(id, LspMethods.TypeDefinition, ct => SendTypeDefinitionAsync(message, id, ct));
                return;

            case LspMethods.Implementation when _state == State.Running:
                DispatchRequest(id, LspMethods.Implementation, ct => SendImplementationAsync(message, id, ct));
                return;

            case LspMethods.DocumentSymbol when _state == State.Running:
                DispatchRequest(id, LspMethods.DocumentSymbol, ct => SendDocumentSymbolsAsync(message, id, ct));
                return;

            case LspMethods.References when _state == State.Running:
                DispatchRequest(id, LspMethods.References, ct => SendReferencesAsync(message, id, ct));
                return;

            case LspMethods.DocumentHighlight when _state == State.Running:
                DispatchRequest(id, LspMethods.DocumentHighlight, ct => SendDocumentHighlightsAsync(message, id, ct));
                return;

            case LspMethods.SelectionRange when _state == State.Running:
                DispatchRequest(id, LspMethods.SelectionRange, ct => SendSelectionRangesAsync(message, id, ct));
                return;

            case LspMethods.DocumentLink when _state == State.Running:
                DispatchRequest(id, LspMethods.DocumentLink, ct => SendDocumentLinksAsync(message, id, ct));
                return;

            case LspMethods.PrepareCallHierarchy when _state == State.Running:
                DispatchRequest(id, LspMethods.PrepareCallHierarchy, ct => SendPrepareCallHierarchyAsync(message, id, ct));
                return;

            case LspMethods.CallHierarchyIncoming when _state == State.Running:
                DispatchRequest(id, LspMethods.CallHierarchyIncoming, ct => SendCallHierarchyIncomingAsync(message, id, ct));
                return;

            case LspMethods.CallHierarchyOutgoing when _state == State.Running:
                DispatchRequest(id, LspMethods.CallHierarchyOutgoing, ct => SendCallHierarchyOutgoingAsync(message, id, ct));
                return;

            case LspMethods.PrepareTypeHierarchy when _state == State.Running:
                DispatchRequest(id, LspMethods.PrepareTypeHierarchy, ct => SendPrepareTypeHierarchyAsync(message, id, ct));
                return;

            case LspMethods.TypeHierarchySupertypes when _state == State.Running:
                DispatchRequest(id, LspMethods.TypeHierarchySupertypes, ct => SendTypeHierarchySupertypesAsync(message, id, ct));
                return;

            case LspMethods.TypeHierarchySubtypes when _state == State.Running:
                DispatchRequest(id, LspMethods.TypeHierarchySubtypes, ct => SendTypeHierarchySubtypesAsync(message, id, ct));
                return;

            case LspMethods.Completion when _state == State.Running:
                DispatchRequest(id, LspMethods.Completion, ct => SendCompletionAsync(message, id, ct),
                    ExtractPositionUri(message));
                return;

            case LspMethods.CompletionResolve when _state == State.Running:
                DispatchRequest(id, LspMethods.CompletionResolve, ct => SendCompletionResolveAsync(message, id, ct));
                return;

            case LspMethods.PrepareRename when _state == State.Running:
                DispatchRequest(id, LspMethods.PrepareRename, ct => SendPrepareRenameAsync(message, id, ct));
                return;

            case LspMethods.Rename when _state == State.Running:
                DispatchRequest(id, LspMethods.Rename, ct => SendRenameAsync(message, id, ct));
                return;

            case LspMethods.WorkspaceSymbol when _state == State.Running:
                DispatchRequest(id, LspMethods.WorkspaceSymbol, ct => SendWorkspaceSymbolsAsync(message, id, ct));
                return;

            case LspMethods.SemanticTokensFull when _state == State.Running:
                DispatchRequest(id, LspMethods.SemanticTokensFull, ct => SendSemanticTokensAsync(message, id, ct));
                return;

            case LspMethods.SemanticTokensDelta when _state == State.Running:
                DispatchRequest(id, LspMethods.SemanticTokensDelta, ct => SendSemanticTokensDeltaAsync(message, id, ct));
                return;

            case LspMethods.SemanticTokensRange when _state == State.Running:
                DispatchRequest(id, LspMethods.SemanticTokensRange, ct => SendSemanticTokensRangeAsync(message, id, ct));
                return;

            case LspMethods.SignatureHelp when _state == State.Running:
                DispatchRequest(id, LspMethods.SignatureHelp, ct => SendSignatureHelpAsync(message, id, ct),
                    ExtractPositionUri(message));
                return;

            case LspMethods.FoldingRange when _state == State.Running:
                DispatchRequest(id, LspMethods.FoldingRange, ct => SendFoldingRangesAsync(message, id, ct));
                return;

            case LspMethods.InlayHint when _state == State.Running:
                DispatchRequest(id, LspMethods.InlayHint, ct => SendInlayHintsAsync(message, id, ct));
                return;

            case LspMethods.Formatting when _state == State.Running:
                DispatchRequest(id, LspMethods.Formatting, ct => SendFormattingAsync(message, id, ct));
                return;

            case LspMethods.CodeLens when _state == State.Running:
                DispatchRequest(id, LspMethods.CodeLens, ct => SendCodeLensesAsync(message, id, ct));
                return;

            case LspMethods.Tests when _state == State.Running:
                DispatchRequest(id, LspMethods.Tests, ct => SendTestsAsync(message, id, ct));
                return;

            case LspMethods.ExecuteCommand when _state == State.Running:
                DispatchRequest(id, LspMethods.ExecuteCommand, ct => SendCommandAsync(message, id, ct));
                return;

            case LspMethods.ExpandMacro when _state == State.Running:
                DispatchRequest(id, LspMethods.ExpandMacro, ct => SendExpandMacroAsync(message, id, ct));
                return;

            case LspMethods.CodeAction when _state == State.Running:
                DispatchRequest(id, LspMethods.CodeAction, ct => SendCodeActionsAsync(message, id, ct));
                return;

            case LspMethods.Diagnostic when _state == State.Running:
                DispatchRequest(id, LspMethods.Diagnostic, ct => SendDocumentDiagnosticsAsync(message, id, ct));
                return;

            case LspMethods.WorkspaceDiagnostic when _state == State.Running:
                DispatchRequest(id, LspMethods.WorkspaceDiagnostic, ct => SendWorkspaceDiagnosticsAsync(message, id, ct));
                return;

            case LspMethods.WillRenameFiles when _state == State.Running:
                DispatchRequest(id, LspMethods.WillRenameFiles, ct => SendWillRenameFilesAsync(message, id, ct));
                return;

            case LspMethods.ServerStatus when _state == State.Running:
                DispatchRequest(id, LspMethods.ServerStatus, ct => SendServerStatusAsync(id, ct));
                return;

            case LspMethods.Shutdown when _state == State.Running:
                _state = State.ShuttingDown;

                // Everything pending is withdrawn here rather than at 'exit': after this answer
                // the client is entitled to assume the server produces nothing further, and a
                // diagnostic arriving afterwards would be exactly that.
                _shutdownCts.Cancel();
                _analysis.Dispose();
                await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
                return;
        }

        if (_state == State.Starting)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.ServerNotInitialized,
                $"'{message.Method}' before 'initialize'", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_state == State.ShuttingDown)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidRequest,
                $"'{message.Method}' after 'shutdown'", cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendErrorAsync(id, JsonRpcErrorCodes.MethodNotFound,
            $"'{message.Method}' is not implemented", cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleNotificationAsync(
        JsonRpcMessage message, CancellationToken cancellationToken)
    {
        // 'exit' is answered in every state, including before 'initialize'. It is the one way out
        // of a session that never started.
        if (message.Method == LspMethods.Exit)
        {
            _exitRequested = true;
            return;
        }

        if (_state != State.Running) return;

        switch (message.Method)
        {
            case LspMethods.Initialized:
                // The one dynamic registration: file watches. Asked for here rather than in the
                // initialize answer because the protocol has no static form for them.
                if (_watchedFilesSupport)
                    await RegisterFileWatchesAsync(cancellationToken).ConfigureAwait(false);
                return;

            case LspMethods.DidChangeWatchedFiles:
                HandleDidChangeWatchedFiles(message);
                return;

            case LspMethods.DidOpen:
                HandleDidOpen(message);
                return;

            case LspMethods.DidChange:
                HandleDidChange(message);
                return;

            case LspMethods.DidClose:
                await HandleDidCloseAsync(message, cancellationToken).ConfigureAwait(false);
                return;

            case LspMethods.DidSave:
                // The buffer is authoritative and was analysed when it changed. Saving adds
                // nothing to know.
                return;

            case LspMethods.CancelRequest:
                if (LspJson.ReadParams(message.Params, LspJson.Default.CancelParams) is { } cancel
                    && _inFlight.TryRemove(cancel.Id.GetRawText(), out var active))
                {
                    try { active.Cancel(); }
                    catch (ObjectDisposedException) { /* answered meanwhile */ }
                }
                return;

            default:
                // An unknown notification is dropped. The protocol requires that of the '$/'
                // namespace and permits it everywhere else.
                return;
        }
    }

    private void HandleDidOpen(JsonRpcMessage message)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.DidOpenTextDocumentParams);
        if (parameters is null) return;

        var document = _documents.Set(
            parameters.TextDocument.Uri,
            parameters.TextDocument.Version,
            parameters.TextDocument.Text);

        if (document is not null) _analysis.Schedule(document);
    }

    private void HandleDidChange(JsonRpcMessage message)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.DidChangeTextDocumentParams);
        if (parameters is null) return;

        // An empty list is not an error, it is a change that changed nothing.
        if (parameters.ContentChanges.Count == 0) return;

        // In order: each change's range counts in the text the one before it left. A change
        // without a range is the whole document; a range on a document this server does not hold
        // has nothing to count in, and the change is dropped rather than applied to nothing.
        var text = _documents.ByUri(parameters.TextDocument.Uri)?.Text;
        foreach (var change in parameters.ContentChanges)
        {
            if (change.Range is not { } range)
            {
                text = change.Text;
                continue;
            }
            if (text is null) return;
            var start = TextOffsets.ToOffset(text, range.Start);
            var end = Math.Max(start, TextOffsets.ToOffset(text, range.End));
            text = string.Concat(text.AsSpan(0, start), change.Text, text.AsSpan(end));
        }

        var document = _documents.Set(parameters.TextDocument.Uri, parameters.TextDocument.Version, text!);

        if (document is not null)
        {
            CancelInteractiveFor(document.Uri);
            _analysis.Schedule(document);
        }
    }

    private void CancelInteractiveFor(string uri)
    {
        foreach (var method in new[] { LspMethods.Completion, LspMethods.SignatureHelp })
        {
            if (_interactiveRequests.TryRemove($"{method}:{uri}", out var cts))
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
    }

    private static string? ExtractPositionUri(JsonRpcMessage message) =>
        message.Params is { ValueKind: JsonValueKind.Object } obj
        && obj.TryGetProperty("textDocument", out var doc)
        && doc.TryGetProperty("uri", out var uriProp)
            ? uriProp.GetString()
            : null;

    private void DispatchRequest(
        JsonElement id, string method, Func<CancellationToken, Task> handler, string? interactiveUri = null)
    {
        var idKey = id.GetRawText();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCts.Token);
        _inFlight[idKey] = cts;

        var interactiveKey = interactiveUri is not null ? $"{method}:{interactiveUri}" : null;
        if (interactiveKey is not null)
        {
            if (_interactiveRequests.TryRemove(interactiveKey, out var previous))
            {
                try { previous.Cancel(); }
                catch (ObjectDisposedException) { }
            }
            _interactiveRequests[interactiveKey] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await handler(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested && !_shutdownCts.IsCancellationRequested)
            {
                _analysis.Telemetry.RecordCancellation();
                await SafeSendAsync(() => SendErrorAsync(id, JsonRpcErrorCodes.RequestCancelled,
                    "the client withdrew the request", CancellationToken.None)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Server is shutting down.
            }
            catch
            {
                // Connection closed.
            }
            finally
            {
                _inFlight.TryRemove(new KeyValuePair<string, CancellationTokenSource>(idKey, cts));
                if (interactiveKey is not null)
                    _interactiveRequests.TryRemove(new KeyValuePair<string, CancellationTokenSource>(interactiveKey, cts));
                cts.Dispose();
            }
        });
    }

    private async Task HandleDidCloseAsync(
        JsonRpcMessage message, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.DidCloseTextDocumentParams);
        if (parameters is null) return;

        var document = _documents.Remove(parameters.TextDocument.Uri);
        if (document is null) return;

        await _analysis.CloseAsync(document, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A file changed behind the editor: a save from another program, a branch switch, a delete.
    /// Each named file is handed to the analysis, which decides what it invalidates.
    /// </summary>
    private void HandleDidChangeWatchedFiles(JsonRpcMessage message)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.DidChangeWatchedFilesParams);
        if (parameters is null) return;

        foreach (var change in parameters.Changes)
            if (DocumentUri.TryToFilePath(change.Uri, out var path))
                _analysis.ChangedOnDisk(path);
    }

    /// <summary>
    /// Asks the client to watch what the analysis reads from disk: the sources, and the project
    /// file that says what belongs together.
    ///
    /// <para>A REQUEST, like the refresh and a progress token. The response carries nothing; it is
    /// read only so a refusal lands in the log rather than nowhere.</para>
    /// </summary>
    private Task RegisterFileWatchesAsync(CancellationToken cancellationToken)
    {
        var registration = new RegistrationParams
        {
            Registrations =
            [
                new Registration
                {
                    Id = "lyric-watched-files",
                    Method = LspMethods.DidChangeWatchedFiles,
                    RegisterOptions = new DidChangeWatchedFilesRegistrationOptions
                    {
                        Watchers =
                        [
                            new LspFileSystemWatcher { GlobPattern = "**/*.lyr" },
                            new LspFileSystemWatcher { GlobPattern = "**/lyric.toml" },
                            new LspFileSystemWatcher { GlobPattern = "**/lyric.lock" },
                        ],
                    },
                },
            ],
        };

        return SendAsync(new JsonRpcRequest
        {
            Id = Interlocked.Increment(ref _lastRequestId),
            Method = LspMethods.RegisterCapability,
            Params = LspJson.ToElement(registration, LspJson.Default.RegistrationParams),
        }, LspJson.Default.JsonRpcRequest, cancellationToken);
    }

    /// <summary>
    /// Ensures the first analysis snapshot of an open file is ready before answering semantic requests,
    /// honouring <paramref name="cancellationToken"/> if withdrawn or superseded.
    /// </summary>
    private async Task<AnalysisSnapshot?> EnsureSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        if (_analysis.LastGood(path) is { } known) return known;
        if (_documents.ByPath(path) is null) return null;
        return await _analysis.FirstAsync(path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows that an analysis runs (E20), where the client draws progress: a token the client is
    /// asked to create, then <c>begin</c>, a <c>report</c> for each phase, and <c>end</c> when the
    /// returned handle is disposed. <c>null</c> for a client that draws none.
    ///
    /// <para>Never awaited by the analysis. A token is the client's once it answered the create
    /// request, and the answer arrives on the read loop — which may itself be waiting on this very
    /// analysis (a pull). The messages go out in order on a task of their own.</para>
    /// </summary>
    private IAnalysisProgress? BeginProgress(string what)
    {
        if (!_workDoneProgress || _state != State.Running) return null;

        var token = $"lyric/analysis/{Interlocked.Increment(ref _progressCount)}";
        var id = Interlocked.Increment(ref _lastRequestId);
        var created = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _awaitingAnswer[id] = created;

        var begun = Task.Run(async () =>
        {
            await SendAsync(new JsonRpcRequest
            {
                Id = id,
                Method = LspMethods.WorkDoneProgressCreate,
                Params = LspJson.ToElement(new WorkDoneProgressCreateParams { Token = token },
                    LspJson.Default.WorkDoneProgressCreateParams),
            }, LspJson.Default.JsonRpcRequest, CancellationToken.None).ConfigureAwait(false);

            if (!await created.Task.ConfigureAwait(false)) return false;

            await SendProgressAsync(token, LspJson.ToElement(
                new WorkDoneProgressBegin { Title = "Analysing", Message = what },
                LspJson.Default.WorkDoneProgressBegin)).ConfigureAwait(false);
            return true;
        });

        return new ProgressHandle(begun, value => SendProgressAsync(token, value));
    }

    private Task SendProgressAsync(string token, JsonElement value) =>
        SendNotificationAsync(LspMethods.Progress,
            LspJson.ToElement(new ProgressParams { Token = token, Value = value }, LspJson.Default.ProgressParams),
            CancellationToken.None);

    private static async Task SafeSendAsync(Func<Task> send)
    {
        try
        {
            await send().ConfigureAwait(false);
        }
        catch
        {
            // The connection is gone; there is no one left to answer.
        }
    }

    /// <summary>
    /// One progress display: its reports and its end, in order, after its begin — each waits for
    /// the one before it on a chain of its own, never on the caller's thread. Ended once; a report
    /// after the end is dropped.
    /// </summary>
    private sealed class ProgressHandle(Task<bool> begun, Func<JsonElement, Task> send) : IAnalysisProgress
    {
        private readonly object _gate = new();
        private Task _tail = begun;
        private bool _ended;

        public void Report(string message, int percentage) =>
            Then(LspJson.ToElement(new WorkDoneProgressReport { Message = message, Percentage = percentage },
                LspJson.Default.WorkDoneProgressReport), end: false);

        public void Dispose() =>
            Then(LspJson.ToElement(new WorkDoneProgressEnd(), LspJson.Default.WorkDoneProgressEnd), end: true);

        private void Then(JsonElement value, bool end)
        {
            lock (_gate)
            {
                if (_ended) return;
                _ended = end;
                _tail = _tail.ContinueWith(async _ =>
                {
                    try
                    {
                        if (await begun.ConfigureAwait(false)) await send(value).ConfigureAwait(false);
                    }
                    catch
                    {
                        // The connection is gone, and with it the bar.
                    }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            }
        }
    }

    /// <summary>
    /// Answers a hover, or answers with null.
    ///
    /// <para>A null result is the protocol's way of saying "nothing here", and it is the RIGHT
    /// answer more often than not: whitespace, a comment, a keyword. An error would make the
    /// client log a failure for every cursor rest.</para>
    ///
    /// <para>The answer comes from the last analysis that produced a model, which may be one edit
    /// behind. That is the trade: the alternative is silence during exactly the seconds someone is
    /// looking something up, because the buffer they are editing does not parse.</para>
    /// </summary>
    private async Task SendHoverAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.TextDocumentPositionParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and a position", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = TextOffsets.ToOffset(snapshot.Text, parameters.Position);
        var found = HoverProvider.At(snapshot.Model, snapshot.Root, snapshot.File, offset);

        if (found is null)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var hover = new Hover
        {
            Contents = new MarkupContent { Value = found.Markdown },

            // Ranged against the snapshot's own source manager, which is where the span's offsets
            // are valid. Against the current buffer they would be off by whatever was typed since.
            Range = SpanMapper.ToRange(snapshot.Sources, found.Span),
        };

        await SendResultAsync(id, hover, LspJson.Default.Hover, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendDefinitionAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.TextDocumentPositionParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and a position", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot
            || DefinitionProvider.At(snapshot.Model, snapshot.Root, snapshot.File,
                TextOffsets.ToOffset(snapshot.Text, parameters.Position)) is not { } target)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendDefinitionTargetAsync(id, parameters.TextDocument.Uri, path, snapshot, target, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendTypeDefinitionAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.TextDocumentPositionParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and a position", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot
            || NavigationProviders.TypeDefinitionAt(snapshot.Model, snapshot.Root, snapshot.File,
                TextOffsets.ToOffset(snapshot.Text, parameters.Position)) is not { } target)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendDefinitionTargetAsync(id, parameters.TextDocument.Uri, path, snapshot, target, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendDefinitionTargetAsync(
        JsonElement id, string requestUri, string path, AnalysisSnapshot snapshot,
        DefinitionTarget target, CancellationToken cancellationToken)
    {
        var targetPath = snapshot.Sources.GetPath(target.File);
        var uri = DocumentUri.PathComparer.Equals(targetPath, path)
            ? requestUri
            : DocumentUri.FromFilePath(targetPath);

        var name = SpanMapper.ToRange(snapshot.Sources, target.NameSpan);

        if (_definitionLinkSupport)
        {
            LocationLink[] link =
            [
                new()
                {
                    TargetUri = uri,
                    TargetRange = SpanMapper.ToRange(snapshot.Sources, target.Span),
                    TargetSelectionRange = name,
                },
            ];

            await SendResultAsync(id, link, LspJson.Default.LocationLinkArray, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var location = new Location { Uri = uri, Range = name };
        await SendResultAsync(id, location, LspJson.Default.Location, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendImplementationAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.TextDocumentPositionParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and a position", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, new List<Location>(), LspJson.Default.ListLocation, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var offset = TextOffsets.ToOffset(snapshot.Text, parameters.Position);
        var targets = NavigationProviders.ImplementationAt(snapshot.Model, snapshot.Root, snapshot.File, offset);
        var locations = new List<Location>(targets.Count);
        foreach (var t in targets)
        {
            var tp = snapshot.Sources.GetPath(t.File);
            locations.Add(new Location
            {
                Uri = DocumentUri.PathComparer.Equals(tp, path)
                    ? parameters.TextDocument.Uri
                    : DocumentUri.FromFilePath(tp),
                Range = SpanMapper.ToRange(snapshot.Sources, t.NameSpan),
            });
        }

        await SendResultAsync(id, locations, LspJson.Default.ListLocation, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendDocumentSymbolsAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.DocumentSymbolParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!_hierarchicalSymbols || !DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path))
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<DocumentSymbol>? symbols = null;
        if (_analysis.LastGood(path) is { } snapshot)
        {
            symbols = DocumentSymbolProvider.Of(snapshot.Sources, snapshot.Root);
        }
        else if (_documents.ByPath(path) is { } openDoc)
        {
            var syntax = _analysis.ParseSyntax(openDoc);
            symbols = DocumentSymbolProvider.Of(syntax.Sources, syntax.Root);
        }

        if (symbols is null)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendResultAsync(id, symbols, LspJson.Default.IReadOnlyListDocumentSymbol,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers every place a name occurs, or answers with null.
    ///
    /// <para>The list covers the program reachable FROM THIS BUFFER, which is what the server
    /// compiles. A file of the same project that imports this one is not in that compilation and its
    /// uses are therefore not found — complete for a call into the standard library, incomplete for
    /// "who uses my function" across a project.</para>
    /// </summary>
    private async Task SendReferencesAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.ReferenceParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document, a position and a context", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var offset = 0;
        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot
            || ReferenceProvider.At(snapshot.Model, snapshot.Root, snapshot.File,
                offset = TextOffsets.ToOffset(snapshot.Text, parameters.Position),
                parameters.Context.IncludeDeclaration) is not { } sites)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The uses in the other packages the editor analysed, each found by its own compilation
        // at the same declaration — a package's function is used where packages depend on it.
        var found = new List<(Core.SourceManager Sources, ReferenceSite Site)>(sites.Select(s => (snapshot.Sources, s)));
        if (Across.DeclarationName(snapshot, offset) is { } name)
            foreach (var (other, file, root, at) in Across.Elsewhere(snapshot, name, _analysis.CurrentCompilations()))
                foreach (var site in ReferenceProvider.At(other.Model, root, file, at, parameters.Context.IncludeDeclaration) ?? [])
                    found.Add((other.Sources, site));

        var locations = new List<Location>(found.Count);
        var seen = new HashSet<(string, int, int)>();
        foreach (var (sources, site) in found)
        {
            var sitePath = sources.GetPath(site.File);
            if (!seen.Add((Path.GetFullPath(sitePath), site.Span.Start, site.Span.End))) continue;

            // The client's own spelling for the file it asked about, a built URI for the rest — the
            // same rule the jump follows, and for the same reason.
            locations.Add(new Location
            {
                Uri = DocumentUri.PathComparer.Equals(sitePath, path)
                    ? parameters.TextDocument.Uri
                    : DocumentUri.FromFilePath(sitePath),
                Range = SpanMapper.ToRange(sources, site.Span),
            });
        }

        await SendResultAsync(id, locations, LspJson.Default.ListLocation, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Answers what can follow a <c>.</c>, or answers with null.
    ///
    /// <para>Off the CURRENT buffer rather than off the last analysis: the request came from a
    /// keystroke, and the model that keystroke invalidated is the one that would answer about the
    /// text before it.</para>
    /// </summary>
    private async Task SendCompletionAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.CompletionParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and a position", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || _documents.ByPath(path) is not { } document)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = TextOffsets.ToOffset(document.Text, parameters.Position);
        var items = await _analysis.CompleteAsync(
                document, offset, _completionSnippetSupport, cancellationToken)
            .ConfigureAwait(false);

        if (items is null)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendResultAsync(id, items, LspJson.Default.IReadOnlyListCompletionItem,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers which parameter the cursor is on, off the CURRENT buffer — see
    /// <see cref="AnalysisService.SignatureHelpAsync"/>.</summary>
    private async Task SendSignatureHelpAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.TextDocumentPositionParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and a position", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || _documents.ByPath(path) is not { } document)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = TextOffsets.ToOffset(document.Text, parameters.Position);
        var help = await _analysis.SignatureHelpAsync(document, offset, cancellationToken)
            .ConfigureAwait(false);

        if (help is null)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendResultAsync(id, help, LspJson.Default.SignatureHelp, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Answers what may collapse, or answers with null. Syntax off the last good tree —
    /// regions must not snap open on a type error.</summary>
    private async Task SendFoldingRangesAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.FoldingRangeParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path))
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<FoldingRange>? ranges = null;
        if (_analysis.LastGood(path) is { } snapshot)
        {
            ranges = FoldingProvider.Of(snapshot.Root, snapshot.File, snapshot.Sources);
        }
        else if (_documents.ByPath(path) is { } openDoc)
        {
            var syntax = _analysis.ParseSyntax(openDoc);
            ranges = FoldingProvider.Of(syntax.Root, syntax.File, syntax.Sources);
        }

        if (ranges is null)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendResultAsync(id, ranges, LspJson.Default.IReadOnlyListFoldingRange,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers the inferred types of the bindings in a range, off the last good
    /// analysis.</summary>
    private async Task SendInlayHintsAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.InlayHintParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and a range", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var hints = InlayHintProvider.Of(snapshot.Model, snapshot.Root, snapshot.File,
            snapshot.Sources, parameters.Range);

        await SendResultAsync(id, hints, LspJson.Default.IReadOnlyListInlayHint,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers where the rename would edit, or with the reason it will not — BEFORE the user has
    /// typed anything, which is what prepare exists for.
    /// </summary>
    private async Task SendPrepareRenameAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.TextDocumentPositionParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and a position", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || _analysis.LastGood(path) is not { } snapshot)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = TextOffsets.ToOffset(snapshot.Text, parameters.Position);
        var (range, refusal) = RenameProvider.Prepare(
            snapshot.Model, snapshot.Root, snapshot.File, offset, snapshot.ProjectWide);

        if (range is null)
        {
            // The reason reaches the user: an editor renders this message where a null would
            // render a shrug.
            await SendErrorAsync(id, JsonRpcErrorCodes.RequestFailed,
                refusal ?? "nothing to rename here", cancellationToken).ConfigureAwait(false);
            return;
        }

        await SendResultAsync(id, new PrepareRenameResult
        {
            Range = SpanMapper.ToRange(snapshot.Sources, range.Span),
            Placeholder = range.Placeholder,
        }, LspJson.Default.PrepareRenameResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers with the edits of the rename over the whole compilation, grouped by file.
    ///
    /// <para>No collision check stands behind this, on purpose: the compile that follows the edit
    /// is the conflict analysis, and its diagnostics point at any name the rename ran into.</para>
    /// </summary>
    private async Task SendRenameAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.RenameParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document, a position and a new name", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || _analysis.LastGood(path) is not { } snapshot)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = TextOffsets.ToOffset(snapshot.Text, parameters.Position);
        var (edits, refusal) = RenameProvider.Rename(snapshot.Model, snapshot.Root, snapshot.File,
            offset, parameters.NewName, snapshot.ProjectWide);

        if (edits is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.RequestFailed,
                refusal ?? "nothing to rename here", cancellationToken).ConfigureAwait(false);
            return;
        }

        // The other packages the editor analysed rename their own uses of the same declaration.
        var all = new List<(Core.SourceManager Sources, ReferenceSite Edit)>(edits.Select(e => (snapshot.Sources, e)));
        if (Across.DeclarationName(snapshot, offset) is { } name)
            foreach (var (other, file, root, at) in Across.Elsewhere(snapshot, name, _analysis.CurrentCompilations()))
                if (RenameProvider.Rename(other.Model, root, file, at, parameters.NewName, other.ProjectWide).Edits is { } more)
                    all.AddRange(more.Select(e => (other.Sources, e)));

        // A package read from git is the cache's, and std is the toolchain's: neither is edited
        // from here, and a rename that would leave their uses behind is no rename.
        if (all.Select(e => e.Sources.GetPath(e.Edit.File)).FirstOrDefault(PackageUnit.IsReadOnly) is { } fixedFile)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.RequestFailed,
                $"the rename would edit '{fixedFile}', which this editor does not change: it is read from git or is the standard library",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var changes = new Dictionary<string, List<TextEdit>>(StringComparer.Ordinal);
        var seen = new HashSet<(string, int, int)>();
        foreach (var (sources, edit) in all)
        {
            var editPath = sources.GetPath(edit.File);
            if (!seen.Add((Path.GetFullPath(editPath), edit.Span.Start, edit.Span.End))) continue;

            // The client's own spelling for the file it asked about, a built URI for the rest —
            // the rule every answer with URIs in it follows here.
            var uri = DocumentUri.PathComparer.Equals(editPath, path)
                ? parameters.TextDocument.Uri
                : DocumentUri.FromFilePath(editPath);

            if (!changes.TryGetValue(uri, out var list))
                changes[uri] = list = [];

            list.Add(new TextEdit
            {
                Range = SpanMapper.ToRange(sources, edit.Span),
                NewText = parameters.NewName,
            });
        }

        await SendResultAsync(id, new WorkspaceEdit { Changes = changes },
            LspJson.Default.WorkspaceEdit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers what every name in the document is, or answers with null.
    ///
    /// <para>Off the last good analysis, like hover: colors one edit old are invisible, colors
    /// that vanish while the file does not parse are a flicker on every keystroke.</para>
    /// </summary>
    private async Task SendSemanticTokensAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.SemanticTokensParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var data = SemanticTokensProvider.Of(
            snapshot.Model, snapshot.Root, snapshot.File, snapshot.Sources);
        var resultId = $"st{Interlocked.Increment(ref _semanticTokenVersion)}";
        _lastSemanticTokens[parameters.TextDocument.Uri] = (resultId, data);

        await SendResultAsync(id, new SemanticTokens { ResultId = resultId, Data = data },
            LspJson.Default.SemanticTokens, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendSemanticTokensDeltaAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.SemanticTokensDeltaParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and previousResultId", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var data = SemanticTokensProvider.Of(
            snapshot.Model, snapshot.Root, snapshot.File, snapshot.Sources);
        var resultId = $"st{Interlocked.Increment(ref _semanticTokenVersion)}";

        if (_lastSemanticTokens.TryGetValue(parameters.TextDocument.Uri, out var previous)
            && previous.ResultId == parameters.PreviousResultId)
        {
            _lastSemanticTokens[parameters.TextDocument.Uri] = (resultId, data);
            var edits = SemanticTokensProvider.ComputeDelta(previous.Data, data);
            await SendResultAsync(id, new SemanticTokensDelta { ResultId = resultId, Edits = edits },
                LspJson.Default.SemanticTokensDelta, cancellationToken).ConfigureAwait(false);
            return;
        }

        _lastSemanticTokens[parameters.TextDocument.Uri] = (resultId, data);
        await SendResultAsync(id, new SemanticTokens { ResultId = resultId, Data = data },
            LspJson.Default.SemanticTokens, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendSemanticTokensRangeAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.SemanticTokensRangeParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document and range", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        var data = SemanticTokensProvider.OfRange(
            snapshot.Model, snapshot.Root, snapshot.File, snapshot.Sources, parameters.Range);
        await SendResultAsync(id, new SemanticTokens { Data = data },
            LspJson.Default.SemanticTokens, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Answers the workspace symbol search over the incremental index and live compilations.</summary>
    private async Task SendWorkspaceSymbolsAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.WorkspaceSymbolParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a query", cancellationToken).ConfigureAwait(false);
            return;
        }

        var symbols = WorkspaceSymbolProvider.Find(
            _analysis.CurrentCompilations(), parameters.Query, _analysis.SymbolIndex);

        await SendResultAsync(id, symbols, LspJson.Default.IReadOnlyListSymbolInformation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Answers with the formatter's one shape, off the CURRENT buffer — an edit computed against
    /// anything older would write stale text over what the user just typed.
    ///
    /// <para>A buffer that does not parse gets NO edits, the same duty <c>lyrfmt</c> has on
    /// disk: the formatter never writes a guess over broken text, and the diagnostics already on
    /// screen say why nothing happened. The client's tab preferences in the request are read for
    /// nothing — one shape is the tool's contract.</para>
    ///
    /// <para>One edit spanning the whole document rather than a diff: the shape a client applies
    /// atomically, and computing minimal edits buys smoother cursors at the price of a diff
    /// algorithm nobody has asked for yet.</para>
    /// </summary>
    private async Task SendFormattingAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(
            message.Params, LspJson.Default.DocumentFormattingParams);

        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                "expected a text document", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || _documents.ByPath(path) is not { } document)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The form 'lyric fmt' writes, Lyric 5's, through the same call; the diagnostics are
        // thrown away — the analysis already published them. A file that does not parse is left
        // as it is.
        var formatted = Lyric5.Build.FmtRun.Format(path, document.Text, out _);

        if (formatted is null)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }

        // In shape already: an empty list, not null — "nothing to change" is an answer. Otherwise
        // a hunk of changed lines each: the editor keeps the cursor and the marks of the rest.
        await SendResultAsync(id, FormattingEdits.Of(document.Text, formatted),
            LspJson.Default.IReadOnlyListTextEdit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The range from the first character to just past the last, in protocol
    /// coordinates. The tail after the last newline may end in <c>\r</c>; the position lands
    /// past the visible end of that line, which a client clamps — the whole file is meant
    /// either way.</summary>
    private static Protocol.Range WholeDocument(string text)
    {
        var lines = 0;
        var lastLineStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            lines++;
            lastLineStart = i + 1;
        }

        return new Protocol.Range
        {
            Start = new Position { Line = 0, Character = 0 },
            End = new Position { Line = lines, Character = text.Length - lastLineStart },
        };
    }

    private InitializeResult BuildInitializeResult() => new()
    {
        Capabilities = new ServerCapabilities
        {
            TextDocumentSync = new TextDocumentSyncOptions { Change = TextDocumentSyncKind.Incremental },

            // The encoding Span already counts in. Announcing anything else would mean converting
            // every offset twice per message for no gain.
            PositionEncoding = "utf-16",

            HoverProvider = true,
            DefinitionProvider = true,
            DocumentSymbolProvider = true,
            ReferencesProvider = true,
            CompletionProvider = new CompletionOptions { TriggerCharacters = [".", ":", "@", "<", "/"], ResolveProvider = true },
            RenameProvider = new RenameOptions { PrepareProvider = true },
            WorkspaceSymbolProvider = true,
            SemanticTokensProvider = new SemanticTokensOptions
            {
                Legend = new SemanticTokensLegend
                {
                    TokenTypes = SemanticTokensProvider.TokenTypes,
                    TokenModifiers = SemanticTokensProvider.TokenModifiers,
                },
                Full = true,
                Range = true,
            },
            SignatureHelpProvider = new SignatureHelpOptions
            {
                TriggerCharacters = ["(", ","],
                RetriggerCharacters = [","],
            },
            FoldingRangeProvider = true,
            InlayHintProvider = true,
            DocumentFormattingProvider = true,
            DiagnosticProvider = new DiagnosticOptions { InterFileDependencies = true, WorkspaceDiagnostics = false },
            CodeActionProvider = new CodeActionOptions { CodeActionKinds = ["quickfix"] },
            ExecuteCommandProvider = new ExecuteCommandOptions { Commands = [LspMethods.ExpandCommand] },
            CodeLensProvider = new CodeLensOptions(),
            TypeDefinitionProvider = true,
            ImplementationProvider = true,
            DocumentHighlightProvider = true,
            SelectionRangeProvider = true,
            CallHierarchyProvider = true,
            TypeHierarchyProvider = true,
            DocumentLinkProvider = new DocumentLinkOptions(),
            Workspace = new WorkspaceServerCapabilities
            {
                FileOperations = new FileOperationsServerCapabilities
                {
                    WillRename = new FileOperationRegistrationOptions
                    {
                        Filters = [new FileOperationFilter { Scheme = "file", Pattern = new FileOperationPattern { Glob = "**/*.lyr" } }],
                    },
                },
            },
        },
        ServerInfo = new ServerInfo { Name = "lyric", Version = _options.Version },
    };

    private async Task PublishDiagnosticsAsync(
        PublishDiagnosticsParams parameters, CancellationToken cancellationToken)
    {
        _lastDiagnostics[parameters.Uri] = parameters;
        _diagnosticResultIds[parameters.Uri] = $"d{Interlocked.Increment(ref _diagnosticVersion)}_{parameters.Version ?? 0}";
        if (!_pullDiagnostics || _documents.ByUri(parameters.Uri) is null)
        {
            await SendNotificationAsync(LspMethods.PublishDiagnostics,
                LspJson.ToElement(parameters, LspJson.Default.PublishDiagnosticsParams),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task OnAnalysisCompleteAsync(CancellationToken cancellationToken)
    {
        if (_pullDiagnostics && _diagnosticRefresh)
            await SendAsync(new JsonRpcRequest { Id = Interlocked.Increment(ref _lastRequestId), Method = LspMethods.DiagnosticRefresh },
                LspJson.Default.JsonRpcRequest, cancellationToken).ConfigureAwait(false);
        if (_semanticTokensRefresh)
            await SendAsync(new JsonRpcRequest { Id = Interlocked.Increment(ref _lastRequestId), Method = LspMethods.SemanticTokensRefresh },
                LspJson.Default.JsonRpcRequest, cancellationToken).ConfigureAwait(false);
        if (_inlayHintRefresh)
            await SendAsync(new JsonRpcRequest { Id = Interlocked.Increment(ref _lastRequestId), Method = LspMethods.InlayHintRefresh },
                LspJson.Default.JsonRpcRequest, cancellationToken).ConfigureAwait(false);
        if (_codeLensRefresh)
            await SendAsync(new JsonRpcRequest { Id = Interlocked.Increment(ref _lastRequestId), Method = LspMethods.CodeLensRefresh },
                LspJson.Default.JsonRpcRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The tests of a document of a package, off its last good analysis; none for a file
    /// outside one.</summary>
    private async Task<(string Root, IReadOnlyList<EditorTest> Tests)?> TestsOfAsync(string uri, CancellationToken cancellationToken)
    {
        if (!DocumentUri.TryToFilePath(uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot
            || _analysis.UnitForPath(path) is not { } unit || unit.ModuleOf(path) is null)
            return null;
        return (unit.Root, TestLensProvider.Of(snapshot, unit, path));
    }

    private static Protocol.Range LineRange(int line) =>
        new() { Start = new Position { Line = line, Character = 0 }, End = new Position { Line = line, Character = 0 } };

    /// <summary>A lens over each test: run it, debug it — commands the client runs with the
    /// package's directory and the test's name.</summary>
    private async Task SendCodeLensesAsync(JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.CodeLensParams);
        var lenses = new List<CodeLens>();
        if (parameters is not null && await TestsOfAsync(parameters.TextDocument.Uri, cancellationToken).ConfigureAwait(false) is { } found)
            foreach (var test in found.Tests)
            {
                lenses.Add(new CodeLens { Range = LineRange(test.Line), Command = new LspCommand { Title = "Run test", Command = LspMethods.RunTest, Arguments = [found.Root, test.Name] } });
                lenses.Add(new CodeLens { Range = LineRange(test.Line), Command = new LspCommand { Title = "Debug test", Command = LspMethods.DebugTest, Arguments = [found.Root, test.Name] } });
            }
        await SendResultAsync(id, lenses, LspJson.Default.IReadOnlyListCodeLens, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>lyric/tests</c>: the package's directory and the document's tests.</summary>
    private async Task SendTestsAsync(JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.CodeLensParams);
        var found = parameters is null ? null : await TestsOfAsync(parameters.TextDocument.Uri, cancellationToken).ConfigureAwait(false);
        await SendResultAsync(id, new DocumentTests
        {
            Root = found?.Root,
            Tests = [.. (found?.Tests ?? []).Select(t => new DocumentTest { Name = t.Name, Kind = t.Kind, Range = LineRange(t.Line) })],
        }, LspJson.Default.DocumentTests, cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>lyric.expand</c>: the module of an open document with its macros expanded, for
    /// the client to show beside it.</summary>
    private async Task SendCommandAsync(JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.ExecuteCommandParams);
        if (parameters is not { Command: LspMethods.ExpandCommand, Arguments: [var uri, ..] })
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams,
                $"the one command is '{LspMethods.ExpandCommand}', with the URI of an open document", cancellationToken).ConfigureAwait(false);
            return;
        }
        if (_documents.ByUri(uri) is not { } document)
        {
            await SendResultAsync(id, new ExpandResult { Refused = "the document is not open" },
                LspJson.Default.ExpandResult, cancellationToken).ConfigureAwait(false);
            return;
        }
        var (text, refused) = await _analysis.ExpandAsync(document, cancellationToken).ConfigureAwait(false);
        await SendResultAsync(id, new ExpandResult { Text = text, Refused = refused },
            LspJson.Default.ExpandResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The quick fixes in a range: off the newest findings of the file — the changes the
    /// compiler suggested, and the <c>@Allow</c> of a lint.</summary>
    private async Task SendCodeActionsAsync(JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.CodeActionParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected a text document and a range",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<CodeAction> actions = [];
        if (DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path) && _analysis.FindingsOf(path) is { } found)
            actions = CodeActionProvider.At(path, parameters.TextDocument.Uri, parameters.Range, found.Sources, found.Findings,
                _analysis.LastGood(path), _options.Version, _analysis.SymbolIndex);
        await SendResultAsync(id, actions, LspJson.Default.IReadOnlyListCodeAction, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A document's diagnostics, asked for: what the newest analysis of its current version said,
    /// running the analysis first where none has said anything of that version yet.
    /// </summary>
    private async Task SendDocumentDiagnosticsAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.DocumentDiagnosticParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "textDocument/diagnostic needs a document",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var uri = parameters.TextDocument.Uri;
        if (_documents.ByUri(uri) is { } document
            && !(_lastDiagnostics.TryGetValue(uri, out var said) && said.Version == document.Version))
        {
            try
            {
                await _analysis.AnalyzeAsync(document, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // A newer run of the same package withdrew this one — the open's own, after its
                // debounce, is the usual one. What it finds is not known yet; the client asks again.
                await SendAsync(
                    new JsonRpcFailure
                    {
                        Id = id,
                        Error = new JsonRpcError
                        {
                            Code = JsonRpcErrorCodes.ServerCancelled,
                            Message = "a newer analysis of the package replaced this one",
                            Data = LspJson.ToElement(new DiagnosticServerCancellationData { RetriggerRequest = true },
                                LspJson.Default.DiagnosticServerCancellationData),
                        },
                    },
                    LspJson.Default.JsonRpcFailure, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        var resultId = _diagnosticResultIds.TryGetValue(uri, out var rid) ? rid : null;
        if (parameters.PreviousResultId is not null && resultId is not null && parameters.PreviousResultId == resultId)
        {
            await SendResultAsync(id, new UnchangedDocumentDiagnosticReport { ResultId = resultId },
                LspJson.Default.UnchangedDocumentDiagnosticReport, cancellationToken).ConfigureAwait(false);
            return;
        }

        var items = _lastDiagnostics.TryGetValue(uri, out var known) ? known.Diagnostics : [];
        await SendResultAsync(id, new FullDocumentDiagnosticReport { ResultId = resultId, Items = items },
            LspJson.Default.FullDocumentDiagnosticReport, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendWorkspaceDiagnosticsAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.WorkspaceDiagnosticParams);
        var prevMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parameters?.PreviousResultIds is { } prevs)
            foreach (var p in prevs)
                prevMap[p.Uri] = p.Value;

        var reports = new List<WorkspaceDocumentDiagnosticReport>();
        foreach (var (uri, pub) in _lastDiagnostics)
        {
            var rid = _diagnosticResultIds.TryGetValue(uri, out var r) ? r : null;
            if (rid is not null && prevMap.TryGetValue(uri, out var prevRid) && prevRid == rid)
            {
                reports.Add(new WorkspaceDocumentDiagnosticReport
                {
                    Kind = "unchanged",
                    Uri = uri,
                    Version = pub.Version,
                    ResultId = rid,
                });
            }
            else
            {
                reports.Add(new WorkspaceDocumentDiagnosticReport
                {
                    Kind = "full",
                    Uri = uri,
                    Version = pub.Version,
                    ResultId = rid,
                    Items = pub.Diagnostics,
                });
            }
        }

        await SendResultAsync(id, new WorkspaceDiagnosticReport { Items = reports },
            LspJson.Default.WorkspaceDiagnosticReport, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendCompletionResolveAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var item = LspJson.ReadParams(message.Params, LspJson.Default.CompletionItem);
        if (item is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected completion item", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var resolved = CompletionProvider.Resolve(item, _analysis.SymbolIndex);
        await SendResultAsync(id, resolved, LspJson.Default.CompletionItem, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendDocumentHighlightsAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.TextDocumentPositionParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected a text document and a position",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, Array.Empty<DocumentHighlight>(),
                LspJson.Default.IReadOnlyListDocumentHighlight, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = TextOffsets.ToOffset(snapshot.Text, parameters.Position);
        var highlights = NavigationProviders.DocumentHighlightsAt(
            snapshot.Model, snapshot.Root, snapshot.File, snapshot.Sources, offset);
        await SendResultAsync(id, highlights, LspJson.Default.IReadOnlyListDocumentHighlight, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendSelectionRangesAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.SelectionRangeParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected a text document and positions",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path))
        {
            await SendResultAsync(id, Array.Empty<SelectionRange>(),
                LspJson.Default.IReadOnlyListSelectionRange, cancellationToken).ConfigureAwait(false);
            return;
        }

        IReadOnlyList<SelectionRange> ranges;
        if (_analysis.LastGood(path) is { } snapshot)
        {
            ranges = NavigationProviders.SelectionRangesAt(snapshot.Root, snapshot.File, snapshot.Sources, parameters.Positions);
        }
        else if (_documents.ByPath(path) is { } openDoc)
        {
            var syntax = _analysis.ParseSyntax(openDoc);
            ranges = NavigationProviders.SelectionRangesAt(syntax.Root, syntax.File, syntax.Sources, parameters.Positions);
        }
        else
        {
            ranges = [];
        }

        await SendResultAsync(id, ranges, LspJson.Default.IReadOnlyListSelectionRange, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendDocumentLinksAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.DocumentLinkParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected a text document",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, Array.Empty<DocumentLink>(),
                LspJson.Default.IReadOnlyListDocumentLink, cancellationToken).ConfigureAwait(false);
            return;
        }

        var links = NavigationProviders.DocumentLinksOf(snapshot.Model, snapshot.Root, snapshot.File, snapshot.Sources);
        await SendResultAsync(id, links, LspJson.Default.IReadOnlyListDocumentLink, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendPrepareCallHierarchyAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.CallHierarchyPrepareParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected call hierarchy params",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, Array.Empty<CallHierarchyItem>(),
                LspJson.Default.IReadOnlyListCallHierarchyItem, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = TextOffsets.ToOffset(snapshot.Text, parameters.Position);
        var items = HierarchyProviders.PrepareCallHierarchy(snapshot.Model, snapshot.Root, snapshot.File, snapshot.Sources, offset);
        await SendResultAsync(id, items, LspJson.Default.IReadOnlyListCallHierarchyItem, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendCallHierarchyIncomingAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.CallHierarchyIncomingCallsParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected call hierarchy item",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var calls = HierarchyProviders.IncomingCalls(_analysis.CurrentCompilations(), parameters.Item);
        await SendResultAsync(id, calls, LspJson.Default.IReadOnlyListCallHierarchyIncomingCall, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendCallHierarchyOutgoingAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.CallHierarchyOutgoingCallsParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected call hierarchy item",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var calls = HierarchyProviders.OutgoingCalls(_analysis.CurrentCompilations(), parameters.Item);
        await SendResultAsync(id, calls, LspJson.Default.IReadOnlyListCallHierarchyOutgoingCall, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendPrepareTypeHierarchyAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.TypeHierarchyPrepareParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected type hierarchy params",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!DocumentUri.TryToFilePath(parameters.TextDocument.Uri, out var path)
            || await EnsureSnapshotAsync(path, cancellationToken).ConfigureAwait(false) is not { } snapshot)
        {
            await SendResultAsync(id, Array.Empty<TypeHierarchyItem>(),
                LspJson.Default.IReadOnlyListTypeHierarchyItem, cancellationToken).ConfigureAwait(false);
            return;
        }

        var offset = TextOffsets.ToOffset(snapshot.Text, parameters.Position);
        var items = HierarchyProviders.PrepareTypeHierarchy(snapshot.Model, snapshot.Root, snapshot.File, snapshot.Sources, offset);
        await SendResultAsync(id, items, LspJson.Default.IReadOnlyListTypeHierarchyItem, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendTypeHierarchySupertypesAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.TypeHierarchySupertypesParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected type hierarchy item",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var items = HierarchyProviders.Supertypes(_analysis.CurrentCompilations(), parameters.Item);
        await SendResultAsync(id, items, LspJson.Default.IReadOnlyListTypeHierarchyItem, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendTypeHierarchySubtypesAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.TypeHierarchySubtypesParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected type hierarchy item",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var items = HierarchyProviders.Subtypes(_analysis.CurrentCompilations(), parameters.Item);
        await SendResultAsync(id, items, LspJson.Default.IReadOnlyListTypeHierarchyItem, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendWillRenameFilesAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.RenameFilesParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected files array",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        var edit = HierarchyProviders.WillRenameFiles(_analysis.CurrentCompilations(), _analysis.UnitForPath, parameters);
        if (edit is null)
        {
            await SendResultAsync(id, cancellationToken).ConfigureAwait(false);
            return;
        }
        await SendResultAsync(id, edit, LspJson.Default.WorkspaceEdit, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendExpandMacroAsync(
        JsonRpcMessage message, JsonElement id, CancellationToken cancellationToken)
    {
        var parameters = LspJson.ReadParams(message.Params, LspJson.Default.ExpandMacroParams);
        if (parameters is null)
        {
            await SendErrorAsync(id, JsonRpcErrorCodes.InvalidParams, "expected textDocument and position",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_documents.ByUri(parameters.TextDocument.Uri) is not { } document)
        {
            await SendResultAsync(id, new ExpandResult { Refused = "the document is not open" },
                LspJson.Default.ExpandResult, cancellationToken).ConfigureAwait(false);
            return;
        }

        var (text, refused) = await _analysis.ExpandAsync(document, cancellationToken).ConfigureAwait(false);
        await SendResultAsync(id, new ExpandResult { Text = text, Refused = refused },
            LspJson.Default.ExpandResult, cancellationToken).ConfigureAwait(false);
    }

    private Task SendServerStatusAsync(JsonElement id, CancellationToken cancellationToken)
    {
        var status = _analysis.Telemetry.Snapshot(
            _options.Version,
            _documents.Count,
            _analysis.AnalysedCount,
            _analysis.SymbolIndex.SymbolCount);
        return SendResultAsync(id, status, LspJson.Default.ServerStatusResult, cancellationToken);
    }

    private Task LogAsync(string message, MessageType type, CancellationToken cancellationToken) =>
        SendNotificationAsync(LspMethods.LogMessage,
            LspJson.ToElement(new LogMessageParams { Type = type, Message = message },
                LspJson.Default.LogMessageParams),
            cancellationToken);

    private async Task SafeLogAsync(string message)
    {
        try
        {
            await LogAsync(message, MessageType.Error, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Nowhere left to report to.
        }
    }

    private Task SendNotificationAsync(
        string method, JsonElement parameters, CancellationToken cancellationToken) =>
        SendAsync(new JsonRpcNotification { Method = method, Params = parameters },
            LspJson.Default.JsonRpcNotification, cancellationToken);

    private Task SendResultAsync<T>(
        JsonElement id, T value, JsonTypeInfo<T> type, CancellationToken cancellationToken) =>
        SendAsync(new JsonRpcSuccess { Id = id, Result = LspJson.ToElement(value, type) },
            LspJson.Default.JsonRpcSuccess, cancellationToken);

    /// <summary>The answer to <c>shutdown</c>: a result that is present and null.</summary>
    private Task SendResultAsync(JsonElement id, CancellationToken cancellationToken) =>
        SendAsync(new JsonRpcSuccess { Id = id, Result = null },
            LspJson.Default.JsonRpcSuccess, cancellationToken);

    private Task SendErrorAsync(
        JsonElement? id, int code, string message, CancellationToken cancellationToken) =>
        SendAsync(
            new JsonRpcFailure { Id = id, Error = new JsonRpcError { Code = code, Message = message } },
            LspJson.Default.JsonRpcFailure, cancellationToken);

    private Task SendAsync<T>(T value, JsonTypeInfo<T> type, CancellationToken cancellationToken) =>
        _connection.WriteAsync(JsonSerializer.SerializeToUtf8Bytes(value, type), cancellationToken);

    public void Dispose()
    {
        try { _shutdownCts.Cancel(); }
        catch (ObjectDisposedException) { }
        foreach (var active in _inFlight.Values)
        {
            try { active.Cancel(); }
            catch (ObjectDisposedException) { /* answered meanwhile */ }
        }
        _analysis.Dispose();
        _shutdownCts.Dispose();
        _connection.Dispose();
    }
}
