import json, os, time
from dapclient import Dap

prog = os.path.abspath("dbg.lyr")
d = Dap()
r = d.req("initialize", {"adapterID":"lyric"})
print("capabilities:", json.dumps(r["body"]))
d.send("launch", {"program": prog, "args": [], "stopOnEntry": False})
d.event("initialized")
r = d.req("setBreakpoints", {"source": {"path": prog},
                             "breakpoints": [{"line": 10, "condition": "n == 20"}]})
print("setBreakpoints(condition n==20):", json.dumps(r["body"]), "success=", r["success"])
d.req("setExceptionBreakpoints", {"filters": ["all"]})
d.req("configurationDone")
ev = d.event("stopped", timeout=60)
print("stopped:", json.dumps(ev["body"]) if ev else None)

st = d.req("stackTrace", {"threadId": 1})
frames = st["body"]["stackFrames"]
print("frames:", [(f["name"], f["line"]) for f in frames])
fid = frames[0]["id"]

sc = d.req("scopes", {"frameId": fid})
print("scopes:", [(s["name"], s["variablesReference"]) for s in sc["body"]["scopes"]])
for s in sc["body"]["scopes"]:
    v = d.req("variables", {"variablesReference": s["variablesReference"]})
    print("  %s:" % s["name"], [(x["name"], x["value"], x["type"]) for x in v["body"]["variables"]])

for expr in ["v.x", "xs[0]", "xs.[0]", "n", "total + 1", "total", "xs.length", "v"]:
    r = d.req("evaluate", {"expression": expr, "frameId": fid})
    if r["success"]: print("evaluate %-12r -> %s" % (expr, json.dumps(r["body"])))
    else:            print("evaluate %-12r -> FAIL: %s" % (expr, r.get("message")))

for cmd, args in [("setVariable", {"variablesReference": 1, "name":"total", "value":"99"}),
                  ("completions", {"text":"v.","column":3}),
                  ("exceptionInfo", {"threadId":1}),
                  ("breakpointLocations", {"source":{"path":prog},"line":10}),
                  ("stepBack", {"threadId":1}),
                  ("restartFrame", {"frameId":fid}),
                  ("dataBreakpointInfo", {"name":"total","variablesReference":1}),
                  ("setFunctionBreakpoints", {"breakpoints":[{"name":"main"}]}),
                  ("loadedSources", {}),
                  ("disassemble", {"memoryReference":"0","instructionCount":4})]:
    r = d.req(cmd, args, timeout=20)
    print("%-24s -> %s" % (cmd, "OK" if r and r["success"] else ("FAIL: " + (r.get("message") if r else "timeout"))))

# how many times does the conditional breakpoint stop?
stops = 1
for _ in range(5):
    d.send("continue", {"threadId":1})
    ev = d.wait(lambda m: m.get("type")=="event" and m.get("event") in ("stopped","exited","terminated"), timeout=20)
    if ev is None: print("no further event"); break
    if ev["event"] != "stopped": print("ended with:", ev["event"], json.dumps(ev.get("body"))); break
    stops += 1
print("total stops at a breakpoint with condition 'n == 20':", stops)
