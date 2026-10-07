using Lyric.AST;
using Lyric.Core;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Ir.Lowering;

/// <summary>
/// Assigns a global slot to every module <c>let</c> and every <c>static let</c>.
///
/// <para>Unlike <see cref="TypeTable"/> and <see cref="ImportTable"/>, everything is collected UP
/// FRONT here rather than interned on demand. The reason is the init function: it has to fill every
/// slot, including one no program path ever reads. An unfilled slot would be a value without a value,
/// and the language has none.</para>
///
/// <para>The order is dependency order across modules — every module after the ones it imports —
/// then declaration order within one, and therefore deterministic. It is at the same time the
/// INITIALIZATION ORDER: a global may use one initialized earlier, which is anything its own module
/// declared before it and anything from a module it imports. The sema decides the same question on
/// the same order (<c>LYR-SEM0057</c>); one walk answers both, or they would drift.</para>
/// </summary>
internal sealed class GlobalTable
{
    private readonly Dictionary<GlobalSymbol, GlobalId> _assigned =
        new(ReferenceEqualityComparer.Instance);

    private readonly List<IrGlobal> _defs = new();

    /// <summary>The initializer per slot, in the same order; the source for the init function.</summary>
    private readonly List<(GlobalSymbol Symbol, BindingStmt Binding, ModuleSymbol Module)> _pending = new();

    public List<IrGlobal> Defs => _defs;

    public IReadOnlyList<(GlobalSymbol Symbol, BindingStmt Binding, ModuleSymbol Module)> Pending
        => _pending;

    public bool IsEmpty => _defs.Count == 0;

    /// <summary>
    /// Collects all globals of a compilation. Runs BEFORE the function lowering: a function body may
    /// read a global that stands later in the source.
    /// </summary>
    public void Collect(Compilation compilation, TypeResult types, TypeTable typeTable)
    {
        foreach (var module in compilation.InitializationOrder())
        {
            // Native modules are NOT skipped. "They only declare signatures" holds for bodyless 'fn',
            // but a 'pub let pi: float = 3.14…' has a value, and that has to go into the Globals section
            // like any other.

            foreach (var decl in compilation.AstOf(module).Declarations)
            {
                switch (decl)
                {
                    case GlobalBindingDecl global
                        when module.Members.LookupLocal(global.Binding.Name) is GlobalSymbol symbol:
                        Add(symbol, global.Binding, module, symbol.Name, types, typeTable);
                        break;

                    // A 'static let' on a type is the same mechanism; only the name carries the type, so
                    // 'Player.MAX' and 'Wall.MAX' do not collide.
                    case ClassDecl or StructDecl or EnumDecl:
                        CollectStatics(decl, module, types, typeTable);
                        break;

                    // A block's constant (05 §6 rule 2) is its type's, at the block's place. A
                    // generic block's is one per instance of the block's parameters: folded.
                    case ExtendDecl ext
                        when compilation.Extensions.Blocks.FirstOrDefault(b => ReferenceEquals(b.Decl, ext)) is { Target: { } target } block:
                        foreach (var sb in ext.Statics)
                            if (block.MethodScope.LookupLocal(sb.Binding.Name) is GlobalSymbol constant)
                            {
                                if (ext.Generics.Length > 0) AddFolded(constant, sb.Binding, target, block);
                                else AddStatic(constant, sb.Binding, module, $"{target.Name}.{constant.Name}", types, typeTable);
                            }
                        break;
                }
            }
        }
    }

    private void CollectStatics(Decl decl, ModuleSymbol module, TypeResult types,
        TypeTable typeTable)
    {
        var (typeName, members) = decl switch
        {
            ClassDecl c => (c.Name, c.Members),
            StructDecl v => (v.Name, v.Members),
            EnumDecl e => (e.Name, (Decl[])e.Statics),
            _ => (null, null),
        };

        if (typeName is null || members is null) return;
        if (module.Members.LookupLocal(typeName) is not TypeSymbol owner) return;

        foreach (var member in members)
        {
            if (member is not StaticBindingDecl binding) continue;
            if (owner.Members.LookupLocal(binding.Binding.Name) is not GlobalSymbol symbol) continue;

            // A generic type's constant is one per instance: folded where it is read.
            if (owner.Generics.Length > 0) AddFolded(symbol, binding.Binding, owner, null);
            else AddStatic(symbol, binding.Binding, module, $"{typeName}.{symbol.Name}", types, typeTable);
        }
    }

