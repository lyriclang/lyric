#!/bin/sh
# 20-ssh.sh apply <admin> | confirm | undo
#
# apply  : SSH with keys alone, no root, only <admin> and deploy. Refuses unless <admin> can already
#          log in with a key and use sudo. It arms a timer that takes the change back after 10 minutes.
# confirm: stops the timer. Run it only after a NEW login as <admin> has worked.
# undo   : takes the change back now.
#
# Keep the session you run this from open until you have confirmed: a reload leaves it standing.
set -eu

[ "$(id -u)" = 0 ] || { echo "20-ssh.sh: run this as root" >&2; exit 1; }
CONF=/etc/ssh/sshd_config.d/00-lyric-mirror.conf
TIMER=lyric-mirror-ssh-undo

case "${1:-}" in
  apply)
    admin=${2:?usage: 20-ssh.sh apply <admin>}
    keys="/home/$admin/.ssh/authorized_keys"
    id "$admin" > /dev/null 2>&1 || { echo "no user $admin" >&2; exit 1; }
    id -nG "$admin" | tr ' ' '\n' | grep -qx sudo || { echo "$admin is not in the group sudo" >&2; exit 1; }
    [ -s "$keys" ] || { echo "$keys is empty or missing" >&2; exit 1; }
    grep -Eq '^(ssh-ed25519|sk-ssh-ed25519@openssh.com|ecdsa-sha2-nistp256|ssh-rsa) AAAA' "$keys" || { echo "$keys holds no public key" >&2; exit 1; }
    [ "$(stat -c '%U' "$keys")" = "$admin" ] || { echo "$keys is not owned by $admin" >&2; exit 1; }
    [ "$(stat -c '%a' "$keys")" = 600 ] || { echo "$keys is not mode 600" >&2; exit 1; }
    passwd -S "$admin" | awk '{print $2}' | grep -qx P || { echo "$admin has no password, so sudo would not work" >&2; exit 1; }

    # A first match wins in sshd's configuration, and 00- comes before what the image ships.
    cat > "$CONF" <<EOF
PermitRootLogin no
PasswordAuthentication no
KbdInteractiveAuthentication no
PubkeyAuthentication yes
AllowUsers $admin deploy
X11Forwarding no
AllowAgentForwarding no
AllowTcpForwarding no
PermitTunnel no
MaxAuthTries 3
LoginGraceTime 20
ClientAliveInterval 300
ClientAliveCountMax 2
EOF
    if ! sshd -t; then
      rm -f "$CONF"
      echo "sshd refuses the configuration; nothing changed" >&2
      exit 1
    fi
    systemctl stop "$TIMER.timer" 2> /dev/null || true
    systemd-run --quiet --unit="$TIMER" --on-active=10min /bin/sh -c "rm -f $CONF; systemctl reload ssh"
    systemctl reload ssh
    echo "applied; it takes itself back in 10 minutes unless confirmed."
    sshd -T | grep -E '^(permitrootlogin|passwordauthentication|kbdinteractiveauthentication|pubkeyauthentication|allowusers|allowtcpforwarding|x11forwarding) '
    echo
    echo "Now, from a NEW terminal on your own machine, log in as $admin and run sudo whoami."
    echo "When that worked: sudo sh 20-ssh.sh confirm"
    ;;
  confirm)
    systemctl stop "$TIMER.timer" 2> /dev/null || true
    [ -f "$CONF" ] || { echo "the change is not in place: the timer has run, or it was never applied" >&2; exit 1; }
    echo "confirmed; the change stays."
    ;;
  undo)
    systemctl stop "$TIMER.timer" 2> /dev/null || true
    rm -f "$CONF"
    systemctl reload ssh
    echo "taken back."
    ;;
  *)
    echo "usage: 20-ssh.sh apply <admin> | confirm | undo" >&2
    exit 2
    ;;
esac
