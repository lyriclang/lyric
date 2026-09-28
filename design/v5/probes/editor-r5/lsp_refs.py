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
def uri_of(rel): return "file:///" + os.path.abspath(rel).replace("\\", "/")
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":None,"rootUri":uri_of("pj"),"capabilities":{}}})
read(); send({"jsonrpc":"2.0","method":"initialized","params":{}})
for rel in ["pj/src/util.lyr", "pj/tests/util_test.lyr"]:
    send({"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":uri_of(rel),"languageId":"lyric","version":1,"text":open(rel,encoding="utf-8").read()}}})
seen = 0; t0 = time.time()
while seen < 2 and time.time()-t0 < 15:
    m = read()
    if m and m.get("method") == "textDocument/publishDiagnostics": seen += 1
send({"jsonrpc":"2.0","id":11,"method":"textDocument/references","params":{"textDocument":{"uri":uri_of("pj/src/util.lyr")},"position":{"line":0,"character":8},"context":{"includeDeclaration":True}}})
while True:
    m = read()
    if m and m.get("id") == 11:
        print("references of util.twice from util.lyr:", sorted(set(os.path.basename(r["uri"]) for r in (m.get("result") or []))), "| error:", m.get("error")); break
send({"jsonrpc":"2.0","id":12,"method":"textDocument/rename","params":{"textDocument":{"uri":uri_of("pj/tests/util_test.lyr")},"position":{"line":3,"character":40},"newName":"thrice"}})
while True:
    m = read()
    if m and m.get("id") == 12:
        r = m.get("result"); print("rename twice from test file:", (sorted(os.path.basename(u) for u in r["changes"]) if r else r), "| error:", m.get("error")); break
send({"jsonrpc":"2.0","id":99,"method":"shutdown","params":None}); read(); send({"jsonrpc":"2.0","method":"exit","params":None})
