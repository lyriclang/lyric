#!/bin/sh
# 10-base.sh: the system up to date, and it stays so. Run once as root on a fresh Debian; safe to run
# again. Changes no login and no firewall, so it cannot lock anyone out.
#
#   - installs every pending update
#   - security updates install themselves and reboot at 04:30 when they need to: GitHub answers
#     for the mirror meanwhile
#   - fail2ban watches SSH
#   - the journal is limited to 200 MB
set -eu

[ "$(id -u)" = 0 ] || { echo "10-base.sh: run this as root" >&2; exit 1; }
. /etc/os-release
[ "${ID:-}" = debian ] || { echo "10-base.sh: written for Debian, this is ${ID:-unknown}" >&2; exit 1; }

export DEBIAN_FRONTEND=noninteractive
apt-get update -q
apt-get -y -q -o Dpkg::Options::=--force-confold full-upgrade
apt-get -y -q install unattended-upgrades fail2ban python3-systemd nftables rsync systemd-timesyncd

cat > /etc/apt/apt.conf.d/20auto-upgrades <<'EOF'
APT::Periodic::Update-Package-Lists "1";
APT::Periodic::Unattended-Upgrade "1";
EOF
cat > /etc/apt/apt.conf.d/52lyric-mirror <<'EOF'
Unattended-Upgrade::Automatic-Reboot "true";
Unattended-Upgrade::Automatic-Reboot-Time "04:30";
Unattended-Upgrade::Remove-Unused-Kernel-Packages "true";
Unattended-Upgrade::Remove-Unused-Dependencies "true";
EOF
systemctl enable --now unattended-upgrades.service

cat > /etc/fail2ban/jail.d/lyric-mirror.local <<'EOF'
[sshd]
enabled = true
backend = systemd
banaction = nftables
maxretry = 5
findtime = 10m
bantime = 1h
EOF
systemctl enable fail2ban
systemctl restart fail2ban

mkdir -p /etc/systemd/journald.conf.d
cat > /etc/systemd/journald.conf.d/lyric-mirror.conf <<'EOF'
[Journal]
SystemMaxUse=200M
EOF
systemctl restart systemd-journald
systemctl enable --now systemd-timesyncd

echo
echo "== checks =="
unattended-upgrade --dry-run > /dev/null 2>&1 && echo "unattended-upgrades: runs" || echo "unattended-upgrades: FAILED, see unattended-upgrade --dry-run --debug"
fail2ban-client status sshd | sed -n 's/.*Currently failed:.*/fail2ban sshd: on/p'
timedatectl show -p NTPSynchronized --value | sed 's/^/time synchronised: /'
[ -f /var/run/reboot-required ] && echo "REBOOT REQUIRED: the updates need one" || echo "no reboot required"
echo
echo "== what listens on this machine (send this to whoever guides you) =="
ss -tulnH | awk '{print $1, $5}' | sort -u
