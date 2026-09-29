import sys, os, json
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from dapclient import Dap
HERE = os.path.dirname(os.path.abspath(__file__))
a = os.path.join(HERE,"pj2","src","a","util.lyr")
for label, prog in (("SOURCE main.lyr (control)", os.path.join(HERE,"pj2","src","main.lyr")),
                    ("ARTIFACT build/app.lyrbc",  os.path.join(HERE,"pj2","build","app.lyrbc"))):
    d = Dap()
    d.req("initialize", {"adapterID":"lyric"})
    d.send("launch", {"program": prog, "args": [], "stopOnEntry": False})
    d.event("initialized")
    r = d.req("setBreakpoints", {"source": {"path": a}, "breakpoints": [{"line": 4}]})
    print(f"[{label}] bp on a/util.lyr:4 -> {json.dumps(r.get('body'))[:80]}")
    d.req("configurationDone")
    stops = []
    for _ in range(6):
        ev = d.wait(lambda m: m.get("type")=="event" and m.get("event") in ("stopped","exited"), timeout=25)
        if ev is None or ev["event"]=="exited": break
        fr = d.req("stackTrace", {"threadId":1})
        top = fr["body"]["stackFrames"][0]
        stops.append(f"{top['name']} @ {top['source'].get('name')} (path {'yes' if top['source'].get('path') else 'NO'})")
        d.req("continue", {"threadId":1})
    print(f"[{label}] stops: {stops if stops else 'none'}")
    d.p.kill()
