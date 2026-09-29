#!/bin/bash
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
for f in "$@"; do
  echo "######## $f"
  dotnet "$LYRC" build "$f" -o "${f%.lyr}.lyrbc" 2>&1
  rc=$?
  echo "-- compile exit=$rc"
  if [ $rc -eq 0 ]; then
    dotnet "$LYRVM" run "${f%.lyr}.lyrbc" 2>&1
    echo "-- run exit=$?"
  fi
done
