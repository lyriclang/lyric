using Lyric.AST;
using Lyric.Core;

namespace Lyric.Resolver;

/// <summary>
/// The built-in types plus the language built-ins `Throwable` (an interface with an abstract
/// `message(): string`), `panic` (return type never) and `same` (identity of two references).
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
        scope.TryDeclare(CreateThrowable());
        scope.TryDeclare(CreatePanic());
        scope.TryDeclare(CreateSame());
        // Coroutine<T>: the name resolves here, the type form is built by the sema.
        scope.TryDeclare(new TypeSymbol("Coroutine", TypeSymbolKind.Builtin, Visibility.Public,
            new SymbolTable(), declaration: null));
        return scope;
    }

    // `Throwable` as a real interface symbol with a synthetic AST, so the conformance check and
    // member lookup run through the ordinary paths.
    private static TypeSymbol CreateThrowable()
    {
        // Nothing here stands in a file, so both spans are the default one and carry an invalid
        // FileId. A consumer that offers to jump to a declaration checks for that already.
        var message = new FunctionDecl(
            IsPublic: true, IsMut: false, IsStatic: false, Name: "message", Generics: [], Parameters: [],
            ReturnType: new NamedType(["string"], [], default) { NameSpan = default },
            Throws: null, Body: null, Span: default)
            { NameSpan = default };
        var decl = new InterfaceDecl(IsPublic: true, Name: "Throwable", Generics: [], Interfaces: [], Members: [message], Span: default)
            { NameSpan = default };
        var members = new SymbolTable();
        members.TryDeclare(new FunctionSymbol("message", Visibility.Public, isMut: false, message));
        return new TypeSymbol("Throwable", TypeSymbolKind.Interface, Visibility.Public, members, decl);
    }

    // `panic(message: string)`: the never return type is not nameable, so the sema sets it for
    // this symbol directly.
    private static FunctionSymbol CreatePanic()
    {
        var decl = new FunctionDecl(
            IsPublic: true, IsMut: false, IsStatic: false, Name: "panic", Generics: [],
            Parameters: [new Param(IsParams: false, Name: "message",
                Type: new NamedType(["string"], [], default) { NameSpan = default },
                Default: null, Span: default)
                { NameSpan = default }],
            ReturnType: null, Throws: null, Body: null, Span: default) { NameSpan = default };
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
            IsPublic: true, IsMut: false, IsStatic: false, Name: "same",
            Generics: [new GenericParam("T", [], default) { NameSpan = default }],
            Parameters: [Parameter("a"), Parameter("b")],
            ReturnType: new NamedType(["bool"], [], default) { NameSpan = default },
            Throws: null, Body: null, Span: default) { NameSpan = default };
        return new FunctionSymbol("same", Visibility.Public, isMut: false, decl);
    }

    public static bool IsBuiltin(string name) => Array.IndexOf(Names, name) >= 0;
}
