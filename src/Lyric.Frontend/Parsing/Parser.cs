using Lyric.AST;
using Lyric.Core;
using Lyric.Lexing;

namespace Lyric.Parsing;

/// <summary>
/// The Lyric parser.
/// Recursive descent for expressions, types, statements and declarations, with a Pratt loop for
/// the operator precedence.
///
/// Error strategy: never throw. Every error goes as a Diagnostic (LYR-PAR####) to
/// the <see cref="DiagnosticEngine"/>; the parser produces an ErrorExpr or ErrorType and carries
/// on as best it can, so one run reports several errors.
/// </summary>
public sealed partial class Parser
{
    private readonly TokenBuffer _buffer;
    private readonly SourceManager _sm;
    private readonly DiagnosticEngine _de;

    // Whether 'IDENT { … }' may be read as a struct initializer. Ambient: at the start of an
    // false at the start of a statement, where it would be ambiguous with a block, and true again
    // inside delimiters through ParseSubExpr.
    private bool _allowStructInit = true;
    private bool _allowTail; // inside a value block's own statement list (see ParseBlock)

    /// <summary>Set while the FIRST primary of a match-arm guard is parsed: a '(' there opens a
    /// group, not a lambda, because the arm's '=>' follows the guard.</summary>
    private bool _guardHead;

    /// <summary>Set while a match-arm guard is parsed, outside any delimiter: a bare name before
    /// '=>' is then the guard's last operand, not a one-parameter lambda ('x if a + b => …').
    /// Inside parentheses or brackets the flag is off, so 'x if xs.any(y => y > 0) => …' keeps
    /// its lambda.</summary>
    private bool _inGuard;

    public Parser(SourceManager sm, FileId id, DiagnosticEngine de)
    {
        _sm = sm;
        _de = de;
        _buffer = new TokenBuffer(sm, id, de);
    }

    /// <summary>
    /// The '///' blocks of this file, keyed by the source offset of what follows them. A side table
    /// rather than a field on <see cref="Decl"/>: the AST records stay untouched, and so does every
    /// pattern match over them.
    /// </summary>
    /// <remarks>Consumers look a declaration up through <see cref="DocOf"/>.</remarks>
    public IReadOnlyDictionary<int, string> DocComments => _buffer.DocComments;

    /// <summary>The doc comment written above <paramref name="node"/>, or <c>null</c>.</summary>
    public string? DocOf(Node node) => _buffer.DocComments.GetValueOrDefault(node.Span.Start);

    // ---------------------------------------------------------------------
    // Public entry point: exactly ONE expression.
    // ---------------------------------------------------------------------

    public Expr ParseExpression()
    {
        var expr = ParseExpr(0);
        if (!_buffer.AtEnd)
            _de.Report("LYR-PAR0001", Severity.Error, _buffer.Current.Span,
                $"unexpected token after expression: {_buffer.Current.TokenKind}");
        return expr;
    }

    // ---------------------------------------------------------------------
    // The Pratt core: binary, assignment, range and cast operators.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Binding powers as (left, right). left &lt; right means left-associative, left &gt; right
    /// means right-associative, and (-1, -1) means no infix operator.
    /// The values mirror the precedence table: higher binds tighter.
    /// </summary>
    private static (int left, int right) BindingPower(TokenKind op) => op switch
    {
        TokenKind.As => (27, 28),
        TokenKind.Is => (11, 12), // the comparison level, like '<' (08 line 11)

        TokenKind.Star or TokenKind.Slash or TokenKind.Percent or TokenKind.StarPercent => (25, 26),
        TokenKind.Plus or TokenKind.Minus or TokenKind.PlusPercent or TokenKind.MinusPercent => (23, 24),
        TokenKind.Shl or TokenKind.Shr => (21, 22),
        TokenKind.DotDot or TokenKind.DotDotEqual => (19, 20), // non-associative, checked explicitly below
        TokenKind.Amp => (17, 18),
        TokenKind.Caret => (15, 16),
        TokenKind.Pipe => (13, 14),
        TokenKind.Less or TokenKind.LessEqual or TokenKind.Greater or TokenKind.GreaterEqual => (11, 12),
        TokenKind.EqualEqual or TokenKind.ExclamationEqual => (9, 10),
        TokenKind.AmpAmp => (7, 8),
        TokenKind.PipePipe => (5, 6),
        TokenKind.QuestionQuestion => (3, 2), // right-associative

        // Assignments, right-associative.
        TokenKind.Equal or TokenKind.PlusEqual or TokenKind.MinusEqual or TokenKind.StarEqual
            or TokenKind.SlashEqual or TokenKind.PercentEqual or TokenKind.ShlEqual or TokenKind.ShrEqual
            or TokenKind.PlusPercentEqual or TokenKind.MinusPercentEqual or TokenKind.StarPercentEqual
            or TokenKind.AmpEqual or TokenKind.PipeEqual or TokenKind.CaretEqual or TokenKind.AmpAmpEqual
            or TokenKind.PipePipeEqual or TokenKind.QuestionQuestionEqual => (1, 0),

        _ => (-1, -1)
    };

    /// <summary>
    /// How deeply expressions and types may nest before the parse refuses (§12.4).
    ///
    /// <para>NOT a language rule, and the specification says so: the depth belongs to a thread's
    /// stack, not to Lyric, so §12.4 fixes only a FLOOR of 128 and that reaching the bound is
    /// reported rather than crashed into. Without it a file of 50,000 nested parentheses took the
    /// whole process down — `Stack overflow.` on stderr, no code, no position — and it killed
    /// `lyrls` and `lyrdbg` just as readily, because they run this parser.</para>
    ///
    /// <para>MEASURED on the whole pipeline rather than on this file: nesting of 350 compiles, 450
    /// takes the process down. So the bound is not "as much as we can get away with" — 192 is 1.5x
    /// the floor the specification requires and roughly half the shallowest measured failure, which
    /// leaves room for a host with less stack than a console process, such as a language server's
    /// thread pool. The first attempt was 512, ABOVE the ceiling, and changed nothing at all.</para>
    /// </summary>
    internal const int MaxNesting = 192;

    private int _depth;

    /// <summary>
    /// Thrown when the bound is reached and caught in <c>ParseModule</c>, which reports it once.
    ///
    /// <para>An exception rather than an error node: the recursion has to STOP, and every frame
    /// between here and the top would otherwise have to check a flag and invent something to
    /// return. Nothing else in this parser unwinds, which is the point — this is the one failure
    /// that cannot be recovered from where it happens.</para>
    /// </summary>
    private sealed class NestingTooDeep(Span at) : Exception
    {
        public Span At { get; } = at;
    }

    private Expr ParseExpr(int minBp)
    {
        if (++_depth > MaxNesting) { _depth--; throw new NestingTooDeep(_buffer.Current.Span); }
        try { return ParseExprInner(minBp); }
        finally { _depth--; }
    }

