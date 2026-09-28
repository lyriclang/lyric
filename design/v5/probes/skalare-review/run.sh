#!/bin/bash
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
cd "$(dirname "$0")"
for f in "$@"; do
  echo "##### $f"
  if dotnet "$LYRC" build "$f.lyr" -o "$f.lyrbc" 2>&1; then
    timeout 20 dotnet "$LYRVM" run "$f.lyrbc" 2>&1; echo "[exit $?]"
  else
    echo "[compile failed]"
  fi
done
