#!/bin/sh
# 30-firewall.sh apply | confirm | undo
#
# apply  : the firewall below, live but not yet lasting. It arms a timer that removes it after 10
#          minutes, and a reboot removes it too.
# confirm: stops the timer and makes it last across reboots. Run it only after a NEW login has worked.
# undo   : removes it now.
#
# Inbound: SSH, HTTP and HTTPS, ICMP as the network needs it, nothing else. Outbound stays free: the
# certificate's renewal and the updates go out from here. Per address: 6 new SSH connections a minute
# (burst 12), 64 connections at once to the web server, 40 new ones a second. What fail2ban bans it
# bans in a table of its own, which this leaves alone.
set -eu

[ "$(id -u)" = 0 ] || { echo "30-firewall.sh: run this as root" >&2; exit 1; }
RULES=/etc/nftables.lyric-mirror.conf
TIMER=lyric-mirror-firewall-undo

write_rules() {
  cat > "$RULES" <<'EOF'
#!/usr/sbin/nft -f
destroy table inet lyric_mirror

table inet lyric_mirror {
    set ssh_rate4 { type ipv4_addr; size 65535; flags dynamic, timeout; timeout 2m; }
    set ssh_rate6 { type ipv6_addr; size 65535; flags dynamic, timeout; timeout 2m; }
    set web_rate4 { type ipv4_addr; size 65535; flags dynamic, timeout; timeout 1m; }
    set web_rate6 { type ipv6_addr; size 65535; flags dynamic, timeout; timeout 1m; }
    set web_conn4 { type ipv4_addr; size 65535; flags dynamic; }
    set web_conn6 { type ipv6_addr; size 65535; flags dynamic; }

    chain input {
        type filter hook input priority filter; policy drop;

        ct state established,related accept
        ct state invalid drop
        iif "lo" accept

        meta l4proto icmp limit rate 20/second accept
        # neighbour discovery: IPv6 does not work without it
        meta l4proto ipv6-icmp accept
        udp dport 546 ip6 saddr fe80::/10 accept

        tcp dport 22 ct state new add @ssh_rate4 { ip saddr limit rate over 6/minute burst 12 packets } counter drop
        tcp dport 22 ct state new add @ssh_rate6 { ip6 saddr limit rate over 6/minute burst 12 packets } counter drop
        tcp dport 22 accept

        tcp dport { 80, 443 } ct state new add @web_conn4 { ip saddr ct count over 64 } counter reject with tcp reset
        tcp dport { 80, 443 } ct state new add @web_conn6 { ip6 saddr ct count over 64 } counter reject with tcp reset
        tcp dport { 80, 443 } ct state new add @web_rate4 { ip saddr limit rate over 40/second burst 80 packets } counter drop
        tcp dport { 80, 443 } ct state new add @web_rate6 { ip6 saddr limit rate over 40/second burst 80 packets } counter drop
        tcp dport { 80, 443 } accept
    }

    chain forward {
        type filter hook forward priority filter; policy drop;
    }
}
EOF
}

case "${1:-}" in
  apply)
    write_rules
    if ! nft -c -f "$RULES"; then
      echo "nft refuses the rules; nothing changed" >&2
      exit 1
    fi
    systemctl stop "$TIMER.timer" 2> /dev/null || true
    systemd-run --quiet --unit="$TIMER" --on-active=10min /usr/sbin/nft destroy table inet lyric_mirror
    nft -f "$RULES"
    echo "applied; it removes itself in 10 minutes unless confirmed."
    echo
    echo "Now, from a NEW terminal on your own machine, log in and run sudo whoami."
    echo "When that worked: sudo sh 30-firewall.sh confirm"
    ;;
  confirm)
    nft list table inet lyric_mirror > /dev/null 2>&1 || { echo "the firewall is not in place: the timer has run, or it was never applied" >&2; exit 1; }
    systemctl stop "$TIMER.timer" 2> /dev/null || true
    cp "$RULES" /etc/nftables.conf
    systemctl enable --quiet nftables
    echo "confirmed; the firewall stays, also after a reboot."
    systemctl is-enabled nftables
    ;;
  undo)
    systemctl stop "$TIMER.timer" 2> /dev/null || true
    nft destroy table inet lyric_mirror
    echo "removed."
    ;;
  *)
    echo "usage: 30-firewall.sh apply | confirm | undo" >&2
    exit 2
    ;;
esac
