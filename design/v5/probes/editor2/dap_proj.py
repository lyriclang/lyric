import sys, os, json
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from dapclient import Dap
HERE = os.path.dirname(os.path.abspath(__file__))
helper = os.path.join(HERE, "pj", "src", "helper.lyr")
for label, prog in (("SOURCE pj/src/main.lyr (control)", os.path.join(HERE,"pj","src","main.lyr")),
                    ("ARTIFACT pj/build/app.lyrbc",      os.path.join(HERE,"pj","build","app.lyrbc"))):
    d = Dap()
    d.req("initialize", {"adapterID":"lyric"})
    d.send("launch", {"program": prog, "args": [], "stopOnEntry": False})
    d.event("initialized")
    r = d.req("setBreakpoints", {"source": {"path": helper}, "breakpoints": [{"line": 4}]})
    print(f"[{label}] setBreakpoints(helper.lyr:4) -> {json.dumps(r.get('body'))[:90]}")
    d.req("configurationDone")
    ev = d.wait(lambda m: m.get("type")=="event" and m.get("event") in ("stopped","exited"), timeout=25)
    print(f"[{label}] first event: {ev['event'] if ev else 'NONE'}")
    if ev and ev["event"]=="stopped":
        fr = d.req("stackTrace", {"threadId": 1})
        print(f"[{label}] top frame: {json.dumps(fr['body']['stackFrames'][0])[:150]}")
    d.p.kill()
