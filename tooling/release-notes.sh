#!/bin/sh
# The part of a release's text that says how to install it and what the other files are for, in
# Markdown: release-notes.sh <channel> <directory with the signed files>. The key is the channel's
# own from tooling/release-keys, the same the binary carries (a test holds the two together), and
# the command's name is read from an archive, so the text says what the archive holds.
set -eu

channel=$1
dist=$2
keys=$(dirname "$0")/release-keys
key=$(tail -n 1 "$keys/$channel.pub")
reserve=$(tail -n 1 "$keys/reserve.pub")
example=$(ls "$dist" | grep -E -- '-linux-x64\.tar\.gz$' | head -n 1)
[ -n "$example" ] || { echo "release-notes.sh: no linux-x64 archive in $dist" >&2; exit 1; }
command=$(tar -tzf "$dist/$example" | sed -n -E 's|^(\./)?(lyric5?)$|\2|p' | head -n 1)
[ -n "$command" ] || { echo "release-notes.sh: no lyric command in $example" >&2; exit 1; }

cat <<EOF
## Installing

Take the archive for your system, unpack it where you keep tools and put that directory on your
\`PATH\`. From then on \`$command toolchain install\` fetches, checks and installs toolchains for you,
and \`$command toolchain update\` the newest of a channel. Building a program needs a C compiler:
\`$command toolchain install zig\` fetches one. The Book has the details:
<https://lyriclang.de/dev/book/getting-started/>.

## The other files

You need none of them to install. \`$channel.json\` is the channel's index: every version with each
archive's name, size and SHA-256. Each \`.minisig\` is the signature of the file it is named after,
and \`SHA256SUMS\` lists the archives' hashes. \`$command toolchain\` reads the index only with a valid
signature and takes an archive only when it matches the index, from GitHub or from the mirror
<https://dl.lyriclang.de> alike.

## Checking a download by hand

With [minisign](https://jedisct1.github.io/minisign/) and the key of the $channel channel:

\`\`\`sh
minisign -Vm $example -P $key
\`\`\`

The same key is in \`tooling/release-keys/$channel.pub\` in the repository and inside every
\`$command\`. Should the channel's key ever have been replaced, the reserve key signs instead:
\`$reserve\`. \`sha256sum -c SHA256SUMS --ignore-missing\` finds a broken download but not a forged
one: whoever can replace an archive can replace \`SHA256SUMS\` beside it. Only the signature tells.
EOF
