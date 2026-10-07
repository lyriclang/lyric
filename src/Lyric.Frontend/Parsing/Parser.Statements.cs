using Lyric.AST;
using Lyric.Core;
using Lyric.Lexing;

namespace Lyric.Parsing;

/// <summary>
/// The statement parser, recursive descent. Dispatch is on the first token; anything without a
/// statement keyword is an <c>ExprStmt</c>. Control statements hold their body as a <c>Block</c>.
/// Like the rest of the parser: never throw,
/// reporting errors as LYR-PAR#### plus an ErrorStmt or ErrorExpr, then carrying on as best it can.
/// </summary>
public sealed partial class Parser
{
    /// <summary>Entry point for exactly ONE statement; a block covers sequences.</summary>
    public Stmt ParseStatement()
    {
        var stmt = ParseStmt();
        if (!_buffer.AtEnd)
            _de.Report("LYR-PAR0001", Severity.Error, _buffer.Current.Span,
                $"unexpected token after statement: {_buffer.Current.TokenKind}");
        return stmt;
    }

    private Stmt ParseStmt() => _buffer.Current.TokenKind switch
    {
        // 'name:' at the start of a statement is a loop label — nothing else begins that way
        // (§6.8: an expression statement is a call, an assignment or 'resume').
        TokenKind.Identifier when _buffer.Peek(1).TokenKind == TokenKind.Colon => ParseLabeled(),
        // 'using let f = …;' (08 Y5 S5): contextual, as nothing else starts with a name and 'let'.
        TokenKind.Identifier when AtContextual("using") && _buffer.Peek(1).TokenKind is TokenKind.Let or TokenKind.Var
            => ParseUsing(),
        // 'loop { … }' (08 S3, 05 E11): contextual, as 'loop' before a brace starts nothing else. A
        // statement here — no ';' after its block — and an expression anywhere else.
        TokenKind.Identifier when IsLoopAhead(0) => LoopStatement(ParseLoop(null, default)),
        TokenKind.LBrace => ParseBlock(),
        TokenKind.Let or TokenKind.Var => ParseBinding(),
        // In a value block an 'if' may be an expression (08 Y4, the tail rule): the form without
        // braces, and the block form with its 'else' standing last — the block's value.
        TokenKind.If when _allowTail && IfIsExpression(0) => ParseExprStmt(),
        TokenKind.If => ParseIf(),
        TokenKind.While => ParseWhile(),
        TokenKind.Do => ParseDoWhile(),
        TokenKind.For => ParseForIn(),
        TokenKind.Break => ParseBreak(),
        TokenKind.Continue => ParseContinue(),
        TokenKind.Return => ParseReturn(),
        TokenKind.Yield => ParseYield(),
        TokenKind.Defer => ParseDefer(),
        TokenKind.Throw => ParseThrow(),
        // 'try {' is the block form; 'try f();' is an expression statement whose expression is
        // marked (design/v5/spec/05 E4).
        TokenKind.Try when _buffer.Peek(1).TokenKind == TokenKind.LBrace => ParseTry(),
        TokenKind.Match => ParseMatchStmt(),
        _ => ParseExprStmt(),
    };

    /// <summary>'outer: while (…) { … }' — the label names the loop that follows it, and only a
    /// loop. On anything else the label is reported and the statement parsed as written, so one
    /// misplaced label yields one diagnostic rather than a cascade.</summary>
    private Stmt ParseLabeled()
    {
        var nameTok = _buffer.Advance(); // IDENTIFIER
        _buffer.Advance();               // ':'
        var label = _sm.Slice(nameTok.Span).ToString();
        var start = nameTok.Span;
        // The label is in scope in the loop's body, where a 'break' reads the name as it (08 S4).
        _loopLabels.Add(label);
        try
        {
            switch (_buffer.Current.TokenKind)
            {
                case TokenKind.While:
                {
                    var w = (WhileStmt)ParseWhile();
                    return w with { Label = label, LabelSpan = nameTok.Span, Span = Span.Union(start, w.Span) };
                }
                case TokenKind.Do:
                {
                    var d = (DoWhileStmt)ParseDoWhile();
                    return d with { Label = label, LabelSpan = nameTok.Span, Span = Span.Union(start, d.Span) };
                }
                case TokenKind.For:
                {
                    var f = (ForInStmt)ParseForIn();
                    return f with { Label = label, LabelSpan = nameTok.Span, Span = Span.Union(start, f.Span) };
                }
                case TokenKind.Identifier when IsLoopAhead(0):
                    return LoopStatement(ParseLoop(label, nameTok.Span));
                default:
                    _de.Report("LYR-PAR0046", Severity.Error, nameTok.Span,
                        $"a label names a loop: expected 'while', 'do', 'for' or 'loop' after '{label}:'");
                    return ParseStmt();
            }
        }
        finally { _loopLabels.RemoveAt(_loopLabels.Count - 1); }
    }

