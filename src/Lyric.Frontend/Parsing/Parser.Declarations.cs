using Lyric.AST;
using Lyric.Core;
using Lyric.Lexing;

namespace Lyric.Parsing;

/// <summary>
/// The module and declaration parser, recursive descent.
///
/// Contextual keywords: <c>throws</c> and <c>type</c> are not in the keyword list — the lexer
/// yields them as identifiers and they are recognised here by position.
///
/// Member separation: in a struct or class a field needs a ',', a block-bodied method does not.
/// Enum variants are separated by ','.
/// In an interface, extend or enum body the members form a sequence without separators.
/// </summary>
public sealed partial class Parser
{
    /// <summary>Entry point for a whole file: an optional module header plus top-level declarations.</summary>
    public Module ParseModule()
    {
        try
        {
            return ParseModuleInner();
        }
        catch (NestingTooDeep deep)
        {
            // §12.4. Reported once, here, because the recursion that hit the bound has unwound
            // past every position that could have reported it — and a second diagnostic from the
            // recovery that followed would only describe the wreckage.
            _de.Report("LYR-PAR0045", Severity.Error, deep.At,
                $"nesting is deeper than this parser carries ({MaxNesting} levels) — the language "
                + "sets no limit, this implementation does, and the alternative was taking the "
                + "process down");
            return new Module(null, [], deep.At);
        }
    }

    private Module ParseModuleInner()
    {
        var start = _buffer.Current.Span;

        // Attributes at the top of the file bind to the HEADER when one follows, and to the first
        // declaration otherwise. The distinction has to fall here: once the header is parsed there
        // is no second place where module attributes could stand.
        var leading = ParseAttributeList();
        AttributeNode[] moduleAttributes = [];
        AttributeNode[] pending = [];
        if (_buffer.Check(TokenKind.Module)) moduleAttributes = leading;
        else pending = leading;

        ModulePath? header = _buffer.Check(TokenKind.Module) ? ParseModuleHeader() : null;

        var decls = new List<Decl>();
        while (!_buffer.AtEnd)
        {
            var before = _buffer.Position;
            decls.Add(ParseTopLevelDecl(pending));
            pending = [];
            if (_buffer.Position == before) _buffer.Advance(); // force progress
        }

        // Attributes with no declaration to bind to: at EOF the list would silently vanish.
        if (pending.Length > 0)
            _de.Report("LYR-PAR0042", Severity.Error, pending[0].Span,
                "an attribute must be followed by the declaration it applies to");

        var end = decls.Count > 0 ? decls[^1].Span : (header?.Span ?? start);
        return new Module(header, decls.ToArray(), Span.Union(start, end)) { Attributes = moduleAttributes };
    }

    private bool AtAttributeStart =>
        _buffer.Check(TokenKind.AtIdentifier) || _buffer.Check(TokenKind.AtLBracket);

    /// <summary>
    /// Zero or more attributes: <c>@Name</c>, <c>@Name { field = expr, … }</c>,
    /// <c>@Name(value)</c>, or a group <c>@[Name, Name { … }, Name(value)]</c> — the group is
    /// the same list the stacked spelling declares. The VALUES are parsed as expressions; that
    /// they must be literals is a semantic rule, so the message can name the offending
    /// expression instead of refusing to read it — and which attribute admits the parenthesized
    /// form is the checker's rule too (<c>WithArg&lt;T&gt;</c>).
    /// </summary>
    private AttributeNode[] ParseAttributeList()
    {
        if (!AtAttributeStart) return [];

        var attributes = new List<AttributeNode>();
        while (AtAttributeStart)
        {
            if (_buffer.Check(TokenKind.AtIdentifier))
            {
                var at = _buffer.Advance();
                // The single-segment case: the name is the token minus its '@'.
                attributes.Add(ParseAttributeEntry(at,
                    _sm.Slice(at.Span)[1..].ToString(),
                    new Span(at.Span.File, at.Span.Start + 1, at.Span.End)));
                continue;
            }

            // '@[A, B { x = 1 }, C(v)]' — the same list the stacked spelling declares. The
            // entries carry no '@' of their own, and at least one must stand, so '@[]' is
            // refused here. On a non-name the loop reports once and leaves the token where it
            // is, rather than building an attribute out of it for the sema to re-refuse.
            _buffer.Advance(); // '@['
            do
            {
                if (!_buffer.Check(TokenKind.Identifier))
                {
                    _de.Report("LYR-PAR0026", Severity.Error, _buffer.Current.Span,
                        $"expected attribute name, got {_buffer.Current.TokenKind}");
                    break;
                }
                var first = _buffer.Advance();
                attributes.Add(ParseAttributeEntry(first,
                    _sm.Slice(first.Span).ToString(), first.Span));
            } while (_buffer.Match(TokenKind.Comma) && !_buffer.Check(TokenKind.RBracket));
            _buffer.Expect(TokenKind.RBracket, "LYR-PAR0018",
                "expected ']' to close the attribute group");
        }
        return attributes.ToArray();
    }

    /// <summary>One attribute, from its already-consumed first token: the rest of the path,
    /// then the arguments — a braces block of named fields, or one parenthesized value.</summary>
    private AttributeNode ParseAttributeEntry(Token first, string firstSegment, Span firstNameSpan)
    {
        var path = new List<string> { firstSegment };
        var pathEnd = first.Span;
        var nameSpan = firstNameSpan;
        while (_buffer.Match(TokenKind.Dot))
        {
            var segment = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0026",
                $"expected attribute name, got {_buffer.Current.TokenKind}");
            path.Add(_sm.Slice(segment.Span).ToString());
            nameSpan = segment.Span;
            pathEnd = segment.Span;
        }

