using System.Runtime.CompilerServices;
using Lyric4.AST;
using Lyric4.Resolver;
using Lyric4.Sema;

namespace Lyric4.From4.Rules;

/// <summary>What the task rules read of a program: the functions that wait, directly or through a
/// call, what each has to throw now, and the <c>spawn</c>s and <c>run</c>s that have a form.</summary>
internal sealed class TaskFacts
{
    private static readonly ConditionalWeakTable<Model, TaskFacts> Known = new();

    /// <summary>A function that waits or closes a scope, and what it throws for it.</summary>
    public Dictionary<FunctionDecl, string> Needs { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>The calls a <c>spawn</c> takes: they run in the task, and the lambda marks them.</summary>
    public HashSet<Expr> Spawned { get; } = new(ReferenceEqualityComparer.Instance);

    /// <summary>The <c>spawn</c>s and the <c>run</c> of a block, which become a scope.</summary>
    public List<(CallExpr[] Spawns, ExprStmt[] Statements, ExprStmt Run)> Scopes { get; } = [];

    /// <summary>A program that names the module with an alias is said by hand as a whole.</summary>
    public bool Qualified { get; private set; }

    public static TaskFacts Of(RuleContext context) => Known.GetValue(context.Model, _ => new TaskFacts(context));

    private TaskFacts(RuleContext context)
    {
        Qualified = context.Modules.Any(m => m.Declarations.OfType<ImportDecl>().Any(i =>
            string.Join('.', i.Path) == "std.task" && i.Clause is not ImportSelective));
        if (Qualified) return;
        var functions = context.Modules.SelectMany(m => Tree.Below(m).OfType<FunctionDecl>()).Where(f => f.Body is not null).ToList();
        foreach (var function in functions)
        {
            if (ReturnsTask(function) || Walk(function.Body!).OfType<YieldStmt>().Any(y => IsWait(context, y))) Needs[function] = "Cancelled";
            foreach (var block in Walk(function.Body!).OfType<Block>().Prepend(function.Body!))
                if (ScopeOf(context, block) is { } scope)
                {
                    Scopes.Add(scope);
                    foreach (var spawn in scope.Spawns) Spawned.Add(spawn.Arguments[0]);
                    Needs[function] = "Error";
                }
        }
        // what any 'spawn' takes runs in the task, even one left as it stands
        foreach (var function in functions)
            foreach (var call in Walk(function.Body!).OfType<CallExpr>())
                if (IsTask(context, call, "spawn") && call.Arguments is [var taken]) Spawned.Add(taken);
        // whoever calls one in its own body now throws what it throws
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var function in functions)
                foreach (var call in Walk(function.Body!).OfType<CallExpr>())
                {
                    if (Spawned.Contains(call) || Tree.Target(context, call.Callee) is not FunctionSymbol { Declaration: FunctionDecl callee }
                        || !Needs.TryGetValue(callee, out var thrown)) continue;
                    if (Needs.GetValueOrDefault(function) is "Error" || Needs.GetValueOrDefault(function) == thrown) continue;
                    Needs[function] = thrown;
                    changed = true;
                }
        }
    }

    /// <summary>A call that throws now: what E04 marks.</summary>
    public bool Throws(RuleContext context, CallExpr call) =>
        !Spawned.Contains(call) && Tree.Target(context, call.Callee) is FunctionSymbol { Declaration: FunctionDecl callee } && Needs.ContainsKey(callee);

    public static bool ReturnsTask(FunctionDecl function) =>
        function.ReturnType is NamedType { Path: ["Coroutine"], TypeArguments: [NamedType { Path: ["Wait"] }] };

    public static bool IsWait(RuleContext context, YieldStmt yield) =>
        yield.Value is { } value && context.Model.Types.TypeOf(value) is NamedRef { Symbol: { Name: "Wait" } symbol } && Tree.InStdlib(context, symbol);

    /// <summary>The nodes of a body, not those of a lambda in it.</summary>
    public static IEnumerable<Node> Walk(Node body)
    {
        foreach (var child in AstChildren.Of(body))
        {
            if (child is LambdaExpr) continue;
            yield return child;
            foreach (var below in Walk(child)) yield return below;
        }
    }

    public static bool IsTask(RuleContext context, CallExpr call, string name) =>
        LibraryTable.KeyOf(context, call.Callee) == $"std.task.{name}";

    /// <summary>A block whose <c>spawn</c>s of plain calls all stand before its one <c>run</c>.</summary>
    private static (CallExpr[] Spawns, ExprStmt[] Statements, ExprStmt Run)? ScopeOf(RuleContext context, Block block)
    {
        var spawns = new List<ExprStmt>();
        ExprStmt? run = null;
        foreach (var statement in block.Statements.OfType<ExprStmt>())
        {
            if (statement.Expr is not CallExpr call) continue;
            if (IsTask(context, call, "spawn"))
            {
                if (run is not null || call.Arguments is not [CallExpr]) return null;
                spawns.Add(statement);
            }
            else if (IsTask(context, call, "run"))
            {
                if (run is not null) return null;
                run = statement;
            }
        }
        return run is null || spawns.Count == 0 ? null : ([.. spawns.Select(s => (CallExpr)s.Expr)], [.. spawns], run);
    }
}

