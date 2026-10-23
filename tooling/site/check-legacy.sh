#!/bin/bash
# Checks the legacy move over a copy of the published site, never the site itself.
#
#   tooling/site/check-legacy.sh [<ref of the published site>]   (default: origin/gh-pages, fetched first)
#
# A detached work tree of the ref is made under a temporary directory, `site legacy` runs in it, and
# the tree after is held against the ref's: every file of a version directory vX.Y.Z/ and the old
# versions.json are under legacy/ with the same bytes, nightly/ is gone, every other file is as it
# was, and nothing else is new but legacy/index.html and 404.html. A second run must find nothing to
# do. The work tree is removed at the end; nothing is committed or pushed.
set -euo pipefail
repo=$(git rev-parse --show-toplevel)
ref=${1:-origin/gh-pages}
if [ "$ref" = origin/gh-pages ]; then git -C "$repo" fetch -q origin gh-pages:refs/remotes/origin/gh-pages; fi

work=$(mktemp -d)/pages
git -C "$repo" worktree add -q --detach "$work" "$ref"
trap 'git -C "$repo" worktree remove --force "$work"' EXIT

site=(dotnet run --project "$repo/tools/Site" -c Release --)
"${site[@]}" legacy --pages "$work" | tail -1

before=$(mktemp); after=$(mktemp)
git -C "$work" ls-tree -r "$ref" | awk '{ print $4 "\t" $3 }' | sort > "$before"
# The temporary work tree's index takes the tree after; its entries carry each file's hash.
git -C "$work" add -A
git -C "$work" ls-files -s | sed -E 's/^[0-9]+ ([0-9a-f]+) [0-9]+\t(.*)$/\2\t\1/' | sort > "$after"

fail=0
expected=$(mktemp)
while IFS=$'\t' read -r path hash; do
  case "$path" in
    nightly/*) ;;
    v[0-9]*.[0-9]*.[0-9]*/*|versions.json) printf "legacy/%s\t%s\n" "$path" "$hash" ;;
    *) printf "%s\t%s\n" "$path" "$hash" ;;
  esac
done < "$before" | sort > "$expected"

missing=$(comm -23 "$expected" "$after" | wc -l)
extra=$(comm -13 "$expected" "$after" | cut -f1 | grep -vxE 'legacy/index\.html|404\.html' || true)
nightly=$(grep -c '^nightly/' "$after" || true)
echo "files before: $(wc -l < "$before"), after: $(wc -l < "$after"), moved and kept: $(comm -12 "$expected" "$after" | wc -l)"
if [ "$missing" -ne 0 ]; then echo "MISSING or changed after the move: $missing"; comm -23 "$expected" "$after" | head; fail=1; fi
if [ -n "$extra" ]; then echo "NEW beyond legacy/index.html and 404.html:"; echo "$extra" | head; fail=1; fi
if [ "$nightly" -ne 0 ]; then echo "nightly/ still holds $nightly files"; fail=1; fi

again=$("${site[@]}" legacy --pages "$work" | tail -1)
echo "second run: $again"
case "$again" in *"nothing to do"*) ;; *) echo "the second run did something"; fail=1 ;; esac

[ "$fail" -eq 0 ] && echo "legacy move: every old page under legacy/ with its bytes, nightly/ gone, nothing else touched" || echo "legacy move: FAILED"
exit "$fail"
