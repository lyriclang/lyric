#!/bin/bash
# usage: run.sh <file.lyr> ...  — compiles each, runs on success, prints compact result
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
OUT="$(dirname "$0")/out"
mkdir -p "$OUT"
for f in "$@"; do
  b=$(basename "$f" .lyr)
  echo "##### $b"
  dotnet "$LYRC" build "$f" -o "$OUT/$b.lyrbc" > "$OUT/$b.build.txt" 2>&1; bc=$?
  if [ $bc -ne 0 ]; then echo "BUILD exit=$bc"; grep -E "error|warning|panic|Stack|Unhandled" "$OUT/$b.build.txt" | head -6; continue; fi
  sz=$(stat -c %s "$OUT/$b.lyrbc")
  dotnet "$LYRVM" run "$OUT/$b.lyrbc" > "$OUT/$b.run.txt" 2>&1; rc=$?
  echo "BUILD ok ${sz}B | RUN exit=$rc | $(head -c 300 "$OUT/$b.run.txt" | tr '\n' '|')"
done
