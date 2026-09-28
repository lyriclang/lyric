import sys, os, json, time
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from lspclient import Client

HERE = os.path.dirname(os.path.abspath(__file__))

def uri(p): return "file:///" + p.replace("\\", "/").lstrip("/")

BAD = """fn main(): int {
    let x = ;
    retrn x
"""
GOOD = """fn main(): int {
    let x = 1;
    return x - 1;
}
"""
UGLY = "fn main(  ):int{\n  let x=1;\n  return x-1;\n}\n"

for name, text, label in (("bad.lyr", BAD, "UNPARSEABLE"),
                          ("good.lyr", GOOD, "ALREADY FORMATTED"),
                          ("ugly.lyr", UGLY, "NEEDS FORMATTING")):
    path = os.path.join(HERE, name)
    open(path, "w", newline="").write(text)
    c = Client()
    i = c.request("initialize", {"processId": None, "rootUri": None, "capabilities": {}})
    c.wait(lambda m: m.get("id") == i)
    c.notify("initialized", {})
    u = uri(path)
    c.notify("textDocument/didOpen", {"textDocument": {"uri": u, "languageId": "lyric",
                                                        "version": 1, "text": text}})
    # ask for semantic tokens IMMEDIATELY, before any diagnostics arrive
    st = c.request("textDocument/semanticTokens/full", {"textDocument": {"uri": u}})
    t0 = time.perf_counter()
    _, m = c.wait(lambda m: m.get("id") == st, timeout=30)
    print(f"[{label}] semanticTokens IMMEDIATELY after didOpen -> "
          f"{'null' if m.get('result') is None else str(len(m['result'].get('data', [])) // 5) + ' tokens'}"
          f"  ({(time.perf_counter()-t0)*1000:.0f} ms)")
    # now wait for diagnostics
    td, dm = c.wait(lambda m: m.get("method") == "textDocument/publishDiagnostics", timeout=30)
    diags = dm["params"]["diagnostics"] if dm else []
    print(f"[{label}] first publishDiagnostics: {len(diags)} diagnostics; "
          f"keys of first = {sorted(diags[0].keys()) if diags else '-'}")
    st2 = c.request("textDocument/semanticTokens/full", {"textDocument": {"uri": u}})
    _, m2 = c.wait(lambda m: m.get("id") == st2, timeout=30)
    print(f"[{label}] semanticTokens AFTER diagnostics -> "
          f"{'null' if m2.get('result') is None else str(len(m2['result'].get('data', [])) // 5) + ' tokens'}")
    f = c.request("textDocument/formatting", {"textDocument": {"uri": u},
                                              "options": {"tabSize": 4, "insertSpaces": True}})
    _, fm = c.wait(lambda m: m.get("id") == f, timeout=30)
    r = fm.get("result")
    print(f"[{label}] formatting -> {'null' if r is None else (str(len(r)) + ' edits')}")
    print(f"[{label}] formatting raw = {json.dumps(r)[:120]}")
    print()
    c.p.kill()
