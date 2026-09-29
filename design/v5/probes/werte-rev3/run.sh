#!/bin/sh
# usage: run.sh name   (name.lyr in this dir)
D="$(cd "$(dirname "$0")" && pwd)"
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
for n in "$@"; do
  echo "=== $n"
  dotnet "$LYRC" build "$D/$n.lyr" -o "$D/$n.lyrbc" 2>&1
  echo "[build exit $?]"
  if [ -f "$D/$n.lyrbc" ]; then
    dotnet "$LYRVM" run "$D/$n.lyrbc" 2>&1
    echo "[run exit $?]"
  fi
done
