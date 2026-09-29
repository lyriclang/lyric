import json, subprocess, sys, os, time
LYRLS = "C:/Users/Olivier/CLionProjects/lyric/src/Lyrls/bin/Debug/net10.0/lyrls.dll"
STD = "C:/Users/Olivier/CLionProjects/lyric/stdlib"
p = subprocess.Popen(["dotnet", LYRLS, "--stdlib", STD], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
def send(o):
    b = json.dumps(o).encode(); p.stdin.write(b"Content-Length: %d\r\n\r\n" % len(b) + b); p.stdin.flush()
def read():
    h = b""
    while b"\r\n\r\n" not in h:
        c = p.stdout.read(1)
        if not c: return None
        h += c
    n = int([l for l in h.decode().split("\r\n") if l.lower().startswith("content-length")][0].split(":")[1])
    return json.loads(p.stdout.read(n))
path = os.path.abspath("hov.lyr"); src = open(path, encoding="utf-8").read()
uri = "file:///" + path.replace("\\", "/")
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":None,"rootUri":None,"capabilities":{}}})
read(); send({"jsonrpc":"2.0","method":"initialized","params":{}})
send({"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":uri,"languageId":"lyric","version":1,"text":src}}})
while True:
    m = read()
    if m and m.get("method") == "textDocument/publishDiagnostics": print("diag", m["params"]["diagnostics"]); break
cases = [("struct Point decl",0,8),("enum Shade decl",1,6),("type Meters decl",2,6),("fn twice decl",3,4),("fn main decl",4,4),("let p decl",5,8),("Point use",5,12),("Meters use",6,11),("twice use",6,20),("field x decl",0,15),("p.x use",6,29)]
for i,(name,l,c) in enumerate(cases):
    send({"jsonrpc":"2.0","id":10+i,"method":"textDocument/hover","params":{"textDocument":{"uri":uri},"position":{"line":l,"character":c}}})
    while True:
        m = read()
        if m and m.get("id") == 10+i:
            r = m.get("result"); print(name, "->", (r["contents"]["value"].replace("\n"," | ") if r else r)); break
send({"jsonrpc":"2.0","id":99,"method":"shutdown","params":None}); read(); send({"jsonrpc":"2.0","method":"exit","params":None})
