# The download mirror

`https://dl.lyriclang.de` serves the toolchain's archives, their signatures and the channels' indexes,
as the second source beside GitHub's releases. `lyric toolchain` asks it first and goes on to GitHub
where it fails, so an outage costs time and nothing else.

**The server is not trusted, and that is the point of the design.** It holds no signing key, and a
toolchain accepts only what the keys in `tooling/release-keys` signed. Taking it over gains an attacker
an outage, or an older signed index for a machine that has seen no newer one.

## What runs there

Debian, set up by the scripts in `setup/`, each one read before it is run:

| Script | What it does | Can lock you out |
|---|---|---|
| `10-base.sh` | every update; security updates by themselves, with a reboot at 04:30 where needed; fail2ban; a limit on the journal | no |
| `20-ssh.sh apply <admin>` | keys alone, no root, only the admin and `deploy` | undoes itself after 10 minutes unless confirmed |
| `30-firewall.sh apply` | in: 22, 80, 443 and ICMP, limited per address; out: free | undoes itself after 10 minutes unless confirmed |
| `40-web.sh <name>` | Caddy with a certificate it fetches and renews; static files; logs of 14 days with shortened addresses | no |
| `41-redirect.sh <old> <new>` | an old name of the site answers with a permanent redirect, path and all | no |
| `50-deploy.sh <pub>` | the user `deploy`, whose one key reaches only `gate.py` | no |

Nothing else belongs on this machine: no site, no build, no secrets of the project. A redirect is the
one exception, being as static as a file: `lyric.oil1i.de`, the site's name until 2026-11-08, sends every
path on to `https://lyriclang.de`, so that the links old toolchains print keep working.

To set up a new one, point the name at it, make an admin user with a key and a password for sudo, copy
`setup/` over with `scp`, and run them in that order. Keep a session open across `20` and `30`, and
confirm each only after a new login has worked.

## What the release pipeline does

The pipeline uploads over SSH as `deploy`, to `incoming/` alone, then runs one command:

```text
publish dev|stable <upload>    the archives first, then the signature and the index
publish staging <upload>       a dry run: public, named by no index; the newest three stay
```

The dev channel keeps ten versions; the gate removes what its index no longer names. The stable channel
removes nothing. `.github/workflows/mirror-upload.yml` holds the upload and `tooling/mirror/known_hosts`
the server's identity, so the connection fails rather than trusting another machine.

`.github/workflows/mirror-watch.yml` looks from outside once a day: does it answer, is the certificate
good for two weeks, does it keep up with GitHub. A failed run reaches the maintainer by mail.

## Everyday work

- **Nothing, normally.** Updates and the certificate take care of themselves. A mail from the watch is
  the signal.
- **Look at it:** `systemctl status caddy fail2ban nftables`, `journalctl -u caddy -n 50`,
  `fail2ban-client status sshd`, `df -h /srv`.
- **Banned yourself from SSH:** through the provider's remote console, `fail2ban-client unban --all`.
- **Locked out altogether:** the provider's remote console, with root's password.

## When something goes wrong

**The server is gone or down.** Installs go on from GitHub by themselves. Set up a new one, point the
name at it, and write its new key into `known_hosts`. The first publish after that puts the archives in
place; older dev versions are gone from it, which nothing depends on.

**The deploy key has leaked.** It reaches only the gate, but it can upload. Make a new one
(`ssh-keygen -t ed25519 -N ""`), run `50-deploy.sh` with its public half, which puts it in place of the
old one, set it as `MIRROR_DEPLOY_KEY` in the environment `mirror`, and delete the private file. It needs
no backup. A new one is always cheaper than a copy.

**The server was broken into.** Assume nothing on it. Toolchains are safe by the signatures. Rebuild it
from these scripts on a clean install, with a new host key and a new deploy key.

**The address must change.** It is compiled into every toolchain (`ToolchainFetch.Mirror`). Keep the old
name answering for as long as old toolchains matter. Where it lapses, they go to GitHub.

**A key has leaked or is lost.** See `tooling/release-keys/README.md`.

The domain's renewal is the one thing here no script can do. If it lapses, the name can fall to anyone:
the signatures hold, but every toolchain then asks a stranger first.
