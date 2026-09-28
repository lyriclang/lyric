import sys, os, json, time
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from lspclient import Client
HERE = os.path.dirname(os.path.abspath(__file__))
def uri(p): return "file:///" + p.replace("\\","/").lstrip("/")
GOOD = "fn main(): int {\n    let x = 1;\n    \n    return x - 1;\n}\n"
path = os.path.join(HERE, "good.lyr"); open(path,"w",newline="").write(GOOD)
c = Client()
i = c.request("initialize", {"processId":None,"rootUri":None,"capabilities":{"textDocument":{"documentSymbol":{"hierarchicalDocumentSymbolSupport":True}}}})
_, init = c.wait(lambda m: m.get("id")==i)
print("capabilities keys:", sorted(init["result"]["capabilities"].keys()))
c.notify("initialized",{})
t0=time.perf_counter()
c.notify("textDocument/didOpen", {"textDocument":{"uri":uri(path),"languageId":"lyric","version":1,"text":GOOD}})
# EXPECT (dossier): semanticTokens immediately after didOpen -> null
rid = c.request("textDocument/semanticTokens/full", {"textDocument":{"uri":uri(path)}})
_, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=30)
print("semanticTokens immediately:", "null" if m.get("result") is None else "data")
td, dm = c.wait(lambda m: m.get("method")=="textDocument/publishDiagnostics", timeout=60)
print("first diagnostics after %.0f ms; keys=%s" % ((td-t0)*1000, sorted(dm["params"]["diagnostics"][0].keys()) if dm["params"]["diagnostics"] else "(none)"))
time.sleep(0.3)
rid = c.request("textDocument/semanticTokens/full", {"textDocument":{"uri":uri(path)}})
_, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=30)
print("semanticTokens after analysis:", "null" if m.get("result") is None else "data")
# EXPECT: codeAction -> error -32601
rid = c.request("textDocument/codeAction", {"textDocument":{"uri":uri(path)},"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":1}},"context":{"diagnostics":[]}})
_, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=30)
print("codeAction:", json.dumps(m.get("error", m.get("result")))[:120])
# EXPECT: hover on 'main' decl -> null ; hover on x in 'return x - 1' -> data
for label,(l,ch) in (("hover decl 'main'",(0,4)),("hover use 'x'",(3,11))):
    rid = c.request("textDocument/hover", {"textDocument":{"uri":uri(path)},"position":{"line":l,"character":ch}})
    _, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=30)
    print(label+":", "null" if m.get("result") is None else json.dumps(m["result"])[:100])
# EXPECT: completion at statement position (line 2, col 4) has no keywords
rid = c.request("textDocument/completion", {"textDocument":{"uri":uri(path)},"position":{"line":2,"character":4}})
_, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=30)
r = m.get("result"); items = r if isinstance(r, list) else (r or {}).get("items", [])
labels = [it["label"] for it in items]
print("completion count:", len(labels), "| keywords present:", [k for k in ("if","match","return","let","var","while","fn") if k in labels])
print("sample labels:", labels[:12])
# NEW: diagnostics for an untitled: buffer? EXPECT: none ever
c.notify("textDocument/didOpen", {"textDocument":{"uri":"untitled:Untitled-1","languageId":"lyric","version":1,"text":"fn main(): int { return x; }\n"}})
td2, dm2 = c.wait(lambda m: m.get("method")=="textDocument/publishDiagnostics" and m["params"]["uri"].startswith("untitled"), timeout=3)
print("untitled diagnostics:", "none within 3 s" if dm2 is None else dm2["params"]["diagnostics"])
# NEW: didSave / didChangeWatchedFiles both accepted? and what does a bad didChange (range-based) do?
c.notify("textDocument/didChange", {"textDocument":{"uri":uri(path),"version":2},"contentChanges":[{"range":{"start":{"line":1,"character":12},"end":{"line":1,"character":13}},"text":"2"}]})
td3, dm3 = c.wait(lambda m: m.get("method")=="textDocument/publishDiagnostics" and m["params"]["uri"]==uri(path), timeout=5)
print("range-based didChange -> diagnostics:", "none within 5 s" if dm3 is None else [d["message"] for d in dm3["params"]["diagnostics"]][:3])
rid = c.request("textDocument/hover", {"textDocument":{"uri":uri(path)},"position":{"line":3,"character":11}})
_, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=30)
print("hover after range-based didChange:", json.dumps(m.get("result", m.get("error")))[:120])
c.p.kill()
