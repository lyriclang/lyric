import json, subprocess, threading, time, os
DLL = r"C:/Users/Olivier/CLionProjects/lyric/src/Lyrdbg/bin/Debug/net10.0/lyrdbg.dll"

class Dap:
    def __init__(self):
        self.p = subprocess.Popen(["dotnet", DLL], stdin=subprocess.PIPE,
                                  stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
        self.seq = 0; self.msgs = []; self.cursor = 0
        self.cv = threading.Condition()
        threading.Thread(target=self._read, daemon=True).start()
    def _read(self):
        f = self.p.stdout
        while True:
            h = {}
            while True:
                line = f.readline()
                if not line: return
                line = line.strip()
                if not line: break
                k,_,v = line.decode().partition(":"); h[k.strip().lower()] = v.strip()
            body = f.read(int(h.get("content-length",0)))
            m = json.loads(body)
            with self.cv:
                self.msgs.append(m); self.cv.notify_all()
    def send(self, cmd, args=None):
        self.seq += 1
        obj = {"seq": self.seq, "type": "request", "command": cmd}
        if args is not None: obj["arguments"] = args
        d = json.dumps(obj).encode()
        self.p.stdin.write(b"Content-Length: %d\r\n\r\n" % len(d)); self.p.stdin.write(d); self.p.stdin.flush()
        return self.seq
    def wait(self, pred, timeout=60):
        end = time.time()+timeout
        with self.cv:
            while True:
                while self.cursor < len(self.msgs):
                    m = self.msgs[self.cursor]; self.cursor += 1
                    if pred(m): return m
                left = end-time.time()
                if left <= 0: return None
                self.cv.wait(left)
    def req(self, cmd, args=None, timeout=60):
        s = self.send(cmd, args)
        return self.wait(lambda m: m.get("type")=="response" and m.get("request_seq")==s, timeout)
    def event(self, name, timeout=60):
        return self.wait(lambda m: m.get("type")=="event" and m.get("event")==name, timeout)
