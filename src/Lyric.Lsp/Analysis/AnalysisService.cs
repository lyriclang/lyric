using System.Collections.Concurrent;
using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;
using Lyric.Parsing;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// One finished analysis as seen from ONE FILE, kept so questions can be answered between compiles.
///
/// <para>They belong together and are useless apart: the model is keyed by nodes whose spans point
/// at <see cref="File"/> in <see cref="Sources"/>, and <see cref="Text"/> is what those offsets
/// count into. A position converted against any other text addresses this tree at the wrong
/// place.</para>
///
/// <para><see cref="Root"/> is the module AST of this file, the walk root for every question about
/// a cursor in it. A workspace compilation holds many modules, and <see cref="SemanticModel.Entry"/>
/// is the right root for exactly one of them; several snapshots of one project share the model and
/// differ in exactly this member, the file and the text.</para>
///
/// <para><see cref="Sources"/> is carried rather than reached through the model: a
/// <c>Compilation</c> keeps its source manager to itself, and a span cannot be turned into a line
/// and a column without one.</para>
/// </summary>
/// <param name="ProjectWide">Whether the compilation behind this snapshot covered a whole project.
/// A rename trusts a project compilation to be complete; a buffer-rooted one it does not.</param>
public sealed record AnalysisSnapshot(
    Compiler.SemanticModel Model,
    Core.SourceManager Sources,
    Core.FileId File,
    Module Root,
    string Text,
    int Version,
    bool ProjectWide);

/// <summary>
/// Turns buffer and disk changes into published diagnostics.
///
/// <para>The unit of analysis is a PACKAGE where there is one: a module under the <c>src/</c> or
/// <c>tests/</c> of a <c>lyric.toml</c> is compiled together with every other module of the
/// package (<see cref="PackageUnit"/>), as one compilation with one symbol world — which is what
/// makes an answer about "every place this name occurs" complete instead of
/// complete-for-one-buffer. A document outside every package is a single file, checked alone with
/// std, as <c>lyric check</c> checks it.</para>
///
/// <para>The compiler is run WHOLE for every analysis, project or not. It has no incremental mode,
/// and building one before the cost is known would be optimising a number nobody has measured in
/// this process. The cost is dominated by the standard library rather than by the user's files,
/// and it is the same work <c>lyric check</c> does.</para>
///
/// <para>A run in flight stops between two phases of the compiler when its token is cancelled
/// (<see cref="CompilerOptions.Cancellation"/>); within a phase it runs on, and its result is
/// discarded. For a keystroke-driven server the second is the distinction that matters — the
/// wasted milliseconds are invisible, a published stale answer is not.</para>
/// </summary>
public sealed class AnalysisService : IDisposable
{
    private readonly DocumentStore _documents;
    private readonly Func<PublishDiagnosticsParams, CancellationToken, Task> _publish;
    private readonly Func<string, MessageType, CancellationToken, Task> _log;
    private readonly TimeSpan _debounce;
    private readonly string? _stdlibRoot;

    /// <summary>The toolchain's version: which catalog pages a diagnostic links to.</summary>
    private readonly string? _toolVersion;

    /// <summary>What the newest analysis of each file found, as the compiler said it — with the
    /// changes its notes suggest, which the code actions offer.</summary>
    private readonly ConcurrentDictionary<string, (Core.SourceManager Sources, IReadOnlyList<Core.Diagnostic> Findings)> _findings =
        new(DocumentUri.PathComparer);

    /// <summary>The findings of the newest analysis of a file, with the source manager their spans
    /// count in; <c>null</c> before one.</summary>
    public (Core.SourceManager Sources, IReadOnlyList<Core.Diagnostic> Findings)? FindingsOf(string path) =>
        _findings.TryGetValue(path, out var found) ? found : null;

    /// <summary>The standard library parsed once, on a thread of its own from the start: an
    /// analysis before it is ready parses the library itself.</summary>
    private readonly Task<StdlibSeed> _seed;

    /// <summary>The pending analysis per triggering path. Replacing an entry cancels the run it
    /// stands for. Keyed by the trigger rather than by the unit, because the unit is not known
    /// until a project file has been read, and the debounce must not wait on disk I/O.</summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending =
        new(DocumentUri.PathComparer);

