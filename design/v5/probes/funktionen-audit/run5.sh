#!/bin/bash
A="/c/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/funktionen-audit"
S="/c/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes"
V="$S/.."
R="/c/Users/Olivier/CLionProjects/lyric"
LYRC="$R/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="$R/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
cd "$A"; mkdir -p out
codes() { grep -oE "LYR-[A-Z]+[0-9]+" | sort | uniq -c | tr '\n' ' '; }
cp "$S/funktionen-rev2/q11_lambda_yield.lyr" c_q11_lambda_yield.lyr; cp "$S/funktionen-rev2/q11c_lambda_coroutine_type.lyr" c_q11c_lambda_coroutine_type.lyr
for f in a30b_trailing_plus_positional c_q11_lambda_yield c_q11c_lambda_coroutine_type; do
  b=$(dotnet "$LYRC" build "$f.lyr" -o "out/$f.lyrbc" --progress never 2>&1 >/dev/null); x=$?; echo "### $f build=$x $(echo "$b" | codes)"; echo "$b" | grep -aE "error\[|warning\[" | head -3 | cut -c1-230
  [ $x -eq 0 ] && echo "  run: $(dotnet "$LYRVM" run "out/$f.lyrbc" 2>&1 | tr '\n' '|')"
done
echo "--- q11c source:"; cat c_q11c_lambda_coroutine_type.lyr
echo "##### stdlib/examples params (recursive)"; grep -rn "params " "$R/stdlib" "$R/examples" --include=*.lyr | head -8; echo "(count: $(grep -rn 'params ' "$R/stdlib" "$R/examples" --include=*.lyr | wc -l))"
echo "##### assignment-as-value candidates"; grep -rnE "[(,]\s*[a-z][a-zA-Z0-9_]*\s*=[^=>]" "$R/examples" "$R/stdlib" "$R/stdlib-tests" --include=*.lyr 2>/dev/null | grep -vE "\{|^\S+:\s*(pub |static |mut )*fn |\bfor \(" | head -8; echo "(count: $(grep -rnE '[(,]\s*[a-z][a-zA-Z0-9_]*\s*=[^=>]' "$R/examples" "$R/stdlib" "$R/stdlib-tests" --include=*.lyr 2>/dev/null | grep -vE '\{|^\S+:\s*(pub |static |mut )*fn |\bfor \(' | wc -l))"
echo "##### STATUS overloading decision"; grep -n "verloading" "$R/STATUS.md" | grep -i "decid\|capab\|convenien\|rules" | head -4
echo "##### LSP AnalysisService stages"; grep -n "Parse\|Sema\|TypeCheck\|Lower\|Compile\|Frontend\." "$R/src/Lyric.Lsp/Analysis/AnalysisService.cs" | head -12
echo "##### CHANGELOG SEM0081 / until"; grep -n "SEM0081\|until =" "$R/CHANGELOG.md" | head -3
echo "##### v5-design: SPEC-RUNDE / lyric-v5-features refs"; ls "$V/parts" "$V/review" 2>/dev/null | head -20; grep -rl "SPEC-RUNDE" "$V" --include=*.md --include=*.txt 2>/dev/null | head -5; find "$V" "$R/docs" -iname "*v5-features*" 2>/dev/null | head; grep -rn "SPEC-RUNDE" "$V/funktionen.md" | head -3
echo "##### Interpreter nested-run constant"; sed -n '176,182p' "$R/src/Lyric.Vm/Interpreter.cs"
echo "##### PLAN.md header of table B (is it marked done?)"; sed -n '96,99p' "$R/docs/Befunde_und_Verbesserungen/PLAN.md"; grep -n "^### B\|^## \|B\. Prozessabbr\|erledigt.*B\b" "$R/docs/Befunde_und_Verbesserungen/PLAN.md" | head -12
