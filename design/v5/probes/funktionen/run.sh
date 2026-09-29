#!/bin/sh
# usage: run.sh <name>
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
for n in "$@"; do
  echo "===== $n ====="
  if dotnet "$LYRC" build "$n.lyr" -o "$n.lyrbc" 2>&1; then
    dotnet "$LYRVM" run "$n.lyrbc" 2>&1
    echo "exit=$?"
  else
    echo "BUILD-FAILED"
  fi
done
