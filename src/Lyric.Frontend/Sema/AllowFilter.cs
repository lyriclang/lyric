using System.Text.RegularExpressions;
using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;

namespace Lyric.Sema;

/// <summary>
/// <c>@Allow(code)</c> and <c>[lints] allow</c> (design/v5/spec/09 A10; 13 M9a P8): two places, one
/// mechanism — the manifest's list is the attribute on the package. A warning whose code one names
/// is not reported: within the declaration the attribute sits on, the module whose header carries
/// it, the package whose manifest lists it. An error always is. An <c>@Allow</c> that suppresses
/// nothing warns itself (Clippy), so a stale one leaves with the code it was written for. Lint
/// names beside the codes — <c>dead-code</c> for <c>LYR-SEM0071</c> — come with M12 (11 G2).
/// </summary>
public static partial class AllowFilter
{
    /// <summary>A code as a diagnostic writes it: <c>LYR-SEM0071</c>.</summary>
    public static bool IsCode(string text) => CodeForm().IsMatch(text);

    [GeneratedRegex("^LYR-[A-Z]+[0-9]{4}$")]
    private static partial Regex CodeForm();

    /// <summary>
    /// Takes back what the program's <c>@Allow</c>s and its packages' lists suppress, once every
    /// warning is in. <paramref name="fieldsOf"/> gives an attribute's computed string fields
    /// (09 F4); <paramref name="packages"/> each package's <c>[lints] allow</c>, by name.
    /// </summary>
    public static void Apply(Compilation compilation, TypeResult types, DiagnosticEngine diagnostics,
        Func<AttributeNode, IReadOnlyDictionary<string, string>?> fieldsOf,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? packages)
    {
        var allow = compilation.FindModule(["std", "core"])?.Members.LookupLocal("Allow");
        var sites = new List<(Span Extent, string Code, AttributeNode Attribute)>();
        var packageOf = new Dictionary<FileId, string>();

        void Collect(Node node)
        {
            foreach (var attribute in AttributesOf(node))
            {
                if (!ReferenceEquals(types.RefOf(attribute), allow)) continue;
                var code = fieldsOf(attribute)?.GetValueOrDefault("code") ?? "";
                if (IsCode(code)) sites.Add((node.Span, code, attribute));
                else
                    diagnostics.Report("LYR-SEM0179", Severity.Error, attribute.Span,
                        $"'@Allow' names a warning by its code, as \"LYR-SEM0071\" — not \"{code}\"; lint names come with M12");
            }
            foreach (var child in AstChildren.Of(node))
                if (child is not AttributeNode) Collect(child);
        }

        foreach (var module in compilation.Modules)
        {
            // what std says of itself is no warning a program sees (11 G11)
            if (compilation.IsNative(module) || module.Path.Length == 0) continue;
            var ast = compilation.AstOf(module);
            packageOf[ast.Span.File] = module.Path[0];
            if (allow is not null) Collect(ast);
        }

        bool Listed(FileId file, string code) =>
            packageOf.TryGetValue(file, out var package) && packages?.GetValueOrDefault(package) is { } listed
            && listed.Contains(code, StringComparer.Ordinal);

        var used = new HashSet<AttributeNode>(ReferenceEqualityComparer.Instance);
        diagnostics.Suppress(d =>
        {
            if (Listed(d.Span.File, d.Code)) return true;
            var hit = false;
            foreach (var (extent, code, attribute) in sites)
                if (code == d.Code && extent.File == d.Span.File && d.Span.Start >= extent.Start && d.Span.End <= extent.End)
                {
                    used.Add(attribute);
                    hit = true;
                }
            return hit;
        });

        foreach (var (_, code, attribute) in sites)
            if (!used.Contains(attribute) && !Listed(attribute.Span.File, "LYR-SEM0180"))
                diagnostics.Report("LYR-SEM0180", Severity.Warning, attribute.Span,
                    $"this '@Allow' suppresses nothing: no '{code}' stands in what it sits on");
    }

    /// <summary>The attributes a node carries: every declaration's (09 T7), and the module's header.</summary>
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
