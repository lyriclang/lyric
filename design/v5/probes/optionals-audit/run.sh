#!/bin/bash
# usage: run.sh file.lyr [file2.lyr ...]  -- compiles into optionals-audit/, runs if compiled
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
OUT="/c/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/optionals-audit"
for f in "$@"; do
  b=$(basename "$f" .lyr)
  echo "===== $b"
  dotnet "$LYRC" build "$f" -o "$OUT/$b.lyrbc" 2>&1
  bc=$?
  echo "[build exit $bc]"
  if [ $bc -eq 0 ]; then
    dotnet "$LYRVM" run "$OUT/$b.lyrbc" 2>&1
    echo "[run exit $?]"
  fi
done
