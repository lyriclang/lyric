#!/usr/bin/env python3
"""Measurement point 5 (design/v5/spec/13 M11 P12): the collector — binarytrees, cache, lru — each
in Lyric under its own collector, the same Lyric program under gc = "boehm", and the Go twin.
Builds them, checks that all print the same checksum on their first line, and prints per program
the wall time (the minimum of several runs), the peak RSS (the largest, from wait4) and the
collector's numbers from the program's second line — `gc <collections> <p99 ns> <max ns>`, from
lyr_gc_stat for Lyric and runtime/metrics for Go — of the fastest run. --ratchet fails where
Lyric is not better than Boehm in time, longest pause and RSS, or where cache's 99th percentile
of the pauses is 10 ms or more (P12).

    python3 bench/gc/run.py [--runs 3] [--lyric5 <exe>] [--ratchet] [benches...]

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
    """One run: the wall time, the peak RSS in bytes, the output's lines."""
    with tempfile.TemporaryFile('w+') as out, tempfile.TemporaryFile('w+') as err:
        start = time.perf_counter()
        child = subprocess.Popen(cmd, stdout=out, stderr=err)
        _, status, usage = os.wait4(child.pid, 0)
        elapsed = time.perf_counter() - start
        code = os.waitstatus_to_exitcode(status)
        out.seek(0); err.seek(0)
        if code != 0:
            print(f'{cmd} exited {code}:\n{err.read()}', file=sys.stderr); sys.exit(2)
        rss = usage.ru_maxrss if sys.platform == 'darwin' else usage.ru_maxrss * 1024
        return elapsed, rss, out.read().strip().splitlines()

def gc_numbers(lines):
    """(collections, p99 ns, max ns) from the `gc` line."""
    for line in lines[1:]:
        words = line.split()
        if len(words) == 4 and words[0] == 'gc':
            return int(words[1]), int(words[2]), int(words[3])
    return None

def measure(cmd, runs):
    best, peak, lines = None, 0, None
    for _ in range(runs):
        elapsed, rss, output = once(cmd)
        if best is None or elapsed < best:
            best, lines = elapsed, output
        peak = max(peak, rss)
    return best, peak, lines

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--runs', type=int, default=3)
    ap.add_argument('--lyric5', default=str(ROOT / 'src' / 'Lyric5' / 'bin' / 'Release' / 'net10.0' / 'lyric5'))
    ap.add_argument('--ratchet', action='store_true', help="exit 1 where Lyric misses P12's bounds")
    ap.add_argument('benches', nargs='*', help='the programs to measure (default: all)')
    args = ap.parse_args()

    rows = []
    for name in args.benches or BENCHES:
        twins = {'Lyric': build_lyric(name, args.lyric5, 'immix'),
                 'Lyric (Boehm)': build_lyric(name, args.lyric5, 'boehm'),
                 'Go': build_go(name)}
        results = {}
        for twin, cmd in twins.items():
            if cmd is None: continue
            results[twin] = measure(cmd, args.runs)
        checksums = {twin: lines[0] if lines else '' for twin, (_, _, lines) in results.items()}
        if len(set(checksums.values())) != 1:
            print(f'{name}: the twins disagree: {checksums}', file=sys.stderr); sys.exit(2)
        rows.append((name, checksums['Lyric'], results))

    print('| bench | twin | time | RSS | collections | pause p99 | pause max |')
    print('|---|---|---|---|---|---|---|')
    for name, checksum, results in rows:
        for twin in TWINS:
            if twin not in results: continue
            elapsed, rss, lines = results[twin]
            numbers = gc_numbers(lines)
            gc = (f'{numbers[0]} | {numbers[1] / 1e6:.2f} ms | {numbers[2] / 1e6:.2f} ms' if numbers else '— | — | —')
            print(f'| {name} | {twin} | {elapsed:.3f} s | {rss / 2**20:.0f} MiB | {gc} |')
    print(f'\ntime: the minimum of {args.runs} runs of the whole process, its numbers; RSS: the largest peak; '
          'the checksums agree')

    bad = []
    for name, _, results in rows:
        lyric, boehm = results['Lyric'], results['Lyric (Boehm)']
        lyric_gc, boehm_gc = gc_numbers(lyric[2]), gc_numbers(boehm[2])
        if lyric[0] >= boehm[0]: bad.append(f'{name}: time {lyric[0]:.3f} s, Boehm {boehm[0]:.3f} s')
        if lyric[1] >= boehm[1]: bad.append(f'{name}: RSS {lyric[1] >> 20} MiB, Boehm {boehm[1] >> 20} MiB')
        if lyric_gc and boehm_gc and lyric_gc[2] >= boehm_gc[2]:
            bad.append(f'{name}: longest pause {lyric_gc[2] / 1e6:.1f} ms, Boehm {boehm_gc[2] / 1e6:.1f} ms')
        if name == 'cache' and lyric_gc and lyric_gc[1] >= 10_000_000:
            bad.append(f'cache: p99 of the pauses {lyric_gc[1] / 1e6:.1f} ms, the bound 10 ms')
    if bad:
        print('\nratchet ' + ('FAILED' if args.ratchet else 'missed (not enforced)') + ':\n  ' + '\n  '.join(bad))
        if args.ratchet: return 1
    return 0

if __name__ == '__main__':
    sys.exit(main())
