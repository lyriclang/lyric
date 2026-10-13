using System.Reflection;
using Lyric.Core;

namespace Lyric.AST;

/// <summary>
/// A tree with some of its nodes replaced (13 M9b P6): what a macro's expansion writes back into
/// the module. Generic over the record types of the AST through their properties: a node whose
/// children changed is cloned (the record's own copy) and the changed properties set, so a node
/// kind added to the AST takes part without a line here. A replaced node is not walked into: what
/// a macro gave back may hold sites of its own, which the next round expands.
///
/// <para>The one place the tree names a node twice, a call's parenthesized and spread arguments
/// (tuples beside the argument array), is kept consistent by hand.</para>
/// </summary>
public static class AstRewriter
{
    /// <param name="single">The node that stands in place of a node, or <c>null</c> to keep it.</param>
    /// <param name="splice">The statements that stand in place of a statement of a block, or
    /// <c>null</c> to keep it; asked before <paramref name="single"/> for the statements of blocks.</param>
    public static T Rewrite<T>(T root, Func<Node, Node?> single, Func<Stmt, Stmt[]?>? splice = null) where T : Node =>
        (T)RewriteNode(root, single, splice);

    private static readonly Dictionary<Type, PropertyInfo[]> Properties = new();
    private static readonly Dictionary<Type, MethodInfo> Clones = new();

    private static PropertyInfo[] PropertiesOf(Type type)
    {
        lock (Properties)
        {
            if (Properties.TryGetValue(type, out var known)) return known;
            var found = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite && p.GetIndexParameters().Length == 0 && Holds(p.PropertyType))
                .ToArray();
            Properties[type] = found;
            return found;
        }
    }

    /// <summary>Whether a property of this type can hold a node: a node, an array of nodes, an
    /// array of tuples that hold one.</summary>
    private static bool Holds(Type type) =>
        typeof(Node).IsAssignableFrom(type)
        || (type.IsArray && type.GetElementType() is { } element && (typeof(Node).IsAssignableFrom(element) || IsNodeTuple(element)));

    private static bool IsNodeTuple(Type type) =>
        type.IsGenericType && type.FullName!.StartsWith("System.ValueTuple`", StringComparison.Ordinal)
        && type.GetGenericArguments().Any(a => typeof(Node).IsAssignableFrom(a));

    private static Node Clone(Node node)
    {
        var type = node.GetType();
        MethodInfo? clone;
        lock (Clones)
        {
            if (!Clones.TryGetValue(type, out clone))
            {
                clone = type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance)
                    ?? throw new InvalidOperationException($"{type.Name} is no record: the rewriter cannot copy it");
                Clones[type] = clone;
            }
        }
        return (Node)clone.Invoke(node, null)!;
    }

    private static Node RewriteNode(Node node, Func<Node, Node?> single, Func<Stmt, Stmt[]?>? splice)
    {
        if (single(node) is { } replaced) return replaced;
        Node? copy = null;
        foreach (var property in PropertiesOf(node.GetType()))
        {
            // the tuples of a call are settled with the arguments, below
            if (node is CallExpr && property.Name is nameof(CallExpr.Parenthesized) or nameof(CallExpr.Spreads)) continue;
            var value = property.GetValue(node);
            var rewritten = RewriteValue(value, single, splice);
            if (ReferenceEquals(rewritten, value)) continue;
            copy ??= Clone(node);
            property.SetValue(copy, rewritten);
        }
        if (copy is CallExpr call && node is CallExpr original) copy = Retie(original, call);
        return copy ?? node;
    }

    private static object? RewriteValue(object? value, Func<Node, Node?> single, Func<Stmt, Stmt[]?>? splice)
    {
        switch (value)
        {
            case null:
                return null;
            case Node child:
                return RewriteNode(child, single, splice);
            case Stmt[] statements when splice is not null:
            {
                List<Stmt>? changed = null;
                for (var i = 0; i < statements.Length; i++)
                {
                    var spliced = splice(statements[i]);
                    var one = spliced is null ? (Stmt)RewriteNode(statements[i], single, splice) : null;
                    if (spliced is null && ReferenceEquals(one, statements[i]) && changed is null) continue;
                    changed ??= [.. statements.Take(i)];
                    if (spliced is not null) changed.AddRange(spliced); else changed.Add(one!);
                }
                return changed is null ? statements : changed.ToArray();
            }
            case Array array when array.GetType().GetElementType() is { } element && typeof(Node).IsAssignableFrom(element):
            {
                Array? changed = null;
                for (var i = 0; i < array.Length; i++)
                {
                    var item = (Node)array.GetValue(i)!;
                    var one = RewriteNode(item, single, splice);
                    if (ReferenceEquals(one, item)) continue;
                    changed ??= (Array)array.Clone();
                    changed.SetValue(one, i);
                }
                return changed ?? array;
            }
            default:
                return value; // a tuple array: a call's, retied with its arguments
        }
    }

    /// <summary>The call's parenthesized and spread arguments name the argument nodes; after the
    /// arguments changed, they name the new ones at the same positions.</summary>
    private static CallExpr Retie(CallExpr original, CallExpr rewritten)
    {
        (Expr Argument, Span Written)[]? Map((Expr Argument, Span Written)[]? pairs) => pairs is null ? null
            : [.. pairs.Select(p =>
            {
                var index = Array.IndexOf(original.Arguments, p.Argument);
                return index >= 0 && index < rewritten.Arguments.Length ? (rewritten.Arguments[index], p.Written) : p;
            })];
        return rewritten with { Parenthesized = Map(original.Parenthesized), Spreads = Map(original.Spreads) };
    }
}