/// <summary>
/// N01, N02: a task of 4.6 was a <c>Coroutine&lt;Wait&gt;</c> that yielded what it waits for, and
/// <c>run()</c> drove them all to their end; in Lyric 5 a function waits by calling, and a program
/// ends with <c>main</c>. So <c>yield Wait.Now</c> is <c>try yieldNow()</c>, <c>yield Wait.Sleep(ms)</c>
/// is <c>try sleep(Duration.ofMillis(ms))</c>, the function and its callers throw <c>Cancelled</c>, and
/// the <c>spawn</c>s and the <c>run</c> of a block are a <c>TaskScope</c>, whose <c>close</c> waits as
/// <c>run</c> did. What has no such form — readiness, an interrupt, a coroutine held in a value, a
/// <c>spawn</c> far from its <c>run</c> — is said by hand.
/// </summary>
internal sealed class Tasks : IRule
{
    public string Id => "N01";

    private const string Scope = "tasks";

    public void Apply(RuleContext context)
    {
        var facts = TaskFacts.Of(context);
        foreach (var module in context.Modules)
        {
            var text = context.TextOf(module);
            if (facts.Qualified)
            {
                foreach (var import in module.Declarations.OfType<ImportDecl>().Where(i => string.Join('.', i.Path) == "std.task" && i.Clause is not ImportSelective))
                    Say(context, import, "'std.task' is named by an alias, and the task rules read selective imports", "import what is used by name, or rewrite the tasks by hand");
                continue;
            }
            var added = new SortedSet<string>(StringComparer.Ordinal);
            var kept = new HashSet<string>(StringComparer.Ordinal);
            var rewritten = new HashSet<CallExpr>(ReferenceEqualityComparer.Instance);
            var nodes = Tree.Below(module).ToList();
            foreach (var function in nodes.OfType<FunctionDecl>())
                if (facts.Needs.TryGetValue(function, out var thrown))
                {
                    Signature(context, text, function, thrown);
                    if (thrown == "Cancelled") added.Add("Cancelled");
                }
            foreach (var yield in nodes.OfType<YieldStmt>())
                if (TaskFacts.IsWait(context, yield) && !Yield(context, yield, added))
                    kept.Add("Wait");
            foreach (var (spawns, statements, run) in facts.Scopes.Where(s => context.Model.Sources.GetPath(s.Run.Span.File) == context.PathOf(module)))
            {
                var indent = Tree.IndentAt(text, statements[0].Span.Start);
                context.Replace(Id, statements[0].Span.File, statements[0].Span.Start, statements[0].Span.Start, $"let {Scope} = TaskScope.new();\n{indent}");
                foreach (var spawn in spawns)
                {
                    context.Replace("N02", spawn.Callee.Span, $"{Scope}.spawn");
                    context.Replace("N02", spawn.Span.File, spawn.Arguments[0].Span.Start, spawn.Arguments[0].Span.Start, "() => try ");
                    rewritten.Add(spawn);
                }
                context.Replace("N02", run.Span, $"try {Scope}.close();");
                rewritten.Add((CallExpr)run.Expr);
                added.Add("TaskScope");
                context.Note("N02", "LYR-MIG0502", NoteKind.Review, statements[0].Span,
                    "the tasks and 'run()' are a 'TaskScope': 'close()' waits for them as 'run()' did, and the first that fails cancels the others",
                    "where a task should outlive its block, 'spawnDetached' is one nobody waits for, and the program ends with 'main'");
            }
            foreach (var call in nodes.OfType<CallExpr>())
            {
                if (rewritten.Contains(call)) continue;
                foreach (var name in new[] { "spawn", "run", "interrupt", "pending" })
                    if (TaskFacts.IsTask(context, call, name))
                    {
                        kept.Add(name);
                        Say(context, call, $"'{name}' of Lyric 4 has no form here: {Why(name)}", Hint(name));
                    }
            }
            Imports(context, module, text, added, kept);
        }
    }