    /// <summary>The labels of the loops around what is being parsed, innermost last: a name after
    /// 'break' is a label exactly when one of them is it (08 S4).</summary>
    private readonly List<string> _loopLabels = new();

    /// <summary>A lambda's body: a function of its own, out of reach of the loops around the lambda
    /// (08 Y11 F5) — their labels name nothing in it, and a name after 'break' there is a value.</summary>
    private T OutsideLoops<T>(Func<T> parse)
    {
        var around = _loopLabels.ToArray();
        _loopLabels.Clear();
        try { return parse(); }
        finally { _loopLabels.Clear(); _loopLabels.AddRange(around); }
    }

    /// <summary>Does 'loop' with its brace start <paramref name="at"/> tokens ahead?</summary>
    private bool IsLoopAhead(int at)
    {
        var token = at == 0 ? _buffer.Current : _buffer.Peek(at);
        return token.TokenKind == TokenKind.Identifier && _sm.Slice(token.Span).ToString() == "loop"
               && _buffer.Peek(at + 1).TokenKind == TokenKind.LBrace;
    }

    /// <summary>'loop { … }' (05 E11): the block, again and again until a 'break' leaves it.</summary>
    private LoopExpr ParseLoop(string? label, Span labelSpan)
    {
        var kw = _buffer.Advance(); // 'loop'
        var body = ParseBlock();
        return new LoopExpr(body, Span.Union(label is null ? kw.Span : labelSpan, body.Span))
            { Label = label, LabelSpan = labelSpan };
    }

    /// <summary>A loop at the start of a statement is the statement, with no ';' after its block
    /// — and, standing last in a value block, that block's value (the tail rule).</summary>
    private Stmt LoopStatement(LoopExpr loop) =>
        AtTail() ? new TailExprStmt(loop, loop.Span) : new ExprStmt(loop, loop.Span);

    /// <summary>
    /// Does the statement just parsed stand LAST in a value block, with no ';' behind it? Then it
    /// is the block's value (08 Y4, design 05's review M5-1; Rust's rule): an 'if' with its
    /// 'else', a 'match' and a 'loop' are worth what their expression forms are worth. In the
    /// middle of the block they are statements, and so is each of them in a statement block.
    /// </summary>
    private bool AtTail() => _allowTail && _buffer.Check(TokenKind.RBrace);

    private Stmt ParseMatchStmt()
    {
        var kw = _buffer.Advance(); // 'match'
        var (scrutinee, arms, end) = ParseMatchCore();
        var span = Span.Union(kw.Span, end);
        return AtTail() ? new TailExprStmt(new MatchExpr(scrutinee, arms, span), span) : new MatchStmt(scrutinee, arms, span);
    }

    /// <summary>A block; with <paramref name="valueBlock"/> one in value position — a match arm,
    /// a lambda body, a clause of a 'try' expression, a branch of an 'if' expression, the right
    /// of '??' — whose last statement may be a tail without ';': an expression, or an 'if', a
    /// 'match' or a 'loop' (§6.9, 08 Y4). The flag holds for the block's OWN statements only: a
    /// nested statement block resets it, so a tail can stand exactly where its value has
    /// somewhere to go.</summary>
    /// <summary>
    /// The body of <c>if</c>, <c>else</c>, <c>while</c>, <c>do</c> and <c>for</c> (08 Y5 S1): a block,
    /// always. A statement written without the braces is refused once, with the form, and read as
    /// the block it belongs in — "expected '}'" behind it was the same mistake said again.
    /// </summary>
    private Block ParseBody(string keyword)
    {
        if (_buffer.Check(TokenKind.LBrace)) return ParseBlock();
        var at = _buffer.Current.Span;
        _de.Report("LYR-PAR0017", Severity.Error, at,
            $"the body of '{keyword}' is a block — write '{keyword} {(keyword is "do" or "else" ? "" : "(…) ")}{{ … }}', braces even around one statement");
        var single = ParseStmt();
        return new Block([single], Span.Union(at, single.Span));
    }

