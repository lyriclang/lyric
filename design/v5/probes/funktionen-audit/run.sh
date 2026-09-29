#!/bin/bash
# Runner for the funktionen-audit probes. Builds each .lyr (debug), runs it, prints a compact report.
A="/c/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/funktionen-audit"
LYRC="/c/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="/c/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
mkdir -p "$A/out"
cd "$A"
codes() { grep -oE "LYR-[A-Z]+[0-9]+" | sort | uniq -c | tr '\n' ' '; }
for src in "$A"/*.lyr; do
  name=$(basename "$src" .lyr)
  case "$name" in t_*) continue;; esac
  out="$A/out/$name.lyrbc"
  berr=$(dotnet "$LYRC" build "$src" -o "$out" 2>&1 >/dev/null); bx=$?
  echo "### $name | build=$bx | $(echo "$berr" | codes)"
  if [ $bx -ne 0 ]; then
    echo "$berr" | grep -E "^(error|warning)" | head -4 | cut -c1-200
    continue
  fi
  echo "$berr" | grep -E "^warning" | head -2 | cut -c1-200
  rout=$(dotnet "$LYRVM" run "$out" 2>"$A/out/$name.err"); rx=$?
  echo "  run=$rx | out: $(echo "$rout" | head -6 | tr '\n' '|') | err: $(cat "$A/out/$name.err" | codes) lines=$(wc -l < "$A/out/$name.err")"
  head -2 "$A/out/$name.err" | cut -c1-200
done
