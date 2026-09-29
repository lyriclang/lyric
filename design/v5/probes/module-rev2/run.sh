#!/bin/bash
cd "$(dirname "$0")"
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
LYRIC="C:/Users/Olivier/CLionProjects/lyric/src/Lyric.Cli/bin/Debug/net10.0/lyric.dll"
run() { # dir entry
  local d=$1 e=$2
  echo "=================== $d ($e)"
  ( cd "$d" && dotnet "$LYRC" build "$e" -o out.lyrbc 2>&1; echo "[build exit $?]" )
  if [ -f "$d/out.lyrbc" ]; then
    ( cd "$d" && dotnet "$LYRVM" run out.lyrbc 2>&1; echo "[run exit $?]" )
  fi
}
for d in q01 q01k q02 q03 q03b q03d q05a q05b q10 q10k; do rm -f $d/out.lyrbc; run $d main.lyr; done
for d in q03c q06 q06k q07 q07k q08k q09 q12 q12b q12c q13; do rm -f $d/out.lyrbc; run $d main.lyr; done
for d in q08 q08c; do rm -f $d/out.lyrbc; run $d app.lyr; done
for d in q04 q04k; do
  echo "=================== $d (api.lyr, lib)"
  ( cd $d && rm -f api.lyrbc && dotnet "$LYRC" build api.lyr -o api.lyrbc 2>&1; echo "[build exit $?]"; dotnet "$LYRVM" info api.lyrbc 2>&1 | grep -i "fn\|func\|entry\|module\|newBox\|helper\|get" )
done
echo "=================== q13 info"
( cd q13 && dotnet "$LYRVM" info out.lyrbc 2>&1 | grep -i "ident\|Secret\|entry" )
echo "=================== q11 verbose"
( cd q11 && dotnet "$LYRC" build main.lyr -o out.lyrbc --verbose 2>&1 )
echo "=================== q14 lyric test / check"
( cd q14 && dotnet "$LYRIC" test 2>&1; echo "[test exit $?]"; dotnet "$LYRIC" check . 2>&1; echo "[check exit $?]" )
echo "=================== q14k lyric test"
( cd q14k && dotnet "$LYRIC" test 2>&1; echo "[test exit $?]" )