    private Expr ParseExprInner(int minBp)
    {
        // An operand at binding power 0 starts an expression — a binding's value, an argument, a
        // return, the right of an '=' — which is where 'try' may stand.
        var left = ParsePrefix(atStart: minBp == 0);

        while (true)
        {
            var op = _buffer.Current.TokenKind;
            var (leftBp, rightBp) = BindingPower(op);
            if (leftBp < minBp) break; // covers (-1, -1) as well

            // 'as': the right-hand side is a type, not an expression.
            if (op == TokenKind.As)
            {
                _buffer.Advance();
                var type = ParseType();
                left = new CastExpr(left, type, Span.Union(left.Span, type.Span));
                continue;
            }

            // 'is' (03 T11): the right-hand side is a type as well.
            if (op == TokenKind.Is)
            {
                _buffer.Advance();
                var type = ParseType();
                left = new TypeTestExpr(left, type, Span.Union(left.Span, type.Span));
                continue;
            }

            // Range: not chainable. 'a..' before ']' is the view to the end (03 T13 A2), which
            // only an index can close that way.
            if (op is TokenKind.DotDot or TokenKind.DotDotEqual)
            {
                var opTok = _buffer.Advance();
                if (_buffer.Check(TokenKind.RBracket))
                {
                    left = new SliceRangeExpr(left, null, op == TokenKind.DotDotEqual, Span.Union(left.Span, opTok.Span));
                    continue;
                }
                var high = ParseExpr(rightBp);
                left = new RangeExpr(left, high, op == TokenKind.DotDotEqual, Span.Union(left.Span, high.Span));
                if (_buffer.Current.TokenKind is TokenKind.DotDot or TokenKind.DotDotEqual)
                    _de.Report("LYR-PAR0005", Severity.Error, _buffer.Current.Span, "range operator is not chainable");
                continue;
            }

            // Assignment, compound included: an AssignExpr with an optional base operator.
            if (Operators.TryMapAssign(op, out var compound))
            {
                _buffer.Advance();

                // The right of an '=' is a value position again, so a struct initializer is
                // allowed there. 'ParseExprStmt' turns the flag off for the whole statement,
                // because a statement must not begin with 'Foo { … }' — ambiguous with a block.
                // The ambiguity concerns the START only: no block can stand after an '='.
                var value = ParseSubExpr(rightBp);
                left = new AssignExpr(left, compound, value, Span.Union(left.Span, value.Span));
                continue;
            }

            // The remaining binary operators. The right of '??' may be a value block (08 Y4):
            // 'x ?? { return 0; }' — nothing else an operand begins with is a brace.
            _buffer.Advance();
            var right = op == TokenKind.QuestionQuestion && _buffer.Check(TokenKind.LBrace) ? ParseValueBlock() : ParseExpr(rightBp);
            left = new BinaryExpr(left, Operators.MapBinary(op), right, Span.Union(left.Span, right.Span));
        }

        return left;
    }

    // ---------------------------------------------------------------------
    // Prefix and postfix levels.
    // ---------------------------------------------------------------------

    private Expr ParsePrefix(bool atStart = false)
    {
        var op = _buffer.Current.TokenKind;
        // 'try e' (design/v5/spec/05 E4, 08 Y4): the mark covers EVERYTHING to its right — 'try a +
        // b' is 'try (a + b)' — so it stands at the start of the expression it covers. To the right
        // of an operator it would cover another expression than it seems to (Swift refuses the
        // same). 'try {' is the block form, which only a statement starts.
        if (op is TokenKind.Try && _buffer.Peek(1).TokenKind is not TokenKind.LBrace)
        {
            var kw = _buffer.Advance();
            // 'try?' and 'try!' (05 E4): the sign written against the keyword. With a space between,
            // '!' is the negation of what follows — 'try !done()' marks 'not done()'.
            var kind = TryKind.Propagate;
            var keyword = kw.Span;
            if (_buffer.Current.TokenKind is TokenKind.Question or TokenKind.Exclamation
                && _buffer.Current.Span.Start == kw.Span.End)
            {
                var sign = _buffer.Advance();
                kind = sign.TokenKind == TokenKind.Question ? TryKind.Optional : TryKind.Force;
                keyword = Span.Union(kw.Span, sign.Span);
            }
            var spelled = _sm.Slice(keyword).ToString();
            if (!atStart)
                _de.Report("LYR-PAR0050", Severity.Error, keyword,
                    $"'{spelled}' covers everything to its right, so it stands at the start of the expression "
                    + $"it covers — '{spelled} a + b', not 'a + {spelled} b'");
            var marked = atStart ? ParseExpr(0) : ParsePrefix();

            // 'try e catch (x: A) v' (08 Y4): the clauses belong to the nearest 'try' on their left,
            // so a clause body that is a 'try' of its own takes the clauses after it.
            var catches = new List<CatchClause>();
            while (_buffer.Check(TokenKind.Catch))
                catches.Add(ParseCatch(expressionForm: true));
            if (catches.Count > 0 && kind != TryKind.Propagate)
                _de.Report("LYR-PAR0051", Severity.Error, catches[0].Span,
                    $"'{spelled}' takes every error itself — a 'catch' clause belongs to a plain 'try'");
            var end = catches.Count > 0 ? catches[^1].Span : marked.Span;
            return new TryExpr(marked, Span.Union(kw.Span, end))
                { KeywordSpan = keyword, Kind = kind, Catches = catches.ToArray() };
        }
        // '&x' marks the argument of a place parameter (03 T12, 08 Y4) and stands nowhere else:
        // there is no address as a value. ParseArguments takes the mark before it gets here; one
        // that arrives is read past, so it gives one message and the rest still parses.
        if (op is TokenKind.Amp)
        {
            var mark = _buffer.Advance();
            _de.Report("LYR-PAR0054", Severity.Error, mark.Span,
                "'&' marks the argument of a place parameter and stands nowhere else — there is no address as a value");
            return ParsePrefix(atStart);
        }
        // '^' at the start of an operand is the from-end index (03 T14 N6); between operands it
        // is still the exclusive or, which the binary loop takes before this is asked.
        if (op is TokenKind.Exclamation or TokenKind.Minus or TokenKind.Tilde or TokenKind.Inc or TokenKind.Dec or TokenKind.Caret)
        {
            var opTok = _buffer.Advance();
            var operand = ParsePrefix();
            return new UnaryExpr(Operators.MapPrefix(op), operand, Span.Union(opTok.Span, operand.Span));
        }
        if (op is TokenKind.Throw) // 'x ?? throw e': a prefix, so 'throw e ?? f' is not 'throw (e ?? f)'
        {
            var kw = _buffer.Advance();
            var value = ParsePrefix();
            return new ThrowExpr(value, Span.Union(kw.Span, value.Span));
        }
        // 'comptime e': contextual, a prefix in shape. It opens the prefix only when what
        // follows can begin an expression, so an identifier 'comptime' before an operator, a
        // comma or a closing bracket is still the name.
        if (AtContextual("comptime") && BeginsExpression(_buffer.Peek(1).TokenKind))
        {
            var kw = _buffer.Advance();
            var inner = ParsePrefix();
            return new ComptimeExpr(inner, Span.Union(kw.Span, inner.Span));
        }

        return ParsePostfix(ParsePrimary());
    }

    /// <summary>
    /// Are these the type arguments of a call — <c>f&lt;int&gt;(…)</c> — or a comparison chain?
    ///
    /// <para>A pure token scan rather than speculative parsing.
    /// matters: <see cref="ParseType"/> reports diagnostics, and a guess that turns out wrong must
    /// leave no error behind. The scan here reports nothing; it counts brackets and looks at what
    /// follows the closing <c>&gt;</c>.</para>
    ///
    /// <para>The rule: they are type arguments when only tokens that can occur in a type expression
    /// stand between the <c>&lt;</c> and its match, and a <c>(</c> follows immediately.</para>
    ///
    /// <para>Conservative by design: in doubt it is a comparison. A misread comparison gives an
    /// understandable type error; a misread type argument list gives a parser error where the user
    /// suspects nothing.</para>
    /// </summary>
    private bool LooksLikeCallTypeArguments()
    {
        var depth = 0;

        for (var offset = 0; ; offset++)
        {
            switch (_buffer.Peek(offset).TokenKind)
            {
                case TokenKind.Less:
                    depth++;
                    break;

                case TokenKind.Greater:
                    depth--;
                    // Closed: the next token alone decides now.
                    if (depth == 0) return _buffer.Peek(offset + 1).TokenKind == TokenKind.LParen;
                    break;

                // What may occur in a type expression: named types with
                // Paths, arrays, optionals, function types, tuples.
                case TokenKind.Identifier:
                case TokenKind.Comma:
                case TokenKind.Dot:
                case TokenKind.LBracket:
                case TokenKind.RBracket:
                case TokenKind.Question:
                case TokenKind.Arrow:
                case TokenKind.Fn:
                case TokenKind.LParen:
                case TokenKind.RParen:
                    break;

                // Anything else cannot be a type, so the '<' was a comparison.
                default:
                    return false;
            }

            // A type argument list is short. The bound keeps a '<' anywhere in the source from
            // scanning half the buffer before giving up.
            if (offset > 64) return false;
        }
    }

