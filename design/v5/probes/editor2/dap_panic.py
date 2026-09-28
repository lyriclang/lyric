import sys, os, json, time
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from dapclient import Dap
HERE = os.path.dirname(os.path.abspath(__file__))
prog = os.path.join(HERE, "panic.lyr")
d = Dap()
d.req("initialize", {"adapterID": "lyric"})
d.send("launch", {"program": prog, "args": [], "stopOnEntry": False})
d.event("initialized")
r = d.req("setExceptionBreakpoints", {"filters": ["all", "uncaught"]})
print("setExceptionBreakpoints ->", json.dumps(r.get("success")), json.dumps(r.get("body"))[:80])
d.req("configurationDone")
print("EVENT SEQUENCE ON A PANIC (kept reading past 'exited'):")
for _ in range(25):
    ev = d.wait(lambda m: m.get("type") == "event", timeout=25)
    if ev is None:
        print("  (no more events)"); break
    print("  event %-12s %s" % (ev["event"], json.dumps(ev.get("body"))[:110]))
    if ev["event"] == "terminated": break
d.p.kill()