    private Block ParseBlock(bool valueBlock = false)
    {
        var open = _buffer.Expect(TokenKind.LBrace, "LYR-PAR0017", "expected '{' to open block");
        return ParseBlockRest(open, valueBlock);
    }

    /// <summary>The statements of a block whose <c>{</c> the caller has consumed, up to and
    /// including its <c>}</c>.</summary>
    private Block ParseBlockRest(Token open, bool valueBlock)
    {
        var stmts = new List<Stmt>();
        var savedTail = _allowTail;
        _allowTail = valueBlock;
        // A block is a delimiter, as ParseSubExpr's are: its statements decide for themselves
        // where a struct initializer may stand — also in a trailing lambda at the start of a
        // statement, whose ban must not reach into the lambda's body.
        var savedStructInit = _allowStructInit;
        _allowStructInit = true;
        while (!_buffer.Check(TokenKind.RBrace) && !_buffer.AtEnd)
        {
            var before = _buffer.Position;
            stmts.Add(ParseStmt());
            if (_buffer.Position == before && !_buffer.AtEnd)
                _buffer.Advance(); // force progress, so an unconsumed token cannot loop forever
        }
        var close = _buffer.Expect(TokenKind.RBrace, "LYR-PAR0018", "expected '}' to close block");
        _allowTail = savedTail;
        _allowStructInit = savedStructInit;
        return new Block(stmts.ToArray(), Span.Union(open.Span, close.Span));
    }

    /// <summary>
    /// <c>using let f = open(p);</c> (design/v5/spec/05 E7 R1–R2, 08 Y5 S5): a binding whose value
    /// is closed when its scope is left — on every way out but a panic, in the scope's one LIFO list
    /// with the <c>defer</c>s. The parser writes that close beside the binding as the
    /// <c>defer try f.close();</c> it is, at the keyword. One name and a value; a <c>let</c>, since
    /// <c>using var</c> could rebind the name and leave the first value open (LYR-PAR0052).
    /// </summary>
    private Stmt ParseUsing()
    {
        var kw = _buffer.Advance(); // 'using'
        if (_buffer.Check(TokenKind.Var))
            _de.Report("LYR-PAR0052", Severity.Error, Span.Union(kw.Span, _buffer.Current.Span),
                "a resource binding is a 'let' — 'using var' could rebind it and leave the first value open");
        var parsed = ParseBinding();
        if (parsed is not BindingStmt { Initializer: not null } binding)
        {
            _de.Report("LYR-PAR0052", Severity.Error, Span.Union(kw.Span, parsed.Span),
                "'using' binds one name to a value — 'using let f = open(p);'");
            return parsed;
        }
        var at = kw.Span;
        var close = new CallExpr(
            new MemberExpr(new IdentifierExpr(binding.Name, at), "close", false, at) { MemberSpan = default },
            [], at);
        var cleanup = new DeferStmt(new ExprStmt(new TryExpr(close, at) { KeywordSpan = at }, at), at);
        return binding with { IsMutable = false, Span = Span.Union(kw.Span, binding.Span), Cleanup = cleanup };
    }

