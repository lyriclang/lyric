#!/usr/bin/env bash
# install.sh <target> <directory> — puts the pinned Pagefind binary for <target> (a release target
# such as x86_64-unknown-linux-musl) into <directory>, after checking the archive against the sha256
# in tooling/pagefind-version. The archive comes from the GitHub release, or from the file
# PAGEFIND_ARCHIVE names when it is set. An archive whose sum differs is refused and nothing is
# unpacked.
set -euo pipefail

target=${1:?usage: install.sh <target> <directory>}
directory=${2:?usage: install.sh <target> <directory>}
pin="$(cd "$(dirname "$0")/.." && pwd)/pagefind-version"

value() { sed -n "s/^$1 = \(.*\)$/\1/p" "$pin"; }
version=$(value version)
expected=$(value "$target")
[ -n "$version" ] || { echo "install.sh: no version in $pin" >&2; exit 1; }
[ -n "$expected" ] || { echo "install.sh: no sum for target '$target' in $pin" >&2; exit 1; }

name="pagefind-v$version-$target.tar.gz"
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
archive=${PAGEFIND_ARCHIVE:-}
if [ -z "$archive" ]; then
  archive="$work/$name"
  curl -fsSL --retry 3 -o "$archive" "https://github.com/CloudCannon/pagefind/releases/download/v$version/$name"
fi

if command -v sha256sum > /dev/null; then actual=$(sha256sum "$archive" | cut -d' ' -f1)
else actual=$(shasum -a 256 "$archive" | cut -d' ' -f1); fi
if [ "$actual" != "$expected" ]; then
  echo "install.sh: $name has sha256 $actual, the pin says $expected — refused" >&2
  exit 1
fi

mkdir -p "$directory"
tar -xzf "$archive" -C "$directory"
echo "$directory"
