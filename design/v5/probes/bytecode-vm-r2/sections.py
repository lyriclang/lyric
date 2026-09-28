import sys
NAMES={1:"Capabilities",2:"Strings",3:"Types",4:"Imports",5:"Functions",6:"SourceMap",
7:"Globals",8:"Impls",9:"DebugInfo",10:"Names",11:"Attributes",12:"Handlers",13:"OpaqueFields",14:"Coroutines"}
def dump(p):
    d=open(p,'rb').read()
    assert d[:4]==b'LYRB'
    maj=int.from_bytes(d[4:6],'little'); mino=int.from_bytes(d[6:8],'little')
    i=8; print(f"{p}  ({len(d)} B, format {maj}.{mino})")
    tot=8
    while i<len(d):
        sid=d[i]; i+=1
        n=0; sh=0
        while True:
            b=d[i]; i+=1; n|=(b&0x7F)<<sh; sh+=7
            if not b&0x80: break
        print(f"   id {sid:3d} {NAMES.get(sid,'?'):14s} payload {n:6d} B")
        tot+=n+1+((sh//7))
        i+=n
    print(f"   header+overhead {len(d)-sum(0 for _ in []) }")
for p in sys.argv[1:]: dump(p); print()
