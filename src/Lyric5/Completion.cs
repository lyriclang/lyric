using System.Text;

namespace Lyric5;

/// <summary>
/// The completion scripts of the shells (spec 14 §7), made from <see cref="Verbs"/>: the verbs at the
/// first word, then the verb's options and the options every verb takes. They complete names only;
/// a value is the shell's own to complete (a file, a directory). Registered for <c>lyric5</c> and
/// <c>lyric</c>, the name the binary takes at the release.
/// </summary>
internal static class Completion
{
    private static readonly string[] Names = ["lyric5", "lyric"];

    public static string? For(string shell) => shell switch
    {
        "bash" => Bash(),
        "zsh" => Zsh(),
        "fish" => Fish(),
        "pwsh" => Pwsh(),
        _ => null,
    };

    /// <summary>Every spelling of a verb's options: each name and alias, a field's <c>--no-</c> form.</summary>
    private static IEnumerable<string> Spellings(IEnumerable<Opt> options) =>
        options.SelectMany(o => new[] { o.Name, o.Alias, o.Negatable ? "--no-" + o.Name[2..] : null }).OfType<string>();

    /// <summary>The options every verb takes that this one does: <c>--json</c> and the lint options
    /// where the table marks it.</summary>
    private static IEnumerable<Opt> GlobalFor(VerbSpec verb) =>
        Verbs.Global.Where(o => o.Name is not ("--json" or "-A" or "-W" or "-D" or "--warn-deps")
                                || verb.Json && o.Name == "--json"
                                || verb.Lints && o.Name is "-A" or "-W" or "-D" or "--warn-deps");

    private static IEnumerable<string> OptionsOf(VerbSpec verb)
    {
        return Spellings(verb.Options.Concat(GlobalFor(verb))).Distinct();
    }

    private static string Bash()
    {
        var text = new StringBuilder();
        text.Append("# lyric completions for bash: eval \"$(lyric5 completions bash)\"\n");
        text.Append("_lyric5() {\n");
        text.Append("    local cur=\"${COMP_WORDS[COMP_CWORD]}\" opts\n");
        text.Append("    if [ \"$COMP_CWORD\" -eq 1 ]; then\n");
        text.Append($"        COMPREPLY=($(compgen -W \"{string.Join(' ', Verbs.All.Select(v => v.Name))}\" -- \"$cur\"))\n");
        text.Append("        return\n");
        text.Append("    fi\n");
        text.Append("    case \"${COMP_WORDS[1]}\" in\n");
        foreach (var verb in Verbs.All)
            text.Append($"        {verb.Name}) opts=\"{string.Join(' ', OptionsOf(verb))}\" ;;\n");
        text.Append("        *) opts=\"\" ;;\n");
        text.Append("    esac\n");
        text.Append("    if [[ \"$cur\" == -* ]]; then\n");
        text.Append("        COMPREPLY=($(compgen -W \"$opts\" -- \"$cur\"))\n");
        text.Append("    else\n");
        text.Append("        COMPREPLY=($(compgen -f -- \"$cur\"))\n");
        text.Append("    fi\n");
        text.Append("}\n");
        text.Append($"complete -F _lyric5 {string.Join(' ', Names)}\n");
        return text.ToString();
    }

    private static string Zsh()
    {
        var text = new StringBuilder();
        text.Append($"#compdef {string.Join(' ', Names)}\n");
        text.Append("# lyric completions for zsh: source <(lyric5 completions zsh), after compinit\n");
        text.Append("_lyric5() {\n");
        text.Append("    if (( CURRENT == 2 )); then\n");
        text.Append($"        compadd -- {string.Join(' ', Verbs.All.Select(v => v.Name))}\n");
        text.Append("        return\n");
        text.Append("    fi\n");
        text.Append("    case \"$words[2]\" in\n");
        foreach (var verb in Verbs.All)
            text.Append($"        {verb.Name}) compadd -- {string.Join(' ', OptionsOf(verb))} ;;\n");
        text.Append("    esac\n");
        text.Append("    _files\n");
        text.Append("}\n");
        text.Append($"compdef _lyric5 {string.Join(' ', Names)}\n");
        return text.ToString();
    }

    private static string Quote(string text) => "'" + text.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    private static string Fish()
    {
        var text = new StringBuilder();
        text.Append("# lyric completions for fish: lyric5 completions fish | source\n");
        foreach (var name in Names)
        {
            foreach (var verb in Verbs.All)
                text.Append($"complete -c {name} -f -n '__fish_use_subcommand' -a {verb.Name} -d {Quote(verb.Summary)}\n");
            foreach (var verb in Verbs.All)
            {
                foreach (var option in verb.Options.Concat(GlobalFor(verb)))
                {
                    var spelled = new List<string>();
                    foreach (var s in new[] { option.Name, option.Alias }.OfType<string>())
                        spelled.Add(s.StartsWith("--", StringComparison.Ordinal) ? $"-l {s[2..]}" : $"-s {s[1..]}");
                    if (option.Negatable) spelled.Add($"-l no-{option.Name[2..]}");
                    text.Append($"complete -c {name} -n '__fish_seen_subcommand_from {verb.Name}' {string.Join(' ', spelled)}"
                                + (option.Value is null ? "" : " -r") + $" -d {Quote(option.Help)}\n");
                }
            }
        }
        return text.ToString();
    }

    private static string Pwsh()
    {
        var text = new StringBuilder();
        text.Append("# lyric completions for PowerShell: lyric5 completions pwsh | Out-String | Invoke-Expression\n");
        text.Append("$lyricOptions = @{\n");
        foreach (var verb in Verbs.All)
            text.Append($"    '{verb.Name}' = @({string.Join(", ", OptionsOf(verb).Select(o => $"'{o}'"))})\n");
        text.Append("}\n");
        text.Append($"Register-ArgumentCompleter -Native -CommandName {string.Join(", ", Names)} -ScriptBlock {{\n");
        text.Append("    param($wordToComplete, $commandAst, $cursorPosition)\n");
        text.Append("    $words = @($commandAst.CommandElements | ForEach-Object { $_.ToString() })\n");
        text.Append("    if ($words.Count -le 1 -or ($words.Count -eq 2 -and $wordToComplete -ne '')) {\n");
        text.Append($"        $candidates = @({string.Join(", ", Verbs.All.Select(v => $"'{v.Name}'"))})\n");
        text.Append("    } else {\n");
        text.Append("        $candidates = $lyricOptions[$words[1]]\n");
        text.Append("    }\n");
        text.Append("    $candidates | Where-Object { $_ -like \"$wordToComplete*\" } |\n");
        text.Append("        ForEach-Object { [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_) }\n");
        text.Append("}\n");
        return text.ToString();
    }
}