        var fields = new List<StructInitField>();
        Expr? positional = null;
        var end = pathEnd;
        if (_buffer.Check(TokenKind.LBrace))
        {
            _buffer.Advance();
            while (!_buffer.Check(TokenKind.RBrace) && !_buffer.AtEnd)
            {
                var nameTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0026",
                    $"expected field name, got {_buffer.Current.TokenKind}");
                _buffer.Expect(TokenKind.Equal, "LYR-PAR0037",
                    "expected '=' in attribute arguments (':' is only for types)");
                var value = ParseSubExpr();
                fields.Add(new StructInitField(_sm.Slice(nameTok.Span).ToString(), value,
                    Span.Union(nameTok.Span, value.Span)) { NameSpan = nameTok.Span });
                if (!_buffer.Match(TokenKind.Comma)) break;
            }
            end = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018",
                "expected '}' to close attribute arguments").Span;
        }
        else if (_buffer.Check(TokenKind.LParen))
        {
            _buffer.Advance();
            positional = ParseSubExpr();
            if (_buffer.Check(TokenKind.RParen))
            {
                end = _buffer.Advance().Span;
            }
            else
            {
                _de.Report("LYR-PAR0018", Severity.Error, _buffer.Current.Span,
                    "expected ')' after the attribute value — the parenthesized form carries one");
                // Recover THROUGH the parenthesis: leaving its leftovers in the stream hands
                // them to the declaration parser, which then blames the attribute for missing
                // its declaration.
                while (!_buffer.Check(TokenKind.RParen) && !_buffer.Check(TokenKind.LBrace)
                    && !_buffer.AtEnd)
                    _buffer.Advance();
                end = _buffer.Check(TokenKind.RParen) ? _buffer.Advance().Span : pathEnd;
            }
        }

        return new AttributeNode(path.ToArray(), fields.ToArray(), Span.Union(first.Span, end))
            { PathSpan = Span.Union(first.Span, pathEnd), NameSpan = nameSpan, Positional = positional };
    }

    private ModulePath ParseModuleHeader()
    {
        var kw = _buffer.Advance(); // 'module'
        var segments = ParseDottedName();
        var semi = ExpectSemicolon();
        return new ModulePath(segments, Span.Union(kw.Span, semi.Span));
    }

    private Decl ParseTopLevelDecl(AttributeNode[] pending)
    {
        // Attributes precede 'pub'. The ones handed down come from the top of a header-less file.
        var attributes = pending;
        if (AtAttributeStart)
        {
            var parsed = ParseAttributeList();
            attributes = pending.Length == 0 ? parsed : [.. pending, .. parsed];
        }

        var start = attributes.Length > 0 ? attributes[0].Span : _buffer.Current.Span;

        if (_buffer.Check(TokenKind.Import))
        {
            RejectAttributes(attributes, "an import");
            return ParseImport(VisibilityWord.None, start);
        }

        var isPublic = ParseVisibility();

        // 'pub import' passes on what it binds (07 V3 I4); an import is otherwise its module's own,
        // and no other word says anything about it.
        if (_buffer.Check(TokenKind.Import))
        {
            RejectAttributes(attributes, "an import");
            if (isPublic is not VisibilityWord.Pub)
                _de.Report("LYR-PAR0053", Severity.Error, start,
                    "an import is its module's own — 'pub import' passes it on, and no other word goes here");
            return ParseImport(isPublic, start);
        }

        // 'sealed' before 'interface' (04 D8): a contextual word, and nothing else's.
        var isSealed = false;
        if (AtContextual("sealed"))
        {
            var word = _buffer.Advance();
            if (_buffer.Check(TokenKind.Interface)) isSealed = true;
            else _de.Report("LYR-PAR0048", Severity.Error, word.Span, "'sealed' stands before 'interface' only");
        }

        switch (_buffer.Current.TokenKind)
        {
            case TokenKind.Mut:
            case TokenKind.Fn:
                return ParseFunctionDecl(isPublic, start) with { Attributes = attributes };
            case TokenKind.Struct:
                return WithAttributes(ParseStructOrClass(isPublic, start, isClass: false), attributes);
            case TokenKind.Class:
                return WithAttributes(ParseStructOrClass(isPublic, start, isClass: true), attributes);
            case TokenKind.Enum:
                return WithAttributes(ParseEnum(isPublic, start), attributes);
            case TokenKind.Interface:
                RejectAttributes(attributes, "an interface");
                return ParseInterface(isPublic, start, isSealed);
            case TokenKind.Extend:
                RejectAttributes(attributes, "an extend block");
                return ParseExtend(isPublic, start);
            case TokenKind.Let:
            case TokenKind.Var:
                RejectAttributes(attributes, "a global binding");
                return ParseGlobalBinding(isPublic, start);

            default:
                if (AtContextual("type"))
                {
                    RejectAttributes(attributes, "a type alias");
                    return ParseTypeAlias(isPublic, isOpaque: false, start);
                }
                // 'extern "dotnet" fn f(x: int): int = "System.Math::Abs";' — contextual like
                // 'type': the word stays an identifier everywhere else, so no program that names
                // something 'extern' loses that name.
                if (AtContextual("extern") && _buffer.Peek(1).TokenKind == TokenKind.StringLiteral)
                {
                    RejectAttributes(attributes, "an extern declaration");
                    return ParseExternDecl(isPublic, start);
                }
                // 'opaque type X = int;' — contextual like 'type' itself: neither word is a
                // keyword, so neither is taken from anyone's identifiers.
                if (AtContextual("opaque") && PeekContextual(1, "type"))
                {
                    RejectAttributes(attributes, "a type alias");
                    _buffer.Advance(); // 'opaque'
                    return ParseTypeAlias(isPublic, isOpaque: true, start);
                }
                if (attributes.Length > 0)
                {
                    // '@Component' followed by something that opens no declaration. The list would
                    // vanish silently; instead the message says what an attribute may precede.
                    _de.Report("LYR-PAR0042", Severity.Error, attributes[0].Span,
                        "an attribute must be followed by the declaration it applies to");
                    var stop = SynchronizeTopLevel();
                    return new ErrorDecl(Span.Union(start, stop));
                }
                _de.Report("LYR-PAR0025", Severity.Error, _buffer.Current.Span,
                    $"expected a declaration, got {_buffer.Current.TokenKind}");
                var end = SynchronizeTopLevel(); // skip to the next declaration start, so only ONE error
                return new ErrorDecl(Span.Union(start, end));
        }
    }

    /// <summary>
    /// The visibility word before a declaration, a member or a field (design/v5/spec/07 V2, 08 D3):
    /// <c>pub</c>, <c>internal</c>, <c>private</c>, or none. One word: a second is reported and
    /// read past, the first one standing.
    /// </summary>
    private VisibilityWord ParseVisibility()
    {
        var word = WordOf(_buffer.Current.TokenKind);
        if (word == VisibilityWord.None) return word;
        _buffer.Advance();
        while (WordOf(_buffer.Current.TokenKind) != VisibilityWord.None)
            _de.Report("LYR-PAR0053", Severity.Error, _buffer.Advance().Span,
                "a declaration takes one visibility word — 'pub', 'internal' or 'private'");
        return word;
    }

    private static VisibilityWord WordOf(TokenKind kind) => kind switch
    {
        TokenKind.Pub => VisibilityWord.Pub,
        TokenKind.Internal => VisibilityWord.Internal,
        TokenKind.Private => VisibilityWord.Private,
        _ => VisibilityWord.None,
    };

    /// <summary>An attribute may precede a function, a struct, a class, an enum or the module
    /// header. Everywhere else the list is reported and dropped; the declaration itself parses
    /// on unharmed.</summary>
    private void RejectAttributes(AttributeNode[] attributes, string what)
    {
        if (attributes.Length == 0) return;
        _de.Report("LYR-PAR0042", Severity.Error, attributes[0].Span,
            $"an attribute cannot sit on {what} — a function, a struct, a class, an enum, "
            + "a member of one, or the module header carries one");
    }

    private static Decl WithAttributes(Decl decl, AttributeNode[] attributes) => decl switch
    {
        _ when attributes.Length == 0 => decl,
        StructDecl s => s with { Attributes = attributes },
        ClassDecl c => c with { Attributes = attributes },
        EnumDecl e => e with { Attributes = attributes },
        _ => decl, // recovery produced an ErrorDecl; the list is lost with the declaration
    };

    private static Decl WithMemberAttributes(Decl member, AttributeNode[] attributes) => member switch
    {
        _ when attributes.Length == 0 => member,
        FunctionDecl f => f with { Attributes = attributes },
        StaticBindingDecl sb => sb with { Attributes = attributes },
        FieldDecl fd => fd with { Attributes = attributes },
        _ => member, // recovery; the list is lost with the member
    };

    /// <summary>Recovery: consumes tokens up to the next plausible declaration start (a keyword,
    /// the contextual 'type', or EOF). Returns the span of the last skipped token.</summary>
    private Span SynchronizeTopLevel()
    {
        var span = _buffer.Current.Span;
        while (!_buffer.AtEnd)
        {
            if (_buffer.Current.TokenKind is TokenKind.Module or TokenKind.Import or TokenKind.Pub
                or TokenKind.Internal or TokenKind.Private or TokenKind.Fn or TokenKind.Mut or TokenKind.Struct or TokenKind.Class or TokenKind.Enum
                or TokenKind.Interface or TokenKind.Extend or TokenKind.Let or TokenKind.Var
                or TokenKind.AtIdentifier)
                break;
            if (AtContextual("type") || AtContextual("extern")) break;
            span = _buffer.Advance().Span;
        }
        return span;
    }

    // --- imports ---

    private Decl ParseImport(VisibilityWord word, Span start)
    {
        _buffer.Advance(); // 'import'
        var path = ParseDottedName();
        ImportClause? clause = null;
        if (_buffer.Check(TokenKind.LBrace)) clause = ParseSelectiveImport();
        else if (_buffer.Check(TokenKind.As)) clause = ParseAliasImport();
        var semi = ExpectSemicolon();
        return new ImportDecl(path, clause, Span.Union(start, semi.Span)) { IsPublic = word == VisibilityWord.Pub };
    }

    private ImportClause ParseSelectiveImport()
    {
        var open = _buffer.Advance(); // '{'
        var names = new List<string>();
        var spans = new List<Span>();
        var renames = new List<string?>();
        while (!_buffer.Check(TokenKind.RBrace) && !_buffer.AtEnd)
        {
            var (name, span) = ExpectNamed("LYR-PAR0026", "import item");
            names.Add(name);
            spans.Add(span);
            // '{ f as g }' (07 V3 I2): bound here under the second name.
            renames.Add(_buffer.Match(TokenKind.As) ? ExpectNamed("LYR-PAR0026", "name after 'as'").Name : null);
            if (!_buffer.Match(TokenKind.Comma)) break;
        }
        var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close import list");

        // Grammar §2: at least one name. An empty list would be an import of nothing, kept
        // without a word.
        if (names.Count == 0)
            _de.Report("LYR-PAR0026", Severity.Error, Span.Union(open.Span, close.Span),
                "expected at least one import item — for the whole module, drop the braces");

        return new ImportSelective(names.ToArray(), Span.Union(open.Span, close.Span))
            { NameSpans = spans.ToArray(), Renames = renames.Any(r => r is not null) ? renames.ToArray() : null };
    }

    private ImportClause ParseAliasImport()
    {
        var asKw = _buffer.Advance(); // 'as'
        var nameTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0026",
            $"expected alias name after 'as', got {_buffer.Current.TokenKind}");
        return new ImportAlias(_sm.Slice(nameTok.Span).ToString(), Span.Union(asKw.Span, nameTok.Span));
    }

    // --- Functions (§3.1) ---

    /// <summary>
    /// <c>extern "abi" fn name(params): T [= "symbol"];</c> — a function whose body lives
    /// outside the program. The ABI string says which binder answers for it; the symbol names
    /// what that binder looks up, defaulting to the function's own name. Whether the ABI is one
    /// this compiler knows, and whether the signature can cross it, is the checker's question.
    /// </summary>
    private Decl ParseExternDecl(VisibilityWord isPublic, Span start)
    {
        var kw = _buffer.Advance(); // 'extern'
        var abiTok = _buffer.Advance(); // the string literal the caller peeked
        var abi = LiteralDecoder.DecodeString(_sm.Slice(abiTok.Span), abiTok.Span, _de);
        var spec = new ExternSpec(abi, null, Span.Union(kw.Span, abiTok.Span)) { AbiSpan = abiTok.Span };
        return ParseFunctionDecl(isPublic, start, spec: spec);
    }

    private FunctionDecl ParseFunctionDecl(VisibilityWord isPublic, Span start, bool isStatic = false,
        ExternSpec? spec = null)
    {
        var isMut = _buffer.Match(TokenKind.Mut);
        _buffer.Expect(TokenKind.Fn, "LYR-PAR0032", $"expected 'fn', got {_buffer.Current.TokenKind}");
        var name = ExpectNamed("LYR-PAR0026", "function name");
        var generics = _buffer.Check(TokenKind.Less) ? ParseGenericParams() : [];

        _buffer.Expect(TokenKind.LParen, "LYR-PAR0019", "expected '(' after function name");
        var parameters = ParseParamList();
        _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' after parameters");

        // allowThrows: false — a trailing 'throws' here is the FUNCTION's clause, as it has been
        // since 1.0. For a coroutine function the checker moves it into the returned type, which
        // is what it has always described: the body runs at the pull, not at the call.
        // Whether the return type is written in parentheses: '(Task<int>) throws E' says the
        // 'throws' is the function's, where 'Task<int> throws E' could mean the task's (M6-6).
        // The grouping leaves no node, so the declaration remembers it.
        var returnGrouped = false;
        TypeNode? returnType = null;
        if (_buffer.Match(TokenKind.Colon))
        {
            _groupedType = null;
            returnType = ParseType(allowThrows: false);
            returnGrouped = ReferenceEquals(_groupedType, returnType);
        }

        ThrowsClause? throws = null;
        if (AtContextual("throws")) throws = ParseThrowsTypes(_buffer.Advance().Span);

        Block? body = null;
        Span end;
        if (spec is not null)
        {
            // An extern declaration has no body by definition; what it may carry is the symbol.
            if (_buffer.Match(TokenKind.Equal))
            {
                var symbolTok = _buffer.Expect(TokenKind.StringLiteral, "LYR-PAR0044",
                    $"expected the symbol as a string after '=', got {_buffer.Current.TokenKind}");
                if (symbolTok.TokenKind == TokenKind.StringLiteral)
                    spec = spec with
                    {
                        Symbol = LiteralDecoder.DecodeString(_sm.Slice(symbolTok.Span), symbolTok.Span, _de),
                        SymbolSpan = symbolTok.Span,
                        Span = Span.Union(spec.Span, symbolTok.Span),
                    };
            }
            end = _buffer.Expect(TokenKind.Semicolon, "LYR-PAR0016",
                "expected ';' to end the extern declaration — its body lives outside the program").Span;
        }
        else if (_buffer.Check(TokenKind.LBrace))
        {
            body = ParseBlock();
            end = body.Span;
        }
        else
        {
            end = _buffer.Expect(TokenKind.Semicolon, "LYR-PAR0016", "expected '{' or ';' to end function").Span;
        }

        return new FunctionDecl(isPublic, isMut, isStatic, name.Name, generics, parameters, returnType, throws, body,
            Span.Union(start, end)) { NameSpan = name.Span, Extern = spec, ReturnGrouped = returnGrouped };
    }

    /// <summary>
    /// What follows a declaration's <c>throws</c> (design/v5/spec/08 D9) — or a lambda's (Y11 F7):
    /// nothing — the bare form, <c>Error</c> — before the body, the <c>;</c>, an extern's <c>=</c> or
    /// a lambda's <c>=&gt;</c>; one type; or a bracketed list (the list rule, D5/D6). Several types
    /// without the brackets are the list rule's error, reported with the form to write and parsed as
    /// the list they meant.
    /// </summary>
    private ThrowsClause ParseThrowsTypes(Span keyword)
    {
        if (_buffer.Check(TokenKind.LBrace) || _buffer.Check(TokenKind.Semicolon) || _buffer.Check(TokenKind.Equal)
            || _buffer.Check(TokenKind.FatArrow))
            return new ThrowsClause([], keyword);

        if (_buffer.Check(TokenKind.LBracket)) return ParseThrownList(keyword);

        var first = ParseType();
        if (!_buffer.Check(TokenKind.Comma)) return new ThrowsClause([first], Span.Union(keyword, first.Span));

        var types = new List<TypeNode> { first };
        while (_buffer.Match(TokenKind.Comma)) types.Add(ParseType());
        var all = Span.Union(first.Span, types[^1].Span);
        _de.Report("LYR-PAR0049", Severity.Error, all,
            $"several thrown types are a list — write 'throws [{string.Join(", ", types.Select(t => _sm.Slice(t.Span).ToString()))}]'");
        return new ThrowsClause(types.ToArray(), Span.Union(keyword, all));
    }

    /// <summary>A bracketed set after <c>throws</c>, <c>[A, B]</c>; an empty one is the list rule's
    /// error, the bare <c>throws</c> being the form that means <c>Error</c>.</summary>
    private ThrowsClause ParseThrownList(Span keyword)
    {
        var open = _buffer.Advance();
        var listed = new List<TypeNode>();
        while (!_buffer.Check(TokenKind.RBracket) && !_buffer.AtEnd)
        {
            listed.Add(ParseType());
            if (!_buffer.Match(TokenKind.Comma)) break;
        }
        var close = _buffer.Expect(TokenKind.RBracket, "LYR-PAR0004", "expected ']' to close the thrown types");
        if (listed.Count == 0)
            _de.Report("LYR-PAR0049", Severity.Error, Span.Union(open.Span, close.Span),
                "a 'throws' list names its types — the bare 'throws' is the one that means 'Error'");
        return new ThrowsClause(listed.ToArray(), Span.Union(keyword, close.Span));
    }

    private Param[] ParseParamList()
    {
        var parameters = new List<Param>();
        if (_buffer.Check(TokenKind.RParen)) return parameters.ToArray();
        do
        {
            if (_buffer.Check(TokenKind.RParen)) break; // trailing comma
            var start = _buffer.Current.Span;

            // '@callerExpr(actual)' (design/v5/spec/09 A11): which attribute a parameter takes is
            // the checker's rule, as it is for a declaration's.
            var attributes = ParseAttributeList();

            var isParams = _buffer.Match(TokenKind.Params);
            // '&x: T' (design/v5/spec/03 T12): the mark at the parameter, never at the type.
            var isPlace = _buffer.Match(TokenKind.Amp);
            var name = ExpectNamed("LYR-PAR0026", "parameter name");
            _buffer.Expect(TokenKind.Colon, "LYR-PAR0031", "expected ':' after parameter name");
            var type = ParseType();
            // 'nums: int...' (design/v5/spec/08): the variadic parameter names its element; it is
            // an array of it, as 4.x's 'params nums: int[]' was, and the rules are the same ones.
            var isEllipsis = _buffer.Check(TokenKind.DotDotDot);
            if (isEllipsis) type = new ArrayType(type, Span.Union(type.Span, _buffer.Advance().Span));
            Expr? def = _buffer.Match(TokenKind.Equal) ? ParseExpr(0) : null;
            parameters.Add(new Param(isParams || isEllipsis, name.Name, type, def, Span.Union(start, def?.Span ?? type.Span))
                { NameSpan = name.Span, Attributes = attributes, IsPlace = isPlace, IsEllipsis = isEllipsis });
        } while (_buffer.Match(TokenKind.Comma));
        return parameters.ToArray();
    }

    // --- Structs / Classes (§3.2/§3.3) ---

    private Decl ParseStructOrClass(VisibilityWord isPublic, Span start, bool isClass)
    {
        _buffer.Advance(); // 'struct' / 'class'
        var name = ExpectNamed("LYR-PAR0026", isClass ? "class name" : "struct name");
        var generics = _buffer.Check(TokenKind.Less) ? ParseGenericParams() : [];
        string?[]? delegates = null;
        var interfaces = _buffer.Check(TokenKind.ColonColon) ? ParseInterfaceList(out delegates, out _) : [];
        _buffer.Expect(TokenKind.LBrace, "LYR-PAR0017", "expected '{' to open type body");
        var members = ParseTypeMembers();
        var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close type body");
        var span = Span.Union(start, close.Span);
        return isClass
            ? new ClassDecl(isPublic, name.Name, generics, interfaces, members, span) { Delegates = delegates, NameSpan = name.Span }
            : new StructDecl(isPublic, name.Name, generics, interfaces, members, span) { Delegates = delegates, NameSpan = name.Span };
    }

    // struct or class body: FieldDecl | FunctionDecl. A field needs a ',', a block-bodied method
    // does not.
    private Decl[] ParseTypeMembers()
    {
        var members = new List<Decl>();
        while (!_buffer.Check(TokenKind.RBrace) && !_buffer.AtEnd)
        {
            // Since 2.1 a member CARRIES its attribute list — the sema admits only the
            // row-less '@Deprecated' there, but which attributes exist where is its call,
            // not the grammar's.
            var attributes = AtAttributeStart ? ParseAttributeList() : [];

            var before = _buffer.Position;
            var member = WithMemberAttributes(ParseTypeMember(), attributes);
            members.Add(member);
            if (_buffer.Position == before) { _buffer.Advance(); continue; } // force progress

            if (_buffer.Check(TokenKind.RBrace)) break;

            // The ',' separates members, and only a FIELD needs it: without the rule `a: int b: int`
            // would be a valid line. Everything else has already closed itself — a block body ends
            // in '}', a bodiless method, a 'static let' and a 'type Item = …' end in ';'.
            if (member is FunctionDecl or StaticBindingDecl or AssociatedTypeDecl)
                _buffer.Match(TokenKind.Comma);
            else
                _buffer.Expect(TokenKind.Comma, "LYR-PAR0029", "expected ',' between members");
        }
        return members.ToArray();
    }

    private Decl ParseTypeMember()
    {
        var start = _buffer.Current.Span;

        // 'type Item = int;' binds an associated type of a conformance (03 T6).
        if (AtContextual("type") && _buffer.Peek(1).TokenKind == TokenKind.Identifier)
            return ParseAssociatedType();

        // Member forms: [word] [static] [mut] fn …  |  [word] static let …  |  [word] [var] a
        // field — the word one of 'pub', 'internal', 'private' (07 V2 S0). 'static' precedes
        // 'mut', so the order is unambiguous; 'mut static fn' does not exist. The sema rejects
        // the combination anyway: a static member has no receiver for 'mut' to apply to.
        var isPublic = ParseVisibility();

        if (_buffer.Check(TokenKind.Static))
        {
            _buffer.Advance();
            if (_buffer.Check(TokenKind.Let) || _buffer.Check(TokenKind.Var))
            {
                var binding = RequireNamedBinding(ParseBinding(), "static let");
                return new StaticBindingDecl(isPublic, binding, Span.Union(start, binding.Span));
            }
            return ParseFunctionDecl(isPublic, start, isStatic: true);
        }

        if (_buffer.Check(TokenKind.Fn) || _buffer.Check(TokenKind.Mut))
            return ParseFunctionDecl(isPublic, start);

        // 'var name: T' — the one word that makes a field writable (design/v5/spec/02 M2).
        if (_buffer.Check(TokenKind.Var))
        {
            _buffer.Advance();
            return ParseField(start, isVar: true, isPublic);
        }

        return ParseField(start, isVar: false, isPublic);
    }

    private FieldDecl ParseField() => ParseField(_buffer.Current.Span, isVar: false, VisibilityWord.None);

    private FieldDecl ParseField(Span start, bool isVar, VisibilityWord visibility)
    {
        var name = ExpectNamed("LYR-PAR0026", "field name");
        _buffer.Expect(TokenKind.Colon, "LYR-PAR0031", "expected ':' after field name");
        var type = ParseType();
        Expr? def = _buffer.Match(TokenKind.Equal) ? ParseExpr(0) : null;
        return new FieldDecl(name.Name, type, def, Span.Union(start, def?.Span ?? type.Span))
            { NameSpan = name.Span, IsVar = isVar, Visibility = visibility };
    }

    // --- Enums (§3.4) ---

    private Decl ParseEnum(VisibilityWord isPublic, Span start)
    {
        _buffer.Advance(); // 'enum'
        var name = ExpectNamed("LYR-PAR0026", "enum name");
        var generics = _buffer.Check(TokenKind.Less) ? ParseGenericParams() : [];
        var interfaces = _buffer.Check(TokenKind.ColonColon) ? ParseInterfaceListWithoutBy() : [];
        _buffer.Expect(TokenKind.LBrace, "LYR-PAR0017", "expected '{' to open enum body");

        var variants = new List<EnumVariant>();
        var membersBegin = false;
        while (!_buffer.Check(TokenKind.RBrace) && !_buffer.Check(TokenKind.Semicolon) && !_buffer.AtEnd)
        {
            // A member where a variant's name is expected: the ';' that ends the variants is
            // missing. Said once, and the members are read as what they are — 'static' and 'let'
            // were taken for two variants, and what was synthesized from those names failed to
            // parse in turn (the review's M8a-10).
            if (AtEnumMember())
            {
                _de.Report("LYR-PAR0057", Severity.Error, _buffer.Current.Span,
                    "the variants of an enum end with ';' before its members — a ',' goes on to the next variant");
                membersBegin = true;
                break;
            }
            // No name: said, and nothing is made a variant of — a keyword read as one gave the
            // enum a variant 'let'.
            if (!_buffer.Check(TokenKind.Identifier) && WordOf(_buffer.Current.TokenKind) == VisibilityWord.None)
            {
                _de.Report("LYR-PAR0026", Severity.Error, _buffer.Current.Span,
                    $"expected enum variant name, got {_buffer.Current.TokenKind}");
                _buffer.Advance();
                if (!_buffer.Match(TokenKind.Comma)) break;
                continue;
            }
            var before = _buffer.Position;
            variants.Add(ParseEnumVariant());
            if (_buffer.Position == before) { _buffer.Advance(); continue; }
            if (!_buffer.Match(TokenKind.Comma)) break;
        }

        var methods = new List<FunctionDecl>();
        var boundTypes = new List<AssociatedTypeDecl>();
        var statics = new List<StaticBindingDecl>();
        if (_buffer.Match(TokenKind.Semicolon) || membersBegin)
            ParseMethodSequence(methods, allowStatic: true, types: boundTypes, statics: statics);

        var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close enum body");
        return new EnumDecl(isPublic, name.Name, generics, interfaces, variants.ToArray(), methods.ToArray(),
            Span.Union(start, close.Span))
            { Types = boundTypes.ToArray(), Statics = statics.ToArray(), NameSpan = name.Span };
    }

    /// <summary>Does a member of an enum begin here — <c>fn</c>, <c>mut fn</c>, <c>static</c>,
    /// either behind a visibility word, an attribute, <c>type Item =</c> — where the variants are
    /// still being read? A visibility word before a NAME is a variant's (refused there,
    /// LYR-PAR0053), and <c>type</c> alone is a name.</summary>
    private bool AtEnumMember()
    {
        var at = WordOf(_buffer.Current.TokenKind) != VisibilityWord.None ? 1 : 0;
        return _buffer.Peek(at).TokenKind is TokenKind.Fn or TokenKind.Mut or TokenKind.Static
            || (at == 0 && AtAttributeStart)
            || (at == 0 && AtContextual("type") && _buffer.Peek(1).TokenKind == TokenKind.Identifier
                && _buffer.Peek(2).TokenKind == TokenKind.Equal);
    }

    private EnumVariant ParseEnumVariant()
    {
        // A variant is as visible as its enum (07 V2 S0): the word belongs before 'enum'.
        if (WordOf(_buffer.Current.TokenKind) != VisibilityWord.None)
            _de.Report("LYR-PAR0053", Severity.Error, _buffer.Advance().Span,
                "a variant is as visible as its enum — the word stands before 'enum'");

        var nameTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0026",
            $"expected enum variant name, got {_buffer.Current.TokenKind}");
        var name = _sm.Slice(nameTok.Span).ToString();

        if (_buffer.Check(TokenKind.LParen)) // tuple variant
        {
            _buffer.Advance();
            var fields = new List<TypeNode>();
            if (!_buffer.Check(TokenKind.RParen))
                do { fields.Add(ParseType()); } while (_buffer.Match(TokenKind.Comma));
            var close = _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' to close tuple variant");
            return new EnumVariant(name, fields.ToArray(), null, Span.Union(nameTok.Span, close.Span))
                { NameSpan = nameTok.Span };
        }

        if (_buffer.Check(TokenKind.LBrace)) // struct variant
        {
            _buffer.Advance();
            var fields = new List<FieldDecl>();
            while (!_buffer.Check(TokenKind.RBrace) && !_buffer.AtEnd)
            {
                fields.Add(ParseField());
                if (!_buffer.Match(TokenKind.Comma)) break;
            }
            var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close struct variant");
            return new EnumVariant(name, null, fields.ToArray(), Span.Union(nameTok.Span, close.Span))
                { NameSpan = nameTok.Span };
        }

        return new EnumVariant(name, null, null, nameTok.Span) { NameSpan = nameTok.Span }; // Unit
    }

    // --- Interfaces (§3.5) ---

    private Decl ParseInterface(VisibilityWord isPublic, Span start, bool isSealed = false)
    {
        _buffer.Advance(); // 'interface'
        var name = ExpectNamed("LYR-PAR0026", "interface name");
        var generics = _buffer.Check(TokenKind.Less) ? ParseGenericParams() : [];

        // 'interface B :: [A]' — B implies its parents: whoever conforms to B conforms to them
        // too. What the list does NOT do the sema explains where it matters; here it is just a
        // type list, shaped like the one on structs. (LYR-PAR0039 rejected this until v1.13.)
        var interfaces = _buffer.Check(TokenKind.ColonColon) ? ParseInterfaceListWithoutBy() : [];

        _buffer.Expect(TokenKind.LBrace, "LYR-PAR0017", "expected '{' to open interface body");
        var members = new List<FunctionDecl>();
        var types = new List<AssociatedTypeDecl>();
        var statics = new List<StaticBindingDecl>();
        ParseMethodSequence(members, allowStatic: true, types: types, statics: statics); // a static member declares, through a constraint (03 T5)
        var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close interface body");
        return new InterfaceDecl(isPublic, name.Name, generics, interfaces, members.ToArray(), Span.Union(start, close.Span))
            { NameSpan = name.Span, Types = types.ToArray(), IsSealed = isSealed, Statics = statics.ToArray() };
    }

    // --- Extend (§3.6) ---

    private Decl ParseExtend(VisibilityWord isPublic, Span start)
    {
        _buffer.Advance(); // 'extend'
        // 'extend<T :: [C]> Target' (03 T7): the block's parameters before the target.
        var generics = _buffer.Check(TokenKind.Less) ? ParseGenericParams() : [];
        var target = ParseType();
        var interfaces = _buffer.Check(TokenKind.ColonColon) ? ParseInterfaceListWithoutBy() : [];
        _buffer.Expect(TokenKind.LBrace, "LYR-PAR0017", "expected '{' to open extend body");
        var methods = new List<FunctionDecl>();
        var boundTypes = new List<AssociatedTypeDecl>();
        var statics = new List<StaticBindingDecl>();
        ParseMethodSequence(methods, allowStatic: true, types: boundTypes, statics: statics);
        var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close extend body");
        return new ExtendDecl(isPublic, target, interfaces, methods.ToArray(), Span.Union(start, close.Span))
            { Types = boundTypes.ToArray(), Generics = generics, Statics = statics.ToArray() };
    }

    /// <summary>
    /// A sequence of FunctionDecl without separators (interface, extend and enum methods).
    ///
    /// <para>The modifier order is the one of FunctionDecl: the visibility word and 'static' are
    /// read here, 'mut' and 'fn' by ParseFunctionDecl.</para>
    /// </summary>
    /// <param name="allowStatic">False in an interface body, where a member is dispatched on a
    /// receiver and a static one has none.</param>
    /// <param name="allowAttributes">True everywhere since 2.15, the sema admitting only
    /// '@Deprecated' (the Member target). It was false in an interface body until then, because
    /// deprecating an abstract member raised a conformance question nobody had answered: do
    /// implementations inherit the clock?
    ///
    /// <para>They do NOT. The deprecation reaches every use that resolves to the interface's
    /// member — which is the population that has to move — and an implementation is not a use. A
    /// conforming type MUST implement what the interface requires, so a warning there would be
    /// one nobody can act on without breaking conformance, and an unactionable warning is the
    /// thing this project keeps refusing to ship.</para></param>
    /// <param name="types">Where an associated type may stand (03 T6) — an interface's
    /// declaration <c>type Item;</c>, a conformance block's binding <c>type Item = int;</c> —
    /// the list it goes to.</param>
    /// <param name="statics">The list a <c>static let</c> goes to: an interface's declaration
    /// <c>static let zero: Self;</c> (03 T5), a block's constant (05 §6), an enum's (03 §4.1).
    /// Every member sequence takes one — LYR-PAR0040, which refused it in an enum, is retired.</param>
    private void ParseMethodSequence(List<FunctionDecl> methods, List<StaticBindingDecl> statics, bool allowStatic,
        bool allowAttributes = true, List<AssociatedTypeDecl>? types = null)
    {
        while (!_buffer.Check(TokenKind.RBrace) && !_buffer.AtEnd)
        {
            if (types is not null && AtContextual("type") && _buffer.Peek(1).TokenKind == TokenKind.Identifier)
            {
                types.Add(ParseAssociatedType());
                continue;
            }
            var attributes = AtAttributeStart ? ParseAttributeList() : [];
            if (attributes.Length > 0 && !allowAttributes)
            {
                RejectAttributes(attributes, "an interface member");
                attributes = [];
            }

            var before = _buffer.Position;
            var start = _buffer.Current.Span;
            var isPublic = ParseVisibility();

            var isStatic = false;
            if (_buffer.Check(TokenKind.Static))
            {
                var kw = _buffer.Advance();

                // 'static let' is a StaticBinding, as in a struct's or a class's body: an
                // interface's declaration (03 T5), a block's constant (05 §6), an enum's.
                if (_buffer.Check(TokenKind.Let) || _buffer.Check(TokenKind.Var))
                {
                    var binding = RequireNamedBinding(ParseBinding(), "static let");
                    statics.Add(new StaticBindingDecl(isPublic, binding, Span.Union(start, binding.Span)) { Attributes = attributes });
                    continue;
                }

                if (!allowStatic)
                    _de.Report("LYR-PAR0041", Severity.Error, kw.Span,
                        "an interface member cannot be 'static' — it is dispatched on a receiver, "
                        + "and a static member has none. Declare it on the implementing type");

                // Read on either way: the rest of the member is well formed, and stopping here
                // would report every following one as well.
                isStatic = allowStatic;
            }

            methods.Add(ParseFunctionDecl(isPublic, start, isStatic) with { Attributes = attributes });
            if (_buffer.Position == before) _buffer.Advance(); // force progress
        }
    }

    /// <summary><c>type Item;</c> or <c>type Item = T;</c> (03 T6): an associated type declared,
    /// with its default, or bound; <c>type Iter :: [Iterator];</c> with what every answer conforms
    /// to (10 B6), written as a type parameter's constraints are.</summary>
    private AssociatedTypeDecl ParseAssociatedType()
    {
        var start = _buffer.Advance().Span; // contextual 'type'
        var name = ExpectNamed("LYR-PAR0026", "associated type name");
        TypeNode[] bounds = [];
        if (_buffer.Match(TokenKind.ColonColon))
        {
            _buffer.Expect(TokenKind.LBracket, "LYR-PAR0030", "expected '[' after '::' in a bound");
            var list = new List<TypeNode>();
            do { list.Add(ParseType()); } while (_buffer.Match(TokenKind.Comma));
            _buffer.Expect(TokenKind.RBracket, "LYR-PAR0004", "expected ']' to close the bound");
            bounds = list.ToArray();
        }
        TypeNode? type = null;
        if (_buffer.Match(TokenKind.Equal)) type = ParseType();
        var semi = ExpectSemicolon();
        return new AssociatedTypeDecl(name.Name, type, Span.Union(start, semi.Span)) { NameSpan = name.Span, Bounds = bounds };
    }

    // --- Global binding & type alias (§2) ---

    /// <summary>A module-level <c>let</c> or <c>var</c> (design/v5/spec/07 V5 G2, G5): filled
    /// eagerly at program start in declaration order; a <c>var</c> is written like a local one.
    /// Lyric 4 allowed <c>let</c> only.</summary>
    private Decl ParseGlobalBinding(VisibilityWord isPublic, Span start)
    {
        var binding = RequireNamedBinding(ParseBinding(), "a module-level binding");
        return new GlobalBindingDecl(isPublic, binding, Span.Union(start, binding.Span));
    }

    /// <summary>A constant has ONE name. Destructuring exists for local bindings only: a global
    /// slot is a named thing, and taking several names from one expression would let one
    /// declaration produce several slots.</summary>
    private BindingStmt RequireNamedBinding(Stmt parsed, string what)
    {
        if (parsed is BindingStmt named) return named;

        _de.Report("LYR-PAR0020", Severity.Error, parsed.Span,
            $"{what} needs a single name — destructuring is only allowed on local bindings");

        // The name is missing rather than wrong, so its span is the empty one at the start of what
        // stood there: inside the statement, which is what the containment rule asks for.
        return new BindingStmt(false, "<error>", null, null, parsed.Span)
            { NameSpan = parsed.Span with { End = parsed.Span.Start } };
    }

    private Decl ParseTypeAlias(VisibilityWord isPublic, bool isOpaque, Span start)
    {
        _buffer.Advance(); // contextual 'type'
        var name = ExpectNamed("LYR-PAR0026", "type alias name");
        // 'type Pair<T> = (T, T);' (03 T15): parameters as on any generic declaration.
        var generics = _buffer.Check(TokenKind.Less) ? ParseGenericParams() : [];
        _buffer.Expect(TokenKind.Equal, "LYR-PAR0028", "expected '=' in type alias");
        var aliased = ParseType();
        var semi = ExpectSemicolon();
        return new TypeAliasDecl(isPublic, isOpaque, name.Name, aliased, Span.Union(start, semi.Span))
            { NameSpan = name.Span, Generics = generics };
    }

    // --- generics ---

    private GenericParam[] ParseGenericParams()
    {
        _buffer.Advance(); // '<'
        var parameters = new List<GenericParam>();
        do
        {
            var nameTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0026",
                $"expected type parameter, got {_buffer.Current.TokenKind}");
            TypeNode[] constraints = [];
            var end = nameTok.Span;
            if (_buffer.Match(TokenKind.ColonColon)) // T :: [I1, I2]
            {
                _buffer.Expect(TokenKind.LBracket, "LYR-PAR0030", "expected '[' after '::' in constraint");
                var cs = new List<TypeNode>();
                do { cs.Add(ParseType()); } while (_buffer.Match(TokenKind.Comma));
                end = _buffer.Expect(TokenKind.RBracket, "LYR-PAR0004", "expected ']' to close constraint list").Span;
                constraints = cs.ToArray();
            }
            // 'Rhs = Self': a default type argument (03 T18).
            TypeNode? fallback = null;
            if (_buffer.Match(TokenKind.Equal))
            {
                fallback = ParseType();
                end = fallback.Span;
            }
            parameters.Add(new GenericParam(_sm.Slice(nameTok.Span).ToString(), constraints,
                Span.Union(nameTok.Span, end)) { NameSpan = nameTok.Span, Default = fallback });
        } while (_buffer.Match(TokenKind.Comma));
        // A generic parameter list closes with a plain '>', never a '>>' — but an alias's may meet
        // its '=' unspaced, 'type Pair<T>= (T, T);', and the lexer reads that '>='.
        if (_buffer.Current.TokenKind == TokenKind.GreaterEqual) _buffer.SplitCurrentGreater();
        _buffer.Expect(TokenKind.Greater, "LYR-PAR0009", "expected '>' to close type parameters");
        return parameters.ToArray();
    }

    /// <param name="delegates">The field each entry delegates to — <c>Walker by legs</c> (04 D1)
    /// — by index, <c>null</c> where none; <c>null</c> as a whole when no entry does.</param>
    /// <param name="bySpan">The span of the first <c>by</c>, for a list that may not carry one.</param>
    private TypeNode[] ParseInterfaceList(out string?[]? delegates, out Span bySpan)
    {
        _buffer.Advance(); // '::'
        _buffer.Expect(TokenKind.LBracket, "LYR-PAR0030", "expected '[' after '::'");
        var interfaces = new List<TypeNode>();
        List<string?>? fields = null;
        bySpan = default;
        do
        {
            interfaces.Add(ParseType());
            string? field = null;
            if (AtContextual("by"))
            {
                var by = _buffer.Advance();
                if (bySpan == default) bySpan = by.Span;
                field = ExpectName("LYR-PAR0026", "the field to delegate to");
            }
            if (field is not null) fields ??= new List<string?>(Enumerable.Repeat<string?>(null, interfaces.Count - 1));
            fields?.Add(field);
        } while (_buffer.Match(TokenKind.Comma) && !_buffer.Check(TokenKind.RBracket));
        _buffer.Expect(TokenKind.RBracket, "LYR-PAR0004", "expected ']' to close interface list");
        delegates = fields?.ToArray();
        return interfaces.ToArray();
    }

    /// <summary>An interface list where <c>by</c> has no place: an enum, an interface's parents,
    /// an extend block delegate nothing.</summary>
    private TypeNode[] ParseInterfaceListWithoutBy()
    {
        var interfaces = ParseInterfaceList(out var delegates, out var bySpan);
        if (delegates is not null)
            _de.Report("LYR-PAR0047", Severity.Error, bySpan,
                "'by' delegates a conformance of a struct or a class to one of its fields; it has no place here");
        return interfaces;
    }

    // --- Shared helpers ---

    private string[] ParseDottedName()
    {
        var segments = new List<string> { ExpectName("LYR-PAR0026", "module path segment") };
        while (_buffer.Match(TokenKind.Dot))
            segments.Add(ExpectName("LYR-PAR0026", "module path segment"));
        return segments.ToArray();
    }

    private string ExpectName(string code, string what) => ExpectNamed(code, what).Name;

    /// <summary>
    /// The name and the span it stands at.
    ///
    /// <para>On failure <see cref="TokenBuffer.Expect"/> returns the offending token without
    /// consuming it, so the span is the position where the name was expected — inside the
    /// declaration being parsed, which keeps the containment every <see cref="INamedDecl"/>
    /// promises.</para>
    /// </summary>
    private (string Name, Span Span) ExpectNamed(string code, string what)
    {
        var tok = _buffer.Expect(TokenKind.Identifier, code, $"expected {what}, got {_buffer.Current.TokenKind}");
        return (_sm.Slice(tok.Span).ToString(), tok.Span);
    }

    /// <summary>A contextual keyword: an identifier with exactly this text (for example 'throws' or
    /// 'type').</summary>
    private bool AtContextual(string word) =>
        _buffer.Check(TokenKind.Identifier) && _sm.Slice(_buffer.Current.Span).SequenceEqual(word);

    private bool PeekContextual(int offset, string word)
    {
        var token = _buffer.Peek(offset);
        return token.TokenKind == TokenKind.Identifier && _sm.Slice(token.Span).SequenceEqual(word);
    }
}
