import struct, sys

def uleb(n):
    out = bytearray()
    while True:
        b = n & 0x7F
        n >>= 7
        if n: out.append(b | 0x80)
        else:
            out.append(b); break
    return bytes(out)

def s(txt):
    b = txt.encode('utf-8')
    return uleb(len(b)) + b

def section(sid, payload):
    return bytes([sid]) + uleb(len(payload)) + payload

def module(strings, funcs, start=None, caps=0):
    out = b'LYRB' + struct.pack('<HH', 4, 0)
    out += section(1, uleb(caps))
    out += section(2, uleb(len(strings)) + b''.join(s(x) for x in strings))
    out += section(4, uleb(0))
    body = uleb(len(funcs))
    for f in funcs:
        body += uleb(f['name'])
        body += uleb(f['params'])
        body += f['ret']
        body += uleb(len(f['slots'])) + b''.join(f['slots'])
        body += uleb(f['maxstack'])
        body += uleb(len(f['blocks'])) + b''.join(uleb(b) for b in f['blocks'])
        body += uleb(len(f['code'])) + f['code']
    out += section(5, body)
    if start is not None:
        out += section(7, uleb(start))
    return out

I64 = b'\x04'
STR = b'\x0D'

# A: type confusion - two strings added as i64
codeA = (b'\x01\x0D' + uleb(0)      # const string "abc"
       + b'\x03' + uleb(0)          # stloc 0
       + b'\x02' + uleb(0)          # ldloc 0
       + b'\x02' + uleb(0)          # ldloc 0
       + b'\x10\x04'                # add i64
       + b'\x42')                   # retval
A = module(["main.main", "abc"], [dict(name=0, params=0, ret=I64, slots=[STR], maxstack=2, blocks=[0], code=codeA)], start=0)
open(sys.argv[1] + "/confuse.lyrbc", "wb").write(A)

# B: control - two ints added
codeB = (b'\x01\x04' + uleb(7)
       + b'\x03' + uleb(0)
       + b'\x02' + uleb(0)
       + b'\x02' + uleb(0)
       + b'\x10\x04'
       + b'\x42')
B = module(["main.main"], [dict(name=0, params=0, ret=I64, slots=[I64], maxstack=2, blocks=[0], code=codeB)], start=0)
open(sys.argv[1] + "/control.lyrbc", "wb").write(B)
print("written", len(A), len(B))
