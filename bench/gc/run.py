#!/usr/bin/env python3
"""Measurement point 5 (design/v5/spec/13 M11 P12): the collector — binarytrees, cache, lru — each
in Lyric under its own collector, the same Lyric program under gc = "boehm", and the Go twin.
Builds them, checks that all print the same checksum, and prints per program the wall time (the
minimum of several runs) and the peak RSS (the largest of the runs, from wait4). The pauses come
with lyr_gc_stats() (M11 S9).

    python3 bench/gc/run.py [--runs 3] [--lyric5 <exe>] [benches...]

Linux and macOS (wait4). A Go twin without a go on the PATH is left out with a note."""
import argparse, glob, os, pathlib, shutil, subprocess, sys, tempfile, time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent.parent
BENCHES = ['binarytrees', 'cache', 'lru']
OUT = HERE / 'out'
TWINS = ['Lyric', 'Lyric (Boehm)', 'Go']

def run(cmd, cwd=None):
    return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True)

def must(result, what):
    if result.returncode != 0:
        print(f'{what} failed:\n{result.stdout}{result.stderr}', file=sys.stderr)
        sys.exit(2)

def build_lyric(name, lyric5, gc):
    must(run([lyric5, 'build', str(HERE / name / f'{name}.lyr'), '--profile', 'release', '--gc', gc], cwd=ROOT),
         f'Lyric {name} ({gc})')
    found = sorted(glob.glob(str(ROOT / 'out' / 'release' / '*' / name)))
    if not found:
        print(f'no binary for {name} under {ROOT / "out" / "release"}', file=sys.stderr); sys.exit(2)
    exe = OUT / f'lyric-{gc}' / name
    exe.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(found[-1], exe)  # the next build of the same name writes the same path
    return [str(exe)]

def build_go(name):
    go = shutil.which('go')
    if not go:
        print(f'{name}: no go on the PATH, the Go twin is left out', file=sys.stderr); return None
    exe = OUT / 'go' / name
    exe.parent.mkdir(parents=True, exist_ok=True)
    must(run([go, 'build', '-o', str(exe), '.'], cwd=HERE / name / 'go'), f'Go {name}')
    return [str(exe)]

def once(cmd):
    """One run: the wall time, the peak RSS in bytes, the output."""
    with tempfile.TemporaryFile('w+') as out, tempfile.TemporaryFile('w+') as err:
        start = time.perf_counter()
        child = subprocess.Popen(cmd, stdout=out, stderr=err)
        _, status, usage = os.wait4(child.pid, 0)
        elapsed = time.perf_counter() - start
        child.returncode = os.waitstatus_to_exitcode(status)
        out.seek(0); err.seek(0)
        if child.returncode != 0:
            print(f'{cmd} exited {child.returncode}:\n{err.read()}', file=sys.stderr); sys.exit(2)
        rss = usage.ru_maxrss if sys.platform == 'darwin' else usage.ru_maxrss * 1024
        return elapsed, rss, out.read().strip()

def measure(cmd, runs):
    best, peak, output = None, 0, None
    for _ in range(runs):
        elapsed, rss, output = once(cmd)
        best = elapsed if best is None else min(best, elapsed)
        peak = max(peak, rss)
    return best, peak, output

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--runs', type=int, default=3)
    ap.add_argument('--lyric5', default=str(ROOT / 'src' / 'Lyric5' / 'bin' / 'Release' / 'net10.0' / 'lyric5'))
    ap.add_argument('benches', nargs='*', help='the programs to measure (default: all)')
    args = ap.parse_args()

    rows = []
    for name in args.benches or BENCHES:
        twins = {'Lyric': build_lyric(name, args.lyric5, 'immix'),
                 'Lyric (Boehm)': build_lyric(name, args.lyric5, 'boehm'),
                 'Go': build_go(name)}
        times, peaks, outputs = {}, {}, {}
        for twin, cmd in twins.items():
            if cmd is None: continue
            times[twin], peaks[twin], outputs[twin] = measure(cmd, args.runs)
        if len(set(outputs.values())) != 1:
            print(f'{name}: the twins disagree: {outputs}', file=sys.stderr); sys.exit(2)
        rows.append((name, outputs['Lyric'], times, peaks))

    print('| bench | checksum | ' + ' | '.join(TWINS) + ' | Lyric / Boehm | Lyric / Go |')
    print('|---|---|' + '---|' * len(TWINS) + '---|---|')
    for name, checksum, times, peaks in rows:
        cells = [f'{times[t]:.3f} s, {peaks[t] / 2**20:.0f} MiB' if t in times else '—' for t in TWINS]
        boehm = f'{times["Lyric"] / times["Lyric (Boehm)"]:.2f}'
        go = f'{times["Lyric"] / times["Go"]:.2f}' if 'Go' in times else '—'
        print(f'| {name} | {checksum} | ' + ' | '.join(cells) + f' | {boehm} | {go} |')
    print(f'\nwall time: the minimum of {args.runs} runs of the whole process; RSS: the largest peak of them')
    return 0

if __name__ == '__main__':
    sys.exit(main())
