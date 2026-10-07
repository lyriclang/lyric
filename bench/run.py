#!/usr/bin/env python3
"""Measurement points 2 and 3 (design/v5/spec/13, "Messpunkte"): loops, arith and struct arrays
(after M3); a Map, a sort and string work (after M8a) — Lyric against C, Go and C# on the same
machine. Builds every twin, checks that all print the same checksum, times each as the minimum of
several runs, prints a table, and with --ratchet fails when Lyric is slower than 3x C or 1.5x Go
(the plan's bounds; point 3 has no C twin). --bound X is the wide fence for a shared CI runner,
whose timings are too noisy for the ratchet: it fails when Lyric is slower than X times Go.

    python3 bench/run.py [--runs 5] [--lyric5 <exe>] [--ratchet] [--bound 3.0]

C is built by the same compiler with the flags of Lyric's release profile (-O2, no contraction),
Go by 'go build', C# by 'dotnet build -c Release' and run through the host (its JIT start is in
the number, as it is for a user). A twin whose toolchain is missing is left out with a note."""
import argparse, glob, os, pathlib, shutil, subprocess, sys, time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent
BENCHES = ['loops', 'arith', 'structs', 'maps', 'sorting', 'strings']
OUT = HERE / 'out'

def run(cmd, cwd=None, env=None):
    return subprocess.run(cmd, cwd=cwd, env=env, capture_output=True, text=True)

def must(result, what):
    if result.returncode != 0:
        print(f'{what} failed:\n{result.stdout}{result.stderr}', file=sys.stderr)
        sys.exit(2)

def build_c(name):
    cc = shutil.which('zig')
    if not cc or not (HERE / name / f'{name}.c').exists(): return None
    exe = OUT / 'c' / name
    exe.parent.mkdir(parents=True, exist_ok=True)
    must(run([cc, 'cc', '-std=c11', '-O2', '-g', '-DNDEBUG', '-ffp-contract=off', '-fno-sanitize=undefined',
              str(HERE / name / f'{name}.c'), '-o', str(exe), '-lm']), f'C {name}')
    return [str(exe)]

def build_go(name):
    go = shutil.which('go')
    if not go: return None
    exe = OUT / 'go' / name
    exe.parent.mkdir(parents=True, exist_ok=True)
    must(run([go, 'build', '-o', str(exe), '.'], cwd=HERE / name / 'go'), f'Go {name}')
    return [str(exe)]

def build_cs(name):
    dotnet = shutil.which('dotnet') or (str(pathlib.Path.home() / '.dotnet' / 'dotnet') if (pathlib.Path.home() / '.dotnet' / 'dotnet').exists() else None)
    if not dotnet: return None
    out = OUT / 'cs' / name
    must(run([dotnet, 'build', '-c', 'Release', '-o', str(out), '--nologo', '-v', 'q'], cwd=HERE / name / 'cs'), f'C# {name}')
    return [dotnet, str(out / f'{name}.dll')]

def build_lyric(name, lyric5):
    must(run([lyric5, 'build', str(HERE / name / f'{name}.lyr'), '--profile', 'release']), f'Lyric {name}')
    found = sorted(glob.glob(str(ROOT / 'out' / 'release' / '*' / name)) + glob.glob(str(ROOT / 'out' / 'release' / '*' / f'{name}.exe')))
    if not found:
        print(f'no binary for {name} under {ROOT / "out" / "release"}', file=sys.stderr); sys.exit(2)
    return [found[-1]]

def measure(cmd, runs):
    best, output = None, None
    for _ in range(runs):
        start = time.perf_counter()
        result = subprocess.run(cmd, capture_output=True, text=True)
        elapsed = time.perf_counter() - start
        if result.returncode != 0:
            print(f'{cmd} exited {result.returncode}:\n{result.stderr}', file=sys.stderr); sys.exit(2)
        output = result.stdout.strip()
        best = elapsed if best is None else min(best, elapsed)
    return best, output

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--runs', type=int, default=5)
    ap.add_argument('--lyric5', default=str(ROOT / 'src' / 'Lyric5' / 'bin' / 'Release' / 'net10.0' / 'lyric5'))
    ap.add_argument('--ratchet', action='store_true', help='exit 1 when Lyric exceeds 3x C or 1.5x Go')
    ap.add_argument('--bound', type=float, help='exit 1 when Lyric exceeds this many times Go (needs Go)')
    ap.add_argument('benches', nargs='*', help='the programs to measure (default: all)')
    args = ap.parse_args()

    if args.bound is not None and not shutil.which('go'):
        print('--bound compares with Go, and there is no go on the PATH', file=sys.stderr); return 2

    rows, bad, beyond = [], [], []
    for name in args.benches or BENCHES:
        twins = {'Lyric': build_lyric(name, args.lyric5), 'C': build_c(name), 'Go': build_go(name), 'C#': build_cs(name)}
        times, outputs = {}, {}
        for lang, cmd in twins.items():
            if cmd is None: continue
            times[lang], outputs[lang] = measure(cmd, args.runs)
        if len(set(outputs.values())) != 1:
            print(f'{name}: the twins disagree: {outputs}', file=sys.stderr); sys.exit(2)
        rows.append((name, outputs['Lyric'], times))
        if 'C' in times and times['Lyric'] > 3.0 * times['C']: bad.append(f'{name}: Lyric {times["Lyric"]:.3f} s > 3x C {times["C"]:.3f} s')
        if 'Go' in times and times['Lyric'] > 1.5 * times['Go']: bad.append(f'{name}: Lyric {times["Lyric"]:.3f} s > 1.5x Go {times["Go"]:.3f} s')
        if args.bound is not None and times['Lyric'] > args.bound * times['Go']:
            beyond.append(f'{name}: Lyric {times["Lyric"]:.3f} s > {args.bound:g}x Go {times["Go"]:.3f} s')

    langs = ['Lyric', 'C', 'Go', 'C#']
    print('| bench | checksum | ' + ' | '.join(langs) + ' | Lyric / C | Lyric / Go |')
    print('|---|---|' + '---|' * len(langs) + '---|---|')
    for name, checksum, times in rows:
        cells = [f'{times[l]:.3f} s' if l in times else '—' for l in langs]
        ratio_c = f'{times["Lyric"] / times["C"]:.2f}' if 'C' in times else '—'
        ratio_go = f'{times["Lyric"] / times["Go"]:.2f}' if 'Go' in times else '—'
        print(f'| {name} | {checksum} | ' + ' | '.join(cells) + f' | {ratio_c} | {ratio_go} |')
    print(f'\nminimum of {args.runs} runs each, wall time of the whole process')
    if bad:
        print('\nratchet ' + ('FAILED' if args.ratchet else 'exceeded (not enforced)') + ':\n  ' + '\n  '.join(bad))
        if args.ratchet: return 1
    if beyond:
        print(f'\nbound of {args.bound:g}x Go FAILED:\n  ' + '\n  '.join(beyond))
        return 1
    return 0

if __name__ == '__main__':
    sys.exit(main())
