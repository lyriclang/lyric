#!/bin/sh
# usage: ./run.sh name   (compiles name.lyr, runs if compile ok)
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
for n in "$@"; do
  echo "##### $n"
  dotnet "$LYRC" build "$n.lyr" -o "$n.lyrbc" 2>&1
  rc=$?
  echo "[build rc=$rc]"
  if [ $rc -eq 0 ]; then dotnet "$LYRVM" run "$n.lyrbc" 2>&1; echo "[run rc=$?]"; fi
done
