import sys, time
from lspclient import Client, uri_of

debounce = sys.argv[1]; path = sys.argv[2]
c = Client(["--stdio", "--debounce", debounce])
i = c.request("initialize", {"processId": None, "rootUri": None, "capabilities": {}})
c.wait_response(i); c.notify("initialized", {})
text = open(path).read(); uri = uri_of(path)
t0 = time.perf_counter()
c.notify("textDocument/didOpen", {"textDocument": {"uri": uri, "languageId":"lyric","version":1,"text":text}})
t1, d = c.wait_diag(uri, timeout=120)
print("%-16s debounce=%-4s first=%.0f ms" % (path, debounce, (t1-t0)*1000), end="")
xs = []
for k in range(6):
    t0 = time.perf_counter()
    c.notify("textDocument/didChange", {"textDocument": {"uri": uri, "version": 2+k},
                                        "contentChanges": [{"text": text + "\n// e%d\n" % k}]})
    t1, d = c.wait_diag(uri, timeout=120)
    xs.append((t1-t0)*1000)
print("  edits: " + " ".join("%.0f" % x for x in xs) + " ms  (median %.0f)" % sorted(xs)[len(xs)//2])
rid = c.request("shutdown", {}); c.wait_response(rid); c.notify("exit", {})