    /// <summary>
    /// The analysis currently running per UNIT — the source root for a project, the document path
    /// for a file outside one. Two documents of one project both schedule the project; the second
    /// run withdraws the first here, so the unit is compiled once per burst rather than once per
    /// buffer.
    /// </summary>
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running =
        new(DocumentUri.PathComparer);

    /// <summary>
    /// What was last said about a project file, by the directory the search started in.
    ///
    /// <para>The project file is read on every analysis — it is one small file and the compile
    /// beside it costs milliseconds — but SAYING something about it belongs to a change. Logging a
    /// warning per keystroke would bury the one line that matters under the same line.</para>
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _projectNotices =
        new(DocumentUri.PathComparer);

    /// <summary>
    /// Which files the last analysis of a unit READ, by unit key.
    ///
    /// <para>Taken from the compilation itself rather than from the imports in the text: the
    /// resolver already followed them, transitively and through the project file's roots, and a
    /// second answer to "what does this depend on" would be the one that is wrong.</para>
    /// </summary>
    private readonly ConcurrentDictionary<string, HashSet<string>> _dependencies =
        new(DocumentUri.PathComparer);

    /// <summary>The unit a document was last analysed under. What the cascade consults to avoid
    /// re-analysing the unit that was just analysed under another of its own documents.</summary>
    private readonly ConcurrentDictionary<string, string> _unitOf =
        new(DocumentUri.PathComparer);

    /// <summary>
    /// The URIs a unit's last round published to, by unit key.
    ///
    /// <para>Diagnostics are state the server owns in the client. A file that leaves the project —
    /// deleted, renamed, or its never-saved buffer closed — must have its squiggles withdrawn, and
    /// the only side that remembers having published them is this one.</para>
    /// </summary>
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _published =
        new(DocumentUri.PathComparer);

    /// <summary>
    /// The last analysis that produced a model, per file path.
    ///
    /// <para>Kept because a question about the program has to be answerable while the program does
    /// not parse. Half the time a cursor rests somewhere, the buffer is mid-edit; a server that
    /// only answered from the current text would go silent exactly when someone is looking
    /// something up. The answer is then one edit old, which is visible and harmless, as against no
    /// answer, which reads as "there is nothing here".</para>
    ///
    /// <para>The TEXT is kept with it: a position from the client has to be turned into an offset
    /// against the text the model was built from, not against the buffer that has moved on.</para>
    /// </summary>
    private readonly ConcurrentDictionary<string, AnalysisSnapshot> _lastGood =
        new(DocumentUri.PathComparer);

    /// <summary>
    /// The questions waiting for a file's FIRST analysis, by path (E20). Before it there is no
    /// model, and an answer of null would read as "nothing here" — a client does not ask again.
    /// Completed with the snapshot once one is stored, or with what there is once an analysis of
    /// the file ended without one.
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AnalysisSnapshot?>> _firstWaiters =
        new(DocumentUri.PathComparer);

    /// <summary>Shows that an analysis runs, where the client draws progress: called with what is
    /// analysed, and the handle it returns is disposed when the analysis ends.</summary>
    private readonly Func<string, IDisposable?>? _progress;

    private bool _disposed;

    public AnalysisService(
        DocumentStore documents,
        Func<PublishDiagnosticsParams, CancellationToken, Task> publish,
        Func<string, MessageType, CancellationToken, Task> log,
        TimeSpan debounce,
        string? stdlibRoot = null,
        string? toolVersion = null,
        Func<string, IDisposable?>? progress = null)
    {
        _toolVersion = toolVersion;
        _progress = progress;
        _documents = documents;
        _publish = publish;
        _log = log;
        _debounce = debounce;
        _stdlibRoot = stdlibRoot;
        _seed = Task.Run(() => StdlibSeed.Shared(stdlibRoot ?? Lyric5.Build.Pipeline.StdlibRoot));
    }

