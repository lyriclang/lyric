import sys, os, json
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from dapclient import Dap
HERE = os.path.dirname(os.path.abspath(__file__))
src  = os.path.join(HERE, "eval.lyr")
for label, prog in (("SOURCE beside it (control)", os.path.join(HERE, "eval.lyr")),
                    ("BYTECODE in out/ (artifact away from source)", os.path.join(HERE, "out", "eval.lyrbc"))):
    d = Dap()
    d.req("initialize", {"adapterID":"lyric"})
    d.send("launch", {"program": prog, "args": [], "stopOnEntry": False})
    d.event("initialized")
    r = d.req("setBreakpoints", {"source": {"path": src}, "breakpoints": [{"line": 6}]})
    print(f"[{label}] setBreakpoints -> {json.dumps(r.get('body'))[:100]}")
    d.req("configurationDone")
    ev = d.wait(lambda m: m.get("type")=="event" and m.get("event") in ("stopped","exited"), timeout=25)
    print(f"[{label}] first event: {ev['event'] if ev else 'NONE'} {json.dumps(ev.get('body'))[:60] if ev else ''}")
    if ev and ev["event"] == "stopped":
        fr = d.req("stackTrace", {"threadId": 1})
        print(f"[{label}] top frame: {json.dumps(fr['body']['stackFrames'][0])[:140]}")
    d.p.kill()