    private Stmt ParseBinding()
    {
        var kw = _buffer.Advance(); // let / var
        var isMutable = kw.TokenKind == TokenKind.Var;

        // 'let (a, b) = …' — destructuring. The parenthesis decides, and at this position it can
        // introduce nothing else: a binding name is an identifier.
        if (_buffer.Check(TokenKind.LParen)) return ParseDestructuring(kw, isMutable);

        // 'let [a, b] = …': a bracket where a name stands opens an array pattern.
        if (_buffer.Check(TokenKind.LBracket)) return ParseLetPattern(kw, isMutable);

        // 'let Circle(r) = …', 'let Point { x, y } = …', 'let Shape.Empty = …': a name followed
        // by '(', '{' or '.' opens a pattern, never a binding — a binding name is followed by
        // ':', '=' or ';'.
        if (_buffer.Check(TokenKind.Identifier)
            && _buffer.Peek(1).TokenKind is TokenKind.LParen or TokenKind.LBrace or TokenKind.Dot)
            return ParseLetPattern(kw, isMutable);

        // 'let .Num(n) = …': the dotted variant of the initializer's enum (08 Y6) is a pattern
        // as well; a binding name never starts with a dot.
        if (_buffer.Check(TokenKind.Dot) && _buffer.Peek(1).TokenKind == TokenKind.Identifier)
            return ParseLetPattern(kw, isMutable);

        var nameTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0020",
            $"expected binding name, got {_buffer.Current.TokenKind}");
        TypeNode? type = _buffer.Match(TokenKind.Colon) ? ParseType() : null;
        Expr? init = _buffer.Match(TokenKind.Equal) ? ParseExpr(0) : null;

        // 'let x = opt else { return; };' — the plain name is a pattern too, and the 'else'
        // makes it a let-else. An if-expression initializer has already consumed its own
        // 'else', so the one seen here is unambiguous.
        if (init is not null && _buffer.Check(TokenKind.Else))
        {
            _buffer.Advance();
            var elseBlock = ParseBlock();
            var end = ExpectSemicolon();
            return new LetPatternStmt(isMutable, new BindingPattern(_sm.Slice(nameTok.Span).ToString(), nameTok.Span),
                type, init, elseBlock, Span.Union(kw.Span, end.Span));
        }

