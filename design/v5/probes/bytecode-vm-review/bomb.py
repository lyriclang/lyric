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
# const i64 7; stloc 0; ldloc 0; ldloc 0; add i64; retval
code = b'\x01\x04' + uleb(7) + b'\x03\x00' + b'\x02\x00' + b'\x02\x00' + b'\x10\x04' + b'\x42'
def mod(maxstack, slots=1, blocks=1):
    fn = (uleb(1) + uleb(0) + uleb(0) + I64
          + uleb(slots) + I64*slots
          + uleb(maxstack)
          + uleb(blocks) + uleb(0)*blocks
          + uleb(len(code)) + code)
    strs = uleb(1) + s("main.main")
    out = b'LYRB' + struct.pack('<HH', 4, 0)
    for p in [sec(1, uleb(0)), sec(2, strs), sec(4, uleb(0)), sec(5, fn), sec(7, uleb(0))]:
        out += p
    return out
open("bomb_maxstack.lyrbc","wb").write(mod(2_000_000_000))
open("bomb_slots.lyrbc","wb").write(mod(2, slots=1))
print("ok")
