import sys, os
sys.path.insert(0, "C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from dapclient import Dap
d = Dap()
print("init caps:", d.req("initialize", {"adapterID":"lyric"})["body"])
r = d.req("launch", {"program": os.path.abspath("cap.lyr"), "grant": "none"})
print("launch:", r.get("success"), r.get("message"))
d.req("configurationDone")
while True:
    m = d.wait(lambda m: m.get("type")=="event" and m.get("event") in ("output","exited","terminated"), 30)
    if m is None: print("timeout"); break
    print("event", m["event"], m.get("body"))
    if m["event"] == "terminated": break
