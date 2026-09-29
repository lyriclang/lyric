#!/usr/bin/env bash
# The C toolchain Lyric 5 compiles through (design/v5/spec/01 C1/C7/C8, 11 T4), checked on the
# runner it runs on: zig cc builds and runs natively, cross-builds every Tier 1 triple, and — on
# Linux x86-64 — clang's sanitizers report the two planted faults. Expected results are written
# next to each check; a check that does not see its expectation fails the job.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
out="$(mktemp -d)"
exe=""; case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) exe=".exe" ;; esac

echo "zig $(zig version)"

# Expect: the native build prints its line.
zig cc -O2 -o "$out/hello$exe" "$here/hello.c"
[ "$("$out/hello$exe")" = "c toolchain ok" ]

# Expect: every Tier 1 triple links.
for triple in x86_64-linux-gnu aarch64-linux-gnu x86_64-windows-gnu aarch64-macos x86_64-macos; do
  suffix=""; [ "$triple" = "x86_64-windows-gnu" ] && suffix=".exe"
  zig cc -target "$triple" -O2 -o "$out/hello-$triple$suffix" "$here/hello.c"
  echo "cross $triple ok"
done

if [ "$(uname -s)" = "Linux" ] && [ "$(uname -m)" = "x86_64" ]; then
  # Expect: ASan reports the heap overflow and fails the run; UBSan reports the signed overflow.
  clang -O1 -g -fsanitize=address -fno-omit-frame-pointer -o "$out/asan" "$here/faults.c"
  if "$out/asan" 1 2>"$out/asan.txt"; then echo "ASan did not fail the run"; exit 1; fi
  grep -q "heap-buffer-overflow" "$out/asan.txt"
  "$out/asan" 0 >/dev/null
  echo "asan ok"

  clang -O1 -g -fsanitize=undefined -o "$out/ubsan" "$here/faults.c"
  "$out/ubsan" 2 2>"$out/ubsan.txt" >/dev/null || true
  grep -q "signed integer overflow" "$out/ubsan.txt"
  echo "ubsan ok"

  # Expect: zig cc carries no ASan runtime (the reason the sanitizer profile uses clang).
  if zig cc -fsanitize=address -o "$out/zasan" "$here/faults.c" 2>/dev/null; then
    echo "zig cc links ASan now — revisit 01 C7"
  else
    echo "zig cc has no ASan runtime (as recorded in 01 C7)"
  fi
fi
rm -rf "$out"
