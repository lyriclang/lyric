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
def section(sid, p): return bytes([sid]) + uleb(len(p)) + p

I64 = b'\x04'
def REF(i): return b'\x40' + uleb(i)

def module(strings, types, funcs, start=None):
    out = b'LYRB' + struct.pack('<HH', 4, 0)
    out += section(1, uleb(0))
    out += section(2, uleb(len(strings)) + b''.join(s(x) for x in strings))
    tb = uleb(len(types))
    for t in types:
        tb += uleb(t['name']) + bytes([0]) + uleb(len(t['fields'])) + b''.join(t['fields'])
    out += section(3, tb)
    out += section(4, uleb(0))
    body = uleb(len(funcs))
    for f in funcs:
        body += uleb(f['name']) + uleb(f['params']) + f['ret']
        body += uleb(len(f['slots'])) + b''.join(f['slots'])
        body += uleb(f['maxstack'])
        body += uleb(len(f['blocks'])) + b''.join(uleb(b) for b in f['blocks'])
        body += uleb(len(f['code'])) + f['code']
    out += section(5, body)
    if start is not None: out += section(7, uleb(start))
    return out

D = sys.argv[1]

# C: an i64 stored into a slot declared as a class reference, then ldfld
codeC = (b'\x01\x04' + uleb(5)
       + b'\x03' + uleb(0)
       + b'\x02' + uleb(0)
       + b'\x51' + uleb(0) + uleb(0)   # ldfld type 0 field 0
       + b'\x42')
C = module(["main.main", "C"],
           [dict(name=1, fields=[I64])],
           [dict(name=0, params=0, ret=I64, slots=[REF(0)], maxstack=2, blocks=[0], code=codeC)],
           start=0)
open(D + "/nullref.lyrbc", "wb").write(C)

# D: a one-field object read through a two-field layout -> out of bounds field index
codeD = (b'\x50' + uleb(1)            # newobj type 1 (Small, 1 field)
       + b'\x03' + uleb(0)            # stloc 0
       + b'\x02' + uleb(0)            # ldloc 0
       + b'\x51' + uleb(2) + uleb(1)  # ldfld type 2 (Big, 2 fields) field 1
       + b'\x42')
Dm = module(["main.main", "Small", "Big"],
            [dict(name=0, fields=[]),           # 0: unused placeholder (name reuse ok)
             dict(name=1, fields=[I64]),        # 1: Small
             dict(name=2, fields=[I64, I64])],  # 2: Big
            [dict(name=0, params=0, ret=I64, slots=[REF(1)], maxstack=2, blocks=[0], code=codeD)],
            start=0)
open(D + "/oob.lyrbc", "wb").write(Dm)
print("ok")
