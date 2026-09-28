import json, subprocess, sys, threading, time, os

DLL = r"C:/Users/Olivier/CLionProjects/lyric/src/Lyrls/bin/Debug/net10.0/lyrls.dll"

class Client:
    def __init__(self, extra=()):
        self.p = subprocess.Popen(["dotnet", DLL, *extra],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL)
        self.id = 0
        self.lock = threading.Lock()
        self.msgs = []
        self.cursor = 0
        self.cv = threading.Condition()
        threading.Thread(target=self._read, daemon=True).start()

    def _read(self):
        f = self.p.stdout
        while True:
            headers = {}
            while True:
                line = f.readline()
                if not line: return
                line = line.strip()
                if not line: break
                k, _, v = line.decode().partition(":")
                headers[k.strip().lower()] = v.strip()
            n = int(headers.get("content-length", 0))
            body = f.read(n)
            m = json.loads(body)
            with self.cv:
                self.msgs.append((time.perf_counter(), m))
                self.cv.notify_all()

    def send(self, obj):
        data = json.dumps(obj).encode()
        with self.lock:
            self.p.stdin.write(b"Content-Length: %d\r\n\r\n" % len(data))
            self.p.stdin.write(data)
            self.p.stdin.flush()

    def request(self, method, params=None):
        self.id += 1
        i = self.id
        self.send({"jsonrpc":"2.0","id":i,"method":method,"params":params or {}})
        return i

    def notify(self, method, params=None):
        self.send({"jsonrpc":"2.0","method":method,"params":params or {}})

    def wait(self, pred, timeout=30):
        deadline = time.perf_counter() + timeout
        with self.cv:
            while True:
                while self.cursor < len(self.msgs):
                    t, m = self.msgs[self.cursor]; self.cursor += 1
                    if pred(m): return t, m
                left = deadline - time.perf_counter()
                if left <= 0: return None, None
                self.cv.wait(left)

    def wait_response(self, i, timeout=30):
        return self.wait(lambda m: m.get("id") == i and ("result" in m or "error" in m), timeout)

    def wait_diag(self, uri=None, timeout=30):
        return self.wait(lambda m: m.get("method") == "textDocument/publishDiagnostics"
                         and (uri is None or m["params"]["uri"] == uri), timeout)

def uri_of(path):
    p = os.path.abspath(path).replace("\\", "/")
    return "file:///" + p
