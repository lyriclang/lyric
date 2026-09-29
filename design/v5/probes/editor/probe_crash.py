import json, os
from dapclient import Dap
prog = os.path.abspath("crash.lyr")
d = Dap()
d.req("initialize", {"adapterID":"lyric"})
d.send("launch", {"program": prog, "args": [], "stopOnEntry": False})
d.event("initialized")
d.req("setExceptionBreakpoints", {"filters": ["all", "uncaught"]})
d.req("configurationDone")
for _ in range(20):
    ev = d.wait(lambda m: m.get("type")=="event", timeout=40)
    if ev is None: print("(no more events)"); break
    print("event %-12s %s" % (ev["event"], json.dumps(ev.get("body"))[:160]))
    if ev["event"] in ("exited","terminated"): break
