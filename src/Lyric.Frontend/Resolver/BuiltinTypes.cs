using Lyric.AST;
using Lyric.Core;

namespace Lyric.Resolver;

/// <summary>
/// The built-in types plus the language built-ins `panic` (return type never) and `same`
/// (identity of two references). The root of what is thrown is no built-in since Lyric 5: it is
/// `std.core`'s `Error` (design/v5/spec/05 E6 O1), visible without an import like every public
/// type of `std.core`.
///
/// They live in a scope that is the root parent of every module scope, so a name resolves through
/// the ordinary lookup chain with no special case in the resolver.
/// </summary>
public static class BuiltinTypes
{
    public static readonly string[] Names =
    {
        "int", "uint", "float",
        "int8", "int16", "int32", "int64",
        "uint8", "uint16", "uint32", "uint64",
        "float32", "float64",
        "bool", "char", "string", "void", "never"
    };

    /// <summary>Creates a fresh scope holding every built-in type symbol.</summary>
    public static SymbolTable CreateScope()
    {
        var scope = new SymbolTable();
        foreach (var name in Names)
            scope.TryDeclare(new TypeSymbol(name, TypeSymbolKind.Builtin, Visibility.Public,
                new SymbolTable(), declaration: null));
        scope.TryDeclare(CreatePanic());
        scope.TryDeclare(CreateSame());
        // Coroutine<T>: the name resolves here, the type form is built by the sema.
        scope.TryDeclare(new TypeSymbol("Coroutine", TypeSymbolKind.Builtin, Visibility.Public,
            new SymbolTable(), declaration: null));
        // Slice<T>, the view of T[] (design/v5/spec/03 T13 A2; 10 C2: a language primitive of
        // std.core, visible without an import): the same way.
        scope.TryDeclare(new TypeSymbol("Slice", TypeSymbolKind.Builtin, Visibility.Public,
            new SymbolTable(), declaration: null));
        // StringView, the view of a string's bytes (10 S1, 03 A2): the same way, with no
        // parameter; its members come from std.core's blocks, as a string's do.
        scope.TryDeclare(new TypeSymbol("StringView", TypeSymbolKind.Builtin, Visibility.Public,
            new SymbolTable(), declaration: null));
        return scope;
    }

    // `panic(message: string): never` (05 E8, E12): it does not return, which its type says like
    // any other function's would.
    private static FunctionSymbol CreatePanic()
    {
        var decl = new FunctionDecl(
            Visibility: VisibilityWord.Pub, IsMut: false, IsStatic: false, Name: "panic", Generics: [],
            Parameters: [new Param(IsParams: false, Name: "message",
                Type: new NamedType(["string"], [], default) { NameSpan = default },
                Default: null, Span: default)
                { NameSpan = default }],
            ReturnType: new NamedType(["never"], [], default) { NameSpan = default },
            Throws: null, Body: null, Span: default) { NameSpan = default };
        return new FunctionSymbol("panic", Visibility.Public, isMut: false, decl);
    }

    // `same(a, b)`: whether two references are one object (design/v5/spec/02 M10). Identity is
    // a function and not an operator — `==` means value equality through `Equatable` and nothing
    // else. The declaration reads as the generic it is for a reader (`fn same<T>(a: T, b: T):
    // bool`); what it accepts — two references of one type, never a value — is the sema's rule,
    // which no constraint of the language can spell.
    private static FunctionSymbol CreateSame()
    {
        Param Parameter(string name) => new(IsParams: false, Name: name,
            Type: new NamedType(["T"], [], default) { NameSpan = default }, Default: null, Span: default)
            { NameSpan = default };
        var decl = new FunctionDecl(
            Visibility: VisibilityWord.Pub, IsMut: false, IsStatic: false, Name: "same",
            Generics: [new GenericParam("T", [], default) { NameSpan = default }],
            Parameters: [Parameter("a"), Parameter("b")],
            ReturnType: new NamedType(["bool"], [], default) { NameSpan = default },
            Throws: null, Body: null, Span: default) { NameSpan = default };
        return new FunctionSymbol("same", Visibility.Public, isMut: false, decl);
    }

    public static bool IsBuiltin(string name) => Array.IndexOf(Names, name) >= 0;
}
