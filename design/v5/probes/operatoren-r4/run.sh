#!/bin/sh
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
cd "C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/operatoren-r4"
for f in "$@"; do
  echo "===== $f"
  out="${f%.lyr}.lyrbc"
  rm -f "$out"
  dotnet "$LYRC" build "$f" -o "$out" 2>&1
  echo "[build exit $?]"
  if [ -f "$out" ]; then
    dotnet "$LYRVM" run "$out" 2>&1
    echo "[run exit $?]"
  fi
done
