import struct
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
REF_C = b'\x40' + uleb(0)
strs = uleb(2) + s("main.main") + s("C")
types = uleb(1) + uleb(1) + b'\x00' + uleb(1) + I64
def fn(code, maxstack):
    return uleb(1) + (uleb(0) + uleb(0) + I64 + uleb(1) + REF_C + uleb(maxstack)
            + uleb(1) + uleb(0) + uleb(len(code)) + code)
def build(code, maxstack):
    out = b'LYRB' + struct.pack('<HH', 4, 0)
    parts = [sec(1, uleb(0)), sec(2, strs), sec(3, types), sec(4, uleb(0)), sec(5, fn(code, maxstack)), sec(7, uleb(0))]
    return out + b''.join(parts)
# uninit: ldloc 0; ldfld C.0; retval   (slot 0 typed as ref C, never stored)
open("uninit.lyrbc","wb").write(build(b'\x02\x00' + b'\x51\x00\x00' + b'\x42', 1))
# control: newobj C; stloc 0; ldloc 0; ldfld C.0; retval
open("uninit_ctl.lyrbc","wb").write(build(b'\x50\x00' + b'\x03\x00' + b'\x02\x00' + b'\x51\x00\x00' + b'\x42', 1))
print("built")
