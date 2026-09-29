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
strs = uleb(3) + s("main.main") + s("C") + s("S")
# type 0: layout C(i64); type 1: struct S(i64)
types = uleb(2) + (uleb(1) + b'\x00' + uleb(1) + I64) + (uleb(2) + b'\x03' + uleb(1) + I64)
# 5 blocks: bb0: br 1 | bb1: br 2 | bb2: br 3 | bb3: const 0; retval | bb4 (handler): const 9; retval
code = b''
offs = []
for blk in [b'\x43\x01', b'\x43\x02', b'\x43\x03', b'\x01\x04\x00\x42', b'\x01\x04\x09\x42']:
    offs.append(len(code)); code += blk
fn = uleb(1) + (uleb(0) + uleb(0) + I64 + uleb(1) + I64 + uleb(1) + uleb(5) + b''.join(uleb(o) for o in offs) + uleb(len(code)) + code)
def handler(fn_i, start, end, kind, catchType, hblock, slot):
    return uleb(fn_i)+uleb(start)+uleb(end)+bytes([kind])+uleb(catchType)+uleb(hblock)+uleb(slot)
def build(handlers):
    out = b'LYRB' + struct.pack('<HH', 4, 0)
    parts = [sec(1, uleb(0)), sec(2, strs), sec(3, types), sec(4, uleb(0)), sec(5, fn), sec(7, uleb(0)), sec(9, uleb(len(handlers)) + b''.join(handlers))]
    return out + b''.join(parts)
# crossing regions [0,2) and [1,3), both catch-all to bb4
open("cross.lyrbc","wb").write(build([handler(0,0,2,0,0,4,0), handler(0,1,3,0,0,4,0)]))
# nested (valid control): [1,2) inside [0,3)
open("nested.lyrbc","wb").write(build([handler(0,1,2,0,0,4,0), handler(0,0,3,0,0,4,0)]))
# catchType = struct S (index 1 -> +1 = 2): a struct is not throwable in the language
open("catchstruct.lyrbc","wb").write(build([handler(0,0,3,0,2,4,0)]))
# catchType = layout C (index 0 -> 1) with slot 0 (i64) bound: type mismatch slot vs catch type
open("catchslot.lyrbc","wb").write(build([handler(0,0,3,0,1,4,1)]))
print("built")
