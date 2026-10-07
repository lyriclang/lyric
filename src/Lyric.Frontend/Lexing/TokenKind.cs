namespace Lyric.Lexing;

public enum TokenKind
{
    Eof,
    BadChar,
    Identifier,
    AtIdentifier,
    AtLBracket,
    
    // Braces
    LParen,
    RParen,
    LBrace,
    RBrace,
    LBracket,
    RBracket,

    // Module
    Module,
    Import,
    As,
    Pub,

    /// <summary>A declaration of its module alone (design/v5/spec/07 V2).</summary>
    Private,

    /// <summary>A declaration of its package — the default the word only says out loud (07 V2).</summary>
    Internal,

    // Type declarations
    Struct,
    Class,
    Enum,
    Interface,
    Extend,

    // Function / binding
    Fn,
    Mut,

    /// <summary>A member without a receiver. Only allowed inside a struct or class body.</summary>
    Static,

    Let,
    Var,
    Params,

    // Control flow
    If,
    Else,
    While,
    Do,
    For,
    In,
    Is,
    Match,

    // Jumps
    Break,
    Continue,
    Return,
    Yield,
    Defer,

    // Exceptions
    Try,
    Catch,
    Throw,

    // Literals
    True,
    False,
    Null,
    IntLiteral ,     // all bases: dec, hex, bin, oct, with or without an integer suffix
    FloatLiteral,    // decimal with a '.', with an exponent, or with a float suffix
    StringLiteral,
    CharLiteral,
    
    // FStrings
    FStringStart,       // f"
    FStringChunk,       // a plain-text span between specials
    FStringInterpStart, // { in f-String
    FStringInterpEnd,   // the } that closes an interpolation
    FStringFormatSpec,  // the span between ':' and '}'
    FStringEnd,         // the closing quote

    // Operators
    //Punctuation
    Comma,
    Dot,
    Semicolon,
    Colon,
    ColonColon,
    Arrow,
    FatArrow,
    
    //Optional/Nullable
    Question,
    QuestionDot,
    QuestionQuestion,
    Exclamation, // prefix (logical not) and postfix (unwrap); the parser disambiguates
    
    //Arithmetic
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    PlusPercent, // +%  wrapping add (design/v5/spec/08 Y4)
    MinusPercent, // -%
    StarPercent, // *%
    Inc, //++
    Dec, //--
    
    //Bitwise
    Amp, //&
    Pipe, //|
    Caret, //^
    Tilde, //~
    Shl, //<<
    Shr, // >>
    
    //Comparison
    EqualEqual, // ==
    ExclamationEqual, // !=
    NotIn, // !in — one token where 'in' follows as a word of its own (08 Y-table)
    Less, // <
    LessEqual, // <=
    Greater, // >
    GreaterEqual, // >=
    
    //Logical
    AmpAmp, // &&
    PipePipe, // ||
    
    //Range 
    DotDot, //..
    DotDotDot, //... — a variadic parameter's mark (design/v5/spec/08)
    DotDotEqual, //..= 
    
    //Assignment
    Equal, // =
    PlusEqual, // +=
    MinusEqual, // -=
    StarEqual, // *=
    SlashEqual, // /=
    PercentEqual, // %=
    PlusPercentEqual, // +%=
    MinusPercentEqual, // -%=
    StarPercentEqual, // *%=
    AmpEqual, // &=
    PipeEqual, // |=
    CaretEqual, // ^=
    ShlEqual, // <<=
    ShrEqual, // >>=
    AmpAmpEqual, // &&=
    PipePipeEqual, // ||=
    QuestionQuestionEqual, // ??=
    
    // This
    This,

    // Doc comments
    DocComment
}
