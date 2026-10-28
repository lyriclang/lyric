# Writes the minisign fixtures of MinisignTests with OpenSSL, a signer that shares no code with
# the checker under test: two key pairs (the secret halves are thrown away), a file signed the way
# minisign signs (Ed25519 over its BLAKE2b-512 hash, and over the signature and the trusted
# comment), the same file signed in minisign's legacy form, two signed channel indexes, and the
# BLAKE2b-512 of a pattern at the lengths around a block's edge.
#
#   python3 tests/Lyric5.Tests/minisign/make.py      (needs openssl 3 on the PATH)
import base64, json, os, subprocess, tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
SCRATCH = tempfile.mkdtemp()


def openssl(*args, data=None):
    return subprocess.run(['openssl', *args], input=data, capture_output=True, check=True).stdout


def blake2b(data):
    return openssl('dgst', '-blake2b512', '-binary', data=data)


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

    def minisig(self, data, name, legacy=False):
        signature = self.sign(data if legacy else blake2b(data))
        comment = f'timestamp:1793145600\tfile:{name}' + ('' if legacy else '\thashed')
        glob = self.sign(signature + comment.encode())
        with open(os.path.join(HERE, name + ('.legacy' if legacy else '') + '.minisig'), 'w', newline='\n') as f:
            f.write('untrusted comment: signature from minisign secret key\n')
            f.write(base64.b64encode((b'Ed' if legacy else b'ED') + self.id + signature).decode() + '\n')
            f.write(f'trusted comment: {comment}\n')
            f.write(base64.b64encode(glob).decode() + '\n')


def pattern(length):
    return bytes(i % 251 for i in range(length))


key, other = Key('key'), Key('other')

archive = pattern(1000)
with open(os.path.join(HERE, 'archive.bin'), 'wb') as f:
    f.write(archive)
key.minisig(archive, 'archive.bin')
key.minisig(archive, 'archive.bin', legacy=True)

sha = subprocess.run(['openssl', 'dgst', '-sha256', '-r'], input=archive, capture_output=True, check=True).stdout.decode().split()[0]
for channel, versions in [('dev', ['5.0.0-dev.20261028+0b3eb5e', '5.0.0-dev.20261027+80182e3']), ('stable', ['5.0.0'])]:
    index = {
        'channel': channel,
        'versions': [
            {
                'version': version,
                'date': '2026-10-' + (version[16:18] if channel == 'dev' else '30'),
                'archives': {
                    triple: {'file': f'lyric-{version.split("+")[0]}-{rid}.{ext}', 'size': len(archive), 'sha256': sha}
                    for triple, rid, ext in [('x86_64-linux-gnu', 'linux-x64', 'tar.gz'), ('x86_64-windows-gnu', 'win-x64', 'zip')]
                },
            }
            for version in versions
        ],
    }
    text = (json.dumps(index, indent=2) + '\n').encode()
    with open(os.path.join(HERE, channel + '.json'), 'wb') as f:
        f.write(text)
    key.minisig(text, channel + '.json')

with open(os.path.join(HERE, 'blake2b.txt'), 'w', newline='\n') as f:
    for length in [0, 1, 63, 64, 127, 128, 129, 255, 256, 257, 1000, 100_000]:
        f.write(f'{length} {blake2b(pattern(length)).hex()}\n')
print('written to', HERE)
