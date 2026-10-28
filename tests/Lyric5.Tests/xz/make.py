# Writes the fixtures of XzTests: .xz files made by Python's lzma (liblzma) and by the xz command,
# sharing no code with the reader, over plain data made from a seed — text that spans several
# LZMA2 chunks, random bytes that xz stores uncompressed, and the two in turn —, with
# each check, other literal properties, blocks that carry their sizes, two streams with padding,
# and a filter the reader does not take. expected.json gives each file's plain size and SHA-256.
#
#   python3 tests/Lyric5.Tests/xz/make.py      (needs xz on the PATH)
import hashlib, json, lzma, os, random, subprocess

HERE = os.path.dirname(os.path.abspath(__file__))


def text(size, seed):
    """Words from a small vocabulary, the same for a seed: compressible, with long and short matches."""
    rng = random.Random(seed)
    words = [''.join(rng.choice('abcdefghijklmnopqrstuvwxyz') for _ in range(rng.randint(2, 9))) for _ in range(400)]
    out = bytearray()
    while len(out) < size:
        out += rng.choice(words).encode() + (b'\n' if rng.random() < 0.1 else b' ')
    return bytes(out[:size])


def noise(size, seed):
    return random.Random(seed).randbytes(size)


def strides(size, seed):
    """Short copies from four distances in a random order, a random byte between: the encoder keeps
    the four in its last distances and reaches the oldest of them, rep3, again and again."""
    rng = random.Random(seed)
    out = bytearray(rng.randbytes(4096))
    while len(out) < size:
        distance = rng.choice([1000, 2000, 3000, 4000])
        for _ in range(rng.randint(6, 12)):
            out.append(out[-distance])
        out.append(rng.randrange(256))
    return bytes(out[:size])


PLAIN = {
    'text': text(400 * 1024, 1),
    'noise': noise(90 * 1024, 2),
    'mixed': noise(10 * 1024, 3) + text(80 * 1024, 4) + noise(70 * 1024, 5) + text(30 * 1024, 6),
    'strides': strides(64 * 1024, 7),
    'small': b'lyric\n',
    'empty': b'',
}

expected = {}


def write(name, plain, data):
    with open(os.path.join(HERE, name), 'wb') as f:
        f.write(data)
    expected[name] = {'plain': plain, 'size': len(PLAIN[plain]), 'sha256': hashlib.sha256(PLAIN[plain]).hexdigest()}


def xz(plain, *args):
    return subprocess.run(['xz', '--stdout', '--threads=1', *args], input=PLAIN[plain], capture_output=True, check=True).stdout


write('text-crc64.xz', 'text', lzma.compress(PLAIN['text'], check=lzma.CHECK_CRC64, preset=6))
write('noise-crc32.xz', 'noise', lzma.compress(PLAIN['noise'], check=lzma.CHECK_CRC32, preset=6))
write('mixed-sha256.xz', 'mixed', lzma.compress(PLAIN['mixed'], check=lzma.CHECK_SHA256, preset=9 | lzma.PRESET_EXTREME))
write('mixed-none.xz', 'mixed', lzma.compress(PLAIN['mixed'], check=lzma.CHECK_NONE, preset=1))
# lc, lp and pb other than the default 3, 0 and 2
write('mixed-properties.xz', 'mixed', lzma.compress(PLAIN['mixed'], check=lzma.CHECK_CRC64,
      filters=[{'id': lzma.FILTER_LZMA2, 'preset': 6, 'lc': 1, 'lp': 3, 'pb': 0}]))
write('strides-crc64.xz', 'strides', lzma.compress(PLAIN['strides'], check=lzma.CHECK_CRC64, preset=9 | lzma.PRESET_EXTREME))
write('small.xz', 'small', lzma.compress(PLAIN['small']))
write('empty.xz', 'empty', lzma.compress(PLAIN['empty']))
# blocks of 128 KiB, each header with its compressed and uncompressed size
write('text-blocks.xz', 'text', xz('text', '--block-size=131072', '-6'))
# two streams, the second after four bytes of stream padding
half = len(PLAIN['mixed']) // 2
PLAIN['mixed-halves'] = PLAIN['mixed']
write('two-streams.xz', 'mixed-halves', lzma.compress(PLAIN['mixed'][:half]) + b'\0' * 4 + lzma.compress(PLAIN['mixed'][half:]))
# a filter before LZMA2: refused
write('small-x86.xz', 'small', xz('small', '--x86', '--lzma2=preset=6'))

with open(os.path.join(HERE, 'expected.json'), 'w', newline='\n') as f:
    json.dump(expected, f, indent=2, sort_keys=True)
    f.write('\n')
print('\n'.join(f'{n}: {os.path.getsize(os.path.join(HERE, n))} bytes' for n in sorted(expected)))
