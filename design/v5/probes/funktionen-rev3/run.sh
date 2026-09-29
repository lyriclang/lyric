#!/bin/bash
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
for p in "$@"; do
  echo "=== $p"
  dotnet "$LYRC" build "$p.lyr" -o "$p.lyrbc" 2>&1 | grep -E "^(error|warning)|LYR-|ok" | head -20
  bc=${PIPESTATUS[0]}
  if [ -f "$p.lyrbc" ]; then dotnet "$LYRVM" run "$p.lyrbc" 2>&1 | head -12; echo "[exit $?]"; fi
done
