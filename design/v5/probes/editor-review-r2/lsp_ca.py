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
src = open(sys.argv[1], encoding="utf-8").read()
uri = "file:///" + os.path.abspath(sys.argv[1]).replace("\\", "/")
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":None,"rootUri":None,"capabilities":{"textDocument":{"documentSymbol":{"hierarchicalDocumentSymbolSupport":True}}}}})
read(); send({"jsonrpc":"2.0","method":"initialized","params":{}})
send({"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":uri,"languageId":"lyric","version":1,"text":src}}})
# wait for first diagnostics
while True:
    m = read()
    if m and m.get("method") == "textDocument/publishDiagnostics": print("diagnostics:", m["params"]["diagnostics"]); break
reqs = [
 ("codeAction", "textDocument/codeAction", {"textDocument":{"uri":uri},"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":5}},"context":{"diagnostics":[]}}),
 ("hover-on-decl 'twice'", "textDocument/hover", {"textDocument":{"uri":uri},"position":{"line":0,"character":4}}),
 ("hover-on-use 'twice'", "textDocument/hover", {"textDocument":{"uri":uri},"position":{"line":4,"character":13}}),
 ("hover-on-decl 'x'", "textDocument/hover", {"textDocument":{"uri":uri},"position":{"line":4,"character":8}}),
 ("hover-on-use 'x'", "textDocument/hover", {"textDocument":{"uri":uri},"position":{"line":5,"character":11}}),
 ("formatting", "textDocument/formatting", {"textDocument":{"uri":uri},"options":{"tabSize":4,"insertSpaces":True}}),
]
rid = 10
for label, method, params in reqs:
    rid += 1; send({"jsonrpc":"2.0","id":rid,"method":method,"params":params})
    while True:
        m = read()
        if m and m.get("id") == rid:
            print(label, "->", json.dumps(m.get("result", m.get("error")))[:300]); break
send({"jsonrpc":"2.0","id":99,"method":"shutdown","params":None}); read(); send({"jsonrpc":"2.0","method":"exit","params":None})
