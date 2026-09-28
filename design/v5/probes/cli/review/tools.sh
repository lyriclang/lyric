B="C:/Users/Olivier/CLionProjects/lyric/src"
lyric()  { dotnet "$B/Lyric.Cli/bin/Debug/net10.0/lyric.dll" "$@"; }
lyrc()   { dotnet "$B/Lyrc/bin/Debug/net10.0/lyrc.dll" "$@"; }
lyrvm()  { dotnet "$B/Lyrvm/bin/Debug/net10.0/lyrvm.dll" "$@"; }
lyrfmt() { dotnet "$B/Lyrfmt/bin/Debug/net10.0/lyrfmt.dll" "$@"; }
lyrpack(){ dotnet "$B/Lyrpack/bin/Debug/net10.0/lyrpack.dll" "$@"; }
lyrtest(){ dotnet "$B/Lyrtest/bin/Debug/net10.0/lyrtest.dll" "$@"; }
lyrls()  { dotnet "$B/Lyrls/bin/Debug/net10.0/lyrls.dll" "$@"; }
lyrdbg() { dotnet "$B/Lyrdbg/bin/Debug/net10.0/lyrdbg.dll" "$@"; }
lyrbuild(){ dotnet "$B/Lyrbuild/bin/Debug/net10.0/lyrbuild.dll" "$@"; }
run() { echo "### $*"; "$@" 2>&1; echo "[exit=$?]"; }
