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
path = os.path.abspath(sys.argv[1]); src = open(path, encoding="utf-8").read()
uri = "file:///" + path.replace("\\", "/")
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":None,"rootUri":"file:///"+os.path.abspath("pj").replace("\\","/"),"capabilities":{}}})
read(); send({"jsonrpc":"2.0","method":"initialized","params":{}})
send({"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":uri,"languageId":"lyric","version":1,"text":src}}})
t0 = time.time()
while time.time()-t0 < 15:
    m = read()
    if m and m.get("method") == "textDocument/publishDiagnostics":
        print(os.path.basename(m["params"]["uri"]), [ (d.get("code"), d["message"]) for d in m["params"]["diagnostics"]]); break
send({"jsonrpc":"2.0","id":99,"method":"shutdown","params":None}); read(); send({"jsonrpc":"2.0","method":"exit","params":None})
