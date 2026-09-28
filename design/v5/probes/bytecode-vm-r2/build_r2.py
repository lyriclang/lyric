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
def mkfn(slots=1, maxstack=2):
    return (uleb(0) + uleb(0) + I64 + uleb(slots) + I64*slots + uleb(maxstack)
            + uleb(1) + uleb(0) + uleb(len(code)) + code)
strs = uleb(1) + s("main.main")
def build(extra=None, slots=1, maxstack=2):
    out = b'LYRB' + struct.pack('<HH', 4, 0)
    parts = [sec(1, uleb(0)), sec(2, strs), sec(4, uleb(0)), sec(5, uleb(1)+mkfn(slots,maxstack)), sec(7, uleb(0))]
    if extra: parts = extra(parts)
    return out + b''.join(parts)

# id 0 as the FIRST section (unknown id, below every assigned id)
open("sec0.lyrbc","wb").write(build(extra=lambda p: [sec(0, b'lyricpp\x00blob')] + p))
# id 255 as the LAST section
open("sec255.lyrbc","wb").write(build(extra=lambda p: p + [sec(255, b'host-blob')]))
# id 0 twice (not strictly ascending)
open("sec00.lyrbc","wb").write(build(extra=lambda p: [sec(0,b'a'), sec(0,b'b')] + p))
# control: plain, no extra section
open("plain.lyrbc","wb").write(build())
# maxStack bomb
open("maxstack.lyrbc","wb").write(build(maxstack=2000000000))
# slot bomb: 4 million declared i64 slots, none used beyond slot 0
open("slotbomb.lyrbc","wb").write(build(slots=4000000))
print("built")
open("maxstack_100m.lyrbc","wb").write(build(maxstack=100000000))
open("maxstack_500m.lyrbc","wb").write(build(maxstack=500000000))
print("scale probes built")
