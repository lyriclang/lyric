using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>
/// The lints' levels (spec 16 §5), once every warning is in.
/// A warning is a lint — a name, a group, a level of its own (the catalog's) —, and where it is met
/// its level is <c>allow</c> (it is not said), <c>warn</c> (it is) or <c>deny</c> (it is an error,
/// with a note naming who denied it). The level is the narrowest <c>@Allow</c>, <c>@Warn</c> or
/// <c>@Deny</c> around it; without one the package's:
/// the command line's (<c>-A</c>, <c>-W</c>, <c>-D</c>) over the manifest's <c>[lints]</c> over the
/// lint's own (<see cref="LintSettings"/>). Three places, one mechanism.
///
/// <para>The warnings of a dependency and of <c>std</c> are not said unless <c>--warn-deps</c> asks,
/// and then never as errors: another package's levels cannot fail this build. Its errors always are.
/// An <c>@Allow</c> that decides no warning warns itself (<c>useless-allow</c>), so a stale one — or
/// one a narrower level overrules — leaves with the lint it was written for.</para>
/// </summary>
public static class LintFilter
{
    private sealed record Site(Span Extent, string Selector, IReadOnlyList<CatalogEntry> Lints, int Specificity,
        LintLevel Level, string Mark, AttributeNode Attribute);

    public static void Apply(Compilation compilation, TypeResult types, DiagnosticEngine diagnostics,
        Func<AttributeNode, IReadOnlyDictionary<string, string>?> fieldsOf,
        IReadOnlyDictionary<string, LintSettings>? packages, string? root = null, bool warnDeps = false)
    {
        var core = compilation.FindModule(["std", "core"])?.Members;
        var marks = new (Symbol? Symbol, LintLevel Level, string Name)[]
        {
            (core?.LookupLocal("Allow"), LintLevel.Allow, "Allow"),
            (core?.LookupLocal("Warn"), LintLevel.Warn, "Warn"),
            (core?.LookupLocal("Deny"), LintLevel.Deny, "Deny"),
        };
        var sites = new List<Site>();
        var packageOf = new Dictionary<FileId, string>();

        void Collect(Node node)
        {
            foreach (var attribute in AttributesOf(node))
            {
                var target = types.RefOf(attribute);
                var mark = marks.FirstOrDefault(m => m.Symbol is not null && ReferenceEquals(target, m.Symbol));
                if (mark.Symbol is null) continue;
                var selector = fieldsOf(attribute)?.GetValueOrDefault("lint") ?? "";
                if (DiagnosticCatalog.Select(selector) is { } named)
                    sites.Add(new Site(node.Span, selector, named.Lints, named.Specificity, mark.Level, mark.Name, attribute));
                else
                    diagnostics.Report("LYR-SEM0179", Severity.Error, attribute.Span,
                        $"'@{mark.Name}' names a lint by its name, its group or its code — there is no lint '{selector}'"
                        + (DiagnosticCatalog.Nearest(selector) is { } near ? $"; did you mean '{near}'?" : ""));
            }
            foreach (var child in AstChildren.Of(node))
                if (child is not AttributeNode) Collect(child);
        }

        // std's files and the dependencies' are foreign: their warnings are theirs to read
        var foreign = new HashSet<FileId>();
        foreach (var module in compilation.Modules)
        {
            if (module.Path.Length == 0) continue;
            var ast = compilation.AstOf(module);
            packageOf[ast.Span.File] = module.Path[0];
            if (compilation.IsNative(module) || (root is not null && module.Path[0] != root)) foreign.Add(ast.Span.File);
            Collect(ast);
        }

        static bool Around(Span extent, Span at) =>
            extent.File == at.File && at.Start >= extent.Start && at.End <= extent.End;

        // the narrowest site around the finding that names its lint; of two alike, the more specific, then the later
        Site? Deciding(Diagnostic d, CatalogEntry lint, Site? not = null)
        {
            Site? best = null;
            foreach (var site in sites)
            {
                if (ReferenceEquals(site, not) || !Around(site.Extent, d.Span) || !site.Lints.Contains(lint)) continue;
                if (best is null || site.Extent.Length < best.Extent.Length
                    || (site.Extent.Length == best.Extent.Length && site.Specificity >= best.Specificity))
                    best = site;
            }
            return best;
        }

        static CatalogEntry? LintOf(Diagnostic d) =>
            DiagnosticCatalog.Entries.GetValueOrDefault(d.Code) is { Lint: not null } lint ? lint : null;

        // which '@Allow's decide a warning: the ones a stale or overruled allow is told from
        var used = new HashSet<Site>(ReferenceEqualityComparer.Instance);

        Diagnostic? Decide(Diagnostic d, Site? about = null)
        {
            if (LintOf(d) is not { } lint || !packageOf.TryGetValue(d.Span.File, out var package)) return d;
            var isForeign = foreign.Contains(d.Span.File);
            if (isForeign && !warnDeps) return null;
            LintLevel level;
            DiagnosticNote why;
            if (Deciding(d, lint, about) is { } site)
            {
                level = site.Level;
                used.Add(site);
                why = new DiagnosticNote(site.Attribute.Span, $"'{lint.Lint}' is denied here by '@{site.Mark}(\"{site.Selector}\")'");
            }
            else
            {
                var (set, origin) = (packages?.GetValueOrDefault(package) ?? LintSettings.Default).LevelOf(lint);
                level = set;
                why = new DiagnosticNote(origin is null ? $"'{lint.Lint}' is denied by its own level" : $"'{lint.Lint}' is denied by {origin}");
            }
            // another package's levels never fail this build
            if (isForeign && level == LintLevel.Deny) level = LintLevel.Warn;
            return level switch
            {
                LintLevel.Allow => null,
                LintLevel.Deny => d with { Severity = Severity.Error, Notes = [.. d.Notes ?? [], why] },
                _ => d,
            };
        }

        diagnostics.Revise(d => Decide(d));

        // an '@Allow' in the package's own files that decided nothing, at the level useless-allow has
        // there — never by the '@Allow' itself, or '@Allow("unused")' would take its own back
        foreach (var site in sites)
            if (site.Level == LintLevel.Allow && !used.Contains(site) && !foreign.Contains(site.Attribute.Span.File)
                && Decide(new Diagnostic("LYR-SEM0180", Severity.Warning, site.Attribute.Span,
                    $"this '@Allow' takes back nothing: no '{site.Selector}' is met in what it sits on"), site) is { } stale)
                diagnostics.Report(stale);
    }

    /// <summary>The attributes a node carries: every declaration's, and a module header's.</summary>
    private static AttributeNode[] AttributesOf(Node node) => node switch
    {
        Module m => m.Attributes,
        Param p => p.Attributes,
        FunctionDecl f => f.Attributes,
        StaticBindingDecl s => s.Attributes,
        FieldDecl f => f.Attributes,
        StructDecl s => s.Attributes,
        ClassDecl c => c.Attributes,
        EnumDecl e => e.Attributes,
        EnumVariant v => v.Attributes,
        InterfaceDecl i => i.Attributes,
        ExtendDecl x => x.Attributes,
        AssociatedTypeDecl t => t.Attributes,
        GlobalBindingDecl g => g.Attributes,
        TypeAliasDecl a => a.Attributes,
        _ => [],
    };
}
