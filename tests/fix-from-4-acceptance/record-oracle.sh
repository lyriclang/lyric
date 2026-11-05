#!/usr/bin/env bash
# Records what 4.6 prints for its own examples: the oracle of the migration's acceptance.
#
#   git worktree add --detach ../v46 release/v4.6.0
#   dotnet build ../v46/src/Lyric.Cli -c Release
#   tests/fix-from-4-acceptance/record-oracle.sh ../v46
#
# It is not run by a test: 4.6 is frozen, and so is what it printed.
set -u
v46=$(cd "$1" && pwd)
here=$(cd "$(dirname "$0")" && pwd)
lyric=$v46/src/Lyric.Cli/bin/Release/net10.0/lyric
mkdir -p "$here/oracle"
cd "$v46/examples" || exit 1
for file in $(find . -name '*.lyr' | sed 's|^\./||' | sort); do
    name=$(echo "${file%.lyr}" | tr '/' '_')
    case $name in
        # a library of a host and a binding of .NET have no program to compare
        embedded | ffi_dotnet) continue ;;
        greet) set -- World Lyric ;;
        wc) set -- hello.lyr ;;
        *) set -- ;;
    esac
    timeout 60 "$lyric" run "$file" -- "$@" > "$here/oracle/$name.out" 2> /dev/null
    echo $? > "$here/oracle/$name.code"
done