    /// <summary>
    /// Plans an analysis of this version and returns at once.
    ///
    /// <para>The caller is the read loop, which must not wait: while it waits it reads no
    /// messages, and one of the unread ones is the edit that makes this analysis obsolete.</para>
    /// </summary>
    public void Schedule(OpenDocument document)
    {
        if (_disposed) return;
        _ = RunAsync(document, Swap(document.Path));
    }

    /// <summary>
    /// Plans an analysis because a file CHANGED ON DISK — a save from another program, a branch
    /// switch, a delete. Returns at once, like <see cref="Schedule"/>.
    ///
    /// <para>A burst of these — a checkout touches many files — lands many triggers on one unit;
    /// the per-unit run replacement makes the unit compile once at the end rather than once per
    /// file, at the price of a few discarded starts.</para>
    /// </summary>
    public void ChangedOnDisk(string path)
    {
        if (_disposed) return;
        _ = RunDiskAsync(path, Swap(path));
    }

    private CancellationTokenSource Swap(string key)
    {
        var source = new CancellationTokenSource();
        if (_pending.TryRemove(key, out var previous)) Retire(previous);
        _pending[key] = source;
        return source;
    }

    /// <summary>
    /// Cancels and disposes a source that left <see cref="_pending"/>. Its run may have finished
    /// and disposed it meanwhile (<see cref="Release"/>): then there is nothing left to cancel — and
    /// <c>Cancel</c> on a disposed source throws, which once took <see cref="Dispose"/> down.
    /// </summary>
    private static void Retire(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { /* its run is already past the finish line */ }
        source.Dispose();
    }

