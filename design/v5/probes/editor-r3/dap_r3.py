import sys, os, json
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from dapclient import Dap
HERE = os.path.dirname(os.path.abspath(__file__))
prog = os.path.join(HERE, "loop.lyr")
def run(label, bp, launch_extra=None, evals=()):
    d = Dap(); d.req("initialize", {"adapterID":"lyric"})
    args = {"program": prog, "args": [], "stopOnEntry": False}; args.update(launch_extra or {})
    d.send("launch", args); d.event("initialized")
    r = d.req("setBreakpoints", {"source": {"path": prog}, "breakpoints": [bp]})
    print(f"[{label}] setBreakpoints -> {json.dumps(r.get('body'))[:80]}")
    d.req("configurationDone")
    stops = 0; outputs = []
    while True:
        ev = d.wait(lambda m: m.get("type")=="event" and m.get("event") in ("stopped","exited","terminated","output"), timeout=30)
        if ev is None: print("  TIMEOUT"); break
        if ev["event"] == "output": outputs.append(ev["body"].get("output","").strip()); continue
        if ev["event"] == "stopped":
            stops += 1
            if stops == 1 and evals:
                fr = d.req("stackTrace", {"threadId": 1})["body"]["stackFrames"][0]["id"]
                for e in evals:
                    er = d.req("evaluate", {"expression": e, "frameId": fr, "context": "watch"})
                    print(f"  evaluate {e!r}: success={er.get('success')} {json.dumps(er.get('body') or er.get('message'))[:70]}")
            d.req("continue", {"threadId": 1}); continue
        if ev["event"] == "terminated": break
    print(f"[{label}] stops={stops} outputs={outputs}")
    d.p.kill()
run("plain (control)", {"line": 7})
run("condition n == 999", {"line": 7, "condition": "n == 999"})
run("hitCondition 2", {"line": 7, "hitCondition": "2"})
run("logMessage", {"line": 7, "logMessage": "n is {n}"})
run("evaluate forms", {"line": 7}, evals=("xs", "xs.[0]", "xs[0]", "n", "xs.[1] + 1"))
run("noDebug=true (VS Code 'Run Without Debugging')", {"line": 7}, launch_extra={"noDebug": True})