        var semi = ExpectSemicolon();
        return new BindingStmt(isMutable, _sm.Slice(nameTok.Span).ToString(), type, init,
            Span.Union(kw.Span, semi.Span)) { NameSpan = nameTok.Span };
    }

    /// <summary>
    /// <c>let Pattern [: Type] = Expr [else Block];</c> — a binding through any pattern. The
    /// else block is what makes a pattern that can fail legal here; whether it is needed is the
    /// sema's question (LYR-SEM0098), the grammar takes both forms.
    /// </summary>
    private Stmt ParseLetPattern(Token kw, bool isMutable)
    {
        var pattern = ParseOrPattern();
        TypeNode? type = _buffer.Match(TokenKind.Colon) ? ParseType() : null;
        if (!_buffer.Match(TokenKind.Equal))
        {
            _de.Report("LYR-PAR0020", Severity.Error, _buffer.Current.Span,
                "a pattern binding needs an initializer ('let Circle(r) = …;')");
            var bad = ExpectSemicolon();
            return new LetPatternStmt(isMutable, pattern, type, new ErrorExpr(bad.Span), null,
                Span.Union(kw.Span, bad.Span));
        }
        var init = ParseExpr(0);
        Block? elseBlock = _buffer.Match(TokenKind.Else) ? ParseBlock() : null;
        var semi = ExpectSemicolon();
        return new LetPatternStmt(isMutable, pattern, type, init, elseBlock, Span.Union(kw.Span, semi.Span));
    }

    /// <summary>
    /// The condition of an <c>if</c> or a <c>while</c>: an expression, or <c>let Pattern = Expr</c>.
    /// Unambiguous, because <c>let</c> is a keyword and begins no expression.
    /// </summary>
    private Expr ParseCondition()
    {
        if (!_buffer.Check(TokenKind.Let)) return ParseExpr(0);
        var kw = _buffer.Advance();
        var pattern = ParseOrPattern();
        _buffer.Expect(TokenKind.Equal, "LYR-PAR0020", "expected '=' after the pattern of a 'let' condition");
        var init = ParseExpr(0);
        return new LetCondExpr(pattern, init, Span.Union(kw.Span, init.Span));
    }

    /// <summary>
    /// <c>let (a, b) = pair;</c> — see <c>docs/Grammar.md</c> §5.
    ///
    /// <para>The pattern is parsed as an ordinary tuple pattern — the same one a
    /// <c>match</c> arm uses. Whatever holds there holds here: nested
    /// pattern uses, with <c>_</c> as a placeholder, and the arity has to match.</para>
    /// </summary>
    private Stmt ParseDestructuring(Token kw, bool isMutable)
    {
        // ParseOrPattern rather than ParsePattern: the latter is the test entry point and requires
        // the file to end after it.
        var pattern = ParseOrPattern();
        if (pattern is not TuplePattern tuple)
        {
            _de.Report("LYR-PAR0020", Severity.Error, pattern.Span,
                "a destructuring binding needs a tuple pattern like '(a, b)'");
            tuple = new TuplePattern([], pattern.Span);
        }

        TypeNode? type = _buffer.Match(TokenKind.Colon) ? ParseType() : null;

        // The initializer is required: without it there would be nothing to take apart, and the
        // definite-assignment analysis would have to track several names without a value.
        if (!_buffer.Match(TokenKind.Equal))
        {
            _de.Report("LYR-PAR0020", Severity.Error, _buffer.Current.Span,
                "a destructuring binding needs an initializer ('let (a, b) = …;')");
            var bad = ExpectSemicolon();
            return new DestructuringStmt(isMutable, tuple, type, new ErrorExpr(bad.Span),
                Span.Union(kw.Span, bad.Span));
        }

        var init = ParseExpr(0);
        var semi = ExpectSemicolon();
        return new DestructuringStmt(isMutable, tuple, type, init, Span.Union(kw.Span, semi.Span));
    }

    private Stmt ParseIf()
    {
        var kw = _buffer.Advance(); // if
        _buffer.Expect(TokenKind.LParen, "LYR-PAR0019", "expected '(' after 'if'");
        var cond = ParseCondition();
        _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' after if-condition");
        var then = ParseBody("if");

        Stmt? elseBranch = null;
        var end = then.Span;
        if (_buffer.Match(TokenKind.Else))
        {
            elseBranch = _buffer.Check(TokenKind.If) ? ParseIf() : ParseBody("else"); // else-if kettet
            end = elseBranch.Span;
        }
        return new IfStmt(cond, then, elseBranch, Span.Union(kw.Span, end));
    }

    /// <summary>
    /// Is the 'if' <paramref name="at"/> tokens ahead — the start of a statement in a value block
    /// — an EXPRESSION? Two shapes are: the form without braces, 'if (c) a else b', which is a
    /// statement nowhere (08 Y5 S1); and the block form whose chain ends in an 'else' and which
    /// stands last in the block — the block's value.
    ///
    /// <para>A statement, as in every other block: an 'if' without a final 'else' (it gives no
    /// value on the other path), one with more of the block behind it, and one whose condition
    /// binds, 'if (let …)' — the names it binds are a statement's.</para>
    ///
    /// <para>A token scan, as the other lookaheads are: nothing is parsed twice and nothing is
    /// reported.</para>
    /// </summary>
    private bool IfIsExpression(int at)
    {
        var i = at; // at the 'if'
        while (true)
        {
            if (_buffer.Peek(i + 1).TokenKind != TokenKind.LParen) return false;
            if (_buffer.Peek(i + 2).TokenKind is TokenKind.Let or TokenKind.Var) return false;
            i = AfterGroup(i + 1);
            if (i < 0) return false;
            if (_buffer.Peek(i).TokenKind != TokenKind.LBrace) return true;   // 'if (c) a else b'
            i = AfterGroup(i);
            if (i < 0 || _buffer.Peek(i).TokenKind != TokenKind.Else) return false; // no 'else': a statement
            i++;
            if (_buffer.Peek(i).TokenKind == TokenKind.If) continue;          // 'else if': the chain goes on
            if (_buffer.Peek(i).TokenKind != TokenKind.LBrace) return true;   // 'else b': an expression's
            i = AfterGroup(i);
            return i >= 0 && _buffer.Peek(i).TokenKind == TokenKind.RBrace;
        }
    }

    /// <summary>The offset behind the group opening <paramref name="i"/> tokens ahead — past its
    /// matching ')', ']' or '}' — or -1 where the input ends first.</summary>
    private int AfterGroup(int i)
    {
        var depth = 0;
        for (; ; i++)
        {
            switch (_buffer.Peek(i).TokenKind)
            {
                case TokenKind.LParen or TokenKind.LBracket or TokenKind.LBrace:
                    depth++;
                    break;
                case TokenKind.RParen or TokenKind.RBracket or TokenKind.RBrace:
                    if (--depth <= 0) return i + 1;
                    break;
                case TokenKind.Eof:
                    return -1;
            }
        }
    }

    private Stmt ParseWhile()
    {
        var kw = _buffer.Advance(); // while
        _buffer.Expect(TokenKind.LParen, "LYR-PAR0019", "expected '(' after 'while'");
        var cond = ParseCondition();
        _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' after while-condition");
        var body = ParseBody("while");
        return new WhileStmt(cond, body, Span.Union(kw.Span, body.Span));
    }

    private Stmt ParseDoWhile()
    {
        var kw = _buffer.Advance(); // do
        var body = ParseBody("do");
        _buffer.Expect(TokenKind.While, "LYR-PAR0022", "expected 'while' after do-block");
        _buffer.Expect(TokenKind.LParen, "LYR-PAR0019", "expected '(' after 'while'");
        var cond = ParseExpr(0);
        _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' after while-condition");
        var semi = ExpectSemicolon();
        return new DoWhileStmt(body, cond, Span.Union(kw.Span, semi.Span));
    }

    private Stmt ParseForIn()
    {
        var kw = _buffer.Advance(); // for
        _buffer.Expect(TokenKind.LParen, "LYR-PAR0019", "expected '(' after 'for'");

        // 'for ((k, v) in …)': a parenthesis where the name stands opens a pattern — the
        // element is taken apart at the top of every iteration. Only names stood here before,
        // so the form is free.
        if (_buffer.Check(TokenKind.LParen))
        {
            var patternStart = new Span(_buffer.Current.Span.File,
                _buffer.Current.Span.Start, _buffer.Current.Span.Start); // no name: an empty span
            var pattern = ParseTuplePattern();
            _buffer.Expect(TokenKind.In, "LYR-PAR0021", "expected 'in' in for-loop");
            var patternIter = ParseExpr(0);
            _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' after for-loop header");
            var patternBody = ParseBody("for");
            return new ForInStmt("_", patternIter, patternBody, Span.Union(kw.Span, patternBody.Span))
                { NameSpan = patternStart, Pattern = pattern };
        }

        var varTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0020",
            $"expected loop variable, got {_buffer.Current.TokenKind}");
        _buffer.Expect(TokenKind.In, "LYR-PAR0021", "expected 'in' in for-loop");
        var iter = ParseExpr(0);
        _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' after for-loop header");
        var body = ParseBody("for");
        return new ForInStmt(_sm.Slice(varTok.Span).ToString(), iter, body, Span.Union(kw.Span, body.Span))
            { NameSpan = varTok.Span };
    }

    private Stmt ParseBreak()
    {
        var kw = _buffer.Advance();
        // 'break', 'break outer', 'break value', 'break outer value' (08 S4): a name is the label
        // when a loop around the 'break' carries it, and the value's beginning otherwise —
        // 'break (x);' is a value whatever 'x' names.
        string? label = null;
        Span labelSpan = default;
        if (_buffer.Check(TokenKind.Identifier) && _loopLabels.Contains(_sm.Slice(_buffer.Current.Span).ToString()))
        {
            var tok = _buffer.Advance();
            label = _sm.Slice(tok.Span).ToString();
            labelSpan = tok.Span;
        }
        var value = _buffer.Check(TokenKind.Semicolon) ? null : ParseExpr(0);
        var semi = ExpectSemicolon();
        return new BreakStmt(Span.Union(kw.Span, semi.Span)) { Label = label, LabelSpan = labelSpan, Value = value };
    }

    private Stmt ParseContinue()
    {
        var kw = _buffer.Advance();
        var (label, labelSpan) = ParseOptionalLabel();
        var semi = ExpectSemicolon();
        return new ContinueStmt(Span.Union(kw.Span, semi.Span)) { Label = label, LabelSpan = labelSpan };
    }

    /// <summary>'break outer;' — the identifier, when one stands before the ';'.</summary>
    private (string? label, Span span) ParseOptionalLabel()
    {
        if (!_buffer.Check(TokenKind.Identifier)) return (null, default);
        var tok = _buffer.Advance();
        return (_sm.Slice(tok.Span).ToString(), tok.Span);
    }

    private Stmt ParseReturn()
    {
        var kw = _buffer.Advance();
        Expr? value = _buffer.Check(TokenKind.Semicolon) ? null : ParseExpr(0);
        var semi = ExpectSemicolon();
        return new ReturnStmt(value, Span.Union(kw.Span, semi.Span));
    }

    private Stmt ParseYield()
    {
        var kw = _buffer.Advance();
        Expr? value = _buffer.Check(TokenKind.Semicolon) ? null : ParseExpr(0);
        var semi = ExpectSemicolon();
        return new YieldStmt(value, Span.Union(kw.Span, semi.Span));
    }

    // resume is an expression; 'resume co;' runs as an ExprStmt through ParseExprStmt.

    private Stmt ParseDefer()
    {
        var kw = _buffer.Advance();
        Stmt body;
        if (_buffer.Check(TokenKind.LBrace))
        {
            body = ParseBlock();
        }
        else
        {
            var expr = ParseExpr(0);
            var semi = ExpectSemicolon();
            body = new ExprStmt(expr, Span.Union(expr.Span, semi.Span));
        }
        return new DeferStmt(body, Span.Union(kw.Span, body.Span));
    }

    private Stmt ParseThrow()
    {
        var kw = _buffer.Advance();
        var value = ParseExpr(0);
        // 'throw e' as the tail of a value block: the arm's value is 'never' (§6.9), and the
        // block diverges — the same thing 'throw e;' says, in the position a tail stands in.
        if (_allowTail && _buffer.Check(TokenKind.RBrace))
        {
            var span = Span.Union(kw.Span, Whole(value));
            return new TailExprStmt(new ThrowExpr(value, span), span);
        }
        var semi = ExpectSemicolon();
        return new ThrowStmt(value, Span.Union(kw.Span, semi.Span));
    }

    private Stmt ParseTry()
    {
        var kw = _buffer.Advance(); // try
        var body = ParseBlock();
        var catches = new List<CatchClause>();
        while (_buffer.Check(TokenKind.Catch))
            catches.Add(ParseCatch());
        // A block without a clause is the sema's to refuse (SEM0036): one rule, one diagnostic.
        var end = catches.Count > 0 ? catches[^1].Span : body.Span;
        return new TryStmt(body, catches.ToArray(), Span.Union(kw.Span, end));
    }

    /// <summary>A clause: <c>catch (e: T) { … }</c>. In the expression form (08 Y4) the body is a
    /// value block, or an expression standing for one — <c>catch (e) v</c> is <c>catch (e) { v }</c>
    /// with the braces left off.</summary>
    private CatchClause ParseCatch(bool expressionForm = false)
    {
        var kw = _buffer.Advance(); // catch
        _buffer.Expect(TokenKind.LParen, "LYR-PAR0019", "expected '(' after 'catch'");
        // CatchBinding: '_' | IDENTIFIER ':' TypeExpr | IDENTIFIER 'in' TypeList | IDENTIFIER
        // ('_' is an identifier)
        var idTok = _buffer.Expect(TokenKind.Identifier, "LYR-PAR0020",
            $"expected catch binding, got {_buffer.Current.TokenKind}");
        var text = _sm.Slice(idTok.Span).ToString();
        string? name = text == "_" ? null : text; // '_' binds nothing
        TypeNode? type = null;
        TypeNode[] set = [];
        if (_buffer.Match(TokenKind.Colon))
        {
            type = ParseType();
            // 'catch (e: A, B)' meant the set form: said so, and read as it.
            if (_buffer.Check(TokenKind.Comma))
            {
                set = ParseCaughtList(type, colon: true);
                type = null;
            }
        }
        else if (_buffer.Match(TokenKind.In)) set = ParseCaughtSet();
        _buffer.Expect(TokenKind.RParen, "LYR-PAR0008", "expected ')' after catch binding");
        if (expressionForm && !_buffer.Check(TokenKind.LBrace))
        {
            // A value position: a struct initializer may stand here, as on the right of an '='.
            var value = ParseSubExpr();
            var tail = new Block([new TailExprStmt(value, Whole(value))], Whole(value));
            return new CatchClause(name, type, tail, Span.Union(kw.Span, Whole(value)))
                { NameSpan = idTok.Span, ExpressionBody = true, BindingTypes = set };
        }
        var body = ParseBlock(valueBlock: expressionForm);
        return new CatchClause(name, type, body, Span.Union(kw.Span, body.Span))
            { NameSpan = idTok.Span, BindingTypes = set };
    }

    /// <summary>The types after a clause's <c>in</c> (05 E9 C5, 08 Y6): one type, or a bracketed
    /// list — the list rule (D5/D6), as for <c>throws</c>.</summary>
    private TypeNode[] ParseCaughtSet()
    {
        if (!_buffer.Check(TokenKind.LBracket))
        {
            var first = ParseType();
            return _buffer.Check(TokenKind.Comma) ? ParseCaughtList(first, colon: false) : [first];
        }
        var open = _buffer.Advance();
        var listed = new List<TypeNode>();
        while (!_buffer.Check(TokenKind.RBracket) && !_buffer.AtEnd)
        {
            listed.Add(ParseType());
            if (!_buffer.Match(TokenKind.Comma)) break;
        }
        var close = _buffer.Expect(TokenKind.RBracket, "LYR-PAR0004", "expected ']' to close the caught types");
        if (listed.Count == 0)
            _de.Report("LYR-PAR0049", Severity.Error, Span.Union(open.Span, close.Span),
                "a 'catch' list names its types — 'catch (e)' is the clause that takes everything");
        return listed.ToArray();
    }

    /// <summary>Several caught types without the brackets: the list rule's error, with the form to
    /// write, parsed as the set they meant.</summary>
    private TypeNode[] ParseCaughtList(TypeNode first, bool colon)
    {
        var types = new List<TypeNode> { first };
        while (_buffer.Match(TokenKind.Comma)) types.Add(ParseType());
        var written = string.Join(", ", types.Select(t => _sm.Slice(t.Span).ToString()));
        _de.Report("LYR-PAR0049", Severity.Error, Span.Union(first.Span, types[^1].Span), colon
            ? $"several caught types are the set form — write 'in [{written}]'"
            : $"several caught types are a list — write 'in [{written}]'");
        return types.ToArray();
    }

    private Stmt ParseExprStmt()
    {
        // No struct initializer at the start of a statement: 'Foo { … };' would otherwise be
        // ambiguous with a block. In value positions (bindings, arguments) it stays allowed.
        var saved = _allowStructInit;
        _allowStructInit = false;
        var expr = ParseExpr(0);
        _allowStructInit = saved;
        // In a value block an expression followed by the closing brace is the block's tail: the
        // one place a missing ';' is not an error (it was one before 4.5, so nothing changes
        // meaning). A tail struct initializer needs parentheses, like any statement-first one.
        //
        // BEFORE the trailing-lambda rule below, because it is the narrower question: '_allowTail'
        // holds only inside a value block, and there the last expression IS the value — including
        // when it is written as a trailing lambda.
        if (_allowTail && _buffer.Check(TokenKind.RBrace))
            return new TailExprStmt(expr, Whole(expr));

        // 'xs.forEach { println(it); }' — a statement that ends in a trailing lambda's '}'
        // needs no ';', as a block arm of a match needs no ','. One may still stand there.
        if (expr is CallExpr { Arguments: [.., LambdaExpr { Form: LambdaForm.Trailing }] } && !_buffer.Check(TokenKind.Semicolon))
            return new ExprStmt(expr, Whole(expr));

        var semi = ExpectSemicolon();
        return new ExprStmt(expr, Span.Union(expr.Span, semi.Span));
    }


    private Token ExpectSemicolon() =>
        _buffer.Expect(TokenKind.Semicolon, "LYR-PAR0016", "expected ';'");
}
