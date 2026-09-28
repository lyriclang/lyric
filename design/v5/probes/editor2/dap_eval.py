import sys, os, json
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from dapclient import Dap
HERE = os.path.dirname(os.path.abspath(__file__))
prog = os.path.join(HERE, "eval.lyr")
d = Dap()
d.req("initialize", {"adapterID":"lyric"})
d.send("launch", {"program": prog, "args": [], "stopOnEntry": False})
d.event("initialized")
d.req("setBreakpoints", {"source": {"path": prog}, "breakpoints": [{"line": 6}]})
d.req("configurationDone")
ev = d.event("stopped")
print("stopped:", json.dumps(ev.get("body")))
fr = d.req("stackTrace", {"threadId": 1})
fid = fr["body"]["stackFrames"][0]["id"]
for expr in ("xs", "xs.[0]", "xs.[9]", "xs[0]", "n", "1/0", "xs.[1] + 1"):
    r = d.req("evaluate", {"expression": expr, "frameId": fid, "context": "watch"})
    print(f"  evaluate {expr!r:14} success={r.get('success')} -> {json.dumps(r.get('body') or r.get('message'))[:90]}")
# is the session still alive?
r = d.req("evaluate", {"expression": "n", "frameId": fid, "context": "watch"})
print("SESSION ALIVE AFTERWARDS:", r.get("success"), json.dumps(r.get("body"))[:60])
d.req("continue", {"threadId": 1})
for _ in range(6):
    e = d.wait(lambda m: m.get("type")=="event", timeout=20)
    if e is None: break
    print("  event", e["event"], json.dumps(e.get("body"))[:70])
    if e["event"] == "terminated": break
d.p.kill()
