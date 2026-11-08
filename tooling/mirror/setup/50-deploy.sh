#!/bin/sh
# 50-deploy.sh <public key file>: the user the release pipeline uploads as. Safe to run again; the key
# named replaces any before it. Run from the directory that holds gate.py, or name it in GATE.
#
# 'deploy' has /bin/sh for a shell because sshd starts a forced command through it; nologin would stop
# the gate from running. It has no password, and its one key can run the gate and nothing else, with
# no terminal and no forwarding. The key file belongs to root, so the user cannot change it. It owns
# incoming/ and public/, and that is all it can write.
set -eu

[ "$(id -u)" = 0 ] || { echo "50-deploy.sh: run this as root" >&2; exit 1; }
pub=${1:?usage: 50-deploy.sh <public key file>}
gate=${GATE:-$(dirname "$0")/gate.py}
[ -f "$gate" ] || { echo "50-deploy.sh: no $gate" >&2; exit 1; }
[ "$(wc -l < "$pub")" -le 1 ] || { echo "50-deploy.sh: $pub holds more than one line" >&2; exit 1; }
grep -Eq '^ssh-ed25519 AAAA[0-9A-Za-z+/=]+( [^ ]+)?$' "$pub" || { echo "50-deploy.sh: $pub is no ed25519 public key" >&2; exit 1; }
grep -qF 'PRIVATE' "$pub" && { echo "50-deploy.sh: $pub is a private key" >&2; exit 1; }

export DEBIAN_FRONTEND=noninteractive
apt-get -y -q install rsync python3
rrsync=$(command -v rrsync || true)
[ "$rrsync" = /usr/bin/rrsync ] || { echo "50-deploy.sh: rrsync is not at /usr/bin/rrsync but at '$rrsync'" >&2; exit 1; }

id deploy > /dev/null 2>&1 || adduser --disabled-password --gecos "" --shell /bin/sh deploy
usermod --shell /bin/sh deploy
passwd -l deploy > /dev/null

install -d -m 755 -o root -g root /usr/local/lib/lyric-mirror
install -m 755 -o root -g root "$gate" /usr/local/lib/lyric-mirror/gate

install -d -m 750 -o root -g deploy /home/deploy/.ssh
{
  printf 'restrict,command="/usr/local/lib/lyric-mirror/gate" '
  cat "$pub"
} > /home/deploy/.ssh/authorized_keys
chown root:deploy /home/deploy/.ssh/authorized_keys
chmod 640 /home/deploy/.ssh/authorized_keys

install -d -m 755 -o root -g root /srv/mirror
install -d -m 755 -o deploy -g deploy /srv/mirror/incoming /srv/mirror/public /srv/mirror/public/dev /srv/mirror/public/stable /srv/mirror/public/staging
touch /srv/mirror/incoming.lock
chown deploy:deploy /srv/mirror/incoming.lock
chmod 644 /srv/mirror/incoming.lock

echo
echo "== checks =="
sshd -T -C user=deploy,host=check,addr=127.0.0.1 2> /dev/null | grep -E '^(allowusers|passwordauthentication) ' || true
sudo -u deploy test -w /srv/mirror/incoming && echo "deploy can write to incoming"
sudo -u deploy test -w /etc/caddy/Caddyfile && echo "PROBLEM: deploy can write the web server's configuration" || echo "deploy cannot write the web server's configuration"
sudo -u deploy test -w /home/deploy/.ssh/authorized_keys && echo "PROBLEM: deploy can change its own key" || echo "deploy cannot change its own key"
echo "ready."
