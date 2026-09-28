import sys, os, time
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from lspclient import Client
HERE = os.path.dirname(os.path.abspath(__file__))
def uri(p): return "file:///" + p.replace("\\","/").lstrip("/")
path = os.path.join(HERE, "good.lyr")
text = open(path, encoding="utf-8").read()
print("source:", repr(text))
CAPS = {"textDocument": {"documentSymbol": {"hierarchicalDocumentSymbolSupport": True}}}
for label, caps in (("WITH hierarchical", CAPS), ("WITHOUT (control)", {})):
    c = Client()
    i = c.request("initialize", {"processId":None,"rootUri":None,"capabilities":caps})
    c.wait(lambda m: m.get("id")==i); c.notify("initialized",{})
    c.notify("textDocument/didOpen", {"textDocument":{"uri":uri(path),"languageId":"lyric","version":1,"text":text}})
    c.wait(lambda m: m.get("method")=="textDocument/publishDiagnostics", timeout=60)
    time.sleep(0.4)
    rid = c.request("textDocument/documentSymbol", {"textDocument":{"uri":uri(path)}})
    _, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=30)
    r = m.get("result")
    print(f"[{label}] documentSymbol -> {'null' if r is None else str(len(r)) + ' items'}")
    for line, ch, what in ((0,4,"'main' in the declaration"), (2,11,"'x' in 'return x - 1'")):
        rid = c.request("textDocument/hover", {"textDocument":{"uri":uri(path)},"position":{"line":line,"character":ch}})
        _, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=30)
        r = m.get("result")
        print(f"[{label}] hover {what} -> {'null' if r is None else str(r)[:80]}")
    c.p.kill()
