#!/usr/bin/env bash
# The runtime's test programs under valgrind memcheck, with runtime/valgrind.supp (M1 S5). Local
# only, on Linux: CI has the sanitizer profiles. Run the Lyric5 tests first — they build the
# programs into the test cache — then:  tooling/valgrind/run.sh [program ...]
# Glibc's debug information comes from the distribution's debuginfod server (Arch's by default;
# set DEBUGINFOD_URLS for another), without which valgrind cannot start on a stripped ld.so.
# Expected: every program ends with its own exit code, and none with 99 (a memcheck error).
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$(cd "$here/../.." && pwd)"
bin="${TMPDIR:-/tmp}/lyric5-test-cache/bin/$(uname -m)-linux-gnu/debug"
export DEBUGINFOD_URLS="${DEBUGINFOD_URLS:-https://debuginfod.archlinux.org}"

programs=("$@")
[ ${#programs[@]} -eq 0 ] && programs=(gc_smoke alloc strings arrays config hello roots weak threads panic_index panic_checks)

failed=0
for program in "${programs[@]}"; do
  [ -x "$bin/$program" ] || { echo "$program: not built ($bin) — run the Lyric5 tests first"; failed=1; continue; }
  valgrind --quiet --error-exitcode=99 --suppressions="$root/runtime/valgrind.supp" \
    "$bin/$program" >/dev/null 2>"$bin/$program.valgrind.txt"
  code=$?
  if [ $code -eq 99 ]; then echo "$program: memcheck errors, see $bin/$program.valgrind.txt"; failed=1
  else echo "$program: clean (exit $code)"; fi
done
exit $failed
