import json, subprocess, sys, os, time
LYRLS = r"C:/Users/Olivier/CLionProjects/lyric/src/Lyrls/bin/Debug/net10.0/lyrls.dll"
STDLIB = r"C:/Users/Olivier/CLionProjects/lyric/stdlib"
p = subprocess.Popen(["dotnet", LYRLS, "--stdlib", STDLIB], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
def send(o):
    b=json.dumps(o).encode(); p.stdin.write(b"Content-Length: %d\r\n\r\n"%len(b)+b); p.stdin.flush()
def read():
    h=b""
    while b"\r\n\r\n" not in h:
        c=p.stdout.read(1)
        if not c: return None
        h+=c
    n=int([l for l in h.decode().split("\r\n") if l.lower().startswith("content-length")][0].split(":")[1])
    return json.loads(p.stdout.read(n).decode())
src=open(sys.argv[1],encoding="utf-8").read()
uri="file:///"+os.path.abspath(sys.argv[1]).replace("\\","/")
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":None,"rootUri":None,"capabilities":{}}}); read()
send({"jsonrpc":"2.0","method":"initialized","params":{}})
t0=time.time()
send({"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":uri,"languageId":"lyric","version":1,"text":src}}})
# wait for the first publishDiagnostics
while True:
    m=read()
    if m and m.get("method")=="textDocument/publishDiagnostics":
        print("first diagnostics after %.0f ms, count=%d" % ((time.time()-t0)*1000, len(m["params"]["diagnostics"])))
        break
send({"jsonrpc":"2.0","id":30,"method":"textDocument/semanticTokens/full","params":{"textDocument":{"uri":uri}}})
send({"jsonrpc":"2.0","id":31,"method":"textDocument/formatting","params":{"textDocument":{"uri":uri},"options":{"tabSize":4,"insertSpaces":True}}})
seen=0
while seen<2:
    m=read()
    if m is None or "id" not in m: continue
    seen+=1
    r=m.get("result")
    if m['id']==30:
        print("semanticTokens/full ->", ("null" if r is None else str(len(r["data"])//5)+" tokens"))
    else:
        print("formatting ->", ("null" if r is None else str(len(r))+" edits"))
        if r: print("   first edit range:", r[0]["range"])
p.kill()