    /// <summary>
    /// A constant of a generic type or of a generic block (07 G2; the review's M8a-3): ONE PER
    /// INSTANCE, and no slot — it could have none, its type names the type's parameter
    /// (<c>static let none: ?T = null;</c>), and which instances a program has is known only
    /// when the lowering has found them. Its initializer is a constant by the sema's rule
    /// (<c>LYR-SEM0169</c>), so a read lowers it in place, under the instance the read names.
    /// </summary>
    private void AddFolded(GlobalSymbol symbol, BindingStmt binding, TypeSymbol owner, ExtensionBlock? block)
    {
        if (binding.Initializer is { } initializer) _folded.TryAdd(symbol, (initializer, owner, block));
    }

    private readonly Dictionary<GlobalSymbol, (Expr Initializer, TypeSymbol Owner, ExtensionBlock? Block)> _folded =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>The initializer of a constant that is one per instance, with the type — and for a
    /// generic block's the block — whose parameters it may name; <c>null</c> for any other.</summary>
    public (Expr Initializer, TypeSymbol Owner, ExtensionBlock? Block)? FoldedOf(GlobalSymbol symbol) =>
        _folded.TryGetValue(symbol, out var folded) ? folded : null;

    /// <summary>
    /// A <c>static let</c> whose initializer is a literal — <c>static let max: int8 = 127;</c> — is
    /// that literal wherever it is read, and has no slot: no line in the init function, nothing a
    /// program that never reads it carries (10 B5's number constants stood in every program as
    /// globals). Any other initializer, and every module <c>let</c>, keeps its slot and runs in order.
    /// </summary>
    private void AddStatic(GlobalSymbol symbol, BindingStmt binding, ModuleSymbol module, string name,
        TypeResult types, TypeTable typeTable)
    {
        if (_assigned.ContainsKey(symbol) || _constants.ContainsKey(symbol)) return;
        if (IsLiteral(binding.Initializer))
        {
            _constants[symbol] = (binding.Initializer!, typeTable.Lower(types.TypeOfGlobal(symbol), binding.Span));
            return;
        }
        if (FloatOfBits(binding.Initializer, types, typeTable.Lower(types.TypeOfGlobal(symbol), binding.Span)) is { } written)
        {
            _values[symbol] = written;
            return;
        }
        Add(symbol, binding, module, name, types, typeTable);
    }

    private readonly Dictionary<GlobalSymbol, (IrConstValue Value, IrType Type)> _values =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>The value a constant <c>static let</c> stands for where its initializer is no
    /// literal the language has but names one all the same — a float by its bits; <c>null</c> for
    /// any other.</summary>
    public (IrConstValue Value, IrType Type)? ValueOf(GlobalSymbol symbol) =>
        _values.TryGetValue(symbol, out var value) ? value : null;

    /// <summary>
    /// <c>float.fromBits(0x7FF0000000000000)</c> — the library's own <c>fromBits</c> of an integer
    /// literal — is the float those bits are. The language has no literal for an infinity or a
    /// NaN, so the library's <c>infinity</c> and <c>nan</c> are written this way, and were four
    /// globals with a call to initialize in every program. A NaN is taken only as the one quiet
    /// NaN a C compiler's own constant is: any other payload keeps its slot and its call.
    /// </summary>
    private static (IrConstValue Value, IrType Type)? FloatOfBits(Expr? initializer, TypeResult types, IrType type)
    {
        if (initializer is not CallExpr { Arguments: [IntLiteralExpr bits], Callee: MemberExpr { Member: "fromBits" } callee }) return null;
        if (types.RefOf(callee) is not FunctionSymbol { IsStatic: true, Home.FullName: "std.core" }) return null;
        switch (type)
        {
            case IrScalarType { Kind: IrScalar.F64 }:
            {
                var value = BitConverter.UInt64BitsToDouble(bits.Value);
                return double.IsNaN(value) && bits.Value != 0x7FF8000000000000UL ? null : (new FloatConst(value), type);
            }
            case IrScalarType { Kind: IrScalar.F32 } when bits.Value <= uint.MaxValue:
            {
                var value = BitConverter.UInt32BitsToSingle((uint)bits.Value);
                return float.IsNaN(value) && bits.Value != 0x7FC00000UL ? null : (new FloatConst(value), type);
            }
            default:
                return null;
        }
    }

    private readonly Dictionary<GlobalSymbol, (Expr Literal, IrType Type)> _constants =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>The literal a constant <c>static let</c> stands for, with its type; <c>null</c> for
    /// a global with a slot.</summary>
    public (Expr Literal, IrType Type)? ConstantOf(GlobalSymbol symbol) =>
        _constants.TryGetValue(symbol, out var constant) ? constant : null;

    private readonly HashSet<GlobalSymbol> _effectFree = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<int, (int Block, int Start, int End)> _initRanges = new();

    /// <summary>Whether a global's initializer does nothing a program could see but give its value
    /// (01 B13): such a global goes where nothing reads it (<see cref="GlobalPruning"/>).</summary>
    public bool IsEffectFree(GlobalSymbol symbol) => _effectFree.Contains(symbol);

