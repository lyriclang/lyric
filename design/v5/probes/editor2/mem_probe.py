import sys, os, time, glob, subprocess, json
sys.path.insert(0, r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor")
from lspclient import Client

PROJ = r"C:/Users/Olivier/AppData/Local/Temp/claude/C--Users-Olivier-CLionProjects-lyric/6d1f3425-b9ac-45c5-b685-291491b2d632/scratchpad/v5-design/probes/editor/proj"

def uri(p): return "file:///" + p.replace("\\", "/").lstrip("/")

def ws(pid):
    out = subprocess.run(["powershell", "-NoProfile", "-Command",
                          f"(Get-Process -Id {pid}).WorkingSet64"],
                         capture_output=True, text=True).stdout.strip()
    try: return int(out) / (1024*1024)
    except: return -1

def run(label, files, bursts):
    c = Client()
    i = c.request("initialize", {"processId": None, "rootUri": uri(PROJ), "capabilities": {}})
    c.wait(lambda m: m.get("id") == i)
    c.notify("initialized", {})
    time.sleep(1.0)
    base = ws(c.p.pid)
    texts = {}
    for f in files:
        t = open(f, encoding="utf-8").read()
        texts[f] = t
        c.notify("textDocument/didOpen", {"textDocument": {"uri": uri(f), "languageId": "lyric",
                                                            "version": 1, "text": t}})
    c.wait(lambda m: m.get("method") == "textDocument/publishDiagnostics", timeout=60)
    time.sleep(2.0)
    after_open = ws(c.p.pid)
    v = 1
    for b in range(bursts):
        for f in files:
            v += 1
            c.notify("textDocument/didChange", {
                "textDocument": {"uri": uri(f), "version": v},
                "contentChanges": [{"text": texts[f] + f"\n// {v}\n"}]})
        time.sleep(0.05)
    peak = 0
    t_end = time.perf_counter() + 12
    while time.perf_counter() < t_end:
        peak = max(peak, ws(c.p.pid))
        time.sleep(0.3)
    time.sleep(3.0)
    settled = ws(c.p.pid)
    print(f"[{label}] files={len(files)} bursts={bursts}: "
          f"base {base:.0f} MB -> after open {after_open:.0f} MB -> peak {peak:.0f} MB "
          f"-> settled {settled:.0f} MB")
    c.p.kill()

files = sorted(glob.glob(os.path.join(PROJ, "src", "*.lyr")))
run("PROJECT, 8 open", files[:8], 4)
run("PROJECT, 1 open (control)", files[:1], 4)
