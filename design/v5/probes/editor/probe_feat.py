import time, json, sys
from lspclient import Client, uri_of

path = sys.argv[1]
c = Client(["--stdio","--debounce","0"])
i = c.request("initialize", {"processId": None, "rootUri": None, "capabilities": {}})
c.wait_response(i); c.notify("initialized", {})
text = open(path).read(); uri = uri_of(path)
c.notify("textDocument/didOpen", {"textDocument": {"uri": uri, "languageId":"lyric","version":1,"text":text}})
c.wait_diag(uri, timeout=120)

lines = text.split("\n")
# find the line with "p.x" for a member completion
target = None
for n, l in enumerate(lines):
    if "p.x" in l: target = (n, l.index("p.") + 2)
print("member-completion anchor:", target)

def timed(method, params, label):
    t0 = time.perf_counter()
    rid = c.request(method, params)
    t, m = c.wait_response(rid, timeout=120)
    ms = (time.perf_counter() - t0) * 1000
    if m is None: print("%-22s TIMEOUT" % label); return None
    if "error" in m: print("%-22s %.0f ms ERROR %s" % (label, ms, m["error"]["message"])); return None
    print("%-22s %.0f ms" % (label, ms)); return m["result"]

if target:
    r = timed("textDocument/completion", {"textDocument":{"uri":uri},"position":{"line":target[0],"character":target[1]}}, "completion (member)")
    if r: print("   items:", [it["label"] for it in (r["items"] if isinstance(r, dict) else r)][:15])

# scope completion in the middle of main's body
r = timed("textDocument/completion", {"textDocument":{"uri":uri},"position":{"line":len(lines)-3,"character":4}}, "completion (scope)")
if r:
    items = r["items"] if isinstance(r, dict) else r
    print("   n=%d first 25:" % len(items), [it["label"] for it in items][:25])
    print("   has docs?", any("documentation" in it for it in items), "has detail?", any("detail" in it for it in items))
    print("   kinds:", sorted({it.get("kind") for it in items}))

r = timed("textDocument/inlayHint", {"textDocument":{"uri":uri},"range":{"start":{"line":0,"character":0},"end":{"line":len(lines),"character":0}}}, "inlayHint")
if r: print("   hints:", [(h["position"]["line"], h["label"]) for h in r][:10])

if target:
    r = timed("textDocument/hover", {"textDocument":{"uri":uri},"position":{"line":target[0],"character":target[1]-2}}, "hover")
    if r: print("   hover:", json.dumps(r)[:300])

r = timed("textDocument/semanticTokens/full", {"textDocument":{"uri":uri}}, "semanticTokens/full")
if r: print("   tokens:", len(r["data"])//5)

r = timed("textDocument/formatting", {"textDocument":{"uri":uri},"options":{"tabSize":4,"insertSpaces":True}}, "formatting")
if r is not None: print("   edits:", len(r))
