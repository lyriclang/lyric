import json, subprocess, sys, os, time
LYRLS = "C:/Users/Olivier/CLionProjects/lyric/src/Lyrls/bin/Debug/net10.0/lyrls.dll"
STD = "C:/Users/Olivier/CLionProjects/lyric/stdlib"
p = subprocess.Popen(["dotnet", LYRLS, "--stdlib", STD, "--debounce", "0"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
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
send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":None,"rootUri":None,"capabilities":{}}})
read(); send({"jsonrpc":"2.0","method":"initialized","params":{}})
t0 = time.perf_counter()
send({"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":uri,"languageId":"lyric","version":1,"text":src}}})
while True:
    m = read()
    if m and m.get("method") == "textDocument/publishDiagnostics": break
print("first diagnostics after didOpen: %.0f ms" % ((time.perf_counter()-t0)*1000))
lat = []
for v in range(2, 12):
    text = src + ("\n// edit %d\n" % v)
    t0 = time.perf_counter()
    send({"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":uri,"version":v},"contentChanges":[{"text":text}]}})
    while True:
        m = read()
        if m and m.get("method") == "textDocument/publishDiagnostics": break
    lat.append((time.perf_counter()-t0)*1000)
lat.sort(); print("didChange->diagnostics, 10 samples, debounce 0: min %.0f  median %.0f  max %.0f ms" % (lat[0], lat[len(lat)//2], lat[-1]))
send({"jsonrpc":"2.0","id":99,"method":"shutdown","params":None}); read(); send({"jsonrpc":"2.0","method":"exit","params":None})
