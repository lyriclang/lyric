#!/usr/bin/python3 -I
"""The one thing the deploy key can run, whatever its client asks for.

  rsync ...                  writes under incoming/, nowhere else, deletes nothing
  publish dev|stable <id>    moves incoming/<id> into the channel: the files first, the signature and
                             the index last, so that the index never names what is not there. The dev
                             channel then loses what its index no longer names.
  publish staging <id>       moves incoming/<id> to staging/<id>, public but named by no index; the
                             newest three stay.

A client asks for one of them through SSH_ORIGINAL_COMMAND. Anything else is refused. An index's
signature is not checked here: the machine holds no key and is not trusted, and a toolchain checks
it. The check below only keeps a mistake from reaching the channel.
"""
import fcntl, json, os, re, shutil, stat, sys, time

ROOT = os.environ.get('LYRIC_MIRROR_ROOT', '/srv/mirror')
RRSYNC = os.environ.get('LYRIC_MIRROR_RRSYNC', '/usr/bin/rrsync')
INCOMING = os.path.join(ROOT, 'incoming')
PUBLIC = os.path.join(ROOT, 'public')
ID = re.compile(r'^[A-Za-z0-9][A-Za-z0-9._+-]{0,120}$')
KEEP_STAGING = 3
STALE_SECONDS = 24 * 3600


def refuse(message):
    print(f'gate: {message}', file=sys.stderr)
    sys.exit(1)


def regular_files(directory):
    """The names of the files in a directory, which holds files alone."""
    names = []
    for name in sorted(os.listdir(directory)):
        info = os.lstat(os.path.join(directory, name))
        if not stat.S_ISREG(info.st_mode):
            refuse(f'{name} is not a plain file')
        names.append(name)
    return names


def named_by(index):
    """Every archive an index names, with its size; refuses what is no index of ours."""
    named = {}
    for release in index['versions']:
        for archive in release['archives'].values():
            name = archive['file']
            if not ID.match(name):
                refuse(f"'{name}' is not an archive's file name")
            named[name] = archive['size']
    return named


def publish_channel(channel, incoming):
    target = os.path.join(PUBLIC, channel)
    names = regular_files(incoming)
    file = f'{channel}.json'
    for needed in (file, file + '.minisig'):
        if needed not in names:
            refuse(f'{needed} is not in the upload')
    try:
        with open(os.path.join(incoming, file), encoding='utf-8') as f:
            index = json.load(f)
        if index['channel'] != channel:
            refuse(f"the index of the channel '{index['channel']}' is uploaded as '{channel}'")
        named = named_by(index)
    except (ValueError, KeyError, TypeError, AttributeError) as e:
        refuse(f'{file} is no index: {e!r}')
    for name, size in named.items():
        here = os.path.join(incoming, name) if name in names else os.path.join(target, name)
        if not os.path.isfile(here):
            refuse(f'the index names {name}, which is neither uploaded nor published')
        if os.path.getsize(here) != size:
            refuse(f'the index gives {name} {size} bytes, and it has {os.path.getsize(here)}')

    for name in names:
        if name not in (file, file + '.minisig'):
            os.chmod(os.path.join(incoming, name), 0o644)
            os.replace(os.path.join(incoming, name), os.path.join(target, name))
    # The two cannot change together. A toolchain that reads between them fetches the pair again.
    for name in (file + '.minisig', file):
        os.chmod(os.path.join(incoming, name), 0o644)
        os.replace(os.path.join(incoming, name), os.path.join(PUBLIC, name))

    removed = 0
    if channel == 'dev':
        keep = set(named) | {name + '.minisig' for name in named} | {'SHA256SUMS', 'SHA256SUMS.minisig'}
        for name in os.listdir(target):
            if name not in keep and os.path.isfile(os.path.join(target, name)):
                os.remove(os.path.join(target, name))
                removed += 1
    os.rmdir(incoming)
    print(f'published {len(named)} archives in {file}; removed {removed} files')


def publish_staging(upload, incoming):
    regular_files(incoming)
    staging = os.path.join(PUBLIC, 'staging')
    target = os.path.join(staging, upload)
    if os.path.exists(target):
        shutil.rmtree(target)
    for name in os.listdir(incoming):
        os.chmod(os.path.join(incoming, name), 0o644)
    os.chmod(incoming, 0o755)
    os.replace(incoming, target)
    runs = sorted(os.listdir(staging), key=lambda n: os.path.getmtime(os.path.join(staging, n)), reverse=True)
    for old in runs[KEEP_STAGING:]:
        shutil.rmtree(os.path.join(staging, old))
    print(f'staged {upload}')


def clear_stale(now):
    """An upload nobody published, left by a run cut short."""
    for name in os.listdir(INCOMING):
        path = os.path.join(INCOMING, name)
        if now - os.path.getmtime(path) > STALE_SECONDS:
            shutil.rmtree(path, ignore_errors=True) if os.path.isdir(path) else os.remove(path)


def main():
    asked = os.environ.get('SSH_ORIGINAL_COMMAND', '')
    if asked.startswith('rsync --server '):
        # write-only, no deleting, under incoming/ whatever path the client names
        os.execv(RRSYNC, [RRSYNC, '-wo', '-no-del', INCOMING])
    words = asked.split()
    if len(words) != 3 or words[0] != 'publish' or words[1] not in ('dev', 'stable', 'staging') or not ID.match(words[2]):
        refuse('this key can only upload and publish')
    channel, upload = words[1], words[2]
    incoming = os.path.join(INCOMING, upload)
    if not os.path.isdir(incoming) or os.path.islink(incoming):
        refuse(f'no upload named {upload}')
    with open(os.path.join(ROOT, 'incoming.lock'), 'w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX)
        if channel == 'staging':
            publish_staging(upload, incoming)
        else:
            publish_channel(channel, incoming)
        clear_stale(time.time())


if __name__ == '__main__':
    main()
