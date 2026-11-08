#!/bin/sh
# 40-web.sh <name>: Caddy serves /srv/mirror/public as <name> over HTTPS and fetches and renews the
# certificate by itself. Safe to run again. The name must already point at this machine: the
# certificate's issuer checks it over port 80 and 443.
#
# Static files alone, no listing of directories, nothing dynamic. HTTP/3 stays off: the firewall opens
# TCP only, and a download gains nothing from it. Logs keep 14 days and cut every address down to its
# network (/24, /48), and no header is kept: there is no tracking on the site, nor here.
set -eu

[ "$(id -u)" = 0 ] || { echo "40-web.sh: run this as root" >&2; exit 1; }
name=${1:?usage: 40-web.sh <name>}
case "$name" in
  *[!a-z0-9.-]*) echo "40-web.sh: '$name' is no host name" >&2; exit 1 ;;
esac

export DEBIAN_FRONTEND=noninteractive
apt-get -y -q install caddy curl openssl

install -d -m 755 -o root -g root /srv/mirror
install -d -m 755 /srv/mirror/public /srv/mirror/public/dev /srv/mirror/public/stable /srv/mirror/public/staging

new=$(mktemp /etc/caddy/Caddyfile.XXXXXX)
trap 'rm -f "$new"' EXIT
cat > "$new" <<EOF
{
	servers {
		protocols h1 h2
	}
}

$name {
	root * /srv/mirror/public

	@root path /
	respond @root "The download mirror of the Lyric toolchain. See https://lyric.oil1i.de/ and lyric toolchain install." 200

	@index path /dev.json /dev.json.minisig /stable.json /stable.json.minisig
	header @index Cache-Control "no-cache"
	@archives path /dev/* /stable/*
	header @archives Cache-Control "public, max-age=31536000, immutable"
	header {
		-Server
		X-Content-Type-Options nosniff
		Strict-Transport-Security "max-age=31536000"
		Referrer-Policy no-referrer
	}

	file_server

	log {
		output file /var/log/caddy/access.log {
			roll_size 50MiB
			roll_keep 14
			roll_keep_for 336h
		}
		format filter {
			wrap json
			fields {
				request>remote_ip ip_mask {
					ipv4 24
					ipv6 48
				}
				request>client_ip ip_mask {
					ipv4 24
					ipv6 48
				}
				request>headers delete
				request>tls delete
				resp_headers delete
			}
		}
	}
}
EOF
chmod 644 "$new"
if ! caddy validate --adapter caddyfile --config "$new"; then
  echo "caddy refuses the configuration; the running one stays" >&2
  exit 1
fi
mv "$new" /etc/caddy/Caddyfile
trap - EXIT
systemctl enable --quiet caddy
systemctl restart caddy

echo
echo "== checks =="
caddy version
ok=
for _ in $(seq 1 30); do
  if curl -fsS -o /dev/null --max-time 5 "https://$name/"; then ok=1; break; fi
  sleep 2
done
if [ -n "$ok" ]; then
  echo "https://$name/ answers"
  curl -fsS "https://$name/"; echo
  curl -sS -o /dev/null -w 'http answers %{http_code}, to %{redirect_url}\n' "http://$name/"
  echo | openssl s_client -connect "$name:443" -servername "$name" 2> /dev/null | openssl x509 -noout -issuer -enddate
else
  echo "https://$name/ does not answer yet; see: journalctl -u caddy --no-pager -n 30"
  exit 1
fi
