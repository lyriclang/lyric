import sys, os, time, glob
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from lspclient import Client
PROJ = r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor/proj"
def uri(p): return "file:///" + p.replace("\\","/").lstrip("/")
f = sorted(glob.glob(os.path.join(PROJ,"src","*.lyr")))[0]
text = open(f, encoding="utf-8").read()
for trial in range(2):
    c = Client()
    i = c.request("initialize", {"processId":None,"rootUri":uri(PROJ),"capabilities":{}})
    c.wait(lambda m: m.get("id")==i); c.notify("initialized",{})
    t0 = time.perf_counter()
    c.notify("textDocument/didOpen", {"textDocument":{"uri":uri(f),"languageId":"lyric","version":1,"text":text}})
    results = []
    for meth, params in (("textDocument/semanticTokens/full", {"textDocument":{"uri":uri(f)}}),
                         ("textDocument/hover", {"textDocument":{"uri":uri(f)},"position":{"line":0,"character":5}}),
                         ("textDocument/documentSymbol", {"textDocument":{"uri":uri(f)}}),
                         ("textDocument/foldingRange", {"textDocument":{"uri":uri(f)}}),
                         ("textDocument/inlayHint", {"textDocument":{"uri":uri(f)},"range":{"start":{"line":0,"character":0},"end":{"line":20,"character":0}}}),
                         ("textDocument/completion", {"textDocument":{"uri":uri(f)},"position":{"line":2,"character":0}})):
        rid = c.request(meth, params)
        _, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=60)
        r = m.get("result") if m else "TIMEOUT"
        kind = "null" if r is None else (f"{len(r)} items" if isinstance(r, list) else ("obj" if isinstance(r, dict) else str(r)[:20]))
        results.append(f"{meth.split('/')[-1]}={kind}")
    t1 = time.perf_counter()
    td, dm = c.wait(lambda m: m.get("method")=="textDocument/publishDiagnostics", timeout=120)
    print(f"trial {trial}: immediate answers ({(t1-t0)*1000:.0f} ms): " + ", ".join(results))
    print(f"   first publishDiagnostics after {(td-t0)*1000:.0f} ms" if td else "   no diagnostics")
    # control: same requests once the analysis is there
    again = []
    for meth, params in (("textDocument/semanticTokens/full", {"textDocument":{"uri":uri(f)}}),
                         ("textDocument/documentSymbol", {"textDocument":{"uri":uri(f)}})):
        rid = c.request(meth, params)
        _, m = c.wait(lambda m, rid=rid: m.get("id")==rid, timeout=60)
        r = m.get("result")
        again.append(f"{meth.split('/')[-1]}=" + ("null" if r is None else (f"{len(r)} items" if isinstance(r,list) else "data")))
    print("   CONTROL after analysis: " + ", ".join(again))
    c.p.kill()
