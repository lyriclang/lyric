import time, json
from lspclient import Client, uri_of
c = Client(["--stdio","--debounce","0"])
i = c.request("initialize", {"processId": None, "rootUri": None, "capabilities": {}})
c.wait_response(i); c.notify("initialized", {})
path = "proj/src/m30.lyr"; text = open(path).read(); uri = uri_of(path)
t0 = time.perf_counter()
c.notify("textDocument/didOpen", {"textDocument": {"uri": uri, "languageId":"lyric","version":1,"text":text}})
time.sleep(25)
print("asked for:", uri)
for t, m in c.msgs:
    meth = m.get("method")
    if meth == "textDocument/publishDiagnostics":
        print("%.1fs  publishDiagnostics %s  n=%d" % (t-t0, m["params"]["uri"], len(m["params"]["diagnostics"])))
    elif meth == "window/logMessage":
        print("%.1fs  log: %s" % (t-t0, m["params"]["message"][:160]))
    else:
        print("%.1fs  %s" % (t-t0, meth or m))
