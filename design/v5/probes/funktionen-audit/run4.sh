#!/bin/bash
A="/c/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/funktionen-audit"
S="/c/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes"
R="/c/Users/Olivier/CLionProjects/lyric"
LYRC="$R/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="$R/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
cd "$A"; mkdir -p out
mainonly() { awk '/^fn main\.main /{p=1} p{print} p&&/^}/{exit}'; }
codes() { grep -oE "LYR-[A-Z]+[0-9]+" | sort | uniq -c | tr '\n' ' '; }
echo "##### raw bytes of a diagnostic line"
dotnet "$LYRC" build n17_bare_no_context.lyr -o out/x.lyrbc --progress never 2>&1 >/dev/null | head -c 120 | od -c | head -4
echo "##### message texts (captured, then grep -a)"
for f in n08_global_in_lambda n17_bare_no_context n18_paren_untyped c_q02_outer_it c_q27_default_refs_param c_q12_default_before_trailing c_q21_overload_lambda c_q22_method_value; do
  b=$(dotnet "$LYRC" build "$f.lyr" -o out/x.lyrbc --progress never 2>&1 >/dev/null); echo "--- $f"; echo "$b" | grep -aE "error\[|warning\[" | head -3 | cut -c1-230
done
echo "##### new probes"
for f in a05b_iface_default_only_iface a05c_iface_default_only_impl a05d_iface_default_differ a30_trailing_before_default; do
  b=$(dotnet "$LYRC" build "$f.lyr" -o "out/$f.lyrbc" --progress never 2>&1 >/dev/null); x=$?; echo "### $f build=$x $(echo "$b" | codes)"; echo "$b" | grep -aE "error\[|warning\[" | head -3 | cut -c1-230
  [ $x -eq 0 ] && echo "  run: $(dotnet "$LYRVM" run "out/$f.lyrbc" 2>&1 | tr '\n' '|')"
done
echo "##### round-1 controls: p43 p63 p36 p20 p34 p66 q17"
for f in p43_overload_default p63_overload_generic p36_arg_order p20_closure_capture p34_var_recursive p66_curry; do cp "$S/funktionen/$f.lyr" "c_$f.lyr"; done
cp "$S/funktionen-rev2/q17_iface_devirt.lyr" c_q17_iface_devirt.lyr
for f in c_p43_overload_default c_p63_overload_generic c_p36_arg_order c_p34_var_recursive c_p66_curry c_q17_iface_devirt; do
  b=$(dotnet "$LYRC" build "$f.lyr" -o "out/$f.lyrbc" --progress never 2>&1 >/dev/null); x=$?; echo "### $f build=$x $(echo "$b" | codes) run: $([ $x -eq 0 ] && dotnet "$LYRVM" run "out/$f.lyrbc" 2>&1 | head -5 | tr '\n' '|')"
done
echo "--- p20 debug lower: cell and env"; dotnet "$LYRC" lower c_p20_closure_capture.lyr --progress never 2>&1 | mainonly | grep -nE "cell|env|newobj|mkclosure|stfld|ldfld|storefield|loadfield" | head -8
echo "--- q17 release main: callvirt count"; dotnet "$LYRC" lower c_q17_iface_devirt.lyr --release --progress never 2>&1 | mainonly | grep -cE "callvirt"
echo "--- q17 debug main: callvirt count"; dotnet "$LYRC" lower c_q17_iface_devirt.lyr --progress never 2>&1 | mainonly | grep -cE "callvirt"
echo "##### repo facts"
echo "--- stdlib params signatures:"; grep -rnE "params " "$R/stdlib/std/"*.lyr | grep -E "= *[^=]" | head -5; echo "(params total: $(grep -rcE 'params ' "$R/stdlib/std/"*.lyr | awk -F: '{s+=$2} END{print s}'))"
echo "--- stdlib params with a default BEFORE it (same line):"; grep -rnE "fn [a-zA-Z]+\(.*=.*params " "$R/stdlib/std/"*.lyr | head -5; echo "(count: $(grep -rcE 'fn [a-zA-Z]+\(.*=.*params ' "$R/stdlib/std/"*.lyr | awk -F: '{s+=$2} END{print s}'))"
echo "--- assignment used as value, rough grep over examples/stdlib/stdlib-tests:"; grep -rnE "\(\s*[a-zA-Z_][a-zA-Z0-9_.]*\s*=\s*[^=]" "$R/examples" "$R/stdlib" "$R/stdlib-tests" --include=*.lyr 2>/dev/null | grep -vE "^\S+:\s*(fn|pub fn|static fn|mut fn)|\bfor\b|\bif\b|\bwhile\b|\{[^}]*=|\b(let|var)\b.*\(.*\{" | head -8; echo "(rough count: $(grep -rnE '\(\s*[a-zA-Z_][a-zA-Z0-9_.]*\s*=\s*[^=]' "$R/examples" "$R/stdlib" "$R/stdlib-tests" --include=*.lyr 2>/dev/null | grep -vE '^\S+:\s*(pub )?(static )?(mut )?fn|\bfor\b|\{[^}]*=' | wc -l))"
echo "--- STATUS: guide is part of the feature / overloading decision:"; grep -n "GUIDE is part of the feature\|guide is part of the feature" "$R/STATUS.md" | head -2; grep -n "a capability, not a convenience\|eine Fähigkeit, kein Komfort" "$R/STATUS.md" | head -2
echo "--- CHANGELOG: until field:"; grep -n "until" "$R/CHANGELOG.md" | head -3
echo "--- v5 files in scratchpad:"; ls "$S/.." | head -30; ls "$S/../SPEC-RUNDE"* 2>/dev/null | head
echo "--- LSP lowering:"; grep -rln "Lower" "$R/src/Lyric.Lsp" | head -5; grep -rn "Lower\|Emit" "$R/src/Lyric.Lsp/Analysis/DiagnosticsProvider.cs" 2>/dev/null | head -5; ls "$R/src/Lyric.Lsp/Analysis/" | head -20
echo "--- Lyrc Program.cs 244-252:"; sed -n '244,252p' "$R/src/Lyrc/Program.cs"
