import struct, sys
def uleb(n):
    out = bytearray()
    while True:
        b = n & 0x7F; n >>= 7
        if n: out.append(b | 0x80)
        else: out.append(b); break
    return bytes(out)
def s(t):
    b = t.encode('utf-8'); return uleb(len(b)) + b
def sec(sid, p): return bytes([sid]) + uleb(len(p)) + p
I64 = b'\x04'
code = b'\x01\x04' + uleb(7) + b'\x03\x00' + b'\x02\x00' + b'\x02\x00' + b'\x10\x04' + b'\x42'
fn = (uleb(1) + uleb(0) + uleb(0) + I64 + uleb(1) + I64 + uleb(2) + uleb(1) + uleb(0)
      + uleb(len(code)) + code)
strs = uleb(1) + s("main.main")

def build(major, minor, caps=0, descending=False, unknown_section=False):
    out = b'LYRB' + struct.pack('<HH', major, minor)
    parts = [sec(1, uleb(caps)), sec(2, strs), sec(4, uleb(0)), sec(5, fn), sec(7, uleb(0))]
    if unknown_section:
        parts.insert(3, sec(40, b'\x01\x02\x03'))
    if descending:
        parts = [parts[1], parts[0]] + parts[2:]
    return out + b''.join(parts)

D = sys.argv[1]
open(D + "/major5.lyrbc", "wb").write(build(5, 0))
open(D + "/minor99.lyrbc", "wb").write(build(4, 99))
open(D + "/cap63.lyrbc", "wb").write(build(4, 0, caps=1 << 63))
open(D + "/descending.lyrbc", "wb").write(build(4, 0, descending=True))
open(D + "/unknownsec.lyrbc", "wb").write(build(4, 0, unknown_section=True))
print("ok")
