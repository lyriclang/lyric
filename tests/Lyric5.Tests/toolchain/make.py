# Writes the fixtures of ToolchainCommandTests: a mirror as `lyric toolchain` reads one — the
# channels' indexes signed the way minisign signs (with OpenSSL, sharing no code with the checker),
# and for each version an archive per Tier 1 target holding a stand-in toolchain with its
# toolchain.json —, plus a signed index that gives every archive a wrong hash. The secret key is
# thrown away. The archives are byte for byte the same on every run but for the key.
#
#   python3 tests/Lyric5.Tests/toolchain/make.py      (needs openssl 3 on the PATH)
import base64, gzip, hashlib, io, json, os, shutil, subprocess, tarfile, tempfile, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
MIRROR = os.path.join(HERE, 'mirror')
SCRATCH = tempfile.mkdtemp()

TARGETS = [
    ('x86_64-linux-gnu', 'linux-x64'),
    ('aarch64-linux-gnu', 'linux-arm64'),
    ('x86_64-windows-gnu', 'win-x64'),
    ('aarch64-macos', 'osx-arm64'),
    ('x86_64-macos', 'osx-x64'),
]

# the older and the newer development build, one whose archive holds a file its manifest does not
# list, and a stable release
DEV = ['9.0.0-dev.20261101+aaaaaaa', '9.0.0-dev.20261102+bbbbbbb', '9.0.0-dev.20261031+ccccccc']
STRAY = '9.0.0-dev.20261031+ccccccc'
STABLE = ['8.0.0']


def openssl(*args, data=None):
    return subprocess.run(['openssl', *args], input=data, capture_output=True, check=True).stdout


class Key:
    def __init__(self, name):
        self.pem = os.path.join(SCRATCH, name + '.pem')
        openssl('genpkey', '-algorithm', 'ed25519', '-out', self.pem)
        self.public = openssl('pkey', '-in', self.pem, '-pubout', '-outform', 'DER')[-32:]
        self.id = os.urandom(8)
        with open(os.path.join(HERE, name + '.pub'), 'w', newline='\n') as f:
            f.write(f'untrusted comment: minisign public key {self.id[::-1].hex().upper()}\n')
            f.write(base64.b64encode(b'Ed' + self.id + self.public).decode() + '\n')

    def sign(self, message):
        path = os.path.join(SCRATCH, 'message')
        with open(path, 'wb') as f:
            f.write(message)
        return openssl('pkeyutl', '-sign', '-inkey', self.pem, '-rawin', '-in', path)

    def minisig(self, data, path):
        signature = self.sign(openssl('dgst', '-blake2b512', '-binary', data=data))
        comment = f'timestamp:1793145600\tfile:{os.path.basename(path)}\thashed'
        glob = self.sign(signature + comment.encode())
        with open(path + '.minisig', 'w', newline='\n') as f:
            f.write('untrusted comment: signature from minisign secret key\n')
            f.write(base64.b64encode(b'ED' + self.id + signature).decode() + '\n')
            f.write(f'trusted comment: {comment}\n')
            f.write(base64.b64encode(glob).decode() + '\n')


def toolchain(version, triple):
    """The stand-in toolchain's files by path, toolchain.json last."""
    exe = 'lyric5.exe' if 'windows' in triple else 'lyric5'
    files = {
        exe: f"#!/bin/sh\necho 'lyric {version}'\n".encode(),
        'stdlib5/std/core.lyr': f'// the standard library of {version}\n'.encode(),
    }
    listed = [{'path': p, 'size': len(b), 'sha256': hashlib.sha256(b).hexdigest()} for p, b in sorted(files.items())]
    manifest = {'version': version, 'triple': triple, 'files': listed}
    if version == STRAY:
        files['stray.txt'] = b'not in the manifest\n'
    files['toolchain.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
    return exe, files


def tar_gz(version, triple):
    exe, files = toolchain(version, triple)
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode='w', format=tarfile.USTAR_FORMAT) as tar:
        def add(name, data=None):
            info = tarfile.TarInfo(name)
            info.mtime = 0
            if data is None:
                info.type, info.mode = tarfile.DIRTYPE, 0o755
                tar.addfile(info)
            else:
                info.size, info.mode = len(data), 0o755 if name == './' + exe else 0o644
                tar.addfile(info, io.BytesIO(data))
        add('./')
        made = set()
        for path in files:
            parts = path.split('/')[:-1]
            for i in range(1, len(parts) + 1):
                d = '/'.join(parts[:i])
                if d not in made:
                    made.add(d)
                    add(f'./{d}/')
            add('./' + path, files[path])
    packed = io.BytesIO()
    with gzip.GzipFile(fileobj=packed, mode='wb', mtime=0, filename='') as z:
        z.write(raw.getvalue())
    return packed.getvalue()


def zip_(version, triple):
    _, files = toolchain(version, triple)
    packed = io.BytesIO()
    with zipfile.ZipFile(packed, 'w', zipfile.ZIP_DEFLATED) as z:
        for path, data in files.items():
            z.writestr(zipfile.ZipInfo(path, date_time=(1980, 1, 1, 0, 0, 0)), data)
    return packed.getvalue()


key, other = Key('key'), Key('other')
shutil.rmtree(MIRROR, ignore_errors=True)
for channel, versions in [('dev', DEV), ('stable', STABLE)]:
    os.makedirs(os.path.join(MIRROR, channel))
    releases = []
    for version in versions:
        archives = {}
        for triple, rid in TARGETS:
            windows = 'windows' in triple
            data = zip_(version, triple) if windows else tar_gz(version, triple)
            file = f'lyric-{version.split("+")[0]}-{rid}.{"zip" if windows else "tar.gz"}'
            with open(os.path.join(MIRROR, channel, file), 'wb') as f:
                f.write(data)
            archives[triple] = {'file': file, 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest()}
        releases.append({'version': version, 'date': '2026-11-' + (version[16:18] if channel == 'dev' else '05'), 'archives': archives})
    index = {'channel': channel, 'versions': releases}
    path = os.path.join(MIRROR, channel + '.json')
    text = (json.dumps(index, indent=2) + '\n').encode()
    with open(path, 'wb') as f:
        f.write(text)
    key.minisig(text, path)
    if channel == 'dev':
        for release in index['versions']:
            for archive in release['archives'].values():
                archive['sha256'] = '0' * 64
        path = os.path.join(HERE, 'badhash.json')
        text = (json.dumps(index, indent=2) + '\n').encode()
        with open(path, 'wb') as f:
            f.write(text)
        key.minisig(text, path)
print('written to', HERE)
