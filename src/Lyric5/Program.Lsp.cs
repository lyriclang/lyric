using Lyric.Lsp;

namespace Lyric5;

public static partial class Program
{
    /// <summary><c>lsp</c>: the language server, speaking LSP on the standard input and output until
    /// the editor says <c>exit</c>; its exit code is the protocol's — 0 after a <c>shutdown</c>, 1
    /// without one.</summary>
    private static int Lsp(string[] args)
    {
        if (Verbs.Read(Verbs.Find("lsp")!, args, Unknown, out var unread) is not { } parsed) return unread;
        if (parsed.Words.Count > 0) return Unknown($"'lsp' takes no file, got '{parsed.Words[0]}'");
        if (parsed.Rest is not null) return Unknown("unknown option '--' for 'lsp'");

        using var server = new LspServer(Console.OpenStandardInput(), Console.OpenStandardOutput());
        return server.RunAsync().GetAwaiter().GetResult();
    }
}