    /// <summary>Where the init function computes and stores an effect-free global: its block and the
    /// range of its instructions, the store included.</summary>
    public void NoteInit(GlobalId id, BlockId block, int start, int end) =>
        _initRanges[id.Value] = (block.Value, start, end);

    /// <summary>The effect-free globals' ranges in the init function, by slot.</summary>
    public IReadOnlyDictionary<int, (int Block, int Start, int End)> InitRanges => _initRanges;

    /// <summary>
    /// An initializer that only gives its value (01 B13): a literal, <c>null</c>, another global
    /// read, or an array, a tuple, a struct or a class built of such — the type's field defaults
    /// included. Nothing that calls or computes: an operator may panic, a call may do anything.
    /// </summary>
    private static bool EffectFree(Expr? e, TypeResult types, int depth)
    {
        if (depth > 8) return false;
        switch (e)
        {
            case IntLiteralExpr or FloatLiteralExpr or BoolLiteralExpr or CharLiteralExpr or StringLiteralExpr
                or NullLiteralExpr:
                return true;
            case UnaryExpr { Operator: UnaryOp.Neg, Operand: IntLiteralExpr or FloatLiteralExpr }:
                return true;
            case IdentifierExpr id:
                var bound = types.RefOf(id);
                if (bound is ImportBindingSymbol import) bound = import.Target;
                return bound is GlobalSymbol;
            case ArrayLitExpr array:
                return array.Elements.All(x => EffectFree(x, types, depth + 1));
            case TupleLitExpr tuple:
                return tuple.Elements.All(x => EffectFree(x, types, depth + 1));
            case StructInitExpr init:
                if (!init.Fields.All(f => EffectFree(f.Value, types, depth + 1))) return false;
                var members = TypeFacts.SymbolOf(types.TypeOf(init))?.Declaration switch
                {
                    StructDecl s => s.Members,
                    ClassDecl c => c.Members,
                    _ => null,
                };
                if (members is null) return false;
                foreach (var field in members.OfType<FieldDecl>())
                    if (field.Default is { } written && !Array.Exists(init.Fields, f => f.Name == field.Name)
                        && !EffectFree(written, types, depth + 1))
                        return false;
                return true;
            default:
                return false;
        }
    }

    private static bool IsLiteral(Expr? initializer) => initializer switch
    {
        IntLiteralExpr or FloatLiteralExpr or BoolLiteralExpr or CharLiteralExpr or StringLiteralExpr => true,
        UnaryExpr { Operator: UnaryOp.Neg, Operand: IntLiteralExpr or FloatLiteralExpr } => true,
        _ => false,
    };

    private void Add(GlobalSymbol symbol, BindingStmt binding, ModuleSymbol module, string name,
        TypeResult types, TypeTable typeTable)
    {
        if (_assigned.ContainsKey(symbol)) return;

        // Without an initializer the slot would stay empty. At module level the grammar allows 'let'
        // only, and a 'let' without a value cannot sensibly be filled in later: there is no later point
        // at which an assignment could stand.
        if (binding.Initializer is null)
            throw new UnsupportedConstructException(
                $"the constant '{name}' has no initializer; a module-level 'let' needs one",
                binding.Span);

        var type = typeTable.Lower(types.TypeOfGlobal(symbol), binding.Span);

        _assigned[symbol] = new GlobalId(_defs.Count);
        _defs.Add(new IrGlobal(name, type) { Module = module.FullName });
        _pending.Add((symbol, binding, module));
        if (EffectFree(binding.Initializer, types, 0)) _effectFree.Add(symbol);
    }

    /// <summary>
    /// A slot no declaration stands behind — the hidden result buffers of struct-returning
    /// natives live here. No entry in <see cref="Pending"/>: the initialization is injected as
    /// IR after the lowering, because the buffer is an object, not an expression.
    /// </summary>
    public GlobalId DeclareSynthetic(string name, IrType type)
    {
        var id = new GlobalId(_defs.Count);
        _defs.Add(new IrGlobal(name, type));
        return id;
    }

    /// <summary>The slot and type of a global. An unknown symbol is a lowering bug: collection was
    /// complete before the first function was lowered.</summary>
    public (GlobalId Id, IrType Type) Resolve(GlobalSymbol symbol, Span span)
    {
        if (_assigned.TryGetValue(symbol, out var id)) return (id, _defs[id.Value].Type);

        throw new UnsupportedConstructException(
            $"the constant '{symbol.Name}' was not collected (is it declared outside this compilation?)",
            span);
    }

    public GlobalId IdOf(GlobalSymbol symbol) => _assigned[symbol];
}
