#!/bin/bash
cd "$(dirname "$0")"
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
for f in "$@"; do
  echo "=================== $f"
  dotnet "$LYRC" build "$f.lyr" -o "$f.lyrbc" 2>&1 | head -40
  echo "--- build exit ${PIPESTATUS[0]}"
  if [ -f "$f.lyrbc" ]; then
    dotnet "$LYRVM" run "$f.lyrbc" 2>&1 | head -20
    echo "--- run exit ${PIPESTATUS[0]}"
  fi
done
