import time, json
from lspclient import Client, uri_of

c = Client(["--stdio"])
i = c.request("initialize", {"processId": None, "rootUri": None, "capabilities": {}})
t, m = c.wait_response(i)
print("initialize ok:", "result" in m)
print("capabilities:", json.dumps(m["result"]["capabilities"], indent=1)[:1200])
c.notify("initialized", {})

text = open("big.lyr").read()
uri = uri_of("big.lyr")
t0 = time.perf_counter()
c.notify("textDocument/didOpen", {"textDocument": {"uri": uri, "languageId": "lyric", "version": 1, "text": text}})
t1, d = c.wait_diag(uri)
print("first diagnostics after %.0f ms, count=%d" % ((t1 - t0) * 1000, len(d["params"]["diagnostics"])))

# steady-state edits
for k in range(5):
    newtext = text + ("\n// edit %d\n" % k)
    t0 = time.perf_counter()
    c.notify("textDocument/didChange", {"textDocument": {"uri": uri, "version": 2 + k},
                                        "contentChanges": [{"text": newtext}]})
    t1, d = c.wait_diag(uri)
    print("edit %d -> diagnostics after %.0f ms" % (k, (t1 - t0) * 1000))

for method, params in [
    ("textDocument/codeAction", {"textDocument": {"uri": uri}, "range": {"start": {"line":0,"character":0}, "end": {"line":0,"character":1}}, "context": {"diagnostics": []}}),
    ("textDocument/typeDefinition", {"textDocument": {"uri": uri}, "position": {"line":35,"character":9}}),
    ("textDocument/implementation", {"textDocument": {"uri": uri}, "position": {"line":35,"character":9}}),
    ("textDocument/documentHighlight", {"textDocument": {"uri": uri}, "position": {"line":35,"character":9}}),
    ("textDocument/rangeFormatting", {"textDocument": {"uri": uri}, "range": {"start": {"line":0,"character":0}, "end": {"line":2,"character":0}}, "options": {"tabSize":4,"insertSpaces":True}}),
    ("textDocument/selectionRange", {"textDocument": {"uri": uri}, "positions": [{"line":35,"character":9}]}),
    ("textDocument/prepareCallHierarchy", {"textDocument": {"uri": uri}, "position": {"line":35,"character":9}}),
    ("textDocument/diagnostic", {"textDocument": {"uri": uri}}),
    ("textDocument/codeLens", {"textDocument": {"uri": uri}}),
    ("textDocument/documentLink", {"textDocument": {"uri": uri}}),
    ("textDocument/inlineValue", {"textDocument": {"uri": uri}, "range": {"start":{"line":0,"character":0},"end":{"line":1,"character":0}}, "context": {"frameId":0,"stoppedLocation":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}}}}),
    ("textDocument/semanticTokens/range", {"textDocument": {"uri": uri}, "range": {"start":{"line":0,"character":0},"end":{"line":3,"character":0}}}),
    ("workspace/executeCommand", {"command": "x"}),
]:
    rid = c.request(method, params)
    t, m = c.wait_response(rid, timeout=10)
    if m is None:
        print("%-42s TIMEOUT" % method)
    elif "error" in m:
        print("%-42s ERROR %s: %s" % (method, m["error"]["code"], m["error"]["message"]))
    else:
        r = json.dumps(m["result"])
        print("%-42s OK %s" % (method, r[:80]))

rid = c.request("shutdown", {})
c.wait_response(rid)
c.notify("exit", {})