    private Expr ParsePostfix(Expr operand)
    {
        while (true)
        {
            switch (_buffer.Current.TokenKind)
            {
                case TokenKind.Dot:
                {
                    _buffer.Advance();
                    // 't.0': the element of a tuple by position (03 T16). The lexer hands the
                    // number over as a literal; 't.0.1' arrives as the float '0.1' and is two
                    // members, as Rust reads it.
                    if (_buffer.Current.TokenKind is TokenKind.IntLiteral or TokenKind.FloatLiteral)
                    {
                        var number = _buffer.Advance();
                        var text = _sm.Slice(number.Span).ToString();
                        if (text.All(char.IsAsciiDigit))
                        {
                            operand = new MemberExpr(operand, text, false, Span.Union(operand.Span, number.Span)) { MemberSpan = number.Span };
                            break;
                        }
                        var dot = text.IndexOf('.');
                        if (dot > 0 && text[..dot].All(char.IsAsciiDigit) && text[(dot + 1)..].All(char.IsAsciiDigit))
                        {
                            var firstSpan = new Span(number.Span.File, number.Span.Start, number.Span.Start + dot);
                            var secondSpan = new Span(number.Span.File, number.Span.Start + dot + 1, number.Span.End);
                            operand = new MemberExpr(operand, text[..dot], false, Span.Union(operand.Span, firstSpan)) { MemberSpan = firstSpan };
                            operand = new MemberExpr(operand, text[(dot + 1)..], false, Span.Union(operand.Span, secondSpan)) { MemberSpan = secondSpan };
                            break;
                        }
                        _de.Report("LYR-PAR0003", Severity.Error, number.Span, $"expected member name after '.', got {text}");
                        break;
                    }
                    var name = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0003",
                        $"expected member name after '.', got {_buffer.Current.TokenKind}");
                    operand = new MemberExpr(operand, _sm.Slice(name.Span).ToString(), false,
                        Span.Union(operand.Span, name.Span)) { MemberSpan = name.Span };
                    break;
                }
                case TokenKind.QuestionDot:
                {
                    _buffer.Advance();
                    var name = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0003",
                        $"expected member name after '?.', got {_buffer.Current.TokenKind}");
                    operand = new MemberExpr(operand, _sm.Slice(name.Span).ToString(), true,
                        Span.Union(operand.Span, name.Span)) { MemberSpan = name.Span };
                    break;
                }
                case TokenKind.LBracket:
                {
                    _buffer.Advance();
                    var index = ParseIndexElement();
                    var close = _buffer.Expect(TokenKind.RBracket, "LYR-PAR0004", "expected ']' to close index");
                    operand = new IndexExpr(operand, index, Span.Union(operand.Span, close.Span));
                    break;
                }
                // 'f<int>()' — explicit type arguments at a call site. Needed where the arguments
                // give nothing: a factory 'empty<T>(): List<T>' has none.
                case TokenKind.Less when LooksLikeCallTypeArguments():
                {
                    var typeArguments = ParseTypeArguments(out _);
                    _buffer.Expect(TokenKind.LParen, "LYR-PAR0008",
                        "expected '(' after type arguments");
                    var typedArgs = ParseArguments(out var typedNames);
                    var typedClose = _buffer.Expect(TokenKind.RParen, "LYR-PAR0008",
                        "expected ')' to close call");
                    operand = new CallExpr(operand, typedArgs,
                        Span.Union(operand.Span, typedClose.Span), typeArguments) { ArgumentNames = typedNames };
                    break;
                }

                case TokenKind.LParen:
                {
                    _buffer.Advance();
                    var args = ParseArguments(out var names);
                    var close = _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' to close call");
                    operand = new CallExpr(operand, args, Span.Union(operand.Span, close.Span)) { ArgumentNames = names };
                    break;
                }
                case TokenKind.Inc:
                case TokenKind.Dec:
                case TokenKind.Exclamation:
                {
                    var opTok = _buffer.Advance();
                    operand = new PostfixExpr(operand, Operators.MapPostfix(opTok.TokenKind),
                        Span.Union(operand.Span, opTok.Span));
                    break;
                }

                // 'p with { x = 1, pos.y = 2 }' (02 M6): a postfix, 'with' contextual, the
                // fields as in an initializer with a path allowed on the left.
                case TokenKind.Identifier when AtContextual("with") && _buffer.Peek(1).TokenKind == TokenKind.LBrace:
                {
                    _buffer.Advance(); // 'with'
                    _buffer.Advance(); // '{'
                    var fields = new List<WithField>();
                    while (!_buffer.Check(TokenKind.RBrace) && !_buffer.Check(TokenKind.Eof))
                    {
                        var first = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0003", "expected a field name in 'with'");
                        var path = new List<string> { _sm.Slice(first.Span).ToString() };
                        while (_buffer.Match(TokenKind.Dot))
                            path.Add(_sm.Slice(_buffer.Expect(TokenKind.Identifier, "LYR-PAR0003", "expected a field name after '.'").Span).ToString());
                        _buffer.Expect(TokenKind.Equal, "LYR-PAR0003", "expected '=' after the field in 'with'");
                        var value = ParseSubExpr();
                        fields.Add(new WithField(path.ToArray(), value, Span.Union(first.Span, value.Span)));
                        if (!_buffer.Match(TokenKind.Comma)) break;
                    }
                    var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close 'with'");
                    operand = new WithExpr(operand, fields.ToArray(), Span.Union(operand.Span, close.Span));
                    break;
                }

                // 'xs.map { it * 2 }', 'run { … }', 'fold(0) { acc + it }': a block directly
                // after a callee or a call is its LAST argument, a lambda whose one parameter
                // is 'it'. A '{' after an expression opened nothing before this — every block
                // of the grammar follows a keyword or ')' — so no program changes meaning. The
                // one look-alike, a struct initializer 'Name { x = 1 }' in statement position
                // (§6.8 keeps it out of there), is told apart by its body: 'IDENT =' or '}'.
                case TokenKind.LBrace when IsTrailingLambdaAhead(operand):
                {
                    var lambda = ParseTrailingLambda();
                    operand = operand is CallExpr call && call.Arguments is not [.., LambdaExpr { Form: LambdaForm.Trailing }]
                        ? new CallExpr(call.Callee, [.. call.Arguments, lambda],
                            Span.Union(call.Span, lambda.Span), call.TypeArguments)
                        : new CallExpr(operand, [lambda], Span.Union(operand.Span, lambda.Span));
                    break;
                }
                default:
                    return operand;
            }
        }
    }

    // ---------------------------------------------------------------------
    // Primary (§6.2)
    // ---------------------------------------------------------------------

    /// <summary>Can a token open an expression? The question a contextual prefix word asks of
    /// its successor: 'comptime x' is the prefix, 'comptime + 1' is a name.</summary>
    private static bool BeginsExpression(TokenKind kind) => kind is TokenKind.Identifier
        or TokenKind.IntLiteral or TokenKind.FloatLiteral or TokenKind.StringLiteral
        or TokenKind.CharLiteral or TokenKind.FStringStart or TokenKind.True or TokenKind.False
        or TokenKind.LParen or TokenKind.LBracket or TokenKind.Minus or TokenKind.Exclamation
        or TokenKind.Tilde or TokenKind.If or TokenKind.Match;

    private Expr ParsePrimary()
    {
        var cur = _buffer.Current;
        if (cur.TokenKind != TokenKind.LParen) _guardHead = false; // the guard's first primary is not a group
        switch (cur.TokenKind)
        {
            case TokenKind.IntLiteral:
            {
                var (value, suffix) = LiteralDecoder.DecodeInt(_sm.Slice(cur.Span), cur.Span, _de);
                _buffer.Advance();
                return new IntLiteralExpr(value, suffix, cur.Span);
            }
            case TokenKind.FloatLiteral:
            {
                var (value, suffix) = LiteralDecoder.DecodeFloat(_sm.Slice(cur.Span), cur.Span, _de);
                _buffer.Advance();
                return new FloatLiteralExpr(value, suffix, cur.Span);
            }
            case TokenKind.StringLiteral:
            {
                var value = LiteralDecoder.DecodeString(_sm.Slice(cur.Span), cur.Span, _de);
                _buffer.Advance();
                return new StringLiteralExpr(value, cur.Span);
            }
            case TokenKind.CharLiteral:
            {
                var value = LiteralDecoder.DecodeChar(_sm.Slice(cur.Span), cur.Span, _de);
                _buffer.Advance();
                return new CharLiteralExpr(value, cur.Span);
            }
            case TokenKind.True:
            case TokenKind.False:
                _buffer.Advance();
                return new BoolLiteralExpr(cur.TokenKind == TokenKind.True, cur.Span);
            case TokenKind.Null:
                _buffer.Advance();
                return new NullLiteralExpr(cur.Span);
            // '.Red', '.Num(3)', '.Rect { w = 1 }': a member of the type the position expects
            // (08 Y9). The call and the initializer take the same shapes as after a name.
            case TokenKind.Dot when _buffer.Peek(1).TokenKind == TokenKind.Identifier:
            {
                _buffer.Advance();
                var name = _buffer.Advance();
                var text = _sm.Slice(name.Span).ToString();
                var span = Span.Union(cur.Span, name.Span);
                if (_allowStructInit && _buffer.Check(TokenKind.LBrace)
                    && (_buffer.Peek(1).TokenKind == TokenKind.RBrace
                        || (_buffer.Peek(1).TokenKind == TokenKind.Identifier && _buffer.Peek(2).TokenKind == TokenKind.Equal)))
                    return ParseStructInitFields([text], [], span, name.Span, implicitMember: true);
                return new ImplicitMemberExpr(text, span);
            }
            case TokenKind.Identifier:
                // 'loop { … }' and 'outer: loop { … }' as an expression (05 E11, 08 S4):
                // 'let found = loop { …; break x; };'. A call reads 'name:' as a named argument
                // before it gets here, so the labeled form is free.
                if (IsLoopAhead(0)) return ParseLoop(null, default);
                if (_buffer.Peek(1).TokenKind == TokenKind.Colon && IsLoopAhead(2))
                {
                    var labelTok = _buffer.Advance();
                    _buffer.Advance(); // ':'
                    var labelText = _sm.Slice(labelTok.Span).ToString();
                    _loopLabels.Add(labelText);
                    try { return ParseLoop(labelText, labelTok.Span); }
                    finally { _loopLabels.RemoveAt(_loopLabels.Count - 1); }
                }
                if (IsStructInitAhead()) return ParseStructInit();
                if (IsTypePathAhead()) return ParseTypePath();
                // 'x => …' — one parameter, no parentheses. Free: a name directly before '=>'
                // was a parse error everywhere but in a match arm, whose pattern is not parsed
                // here, and in a guard, which the flag excludes.
                if (!_inGuard && _buffer.Peek(1).TokenKind == TokenKind.FatArrow) return ParseBareLambda();
                _buffer.Advance();
                return new IdentifierExpr(_sm.Slice(cur.Span).ToString(), cur.Span);
            case TokenKind.This:
                _buffer.Advance();
                return new ThisExpr(cur.Span);
            case TokenKind.AtIdentifier:
            {
                _buffer.Advance();
                var name = _sm.Slice(cur.Span).ToString();
                if (_buffer.Check(TokenKind.LParen))
                {
                    _buffer.Advance();
                    var args = ParseArguments(out _);
                    var close = _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' to close attribute arguments");
                    return new AtIdentifierExpr(name, args, Span.Union(cur.Span, close.Span));
                }
                return new AtIdentifierExpr(name, null, cur.Span);
            }
            case TokenKind.LBracket:
                return ParseArrayLit();
            case TokenKind.FStringStart:
                return ParseFString();
            case TokenKind.If:
                return ParseIfExpr();      // if expression, needs an else
            case TokenKind.Match:
                return ParseMatchExpr();
            case TokenKind.LParen:
                return ParseParenOrTupleOrLambda();
            default:
            {
                _de.Report("LYR-PAR0002", Severity.Error, cur.Span, $"expected an expression, got {cur.TokenKind}");
                // Closing tokens are not consumed: they end the surrounding construct and serve
                // its recovery.
                if (cur.TokenKind is not (TokenKind.Eof or TokenKind.RParen or TokenKind.RBracket
                    or TokenKind.RBrace or TokenKind.Comma or TokenKind.Semicolon))
                    _buffer.Advance();
                return new ErrorExpr(cur.Span);
            }
        }
    }

    /// <summary>
    /// '(' introduces three forms: a lambda <c>(params) =&gt; body</c>, a tuple literal
    /// <c>(a, b)</c> or a parenthesized expression <c>(expr)</c>. Lambdas are recognised by looking
    /// ahead for a '=&gt;' after the matching ')'.
    /// </summary>
    private Expr ParseParenOrTupleOrLambda()
    {
        var atGuardHead = _guardHead;
        _guardHead = false;
        if (!atGuardHead && IsLambdaAhead()) return ParseLambda();

        var open = _buffer.Advance(); // '('
        var first = ParseSubExpr();

        if (_buffer.Check(TokenKind.Comma))
        {
            var elems = new List<Expr> { first };
            while (_buffer.Match(TokenKind.Comma))
            {
                if (_buffer.Check(TokenKind.RParen)) break; // tolerate a trailing comma
                elems.Add(ParseSubExpr());
            }
            var close = _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' to close tuple literal");
            var span = Span.Union(open.Span, close.Span);
            if (elems.Count < 2) // one element is a grouping, not a tuple; no upper bound
                _de.Report("LYR-PAR0010", Severity.Error, span, "tuple literals need at least 2 elements");
            return new TupleLitExpr(elems.ToArray(), span);
        }

        _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' to close parenthesized expression");
        return first;
    }

    private ArrayLitExpr ParseArrayLit()
    {
        var open = _buffer.Advance(); // '['
        var elems = new List<Expr>();
        if (!_buffer.Check(TokenKind.RBracket))
        {
            while (true)
            {
                elems.Add(ParseSubExpr());
                if (!_buffer.Match(TokenKind.Comma)) break;
                if (_buffer.Check(TokenKind.RBracket)) break; // trailing comma
            }
        }
        var close = _buffer.Expect(TokenKind.RBracket, "LYR-PAR0004", "expected ']' to close array literal");
        return new ArrayLitExpr(elems.ToArray(), Span.Union(open.Span, close.Span));
    }

    /// <param name="names">The name each argument was written with, or <c>null</c> for every
    /// positional one — and <c>null</c> as a whole when none was named.</param>
    private Expr[] ParseArguments(out string?[]? names)
    {
        var args = new List<Expr>();
        List<string?>? written = null;
        names = null;
        if (_buffer.Check(TokenKind.RParen)) return args.ToArray();
        while (true)
        {
            // 'host: "h"' — an argument by its parameter's name (04 D5, 08 Y9): ':' after an
            // identifier opens nothing else inside a call's parentheses.
            string? name = null;
            if (_buffer.Check(TokenKind.Identifier) && _buffer.Peek(1).TokenKind == TokenKind.Colon)
            {
                name = _sm.Slice(_buffer.Advance().Span).ToString();
                _buffer.Advance();
            }
            // '&x' (03 T12, 08 Y4): the place x for a place parameter. The mark covers the whole
            // argument, as 'try' covers everything to its right; whether what it covers is a place
            // is the checker's question.
            var marked = _buffer.Check(TokenKind.Amp);
            var markSpan = _buffer.Current.Span;
            if (marked) _buffer.Advance();
            var argument = ParseSubExpr();
            args.Add(marked ? new UnaryExpr(UnaryOp.Place, argument, Span.Union(markSpan, argument.Span)) : argument);
            if (name is not null) written ??= new List<string?>(Enumerable.Repeat<string?>(null, args.Count - 1));
            written?.Add(name);
            if (!_buffer.Match(TokenKind.Comma)) break;
            if (_buffer.Check(TokenKind.RParen)) break; // trailing comma
        }
        names = written?.ToArray();
        return args.ToArray();
    }

    /// <summary>
    /// Parses an expression inside a delimiter (parenthesis, argument, index, array, hole): a
    /// struct initializer is always allowed there, whatever the ambient flag says outside.
    /// </summary>
    /// <summary>
    /// What stands between <c>[</c> and <c>]</c>: an index, or a range that takes a view of the
    /// indexed value (03 T13 A2) — <c>a..b</c>, <c>a..=b</c>, <c>..b</c>, <c>a..</c>, <c>..</c>,
    /// with either bound left open. A closed range arrives as the ordinary range node and is
    /// re-read here as the view it is in this position.
    /// </summary>
    private Expr ParseIndexElement()
    {
        if (_buffer.Current.TokenKind is TokenKind.DotDot or TokenKind.DotDotEqual)
        {
            var opTok = _buffer.Advance();
            var inclusive = opTok.TokenKind == TokenKind.DotDotEqual;
            if (_buffer.Check(TokenKind.RBracket)) return new SliceRangeExpr(null, null, inclusive, opTok.Span);
            var high = ParseSubExpr();
            return new SliceRangeExpr(null, high, inclusive, Span.Union(opTok.Span, high.Span));
        }
        var index = ParseSubExpr();
        return index is RangeExpr r ? new SliceRangeExpr(r.Low, r.High, r.IsInclusive, r.Span) : index;
    }

    private Expr ParseSubExpr(int minBindingPower = 0)
    {
        var saved = _allowStructInit;
        var savedGuard = _inGuard;
        _allowStructInit = true;
        _inGuard = false; // a delimiter ends the guard's '=>' ambiguity
        var expr = ParseExpr(minBindingPower);
        _allowStructInit = saved;
        _inGuard = savedGuard;
        return expr;
    }

    // Lookahead from an identifier: is this a struct initializer 'TypePath { … }'? Only when
    // allowed and a '{' follows the type path directly, dotted and generic paths included. The '<'
    // counts as a type argument list only when it closes balanced and a '{' follows; otherwise it
    // is a comparison (a < b).
    private bool IsStructInitAhead()
    {
        if (!_allowStructInit) return false;
        var i = 1; // past the current identifier
        while (_buffer.Peek(i).TokenKind == TokenKind.Dot
               && _buffer.Peek(i + 1).TokenKind == TokenKind.Identifier)
            i += 2;
        if (_buffer.Peek(i).TokenKind == TokenKind.Less)
        {
            i = SkipTypeArgs(i);
            if (i < 0) return false;

            // One more segment may follow the arguments: in 'Ev<int>.Hit { … }' the arguments
            // belong to the enum and the variant hangs off the back. Without this line a struct
            // variant of a generic enum cannot be written.
            if (_buffer.Peek(i).TokenKind == TokenKind.Dot
                && _buffer.Peek(i + 1).TokenKind == TokenKind.Identifier)
                i += 2;
        }
        // The body decides the rest: a struct initializer holds nothing or 'name = …'; a block
        // that holds anything else is a trailing lambda ('run { 7 }', 'xs.map { it * 2 }').
        if (_buffer.Peek(i).TokenKind != TokenKind.LBrace) return false;
        var after = _buffer.Peek(i + 1).TokenKind;
        return after == TokenKind.RBrace
            || (after == TokenKind.Identifier && _buffer.Peek(i + 2).TokenKind == TokenKind.Equal);
    }

    /// <summary>
    /// Lookahead from an identifier: is this a type path WITH arguments in value position, that is
    /// segments joined by <c>.</c>, then <c>&lt;…&gt;</c>, then a <c>.</c> directly after — or the
    /// end of the expression, which makes it an instantiated generic function as a value,
    /// <c>map(xs, ident&lt;int&gt;)</c> (design/v5/spec/03 T17)?
    ///
    /// <para>Without arguments the <c>&lt;</c> is no type path: <c>P.neu()</c> is an ordinary
    /// identifier whose symbol happens to be a type and does not need this route. Hence there is NO
    /// optional <c>&lt;</c> here, unlike in <see cref="IsStructInitAhead"/>.</para>
    ///
    /// <para>The rule costs no ambiguity: a <c>.</c> after a comparison chain
    /// (<c>a &lt; b &gt; .c</c>) is not a valid expression anyway, and neither is one that ends
    /// right after its <c>&gt;</c> (<c>f(a &lt; b, c &gt;)</c>). A call, <c>f&lt;int&gt;(x)</c>,
    /// is not this route either: the postfix loop reads its arguments.</para>
    /// </summary>
    private bool IsTypePathAhead()
    {
        var i = 1; // past the current identifier
        while (_buffer.Peek(i).TokenKind == TokenKind.Dot
               && _buffer.Peek(i + 1).TokenKind == TokenKind.Identifier)
            i += 2;

        if (_buffer.Peek(i).TokenKind != TokenKind.Less) return false;

        i = SkipTypeArgs(i);
        return i >= 0 && _buffer.Peek(i).TokenKind is TokenKind.Dot
            or TokenKind.RParen or TokenKind.Comma or TokenKind.Semicolon
            or TokenKind.RBracket or TokenKind.RBrace or TokenKind.Eof;
    }

    private Expr ParseTypePath()
    {
        var first = _buffer.Advance(); // first IDENT
        var path = new List<string> { _sm.Slice(first.Span).ToString() };
        var nameSpan = first.Span;

        // The lookahead guaranteed 'IDENT (. IDENT)* <', so the loop ends at the '<'.
        while (_buffer.Match(TokenKind.Dot))
        {
            var segment = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0026",
                $"expected type name, got {_buffer.Current.TokenKind}");
            path.Add(_sm.Slice(segment.Span).ToString());
            nameSpan = segment.Span;
        }

        var typeArgs = ParseTypeArguments(out var close);
        return new TypePathExpr(path.ToArray(), typeArgs, Span.Union(first.Span, close))
            { NameSpan = nameSpan };
    }

    // Skips a balanced type argument group starting at Peek(start)=='<' (depth over '<' and '>',
    // '>>' closes two). Returns the index past the closing '>', or -1 when it is unbalanced or a
    // non-type-like token appears, in which case the '<' was a comparison.
    private int SkipTypeArgs(int start)
    {
        var depth = 0;
        for (var i = start; ; i++)
        {
            switch (_buffer.Peek(i).TokenKind)
            {
                case TokenKind.Less: depth++; break;
                case TokenKind.Greater: depth--; break;
                case TokenKind.Shr: depth -= 2; break;
                case TokenKind.Identifier or TokenKind.Dot or TokenKind.Comma
                    or TokenKind.LBracket or TokenKind.RBracket or TokenKind.Question
                    or TokenKind.LParen or TokenKind.RParen or TokenKind.Fn or TokenKind.Arrow:
                    break; // type-like, depth unchanged
                default: return -1; // ';', '{', a literal or an operator is no type argument
            }
            if (depth == 0) return i + 1; // closed cleanly
            if (depth < 0) return -1;      // over-closed
        }
    }

    private Expr ParseStructInit()
    {
        var first = _buffer.Advance(); // first IDENT
        var path = new List<string> { _sm.Slice(first.Span).ToString() };
        var nameSpan = first.Span;
        while (_buffer.Match(TokenKind.Dot))
        {
            var segment = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0026",
                $"expected type name, got {_buffer.Current.TokenKind}");
            path.Add(_sm.Slice(segment.Span).ToString());
            nameSpan = segment.Span;
        }

        TypeNode[] typeArgs = [];
        if (_buffer.Check(TokenKind.Less))
        {
            typeArgs = ParseTypeArguments(out _);

            // 'Ev<int>.Hit { … }': the variant stands BEHIND the enum's arguments.
            while (_buffer.Match(TokenKind.Dot))
            {
                var variant = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0026",
                    $"expected variant name, got {_buffer.Current.TokenKind}");
                path.Add(_sm.Slice(variant.Span).ToString());
                nameSpan = variant.Span;
            }
        }

        return ParseStructInitFields(path, typeArgs, first.Span, nameSpan, implicitMember: false);
    }

    /// <summary>The <c>{ field = value, … }</c> of an initializer, after its name.</summary>
    private Expr ParseStructInitFields(List<string> path, TypeNode[] typeArgs, Span start, Span nameSpan, bool implicitMember)
    {
        _buffer.Advance(); // '{', guaranteed by the caller's lookahead
        var fields = new List<StructInitField>();
        while (!_buffer.Check(TokenKind.RBrace) && !_buffer.AtEnd)
        {
            var nameTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0026",
                $"expected field name, got {_buffer.Current.TokenKind}");
            _buffer.Expect(TokenKind.Equal, "LYR-PAR0037", "expected '=' in struct initializer (':' is only for types)");
            var value = ParseSubExpr();
            fields.Add(new StructInitField(_sm.Slice(nameTok.Span).ToString(), value,
                Span.Union(nameTok.Span, value.Span)) { NameSpan = nameTok.Span });
            if (!_buffer.Match(TokenKind.Comma)) break;
        }
        var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close struct initializer");
        return new StructInitExpr(path.ToArray(), typeArgs, fields.ToArray(),
            Span.Union(start, close.Span)) { NameSpan = nameSpan, IsImplicit = implicitMember };
    }

    // ---------------------------------------------------------------------
    // f-strings. The lexer already yields the sub-tokens; this only assembles them:
    // FStringStart { Chunk | InterpStart Expr [FormatSpec] InterpEnd } FStringEnd.
    // ---------------------------------------------------------------------

    private InterpolatedStringExpr ParseFString()
    {
        var start = _buffer.Advance(); // FStringStart
        var segments = new List<InterpSegment>();
        var end = start;

        while (true)
        {
            var t = _buffer.Current;
            if (t.TokenKind == TokenKind.FStringChunk)
            {
                _buffer.Advance();
                segments.Add(new InterpText(_sm.Slice(t.Span).ToString(), t.Span)); // raw, escapes stay as they are
                continue;
            }
            if (t.TokenKind == TokenKind.FStringInterpStart)
            {
                _buffer.Advance();
                var expr = ParseSubExpr();
                string? formatSpec = null;
                if (_buffer.Check(TokenKind.FStringFormatSpec))
                    formatSpec = _sm.Slice(_buffer.Advance().Span).ToString();
                var interpEnd = _buffer.Expect(TokenKind.FStringInterpEnd, "LYR-PAR0014",
                    "expected '}' to close interpolation");
                segments.Add(new InterpHole(expr, formatSpec, Span.Union(t.Span, interpEnd.Span)));
                continue;
            }
            if (t.TokenKind == TokenKind.FStringEnd)
            {
                end = _buffer.Advance();
                break;
            }
            // EOF or something unexpected: the lexer already reported the unterminated f-string.
            end = t;
            break;
        }

        return new InterpolatedStringExpr(segments.ToArray(), Span.Union(start.Span, end.Span));
    }

    // ---------------------------------------------------------------------
    // Lambdas.
    // ---------------------------------------------------------------------

    private LambdaExpr ParseLambda()
    {
        var open = _buffer.Advance(); // '('
        var parameters = new List<LambdaParam>();
        if (!_buffer.Check(TokenKind.RParen))
        {
            while (true)
            {
                // '((k, v)) => …': a parenthesis where a name stands opens an irrefutable
                // pattern over the parameter. The outer pair is still the parameter list, so
                // '(k, v) => …' keeps its two parameters.
                if (_buffer.Check(TokenKind.LParen))
                {
                    var patternStart = new Span(_buffer.Current.Span.File,
                        _buffer.Current.Span.Start, _buffer.Current.Span.Start); // no name
                    var pattern = ParseTuplePattern();
                    TypeNode? patternType = null;
                    if (_buffer.Match(TokenKind.Colon)) patternType = ParseType();
                    var span = patternType is null ? pattern.Span : Span.Union(pattern.Span, patternType.Span);
                    parameters.Add(new LambdaParam("_", patternType, span)
                        { NameSpan = patternStart, Pattern = pattern });
                }
                else
                {
                    var nameTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0013",
                        $"expected lambda parameter name, got {_buffer.Current.TokenKind}");
                    TypeNode? type = null;
                    if (_buffer.Match(TokenKind.Colon)) type = ParseType();
                    var pspan = type is null ? nameTok.Span : Span.Union(nameTok.Span, type.Span);
                    parameters.Add(new LambdaParam(_sm.Slice(nameTok.Span).ToString(), type, pspan)
                        { NameSpan = nameTok.Span });
                }
                if (!_buffer.Match(TokenKind.Comma)) break;
                if (_buffer.Check(TokenKind.RParen)) break; // trailing comma
            }
        }
        _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' after lambda parameters");

        // A return type, and after it the lambda's own set (08 Y11 F7) — read as a declaration's
        // clause is, so the return type does not take it.
        TypeNode? returnType = null;
        ThrowsClause? throws = null;
        if (_buffer.Match(TokenKind.Colon))
        {
            returnType = ParseType(allowThrows: false);
            if (AtContextual("throws")) throws = ParseThrowsTypes(_buffer.Advance().Span);
        }

        _buffer.Expect(TokenKind.FatArrow, "LYR-PAR0012",
            $"expected '=>' in lambda, got {_buffer.Current.TokenKind}");

        // Body: an expression or a block, '=> expr' or '=> { ... }'. The block is a value block:
        // its tail is the lambda's result, like 'return tail;' at its end.
        Node body = OutsideLoops<Node>(() => _buffer.Check(TokenKind.LBrace) ? ParseBlock(valueBlock: true) : ParseExpr(0));
        return new LambdaExpr(parameters.ToArray(), returnType, body, Span.Union(open.Span, body.Span)) { Throws = throws };
    }

    /// <summary><c>x =&gt; body</c>: one parameter without parentheses and without an annotation —
    /// the type comes from the context, as it does for '(x) => body'.</summary>
    private LambdaExpr ParseBareLambda()
    {
        var nameTok = _buffer.Advance();
        _buffer.Advance(); // '=>', checked by the caller
        var parameter = new LambdaParam(_sm.Slice(nameTok.Span).ToString(), null, nameTok.Span)
            { NameSpan = nameTok.Span };
        // The block is a value block, as the parenthesized form's is: 'x => { …; v }'.
        Node body = OutsideLoops<Node>(() => _buffer.Check(TokenKind.LBrace) ? ParseBlock(valueBlock: true) : ParseExpr(0));
        return new LambdaExpr([parameter], null, body, Span.Union(nameTok.Span, body.Span))
            { Form = LambdaForm.Bare };
    }

    /// <summary>Is the '{' at the cursor a trailing lambda after <paramref name="operand"/>?
    ///
    /// <para>Only after a name, a member or a call — the three things a call can be made of. A
    /// STRUCT INITIALIZER never reaches this point: <see cref="IsStructInitAhead"/> decides at
    /// the primary, before the postfix loop runs, and it takes everything shaped like
    /// <c>Name { }</c> or <c>Name { field = … }</c>. What arrives here is what it left.</para>
    ///
    /// <para>The cost is one error message: a call statement whose ';' is missing, followed by a
    /// block, used to be "expected ';'" and now reads as a trailing lambda. Kotlin and Swift pay
    /// the same, and they have no ';' to miss.</para></summary>
    private bool IsTrailingLambdaAhead(Expr operand) =>
        operand switch
        {
            // At the START OF A STATEMENT a bare name followed by '{' is what §6.8 keeps out:
            // 'Point { x = 1 };' must stay the error it is rather than turning into a call with
            // a trailing lambda. Everywhere else — and after a member or a call anywhere — the
            // brace is the lambda.
            IdentifierExpr => _allowStructInit,
            MemberExpr or CallExpr => true,
            _ => false,
        };

    /// <summary>
    /// <c>{ it * 2 }</c> or <c>{ println(it); }</c>: a trailing lambda's body. Without a ';' at
    /// the block's own level the braces hold ONE expression and the lambda yields it; with one
    /// they hold statements, exactly as '=> { … }' does — a value block, whose tail is the
    /// lambda's result: <c>{ let d = it * 2; d + 1 }</c>.
    /// </summary>
    private LambdaExpr ParseTrailingLambda()
    {
        var open = _buffer.Current; // '{'

        // '{ acc, x => … }', '{ (k, v) => … }' (08 Y11 F2): a parameter list before '=>' names
        // the parameters instead of the implicit 'it'; the body is the rest of the block, a
        // value block whose tail is the result.
        if (TrailingParametersAhead())
        {
            _buffer.Advance(); // '{'
            var parameters = new List<LambdaParam>();
            while (true)
            {
                if (_buffer.Check(TokenKind.LParen))
                {
                    var patternStart = new Span(_buffer.Current.Span.File, _buffer.Current.Span.Start, _buffer.Current.Span.Start);
                    var pattern = ParseTuplePattern();
                    parameters.Add(new LambdaParam("_", null, pattern.Span) { NameSpan = patternStart, Pattern = pattern });
                }
                else
                {
                    var nameTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0013",
                        $"expected lambda parameter name, got {_buffer.Current.TokenKind}");
                    parameters.Add(new LambdaParam(_sm.Slice(nameTok.Span).ToString(), null, nameTok.Span) { NameSpan = nameTok.Span });
                }
                if (!_buffer.Match(TokenKind.Comma)) break;
            }
            _buffer.Expect(TokenKind.FatArrow, "LYR-PAR0012", $"expected '=>' after the parameters, got {_buffer.Current.TokenKind}");
            var rest = OutsideLoops(() => ParseBlockRest(open, valueBlock: true));
            return new LambdaExpr(parameters.ToArray(), null, rest, Span.Union(open.Span, rest.Span)) { Form = LambdaForm.Trailing };
        }
        // 'it' is implicit: the source does not write it, so its name span is empty.
        var it = new LambdaParam("it", null, open.Span)
            { NameSpan = new Span(open.Span.File, open.Span.Start, open.Span.Start), Implicit = true };

        Node body;
        if (HoldsStatements())
        {
            body = OutsideLoops(() => ParseBlock(valueBlock: true));
        }
        else
        {
            _buffer.Advance(); // '{'
            var expr = OutsideLoops(() => ParseSubExpr());
            var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close the lambda body");
            body = expr;
            return new LambdaExpr([it], null, body, Span.Union(open.Span, close.Span)) { Form = LambdaForm.Trailing };
        }
        return new LambdaExpr([it], null, body, Span.Union(open.Span, body.Span)) { Form = LambdaForm.Trailing };
    }

    /// <summary>Is there a parameter list before a <c>=&gt;</c> right after the <c>{</c> at the
    /// cursor? Names or parenthesized patterns, comma-separated, then the arrow.</summary>
    private bool TrailingParametersAhead()
    {
        var i = 1;
        while (true)
        {
            if (_buffer.Peek(i).TokenKind == TokenKind.Identifier) i++;
            else if (_buffer.Peek(i).TokenKind == TokenKind.LParen)
            {
                var depth = 0;
                while (true)
                {
                    var kind = _buffer.Peek(i).TokenKind;
                    if (kind == TokenKind.Eof) return false;
                    if (kind == TokenKind.LParen) depth++;
                    if (kind == TokenKind.RParen && --depth == 0) { i++; break; }
                    i++;
                }
            }
            else return false;
            if (_buffer.Peek(i).TokenKind == TokenKind.FatArrow) return true;
            if (_buffer.Peek(i).TokenKind != TokenKind.Comma) return false;
            i++;
        }
    }

    // Does the block at the cursor contain a ';' at its own level, or begin with a statement
    // keyword? Then it is a statement block rather than a single expression.
    private bool HoldsStatements()
    {
        // An 'if' that is a statement — no final 'else', a binding condition, more behind it —
        // makes the braces a block; one that is an expression is the ONE expression they hold.
        if (_buffer.Peek(1).TokenKind == TokenKind.If && !IfIsExpression(1)) return true;
        if (_buffer.Peek(1).TokenKind is TokenKind.Let or TokenKind.Var or TokenKind.Return
            or TokenKind.Throw or TokenKind.Defer or TokenKind.Try or TokenKind.While or TokenKind.For
            or TokenKind.Do or TokenKind.Break or TokenKind.Continue or TokenKind.Yield or TokenKind.LBrace)
            return true;
        var depth = 0;
        for (var i = 0; ; i++)
        {
            switch (_buffer.Peek(i).TokenKind)
            {
                case TokenKind.LParen or TokenKind.LBracket or TokenKind.LBrace: depth++; break;
                case TokenKind.RParen or TokenKind.RBracket or TokenKind.RBrace:
                    depth--;
                    if (depth == 0) return false;
                    break;
                case TokenKind.Semicolon when depth == 1: return true;
                case TokenKind.Eof: return true;
            }
        }
    }

    /// <summary>
    /// Lookahead from '(': balances parentheses up to the matching ')' and checks whether a
    /// '=&gt;' follows directly. Only then is it a lambda. Resolves lambda vs tuple vs grouping
    /// without backtracking.
    /// </summary>
    private bool IsLambdaAhead()
    {
        var depth = 0;
        for (var i = 0; ; i++)
        {
            switch (_buffer.Peek(i).TokenKind)
            {
                case TokenKind.LParen:
                case TokenKind.LBracket:
                case TokenKind.LBrace:
                    depth++;
                    break;
                case TokenKind.RParen:
                case TokenKind.RBracket:
                case TokenKind.RBrace:
                    depth--;
                    if (depth == 0) return LambdaTailAhead(i + 1);
                    break;
                case TokenKind.Eof:
                    return false;
            }
        }
    }

    // After the closing ')': either '=>' directly OR ': TypeExpr =>' with a return annotation.
    // The type is skipped by token classification only, as in SkipTypeArgs.
    private bool LambdaTailAhead(int i)
    {
        if (_buffer.Peek(i).TokenKind == TokenKind.FatArrow) return true;
        if (_buffer.Peek(i).TokenKind != TokenKind.Colon) return false;
        var depth = 0;
        for (var j = i + 1; ; j++)
        {
            switch (_buffer.Peek(j).TokenKind)
            {
                case TokenKind.FatArrow when depth == 0: return true;
                case TokenKind.LParen or TokenKind.LBracket: depth++; break;
                case TokenKind.RParen or TokenKind.RBracket: depth--; if (depth < 0) return false; break;
                case TokenKind.Identifier or TokenKind.Dot or TokenKind.Comma or TokenKind.Question
                    or TokenKind.Fn or TokenKind.Arrow or TokenKind.Less or TokenKind.Greater
                    or TokenKind.Shr or TokenKind.IntLiteral:
                    break; // type-like
                default: return false; // ';', a literal or an operator is no lambda tail
            }
        }
    }

    // ---------------------------------------------------------------------
    // Type expressions.
    // ---------------------------------------------------------------------

    /// <param name="allowThrows">Whether a trailing <c>throws</c> belongs to the TYPE. False in a
    /// function's return position, where the clause is the FUNCTION's and has been since 1.0:
    /// reading 'fn f(): MyType throws E' as a throwing type would silently retype every existing
    /// signature. A coroutine function needs nothing there — the checker moves its clause into the
    /// coroutine type, which is what that clause has always meant.</param>
    private TypeNode ParseType(bool allowThrows = true)
    {
        if (++_depth > MaxNesting) { _depth--; throw new NestingTooDeep(_buffer.Current.Span); }
        try { return ParseTypeInner(allowThrows); }
        finally { _depth--; }
    }

    private TypeNode ParseTypeInner(bool allowThrows)
    {
        // '?T', and since Lyric 5 '??T' (design/v5/spec/03 T4 O1): an optional of an optional is
        // a type. The lexer hands '??' over as one token, the coalesce operator; in type
        // position it is two levels.
        var qTok = _buffer.Current;
        var levels = 0;
        while (true)
        {
            if (_buffer.Match(TokenKind.Question)) levels++;
            else if (_buffer.Match(TokenKind.QuestionQuestion)) levels += 2;
            else break;
        }

        var type = ParseTypeAtom();

        while (_buffer.Check(TokenKind.LBracket)) // T[]
        {
            _buffer.Advance();

            // 'T[3]' is the inline array (design/v5/spec/03 T13 A4): a value of that many
            // elements. The length is a literal in 5.0; a constant or a parameter is a door (T18).
            int? length = null;
            if (_buffer.Check(TokenKind.IntLiteral))
            {
                var sizeTok = _buffer.Advance();
                var text = _sm.Slice(sizeTok.Span).ToString().Replace("_", "");
                if (!int.TryParse(text, out var n) || n <= 0)
                    _de.Report("LYR-PAR0043", Severity.Error, sizeTok.Span,
                        "the length of an inline array is a positive decimal literal");
                else length = n;
            }
            var close = _buffer.Expect(TokenKind.RBracket, "LYR-PAR0004", "expected ']' to close array type");
            type = new ArrayType(type, Span.Union(type.Span, close.Span)) { Length = length };
        }

        // 'Coroutine<int> throws Exception'. Binds tighter than '?', so '?Coroutine<int> throws E'
        // is an optional of the throwing coroutine rather than the other way round — the throwing
        // is a property of the coroutine, and an optional of it is still one value or none.
        if (allowThrows && AtContextual("throws"))
        {
            var tk = _buffer.Advance();
            var thrown = StartsType() ? ParseType() : null;
            type = new ThrowingType(type, thrown, Span.Union(type.Span, thrown?.Span ?? tk.Span));
        }

        for (var i = 0; i < levels; i++)
            type = new NullableType(type, Span.Union(qTok.Span, type.Span));
        return type;
    }

    /// <summary>Does a type start here? Asked after a type-level <c>throws</c>, which may stand
    /// alone: <c>Coroutine&lt;int&gt; throws</c> before a ',', a ')' or an '=' throws anything.</summary>
    private bool StartsType() => _buffer.Current.TokenKind
        is TokenKind.Identifier or TokenKind.Fn or TokenKind.LParen or TokenKind.Question
            or TokenKind.QuestionQuestion;

    private TypeNode ParseTypeAtom()
    {
        var cur = _buffer.Current;
        switch (cur.TokenKind)
        {
            case TokenKind.Fn:
                return ParseFunctionType();
            case TokenKind.LParen:
                return ParseParenthesizedType();
            case TokenKind.Identifier:
            {
                _buffer.Advance();
                var path = new List<string> { _sm.Slice(cur.Span).ToString() };
                var end = cur.Span;
                var nameSpan = cur.Span;
                while (_buffer.Match(TokenKind.Dot))
                {
                    var seg = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0011",
                        $"expected identifier in type path, got {_buffer.Current.TokenKind}");
                    path.Add(_sm.Slice(seg.Span).ToString());
                    end = seg.Span;
                    nameSpan = seg.Span;
                }
                TypeNode[] args = [];
                string?[]? names = null;
                if (_buffer.Check(TokenKind.Less))
                {
                    args = ParseTypeArguments(out var closeSpan, out names);
                    end = closeSpan;
                }
                return new NamedType(path.ToArray(), args, Span.Union(cur.Span, end))
                    { NameSpan = nameSpan, ArgumentNames = names };
            }
            default:
                _de.Report("LYR-PAR0011", Severity.Error, cur.Span, $"expected a type, got {cur.TokenKind}");
                return new ErrorType(cur.Span);
        }
    }

    private FunctionType ParseFunctionType()
    {
        var start = _buffer.Advance(); // 'fn'
        _buffer.Expect(TokenKind.LParen, "LYR-PAR0008", "expected '(' after 'fn' in function type");
        var parameters = new List<TypeNode>();
        // 'fn(&int) -> void' (03 T12, T17): the mark at the parameter's start, the type unmarked.
        var places = new List<bool>();
        if (!_buffer.Check(TokenKind.RParen))
            do
            {
                places.Add(_buffer.Match(TokenKind.Amp));
                parameters.Add(ParseType());
            } while (_buffer.Match(TokenKind.Comma));
        _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' in function type");
        _buffer.Expect(TokenKind.Arrow, "LYR-PAR0015",
            $"expected '->' in function type, got {_buffer.Current.TokenKind}");
        // A 'throws' after the return type is the function TYPE's set (03 T17): the return type is
        // read without the coroutine suffix, so the nearest function type takes it.
        var returnType = ParseType(allowThrows: false);
        ThrowsClause? throws = null;
        if (AtContextual("throws")) throws = ParseTypeThrows(_buffer.Advance().Span);
        return new FunctionType(parameters.ToArray(), returnType,
            Span.Union(start.Span, throws?.Span ?? returnType.Span))
            { Throws = throws, Places = places.Contains(true) ? places.ToArray() : [] };
    }

    /// <summary>
    /// What follows a function type's <c>throws</c> (design/v5/spec/03 T17, 05 E2): a bracketed list,
    /// one type, or nothing — the bare form, <c>Error</c> — where no type starts. Unlike a
    /// declaration's clause, a comma ends the type: in <c>fn(f: fn() -&gt; void throws A, b: B)</c> it
    /// separates two parameters, so several types without the brackets are no list here.
    /// </summary>
    private ThrowsClause ParseTypeThrows(Span keyword)
    {
        if (_buffer.Check(TokenKind.LBracket)) return ParseThrownList(keyword);
        if (!StartsType()) return new ThrowsClause([], keyword);
        var one = ParseType(allowThrows: false);
        return new ThrowsClause([one], Span.Union(keyword, one.Span));
    }

    /// <summary>
    /// <c>(</c> in type position: either a TUPLE, from two elements on, or a plain GROUPING.
    ///
    /// <para>No conflict between the two, because Lyric has no 1-tuple: <c>TupleType</c> requires
    /// arity 2. Rust needs <c>(T,)</c> for this; here the spot is free.</para>
    ///
    /// <para>What the grouping is for: <c>fn(A) -&gt; R</c> is the only type in the language open
    /// to the right — <c>fn(int) -&gt; void[]</c> reads as a function returning <c>void[]</c>, and
    /// an array of function values could not be written at all. The precedence stays as it is.</para>
    /// </summary>
    private TypeNode ParseParenthesizedType()
    {
        var open = _buffer.Advance(); // '('

        var elems = new List<TypeNode>();
        var labels = new List<string?>();
        var sawComma = false;
        do
        {
            // '(x: int, y: int)': a label before an element (03 T16), 'name' then ':'.
            if (_buffer.Check(TokenKind.Identifier) && _buffer.Peek(1).TokenKind == TokenKind.Colon)
            {
                labels.Add(_sm.Slice(_buffer.Advance().Span).ToString());
                _buffer.Advance(); // ':'
            }
            else labels.Add(null);
            elems.Add(ParseType());
            if (!_buffer.Match(TokenKind.Comma)) break;
            sawComma = true;
        } while (!_buffer.Check(TokenKind.RParen));

        var close = _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' to close type");
        var span = Span.Union(open.Span, close.Span);

        // One element WITHOUT a comma is a grouping: the inner type moves up unchanged. With a
        // comma ('(T,)') a tuple was meant, and its second element is missing.
        if (elems.Count == 1 && !sawComma && labels[0] is null) return elems[0];

        if (elems.Count < 2) // no upper bound
            _de.Report("LYR-PAR0010", Severity.Error, span, "tuple types need at least 2 elements");
        for (var i = 0; i < labels.Count; i++)
            if (labels[i] is { } l && labels.IndexOf(l) != i)
                _de.Report("LYR-PAR0010", Severity.Error, span, $"tuple label '{l}' is given twice");

        return new TupleType(elems.ToArray(), span) { Labels = labels.Any(l => l is not null) ? labels.ToArray() : null };
    }

    private TypeNode[] ParseTypeArguments(out Span closeSpan) => ParseTypeArguments(out closeSpan, out _);

    /// <param name="names">'Item = int' (03 T6): the associated type an argument fixes, by
    /// index, <c>null</c> for a positional one; <c>null</c> as a whole when none is named.</param>
    private TypeNode[] ParseTypeArguments(out Span closeSpan, out string?[]? names)
    {
        _buffer.Expect(TokenKind.Less, "LYR-PAR0009", "expected '<' to open type arguments");
        var args = new List<TypeNode>();
        List<string?>? written = null;
        do
        {
            string? name = null;
            if (_buffer.Check(TokenKind.Identifier) && _buffer.Peek(1).TokenKind == TokenKind.Equal)
            {
                name = _sm.Slice(_buffer.Advance().Span).ToString();
                _buffer.Advance();
            }
            args.Add(ParseType());
            if (name is not null) written ??= new List<string?>(Enumerable.Repeat<string?>(null, args.Count - 1));
            written?.Add(name);
        } while (_buffer.Match(TokenKind.Comma));
        names = written?.ToArray();

        // Nested generics: split '>>', '>=' and '>>=' into single '>' tokens.
        if (_buffer.Current.TokenKind is TokenKind.Shr or TokenKind.ShrEqual or TokenKind.GreaterEqual)
            _buffer.SplitCurrentGreater();

        closeSpan = _buffer.Expect(TokenKind.Greater, "LYR-PAR0009",
            $"expected '>' to close type arguments, got {_buffer.Current.TokenKind}").Span;
        return args.ToArray();
    }
}
