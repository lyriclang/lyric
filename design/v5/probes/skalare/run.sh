#!/bin/sh
# usage: run.sh <name>
D="C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/skalare"
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
echo "=== $1 ==="
dotnet "$LYRC" build "$D/$1.lyr" -o "$D/$1.lyrbc" 2>&1
rc=$?
if [ $rc -ne 0 ]; then echo "[compile exit $rc]"; exit 0; fi
dotnet "$LYRVM" run "$D/$1.lyrbc" 2>&1
echo "[run exit $?]"
