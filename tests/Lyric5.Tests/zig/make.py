# Writes the fixtures of ZigInstallTests: zig's download page as `lyric toolchain install zig` reads
# it — index.json in zig's format, and for each Tier 1 target an archive in zig's layout (a .tar.xz,
# a .zip for Windows) with a stand-in zig and its lib, signed the way zig signs (minisign, with
# OpenSSL, the trusted comment naming the file) with a key whose secret half is thrown away —, plus
# the signatures a mirror must not get past: one by another key, and one that names another file.
# Version 9.9.1's archive lacks lib. The archives are byte for byte the same on every run.
#
#   python3 tests/Lyric5.Tests/zig/make.py      (needs openssl 3 on the PATH)
import base64, hashlib, io, json, lzma, os, shutil, subprocess, tarfile, tempfile, zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
DOWNLOAD = os.path.join(HERE, 'download')
SCRATCH = tempfile.mkdtemp()

KEYS = ['x86_64-linux', 'aarch64-linux', 'x86_64-windows', 'aarch64-macos', 'x86_64-macos']
VERSIONS = ['9.9.0', '9.9.1']


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

    def minisig(self, data, path, named):
        signature = self.sign(openssl('dgst', '-blake2b512', '-binary', data=data))
        comment = f'timestamp:1776173777\tfile:{named}\thashed'
        glob = self.sign(signature + comment.encode())
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'w', newline='\n') as f:
            f.write('untrusted comment: signature from minisign secret key\n')
            f.write(base64.b64encode(b'ED' + self.id + signature).decode() + '\n')
            f.write(f'trusted comment: {comment}\n')
            f.write(base64.b64encode(glob).decode() + '\n')


def name(key, version):
    return f'zig-{key}-{version}.' + ('zip' if 'windows' in key else 'tar.xz')


def files(key, version):
    exe = 'zig.exe' if 'windows' in key else 'zig'
    top = f'zig-{key}-{version}'
    out = {f'{top}/{exe}': f"#!/bin/sh\necho {version}\n".encode()}
    if version != '9.9.1':
        out[f'{top}/lib/std/std.zig'] = f'// the standard library of zig {version}\n'.encode()
    return top, out


def archive(key, version):
    top, content = files(key, version)
    if 'windows' in key:
        raw = io.BytesIO()
        with zipfile.ZipFile(raw, 'w', zipfile.ZIP_DEFLATED) as z:
            for path, data in sorted(content.items()):
                info = zipfile.ZipInfo(path, date_time=(2026, 4, 14, 0, 0, 0))
                info.external_attr = 0o755 << 16
                z.writestr(info, data)
        return raw.getvalue()
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode='w', format=tarfile.USTAR_FORMAT) as tar:
        dirs = sorted({top} | {os.path.dirname(p) for p in content})
        for d in dirs:
            info = tarfile.TarInfo(d + '/')
            info.type, info.mode, info.mtime = tarfile.DIRTYPE, 0o755, 1776124800
            tar.addfile(info)
        for path, data in sorted(content.items()):
            info = tarfile.TarInfo(path)
            info.size, info.mode, info.mtime = len(data), 0o755, 1776124800
            tar.addfile(info, io.BytesIO(data))
    return lzma.compress(raw.getvalue(), check=lzma.CHECK_CRC64, preset=6)


shutil.rmtree(DOWNLOAD, ignore_errors=True)
zig, other = Key('key'), Key('other')
index = {'master': {'version': '9.10.0-dev.1+0000000', 'date': '2026-04-15'}}
for version in VERSIONS:
    entry = {'date': '2026-04-14', 'docs': f'https://ziglang.org/documentation/{version}/'}
    for key in KEYS:
        file = name(key, version)
        data = archive(key, version)
        os.makedirs(os.path.join(DOWNLOAD, version), exist_ok=True)
        with open(os.path.join(DOWNLOAD, version, file), 'wb') as f:
            f.write(data)
        zig.minisig(data, os.path.join(DOWNLOAD, version, file + '.minisig'), file)
        if version == '9.9.0':
            other.minisig(data, os.path.join(DOWNLOAD, 'other-key', file + '.minisig'), file)
            zig.minisig(data, os.path.join(DOWNLOAD, 'wrong-file', file + '.minisig'), name(key, '9.8.0'))
        entry[key] = {'tarball': f'https://ziglang.org/download/{version}/{file}',
                      'shasum': hashlib.sha256(data).hexdigest(), 'size': str(len(data))}
    index[version] = entry
with open(os.path.join(DOWNLOAD, 'index.json'), 'w', newline='\n') as f:
    json.dump(index, f, indent=2)
    f.write('\n')
shutil.rmtree(SCRATCH)
print('ok')