    private async Task RunAsync(OpenDocument document, CancellationTokenSource source)
    {
        var token = source.Token;
        try
        {
            if (_debounce > TimeSpan.Zero)
                await Task.Delay(_debounce, token).ConfigureAwait(false);

            // The cheapest of the three guards, and the one that catches the common case: the user
            // is still typing, so several versions were superseded before any of them compiled.
            if (!_documents.IsCurrent(document)) return;

            await AnalyzeAsync(document, token).ConfigureAwait(false);
            await AnalyzeDependentsAsync(document.Path, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer version arrived. Nothing to report: the run that replaced this one will.
        }
        catch (Exception exception)
        {
            // A compiler fault must not take the connection down with it. The user keeps the
            // diagnostics of the last run, which are stale but were true of a real state of the
            // file, and the reason lands in the client's log rather than nowhere.
            await SafeLogAsync(
                $"analysis of {document.Path} failed: {exception.GetType().Name}: {exception.Message}")
                .ConfigureAwait(false);
            Settle(document.Path);
        }
        finally
        {
            Release(document.Path, source);
        }
    }

    private async Task RunDiskAsync(string path, CancellationTokenSource source)
    {
        var token = source.Token;
        try
        {
            if (_debounce > TimeSpan.Zero)
                await Task.Delay(_debounce, token).ConfigureAwait(false);

            // An open buffer is authoritative over its own disk file, but the ordinary path is
            // taken all the same: what the disk change may have altered is a file the buffer's
            // program READS, and the compile is what finds out.
            if (_documents.ByPath(path) is { } open)
            {
                await AnalyzeAsync(open, token).ConfigureAwait(false);
                await AnalyzeDependentsAsync(path, token).ConfigureAwait(false);
                return;
            }

            var unit = await UnitForAsync(path).ConfigureAwait(false);
            if (unit is not null && (PackageUnit.IsManifest(path) || unit.ModuleOf(path) is not null))
            {
                await AnalyzeProjectAsync(unit, trigger: null, token).ConfigureAwait(false);
            }

            // A closed file some OPEN unit reads: the unit's last compile read the old bytes.
            await AnalyzeDependentsAsync(path, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer trigger arrived; its run answers.
        }
        catch (Exception exception)
        {
            await SafeLogAsync(
                $"analysis after a change to {path} failed: "
                + $"{exception.GetType().Name}: {exception.Message}").ConfigureAwait(false);
            Settle(path);
        }
        finally
        {
            Release(path, source);
        }
    }

    private void Release(string key, CancellationTokenSource source)
    {
        if (_pending.TryGetValue(key, out var current) && current == source)
            _pending.TryRemove(key, out _);
        source.Dispose();
    }

    /// <summary>
    /// The package of this path, or <c>null</c> when there is none or its manifest cannot be read.
    ///
    /// <para>A BROKEN MANIFEST DOES NOT STOP THE ANALYSIS. The file is checked alone, which may
    /// resolve wrongly, and the log says why; publishing nothing would leave the editor showing the
    /// diagnostics of some earlier state with no hint that anything happened. For a tool someone is
    /// looking at, wrong-and-explained beats silent.</para>
    ///
    /// <para>Not published as a diagnostic on this document: the fault is in another file, and
    /// pointing at the wrong place is how a reader loses an afternoon. The log is where a message
    /// about a file the client did not ask about belongs.</para>
    /// </summary>
    private async Task<PackageUnit?> UnitForAsync(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return null;

        var unit = PackageUnit.Find(path, out var refused);
        await NoticeAsync(directory, refused ?? string.Empty, MessageType.Error).ConfigureAwait(false);
        return unit;
    }

    /// <summary>
    /// Re-analyses every open document whose unit's last compilation read this path, except the
    /// unit that was just analysed.
    ///
    /// <para>ONE LEVEL ONLY: a cascaded run does not cascade again. Two modules may import each
    /// other — that is a diagnostic rather than a crash, so both still compile — and a transitive
    /// cascade over such a pair would not terminate. One level is also all that is needed: a change
    /// reaches every unit that reads the changed file, and a unit that reads THAT one was itself
    /// re-read in the process.</para>
    /// </summary>
    private async Task AnalyzeDependentsAsync(string changedPath, CancellationToken token)
    {
        string full;
        try { full = Path.GetFullPath(changedPath); }
        catch (ArgumentException) { return; }

        var changedUnit = _unitOf.TryGetValue(changedPath, out var unit) ? unit : changedPath;
        var visited = new HashSet<string>(DocumentUri.PathComparer) { changedUnit };

        foreach (var other in _documents.All)
        {
            if (DocumentUri.PathComparer.Equals(other.Path, changedPath)) continue;

            var key = _unitOf.TryGetValue(other.Path, out var k) ? k : other.Path;
            if (!visited.Add(key)) continue;
            if (!_dependencies.TryGetValue(key, out var reads) || !reads.Contains(full)) continue;

            token.ThrowIfCancellationRequested();
            await AnalyzeAsync(other, token).ConfigureAwait(false);
        }
    }

    /// <summary>Every file a compilation put in its source manager, as absolute paths. A virtual
    /// name that is not a path is dropped rather than guessed at.</summary>
    private static HashSet<string> FilesRead(Core.SourceManager sources)
    {
        var files = new HashSet<string>(DocumentUri.PathComparer);

        for (var i = 1; i <= sources.FileCount; i++)
        {
            try { files.Add(Path.GetFullPath(sources.GetPath(new Core.FileId(i)))); }
            catch (ArgumentException) { /* not a path; it can match no document */ }
        }

        return files;
    }

    /// <summary>Says something about a manifest only when it differs from what was last said
    /// about it.</summary>
    private async Task NoticeAsync(string directory, string message, MessageType type)
    {
        if (_projectNotices.TryGetValue(directory, out var previous)
            && string.Equals(previous, message, StringComparison.Ordinal))
            return;

        _projectNotices[directory] = message;
        if (message.Length > 0) await SafeLogAsync(message, type).ConfigureAwait(false);
    }

    /// <summary>
    /// How a lone document is compiled: as a module of its package, under the package's options, or
    /// alone; with the text of everything else the editor holds.
    ///
    /// <para>One place, because the analysis and the completion have to compile the same program.
    /// Two option sets that differ in the package would answer two different questions about one
    /// file, and only one of them would be on screen.</para>
    /// </summary>
    private async Task<(CompilerOptions Options, string? Module)> OptionsForAsync(OpenDocument document,
        CancellationToken cancellationToken = default)
    {
        var unit = await UnitForAsync(document.Path).ConfigureAwait(false);
        return unit?.ModuleOf(document.Path) is { } module
            ? (Options(unit.Options, cancellationToken), module)
            : (Options(PackageUnit.SingleFileOptions(document.Path), cancellationToken), PackageUnit.SingleFileModule(document.Path));
    }

    /// <summary>The steps a <c>comptime</c> site of the program may take in the editor, a tenth of
    /// the default: a keystroke's analysis does not wait seconds on one site.</summary>
    public const long EditorBudget = Lyric.Ir.Interp.ComptimeEvaluator.DefaultBudget / 10;

    /// <summary>What the editor compiles under: the unit's options, the library parsed once, no
    /// lowering — the editor shows what the checker and the evaluation say —, and the run's token,
    /// which stops a compilation whose text has changed between two of its phases.</summary>
    private CompilerOptions Options(CompilerOptions unit, CancellationToken cancellationToken) => unit with
    {
        StdlibRoot = _stdlibRoot ?? unit.StdlibRoot,
        StdlibSeed = _seed.IsCompletedSuccessfully ? _seed.Result : null,
        Lower = false,
        EditorBudget = EditorBudget,
        Cancellation = cancellationToken,

        // Everything the editor holds, not only the files this run starts from. Without it a
        // program is checked against the last SAVE of every module it imports, and the two
        // disagree for as long as an edit is unsaved.
        SourceOverlay = _documents.Overlay(),
    };

    /// <summary>
    /// The completions at an offset in a document's CURRENT text.
    ///
    /// <para>Not from the last good analysis: that model was built from the text before the
    /// keystroke that triggered this, and its spans are indices into that text. A compile of its own
    /// is what makes the answer about what the user is looking at.</para>
    /// </summary>
    public async Task<IReadOnlyList<CompletionItem>?> CompleteAsync(
        OpenDocument document, int offset, CancellationToken cancellationToken)
    {
        var (options, module) = await OptionsForAsync(document, cancellationToken).ConfigureAwait(false);

        return await Task.Run(
            () => CompletionProvider.At(document.Path, document.Text, offset, options, module),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The signature at an offset in a document's CURRENT text — compiled fresh, like completion
    /// and for the same reason: the request came from typing <c>(</c> or <c>,</c>, and the last
    /// good model predates exactly that keystroke.
    /// </summary>
    public async Task<SignatureHelp?> SignatureHelpAsync(
        OpenDocument document, int offset, CancellationToken cancellationToken)
    {
        var (options, module) = await OptionsForAsync(document, cancellationToken).ConfigureAwait(false);

        return await Task.Run(
            () => SignatureHelpProvider.At(document.Path, document.Text, offset, options, module),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The document's module with its macros expanded, in the formatter's form — what
    /// <c>lyric expand</c> prints —, or the first finding that stopped the expansion.
    /// </summary>
    public async Task<(string? Text, string? Refused)> ExpandAsync(OpenDocument document, CancellationToken cancellationToken)
    {
        var (options, module) = await OptionsForAsync(document, cancellationToken).ConfigureAwait(false);
        var result = await Task.Run(() => SourceCompiler.Expand(ScriptSource.FromBuffer(document.Path, document.Text, module),
            options with { Cancellation = cancellationToken }), cancellationToken).ConfigureAwait(false);
        if (result.Expanded is { } expanded)
            return (Lyric.Formatting.AstFormatter.FormatDeclarations(expanded.Declarations), null);
        var errors = result.Diagnostics.SortedSnapshot().Where(d => d.Severity == Core.Severity.Error).ToList();
        return (null, errors.Count == 0 ? "the module does not expand" : $"{errors[0].Code}: {errors[0].Message}");
    }

    /// <summary>Compiles the unit this document belongs to and publishes its diagnostics, without
    /// the debounce. The analysis a test drives directly.</summary>
    public async Task AnalyzeAsync(OpenDocument document, CancellationToken cancellationToken)
    {
        // The first analysis of an open file is the wait an editor shows (E20): before it, nothing
        // answers. Reported from its start, the package's manifest and graph included, which take
        // seconds the first time. A later one takes milliseconds and is not worth a flicker.
        using var progress = LastGood(document.Path) is null ? _progress?.Invoke(Path.GetFileName(document.Path)) : null;

        var unit = await UnitForAsync(document.Path).ConfigureAwait(false);

        if (unit is not null && unit.ModuleOf(document.Path) is not null)
        {
            await AnalyzeProjectAsync(unit, document, cancellationToken).ConfigureAwait(false);
            return;
        }

        await AnalyzeSingleAsync(document, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The whole package as one compilation: every module under <c>src/</c> and <c>tests/</c>,
    /// saved or not, plus every open buffer there that was never saved at all.
    /// </summary>
    private async Task AnalyzeProjectAsync(
        PackageUnit unit, OpenDocument? trigger, CancellationToken cancellationToken)
    {
        var unitKey = Path.GetFullPath(unit.Root);

        // A second trigger for the same unit withdraws the run of the first; see _running.
        using var run = BeginRun(unitKey, cancellationToken);
        var token = run.Token;

        var roots = CollectRoots(unit);
        if (roots.Count == 0)
        {
            if (trigger is not null) Settle(trigger.Path);
            return;
        }

        // The buffers that go into this compile, captured BEFORE it: the version guard below has
        // to ask about exactly what was compiled, not about whatever is open afterwards.
        var opens = new List<OpenDocument>();
        var scripts = new List<ScriptSource>(roots.Count);
        foreach (var path in roots)
        {
            var name = unit.ModuleOf(path)!;
            if (_documents.ByPath(path) is { } open)
            {
                opens.Add(open);
                scripts.Add(ScriptSource.FromBuffer(path, open.Text, name));
            }
            else
            {
                scripts.Add(ScriptSource.FromDisk(path, name));
            }
        }

        var options = Options(unit.Options, token);

        // Off the caller's thread: the compile is synchronous and CPU-bound, and the caller is
        // either the read loop or a test.
        var result = await Task.Run(() => SourceCompiler.CheckProject(scripts, options), token)
            .ConfigureAwait(false);

        token.ThrowIfCancellationRequested();

        // Recorded even when the result is discarded below: what this unit reads does not depend
        // on whether its diagnostics are still wanted.
        _dependencies[unitKey] = FilesRead(result.Sources);
        foreach (var path in roots) _unitOf[path] = unitKey;

        // Asked AFTER the compile as well as before it, for EVERY buffer that went in. The compile
        // takes long enough for a file to have changed while it ran, and that is precisely when
        // publishing does damage.
        foreach (var open in opens)
            if (!_documents.IsCurrent(open))
                return;
        if (trigger is not null && !_documents.IsCurrent(trigger)) return;

        var diagnostics = result.Diagnostics.SortedSnapshot();

        var published = new List<string>(roots.Count);
        foreach (var path in roots)
        {
            var file = DiagnosticMapper.FindFile(result.Sources, path);
            var open = _documents.ByPath(path);
            var uri = open?.Uri ?? DocumentUri.FromFilePath(path);

            // Errors do not disqualify a model — a program with a type error still has resolved
            // names and types for everything around it, which is the state an editor is in most of
            // the time.
            if (file.IsValid && result.Model is { } model && RootOf(model, file) is { } moduleAst)
            {
                _lastGood[path] = new AnalysisSnapshot(model, result.Sources, file, moduleAst,
                    result.Sources.GetText(file), open?.Version ?? 0, ProjectWide: true);
            }
            Settle(path);

            published.Add(uri);
            if (file.IsValid) _findings[path] = (result.Sources, diagnostics.Where(d => d.Span.File == file).ToList());
            await _publish(new PublishDiagnosticsParams
            {
                Uri = uri,
                Version = open?.Version,
                Diagnostics = file.IsValid
                    ? DiagnosticMapper.ForFile(result.Sources, diagnostics, file, _toolVersion)
                    : [],
            }, token).ConfigureAwait(false);
        }

        // Withdraw what the last round said about files that are no longer in the project: a
        // deleted file's squiggles otherwise outlive the file.
        if (_published.TryGetValue(unitKey, out var previous))
        {
            foreach (var uri in previous)
                if (!published.Contains(uri, StringComparer.Ordinal))
                {
                    _lastGood.TryRemove(PathOf(uri), out _);
                    await _publish(new PublishDiagnosticsParams { Uri = uri, Diagnostics = [] },
                        token).ConfigureAwait(false);
                }
        }
        _published[unitKey] = published;

        // A diagnostic addressed to no file — a root that could not be read — would be dropped by
        // the per-file filter and the editor would show clean documents that do not compile.
        foreach (var diagnostic in diagnostics)
            if (!diagnostic.Span.File.IsValid)
                await SafeLogAsync($"{diagnostic.Code}: {diagnostic.Message}").ConfigureAwait(false);
    }

    /// <summary>A document outside every project: compiled from itself, exactly as before the
    /// server knew what a project was.</summary>
    private async Task AnalyzeSingleAsync(OpenDocument document, CancellationToken cancellationToken)
    {
        var (options, module) = await OptionsForAsync(document, cancellationToken).ConfigureAwait(false);

        var result = await Task.Run(
            () => SourceCompiler.Check(
                ScriptSource.FromBuffer(document.Path, document.Text, module), options),
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        _dependencies[document.Path] = FilesRead(result.Sources);
        _unitOf[document.Path] = document.Path;

        if (!_documents.IsCurrent(document)) return;

        var file = DiagnosticMapper.FindFile(result.Sources, document.Path);
        if (file.IsValid)
            _findings[document.Path] = (result.Sources, result.Diagnostics.SortedSnapshot().Where(d => d.Span.File == file).ToList());
        var diagnostics = file.IsValid
            ? DiagnosticMapper.ForFile(result.Sources, result.Diagnostics.SortedSnapshot(), file, _toolVersion)
            : [];

        if (file.IsValid && result.Model is { } model)
        {
            _lastGood[document.Path] =
                new AnalysisSnapshot(model, result.Sources, file, model.Entry, document.Text,
                    document.Version, ProjectWide: false);
        }
        Settle(document.Path);

        // The entry file is missing from the source manager only when the run never opened it. The
        // diagnostic explaining why is in the result and is addressed to no file, so it would be
        // dropped by the filter and the editor would show a clean document that does not compile.
        if (!file.IsValid)
        {
            foreach (var diagnostic in result.Diagnostics.SortedSnapshot())
                await SafeLogAsync($"{diagnostic.Code}: {diagnostic.Message}").ConfigureAwait(false);
        }

        _published[document.Path] = [document.Uri];

        await _publish(new PublishDiagnosticsParams
        {
            Uri = document.Uri,
            Version = document.Version,
            Diagnostics = diagnostics,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// What closing a document means depends on what the document was.
    ///
    /// <para>A package module's diagnostics belong to the PACKAGE, not to the buffer: they stay, and
    /// the project is re-analysed because the authority over the text just moved back to the disk —
    /// which may hold something older than the buffer did. A never-saved buffer has no disk side at
    /// all; the re-analysis no longer finds it and withdraws its diagnostics through the ordinary
    /// bookkeeping.</para>
    ///
    /// <para>A document outside every package takes its diagnostics with it, as it always did: an
    /// empty list rather than no message, because a server that simply stops talking about a file
    /// leaves its last squiggles in the editor for as long as the session lasts.</para>
    /// </summary>
    public async Task CloseAsync(OpenDocument document, CancellationToken cancellationToken)
    {
        if (_pending.TryRemove(document.Path, out var pending)) Retire(pending);
        Settle(document.Path);

        var unit = await UnitForAsync(document.Path).ConfigureAwait(false);
        if (unit is not null && unit.ModuleOf(document.Path) is not null)
        {
            // The document is already out of the store, so the re-analysis reads the disk.
            ChangedOnDisk(document.Path);
            return;
        }

        // The model goes with the buffer. Keeping it would answer questions about a file the editor
        // has closed, from text it no longer holds.
        _lastGood.TryRemove(document.Path, out _);
        _published.TryRemove(document.Path, out _);
        _unitOf.TryRemove(document.Path, out _);
        _dependencies.TryRemove(document.Path, out _);

        await _publish(new PublishDiagnosticsParams
        {
            Uri = document.Uri,
            Diagnostics = [],
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The newest analysis of this file that produced a model, or <c>null</c> when none ever
    /// did — a file whose very first version failed to open.
    /// </summary>
    public AnalysisSnapshot? LastGood(string path) =>
        _lastGood.TryGetValue(path, out var snapshot) ? snapshot : null;

    /// <summary>
    /// <see cref="LastGood"/>, waiting for the file's first analysis where there is none yet
    /// (E20): <c>null</c> only when that analysis ended without a model, or the file was closed.
    /// </summary>
    public Task<AnalysisSnapshot?> FirstAsync(string path, CancellationToken cancellationToken)
    {
        if (LastGood(path) is { } known) return Task.FromResult<AnalysisSnapshot?>(known);

        var waiter = _firstWaiters.GetOrAdd(path,
            _ => new TaskCompletionSource<AnalysisSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously));

        // Stored between the look above and the registration: nothing would settle it again.
        if (LastGood(path) is not null || _disposed) Settle(path);
        return waiter.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Answers what waits for the first analysis of this file, with what there is.</summary>
    private void Settle(string path)
    {
        if (_firstWaiters.TryRemove(path, out var waiter)) waiter.TrySetResult(LastGood(path));
    }

    /// <summary>
    /// One snapshot per live compilation — what a question WITHOUT a document behind it (the
    /// workspace symbol search) iterates. The per-file snapshots of one project share their model,
    /// so distinctness by model identity is distinctness by compilation.
    /// </summary>
    public IReadOnlyList<AnalysisSnapshot> CurrentCompilations() =>
        _lastGood.Values
            .DistinctBy(snapshot => snapshot.Model, ReferenceEqualityComparer.Instance)
            .ToList();

    /// <summary>
    /// Every root of the package: its modules as the disk knows them, plus the open buffers among
    /// them the disk does not know yet.
    ///
    /// <para>Sorted, so the compilation is the same one whatever order the triggers arrived in —
    /// the first root becomes <see cref="SemanticModel.Entry"/>, and an unstable choice there would
    /// make two analyses of one state disagree.</para>
    /// </summary>
    private List<string> CollectRoots(PackageUnit unit)
    {
        var files = new HashSet<string>(DocumentUri.PathComparer);
        foreach (var (file, _) in unit.Files) files.Add(Path.GetFullPath(file));
        foreach (var open in _documents.All)
            if (unit.ModuleOf(open.Path) is not null)
                files.Add(Path.GetFullPath(open.Path));

        var roots = files.ToList();
        roots.Sort(StringComparer.OrdinalIgnoreCase);
        return roots;
    }

    /// <summary>The module AST a file was parsed into, or <c>null</c> for a file that holds no
    /// module of this compilation — which happens when the file could not be read.</summary>
    private static Module? RootOf(Compiler.SemanticModel model, Core.FileId file)
    {
        foreach (var module in model.Compilation.Modules)
        {
            var ast = model.Compilation.AstOf(module);
            if (ast.Span.File == file) return ast;
        }

        return null;
    }

    /// <summary>The path form of a URI this server itself published under; falls back to the URI
    /// when it cannot be read back, in which case it matches no stored path and nothing is
    /// removed.</summary>
    private static string PathOf(string uri) =>
        DocumentUri.TryToFilePath(uri, out var path) ? path : uri;

    private CancellationTokenSource BeginRun(string unitKey, CancellationToken outer)
    {
        var run = CancellationTokenSource.CreateLinkedTokenSource(outer);

        if (_running.TryRemove(unitKey, out var previous))
        {
            try { previous.Cancel(); }
            catch (ObjectDisposedException) { /* its owner is already past the finish line */ }
        }

        _running[unitKey] = run;
        return run;
    }

    /// <summary>Logging must not itself throw on a connection that is already going down.</summary>
    private async Task SafeLogAsync(string message, MessageType type = MessageType.Error)
    {
        try
        {
            await _log(message, type, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // The client is gone. There is nowhere left to report this.
        }
    }

    public void Dispose()
    {
        _disposed = true;
        foreach (var path in _firstWaiters.Keys) Settle(path);
        foreach (var pending in _pending.Values) Retire(pending);
        _pending.Clear();

        foreach (var running in _running.Values)
        {
            try { running.Cancel(); }
            catch (ObjectDisposedException) { /* already finished */ }
        }
        _running.Clear();
    }
}
