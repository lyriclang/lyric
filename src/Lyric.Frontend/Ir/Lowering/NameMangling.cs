using Lyric.AST;
using Lyric.Resolver;

namespace Lyric.Ir.Lowering;

/// <summary>
/// Produces the names under which functions stand in the IR. They are not cosmetic: they become the
/// symbol names in the bytecode, and the verifier rejects collisions, because two functions under the
/// same name would be a silent wrong call.
///
/// <para>The scheme is <c>&lt;module path&gt;.&lt;function&gt;</c>, so <c>main.add</c>. The module path
/// is already unique — one file is one module — so that suffices.</para>
///
/// <para>For monomorphization an instance needs its type arguments in the name, or
/// <c>max&lt;int&gt;</c> and <c>max&lt;float&gt;</c> collide. That belongs here and nowhere else, which
/// is why the mangling lives in a class of its own rather than as string interpolation in the
/// lowerer.</para>
/// </summary>
internal static class NameMangling
{
    public static string ForFunction(ModuleSymbol module, string functionName) =>
        $"{module.FullName}.{functionName}";

    /// <summary>A method: <c>&lt;module&gt;.&lt;type&gt;.&lt;method&gt;</c>. The type name has to be in
    /// it, or <c>Account.get</c> and <c>Player.get</c> collide, and the verifier rejects duplicate
    /// function names, because they would be a silent wrong call.</summary>
    public static string ForMethod(ModuleSymbol module, string typeName, string methodName) =>
        $"{module.FullName}.{typeName}.{methodName}";

    /// <summary>An extension method:
    /// <c>&lt;declaring module&gt;.&lt;extend&gt;.&lt;target&gt;.&lt;method&gt;</c>.
    ///
    /// <para>Two things distinguish this from <see cref="ForMethod"/>. First, the DECLARING module
    /// stands here rather than that of the target type: <c>extend string</c> may stand in any number of
    /// modules, and the target type may belong to none of them. Second, the <c>&lt;extend&gt;</c> infix:
    /// an extension may shadow a member of the same name — the sema does NOT report that, it simply lets
    /// the own member win. Without the infix both would be called <c>main.Player.get</c>, and the
    /// verifier rejects duplicate function names: a cleanly type-checked program would crash in the
    /// lowering.</para>
    ///
    /// <para>The angle brackets are no accident — an identifier cannot contain them, so the name is not
    /// producible in source. The same convention as for <c>&lt;globals&gt;</c>.</para></summary>
    public static string ForExtension(ModuleSymbol declaringModule, string targetName, string methodName) =>
        $"{declaringModule.FullName}.<extend>.{targetName}.{methodName}";

    /// <summary>
    /// The suffix that tells OVERLOADS apart: <c>main.show(int)</c> beside <c>main.show(string)</c>.
    ///
    /// <para>Empty for a name declared once, which is nearly every name — so a program without
    /// overloads compiles to the same bytes it always did, and a disassembly of one keeps reading
    /// the way it always read.</para>
    ///
    /// <para>The parameter types come from what was WRITTEN rather than from the lowered types: a
    /// disassembly is read beside the source, and <c>?Item[]</c> says more there than the layout
    /// it becomes. Two different types whose written forms print alike would collide, so the
    /// caller passes an ordinal that breaks the tie.</para>
    /// </summary>
    public static string OverloadSuffix(Param[] parameters, int ordinal)
    {
        var written = string.Join(", ", parameters.Select(p => Written(p.Type)));
        return ordinal == 0 ? $"({written})" : $"({written})#{ordinal}";
    }

    /// <summary>A written type as one short token. Not a full printer: it separates signatures,
    /// which is all a name has to do.</summary>
    private static string Written(TypeNode node) => node switch
    {
        NamedType named => named.TypeArguments.Length == 0
            ? named.Path[^1]
            : named.Path[^1] + "<" + string.Join(", ", named.TypeArguments.Select(Written)) + ">",
        NullableType option => "?" + Written(option.Inner),
        ArrayType array => Written(array.Element) + "[]",
        TupleType tuple => "(" + string.Join(", ", tuple.Elements.Select(Written)) + ")",
        ThrowingType throwing => Written(throwing.Inner) + " throws",
        FunctionType fn => "fn(" + string.Join(", ", fn.Parameters.Select(Written)) + ") -> "
                           + Written(fn.ReturnType),
        _ => "?",
    };
}
