using Lyric.AST;
using Lyric.Compiler;
using Lyric.Core;
using Lyric.Lsp.Documents;
using Lyric.Lsp.Protocol;
using Lyric.Resolver;
using Lyric.Sema;

namespace Lyric.Lsp.Analysis;

/// <summary>
/// The type the inference gave a binding that names none (<c>let total = …</c>), plus concise
/// parameter-name hints on multi-argument calls whose arguments are not self-explanatory.
///
/// <para>Off the last good analysis, like hover: a hint one edit behind is invisible, a hint
/// column that empties while the file does not parse flickers on every keystroke.</para>
/// </summary>
public static class InlayHintProvider
{
    public static IReadOnlyList<InlayHint> Of(
        Compiler.SemanticModel model, Module root, FileId file, SourceManager sources,
        Protocol.Range range)
    {
        var hints = new List<InlayHint>();
        var text = sources.GetText(file);
        var startOffset = TextOffsets.ToOffset(text, range.Start);
        var endOffset = range.End.Line == int.MaxValue ? text.Length : TextOffsets.ToOffset(text, range.End);

        Walk(root);
        return hints;

        void Walk(Node node)
        {
            if (node is not Module && node.Span.File == file
                && (node.Span.End < startOffset || node.Span.Start > endOffset))
                return;

            switch (node)
            {
                // A written annotation already says it; a binding without an initializer has
                // nothing inferred to show.
                case BindingStmt { Type: null, Initializer: not null } binding:
                    AddTypeHint(binding, binding.NameSpan);
                    break;

                // The loop variable never takes an annotation; the element type is always
                // inferred, which is exactly when a hint earns its place.
                case ForInStmt loop:
                    AddTypeHint(loop, loop.NameSpan);
                    break;

                case CallExpr { Arguments.Length: >= 2 } call when call.Span.File == file:
                    AddParameterHints(call);
                    break;
            }

            foreach (var child in AstChildren.Of(node)) Walk(child);
        }

        void AddTypeHint(Node declaration, Span nameSpan)
        {
            if (nameSpan.File != file) return;

            // The definite-assignment analysis bound the declaration to its own symbol; the
            // symbol carries the inferred type.
            if ((model.Types.RefOf(declaration)
                ?? model.Binding.Resolve(declaration)) is not LocalSymbol local) return;

            // ErrorType means "already reported here"; a hint would repeat the failure as though
            // it were an answer. The internal types are not nameable in the language.
            if (local.Type is Sema.ErrorType or NullType or NeverType) return;

            var position = SpanMapper.ToRange(sources, nameSpan).End;
            if (position.Line < range.Start.Line || position.Line > range.End.Line) return;

            var label = $": {TypeFacts.Display(local.Type)}";
            hints.Add(new InlayHint
            {
                Position = position,
                Label = label,
                Kind = InlayHintKind.Type,
                TextEdits = declaration is BindingStmt
                    ? [new TextEdit { Range = new Protocol.Range { Start = position, End = position }, NewText = label }]
                    : null,
            });
        }

        void AddParameterHints(CallExpr call)
        {
            var sym = model.Types.RefOf(call.Callee) ?? model.Binding.Resolve(call.Callee);
            if (sym is not null) sym = ReferenceProvider.Target(sym);
            if (sym is not FunctionSymbol { Declaration: FunctionDecl fn } || fn.Parameters.Length < 2) return;

            for (var i = 0; i < call.Arguments.Length && i < fn.Parameters.Length; i++)
            {
                if (call.ArgumentNames is { } names && i < names.Length && names[i] is not null)
                    continue;

                var paramName = fn.Parameters[i].Name;
                if (string.IsNullOrEmpty(paramName)) continue;

                var arg = call.Arguments[i];
                if (arg.Span.File != file) continue;
                if (arg is IdentifierExpr id && string.Equals(id.Name, paramName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var pos = SpanMapper.ToRange(sources, arg.Span).Start;
                if (pos.Line < range.Start.Line || pos.Line > range.End.Line) continue;

                hints.Add(new InlayHint
                {
                    Position = pos,
                    Label = $"{paramName}:",
                    Kind = InlayHintKind.Parameter,
                    PaddingRight = true,
                });
            }
        }
    }
}