    private static string Why(string name) => name switch
    {
        "spawn" => "it stands apart from its 'run()', or takes a coroutine held in a value",
        "run" => "no 'spawn' of plain calls stands before it in its block",
        "interrupt" => "a task waits for a signal on a channel in Lyric 5",
        _ => "nothing counts the tasks in Lyric 5",
    };

    private static string Hint(string name) => name switch
    {
        "spawn" or "run" => "start the tasks in a 'TaskScope', whose 'close()' waits for them, or with 'spawnDetached'",
        "interrupt" => "'os.signals(Signal.Interrupt)' gives the channel",
        _ => "keep the handles 'spawn' returns",
    };

    private void Say(RuleContext context, Node node, string message, string hint) =>
        context.Note(Id, "LYR-MIG0501", NoteKind.Hand, node.Span, message, hint);

    /// <summary>A task returns nothing now and throws what its waits throw.</summary>
    private void Signature(RuleContext context, string text, FunctionDecl function, string thrown)
    {
        var file = function.Span.File;
        if (TaskFacts.ReturnsTask(function)) context.Replace(Id, function.ReturnType!.Span, "void");
        if (function.Throws is { Type: { } type })
        {
            var written = text[type.Span.Start..type.Span.End];
            if (written != thrown) context.Replace(Id, type.Span, $"[{written}, {thrown}]");
        }
        else if (function.Throws is null)
        {
            if (function.ReturnType is { } returned) context.Replace(Id, file, returned.Span.End, returned.Span.End, $" throws {thrown}");
            else context.Replace(Id, file, function.Body!.Span.Start, function.Body.Span.Start, $"throws {thrown} ");
        }
    }

    /// <summary>A yield as the call that waits; <c>false</c> where it has no form.</summary>
    private bool Yield(RuleContext context, YieldStmt yield, SortedSet<string> added)
    {
        var file = yield.Span.File;
        switch (yield.Value)
        {
            case MemberExpr { Member: "Now" }:
                context.Replace(Id, yield.Span, "try yieldNow();");
                added.Add("yieldNow");
                return true;
            case CallExpr { Callee: MemberExpr { Member: "Sleep" }, Arguments: [var time] }:
                context.Replace(Id, file, yield.Span.Start, time.Span.Start, "try sleep(Duration.ofMillis(");
                context.Replace(Id, file, time.Span.End, yield.Span.End, "));");
                added.Add("sleep");
                return true;
        }
        Say(context, yield, "a task that waits for a descriptor or an interrupt has no form here: I/O parks by itself in Lyric 5",
            "read or write without the 'yield'; 'os.signals(Signal.Interrupt)' gives a channel for an interrupt");
        return false;
    }

    /// <summary>The imports of <c>std.task</c> say what the rules wrote; <c>Duration</c> comes from <c>std.time</c>.</summary>
    private void Imports(RuleContext context, Module module, string text, SortedSet<string> added, HashSet<string> kept)
    {
        var imports = module.Declarations.OfType<ImportDecl>().ToList();
        var task = imports.FirstOrDefault(i => string.Join('.', i.Path) == "std.task" && i.Clause is ImportSelective);
        var line = "";
        if (added.Contains("sleep") && !imports.Any(i => string.Join('.', i.Path) == "std.time" && i.Clause is ImportSelective s && s.Names.Contains("Duration")))
            line = "import std.time { Duration };\n";
        if (task?.Clause is ImportSelective selective)
        {
            var names = selective.Names.Where(n => n is not ("Wait" or "spawn" or "run") || kept.Contains(n)).Concat(added)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (names.Count > 0 && !names.SequenceEqual(selective.Names, StringComparer.Ordinal))
                context.Replace(Id, selective.Span, "{ " + string.Join(", ", names) + " }");
            if (line.Length > 0) Insert(context, module, text, task, line);
        }
        else if (added.Count > 0)
            Insert(context, module, text, imports.LastOrDefault(), $"import std.task {{ {string.Join(", ", added)} }};\n{line}");
    }

    private void Insert(RuleContext context, Module module, string text, ImportDecl? after, string line)
    {
        var at = 0;
        if (after is not null)
        {
            at = after.Span.End;
            if (at < text.Length && text[at] == '\r') at++;
            if (at < text.Length && text[at] == '\n') at++;
        }
        context.Replace(Id, module.Span.File, at, at, line);
    }
}
