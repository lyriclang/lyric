import json, os, sys
from dapclient import Dap
prog = os.path.abspath("dbg.lyr")

def run(bp, label):
    d = Dap()
    d.req("initialize", {"adapterID":"lyric"})
    d.send("launch", {"program": prog, "args": [], "stopOnEntry": False})
    d.event("initialized")
    r = d.req("setBreakpoints", {"source": {"path": prog}, "breakpoints": [bp]})
    d.req("configurationDone")
    stops = 0; outputs = []
    while True:
        ev = d.wait(lambda m: m.get("type")=="event" and m.get("event") in ("stopped","exited","terminated","output"), timeout=40)
        if ev is None: break
        if ev["event"] == "output":
            outputs.append(ev["body"]["output"].strip()); continue
        if ev["event"] != "stopped": break
        stops += 1
        d.send("continue", {"threadId":1})
        if stops > 8: break
    print("%-38s verified=%s stops=%d output=%s" % (label, r["body"]["breakpoints"], stops, outputs))

run({"line": 10}, "no condition (control)")
run({"line": 10, "condition": "n == 999"}, "condition 'n == 999' (never true)")
run({"line": 10, "hitCondition": "2"}, "hitCondition '2'")
run({"line": 10, "logMessage": "n is {n}"}, "logMessage (logpoint)")
