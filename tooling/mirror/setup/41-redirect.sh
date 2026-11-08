#!/bin/sh
# 41-redirect.sh <old name> <new address>: the old name answers every request with a permanent
# redirect to the same path under the new address, over HTTPS with a certificate of its own. For a
# name the project has left, so that links in old toolchains, pages and posts keep working. Safe to
# run again; run 40-web.sh first.
#
#   sh 41-redirect.sh lyric.oil1i.de https://lyriclang.de
set -eu

[ "$(id -u)" = 0 ] || { echo "41-redirect.sh: run this as root" >&2; exit 1; }
from=${1:?usage: 41-redirect.sh <old name> <new address>}
to=${2:?usage: 41-redirect.sh <old name> <new address>}
case "$from" in
  *[!a-z0-9.-]*) echo "41-redirect.sh: '$from' is no host name" >&2; exit 1 ;;
esac
case "$to" in
  https://*) ;;
  *) echo "41-redirect.sh: '$to' does not start with https://" >&2; exit 1 ;;
esac
case "$to" in
  *[!a-zA-Z0-9.:/-]*) echo "41-redirect.sh: '$to' is no plain address" >&2; exit 1 ;;
esac
to=${to%/}

install -d -m 755 /etc/caddy/redirects
cat > "/etc/caddy/redirects/$from.caddy" <<EOF
$from {
	redir $to{uri} permanent
}
EOF
chmod 644 "/etc/caddy/redirects/$from.caddy"

grep -qxF 'import /etc/caddy/redirects/*.caddy' /etc/caddy/Caddyfile || printf '\nimport /etc/caddy/redirects/*.caddy\n' >> /etc/caddy/Caddyfile
if ! caddy validate --adapter caddyfile --config /etc/caddy/Caddyfile; then
  rm -f "/etc/caddy/redirects/$from.caddy"
  echo "caddy refuses the configuration; the redirect is taken out again" >&2
  exit 1
fi
systemctl reload caddy

echo
echo "== checks =="
ok=
for _ in $(seq 1 30); do
  if curl -sS -o /dev/null --max-time 5 "https://$from/"; then ok=1; break; fi
  sleep 2
done
if [ -n "$ok" ]; then
  curl -sS -o /dev/null -w "https://$from/x/y?z answers %{http_code}, to %{redirect_url}\n" "https://$from/x/y?z"
else
  echo "https://$from/ does not answer yet: the name must point at this machine first; see journalctl -u caddy --no-pager -n 30"
fi
