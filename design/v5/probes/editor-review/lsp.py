import json, subprocess, sys, os, time, threading

LYRLS = r"C:/Users/Olivier/CLionProjects/lyric/src/Lyrls/bin/Debug/net10.0/lyrls.dll"
STDLIB = r"C:/Users/Olivier/CLionProjects/lyric/stdlib"

p = subprocess.Popen(["dotnet", LYRLS, "--stdlib", STDLIB],
                     stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)

def send(obj):
    b = json.dumps(obj).encode("utf-8")
    p.stdin.write(b"Content-Length: %d\r\n\r\n" % len(b) + b)
    p.stdin.flush()

def read():
    hdr = b""
    while b"\r\n\r\n" not in hdr:
        c = p.stdout.read(1)
        if not c: return None
        hdr += c
    n = int([l for l in hdr.decode().split("\r\n") if l.lower().startswith("content-length")][0].split(":")[1])
    return json.loads(p.stdout.read(n).decode("utf-8"))

src = open(sys.argv[1], encoding="utf-8").read()
path = os.path.abspath(sys.argv[1]).replace("\\","/")
uri = "file:///" + path

send({"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId":None,"rootUri":None,"capabilities":{}}})
init = read()
print("== CAPABILITIES ==")
print(json.dumps(init["result"]["capabilities"], indent=1))
send({"jsonrpc":"2.0","method":"initialized","params":{}})
send({"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":uri,"languageId":"lyric","version":1,"text":src}}})

reqs = [
 ("textDocument/codeAction", {"textDocument":{"uri":uri},"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"context":{"diagnostics":[]}}),
 ("textDocument/typeDefinition", {"textDocument":{"uri":uri},"position":{"line":0,"character":0}}),
 ("textDocument/implementation", {"textDocument":{"uri":uri},"position":{"line":0,"character":0}}),
 ("textDocument/documentHighlight", {"textDocument":{"uri":uri},"position":{"line":0,"character":0}}),
 ("textDocument/selectionRange", {"textDocument":{"uri":uri},"positions":[{"line":0,"character":0}]}),
 ("textDocument/diagnostic", {"textDocument":{"uri":uri}}),
 ("textDocument/codeLens", {"textDocument":{"uri":uri}}),
 ("textDocument/inlineValue", {"textDocument":{"uri":uri},"range":{"start":{"line":0,"character":0},"end":{"line":1,"character":0}},"context":{}}),
 ("textDocument/semanticTokens/range", {"textDocument":{"uri":uri},"range":{"start":{"line":0,"character":0},"end":{"line":5,"character":0}}}),
 ("textDocument/semanticTokens/full", {"textDocument":{"uri":uri}}),
 ("textDocument/formatting", {"textDocument":{"uri":uri},"options":{"tabSize":4,"insertSpaces":True}}),
]
rid = 10
for method, params in reqs:
    rid += 1
    send({"jsonrpc":"2.0","id":rid,"method":method,"params":params})

# completion at a marked position
line_no, col = None, None
for i, l in enumerate(src.split("\n")):
    if "@@HERE@@" in l:
        line_no, col = i, l.index("@@HERE@@")
if line_no is not None:
    rid += 1
    send({"jsonrpc":"2.0","id":rid,"method":"textDocument/completion","params":{"textDocument":{"uri":uri},"position":{"line":line_no,"character":col}}})

print("== RESPONSES ==")
deadline = time.time() + 90
seen = 0
while seen < len(reqs) + (1 if line_no is not None else 0) and time.time() < deadline:
    m = read()
    if m is None: break
    if "id" not in m: 
        if m.get("method") == "textDocument/publishDiagnostics":
            print("  [diag event] count=", len(m["params"]["diagnostics"]))
        continue
    seen += 1
    if "error" in m:
        print(f"id={m['id']}  ERROR {m['error']['code']}: {m['error']['message'][:80]}")
    else:
        r = m["result"]
        if isinstance(r, dict) and "data" in r:
            print(f"id={m['id']}  OK semanticTokens: {len(r['data'])//5} tokens")
        elif isinstance(r, list):
            print(f"id={m['id']}  OK list of {len(r)}")
            if r and isinstance(r[0], dict) and "label" in r[0]:
                labels = [x["label"] for x in r]
                kw = [k for k in ("if","match","return","while","for","let","var","fn","else") if k in labels]
                print(f"        keywords present: {kw}")
                print(f"        first 15: {labels[:15]}")
        elif isinstance(r, dict) and "items" in r:
            labels = [x["label"] for x in r["items"]]
            kw = [k for k in ("if","match","return","while","for","let","var","fn","else") if k in labels]
            print(f"id={m['id']}  OK completion {len(labels)} items; keywords present: {kw}")
            print(f"        first 15: {labels[:15]}")
        else:
            print(f"id={m['id']}  OK {str(r)[:120]}")
p.kill()
