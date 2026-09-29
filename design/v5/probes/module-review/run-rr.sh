#!/bin/bash
P="C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/module-review"
LYRC="C:/Users/Olivier/CLionProjects/lyric/src/Lyrc/bin/Debug/net10.0/lyrc.dll"
LYRVM="C:/Users/Olivier/CLionProjects/lyric/src/Lyrvm/bin/Debug/net10.0/lyrvm.dll"
b(){ # dir file [grant]
  cd "$P/$1"
  echo "=============== $1/$2"
  dotnet "$LYRC" build "$2" -o out.lyrbc 2>&1 | grep -v "^$" | head -40
  echo "--- build exit: ${PIPESTATUS[0]}"
  if [ -f out.lyrbc ]; then
    if [ -n "$3" ]; then dotnet "$LYRVM" run out.lyrbc --grant all 2>&1 | head -20; else dotnet "$LYRVM" run out.lyrbc 2>&1 | head -20; fi
    echo "--- run exit: ${PIPESTATUS[0]}"
  fi
}
for d in rr01 rr01k rr02a rr02b rr02k rr04 rr05 rr06 rr07a rr07b rr08 rr09 rr10 rr10b rr11a rr11b rr11c rr13a rr13b rr14 rr16a rr16b rr17 rr18 rr19 rr21 rr22 rr23 rr25; do b $d main.lyr; done
b rr24 main.lyr grant
b rr20 app.lyr
echo "=============== rr03 app.lyr"
cd "$P/rr03"; dotnet "$LYRC" build app.lyr -o app.lyrbc 2>&1 | head; dotnet "$LYRVM" info app.lyrbc 2>&1 | grep -i "entry\|module" | head -5
dotnet "$LYRC" build hdr.lyr -o hdr.lyrbc 2>&1 | head; dotnet "$LYRVM" info hdr.lyrbc 2>&1 | grep -i "entry\|module" | head -5
echo "=============== rr12"
cd "$P/rr12"; for f in m1 m2 m3; do dotnet "$LYRC" build $f.lyr -o $f.lyrbc 2>&1 | head -4; done
echo "=============== rr15 library"
cd "$P/rr15"; dotnet "$LYRC" build api.lyr -o api.lyrbc 2>&1 | head; dotnet "$LYRVM" info api.lyrbc 2>&1 | head -40
echo "=============== rr26 verbose"
cd "$P/rr26"; dotnet "$LYRC" build main.lyr -o out.lyrbc --verbose 2>&1 | head -30
