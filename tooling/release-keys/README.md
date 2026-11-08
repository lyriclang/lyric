# The release keys

The public halves of the keys Lyric's toolchains are signed with. A toolchain carries them in
`src/Lyric5.Build/ToolchainFetch.cs` (a test holds the two the same), and the book prints them for a
first install checked by hand. Secret keys and passphrases are never in this repository, in an issue
or in a log.

| Key | Signs | Secret half kept |
|---|---|---|
| `dev.pub` | the dev channel, on every push to `main`, without anyone's approval | GitHub environment `sign-dev`, which admits `main` alone; a copy in the maintainer's password manager |
| `stable.pub` | stable releases and their dry runs, after the maintainer's approval | GitHub environment `sign-stable`, which admits `main` and tags `v5.*`; a copy in the password manager |
| `reserve.pub` | nothing, until a channel's own key is lost | offline on a USB stick, never on GitHub; its passphrase in the password manager |

A toolchain accepts a channel's index under that channel's key or the reserve. The dev key signs
nothing the stable channel accepts, and neither does the stable key for dev.

## What a signature says

That an archive and its index came out of the release pipeline: not that the code in them is good.

- **Holds against:** a broken or taken-over mirror, DNS or TLS. It cannot offer an archive of its own.
  A deploy key that leaks reaches only the mirror's upload directory.
- **Does not hold against:** bad code merged to `main`, which is signed like any other; a compromised
  workflow or runner while it holds the dev key; the maintainer's GitHub account or an admin token.
- **There is no revocation.** A toolchain trusts the keys it was built with for as long as it runs. A
  new key reaches a user with a new toolchain.
- **Rollback is noticed only by a machine that has seen better.** An index carries a number, and a
  machine refuses one lower than it has seen from the same signer. A machine that has seen nothing
  takes an older signed index for the newest.
- **The first install is as good as where it came from.** The archive comes from GitHub, and the book
  that prints the keys is on GitHub Pages, under the same account.

## Checking a file by hand

```sh
minisign -Vm lyric-<version>-linux-x64.tar.gz -p tooling/release-keys/dev.pub
```

## Making the keys

On the maintainer's own machine, in a directory only they can read, outside every checkout:

```sh
minisign -G -p lyric-dev.pub -s lyric-dev.key
```

Always with a passphrase. minisign asks for it; it goes in no command line, so it reaches no shell
history. A key file alone opens nothing, which is what makes its copies safe to keep.

In a GitHub environment the secrets are named `MINISIGN_SECRET_KEY` (both lines of the `.key` file)
and `MINISIGN_PASSWORD`. The signing job checks each signature against the public key in this
directory, so a wrong secret stops a run before anything is published.

## When a key is lost

The secret half is gone, nobody else has it, and the channel cannot be signed.

1. Sign with the reserve: put its key and passphrase in the channel's environment and change the
   channel's public key in the workflow (`tooling/release-keys/reserve.pub`). Toolchains already out
   there accept it.
2. Make a new channel key and a new reserve (see above), and put the public halves here and in
   `ToolchainFetch.cs`, the old reserve remaining as the channel's key until the release after.
3. Ship a toolchain carrying them; from it on, the new channel key signs.

Toolchains older than that release stop at the old reserve: they take its signatures, and no others.

## When a key has leaked

The secret half is in other hands, and it stays valid in every toolchain that carries it.

1. Delete the secrets from the environment at once, and stop what signs with them.
2. Find out how it got out before putting anything back. A leak through a workflow will take the next
   key the same way.
3. Move to the reserve as above, and ship a toolchain that **no longer carries the leaked key**. A dev
   key's leak ends at the dev channel; a stable key's reaches every stable user.
4. Say so publicly, with the id of the key and the date: toolchains that do not update go on trusting
   it, and only their users can act.

Rotating a key that has not leaked follows the same steps, unhurried. It is not needed on a schedule.

## Changing the pinned minisign

`tooling/minisign-version` holds its version and sum. Fetch the new archive with its `.minisig`, check
it against the author's key as printed on the project's page, and write the sum of the archive.
