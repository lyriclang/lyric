#!/bin/sh
# Builds native/lib/<triple>/libcrc32.a for every Tier 1 target from native/src/crc32.c with zig —
# the archives this package brings prebuilt. The same zig makes the same bytes on any machine, so
# the toolchain's tests build them again and compare: the paths are relative to native/, no debug
# information names a directory, and a Windows object is named crc32.obj, not after zig's
# temporary file. ZIG names zig where it is not on the PATH.
set -eu
zig="${ZIG:-zig}"
cd "$(dirname "$0")"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
for triple in x86_64-linux-gnu aarch64-linux-gnu x86_64-windows-gnu aarch64-macos x86_64-macos; do
    case "$triple" in
        *windows*) own="-Xclang -object-file-name=crc32.obj" ;;
        *) own="-fPIC" ;;
    esac
    "$zig" cc -target "$triple" -O2 -g0 $own -I include -c src/crc32.c -o "$work/crc32.o"
    (cd "$work" && rm -f libcrc32.a && "$zig" ar rcsD libcrc32.a crc32.o)
    mkdir -p "lib/$triple"
    mv "$work/libcrc32.a" "lib/$triple/libcrc32.a"
done
