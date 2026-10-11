#!/usr/bin/env python3
"""The specification's conformance suite, its cases run side by side.

The reference runner (lyric-spec, tools/run_conformance.py) runs one case after the other: about
a second each, 22 minutes for 1200 cases on a CI runner with four processors, three of them idle.
This wrapper loads THE PINNED runner — the one `spec.pin` names, never another — and runs its
cases on a pool of threads, each case in a work directory of its own. The judgement of a case is
the runner's (`run_case`), the header's reading is the runner's (`parse_header`), the list of
cases is the runner's (`find_cases`); this file decides only who runs when. The last line is the
runner's too, so what reads the gate's result ("1222/1222 passed (debug)") goes on reading it.

Threads, not processes: a case's work is the compiler, the C toolchain and the program, each a
process of its own; the runner's Python between them is a copy and a comparison.
"""

import argparse
import concurrent.futures
import importlib.util
import os
import pathlib
import shutil
import sys
import tempfile
import threading


def load_runner(path):
    spec = importlib.util.spec_from_file_location("run_conformance", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--runner", type=pathlib.Path, required=True,
                    help="the specification's tools/run_conformance.py, at the pinned commit")
    ap.add_argument("--lyric5", type=pathlib.Path, default=None)
    ap.add_argument("--profile", default="debug", choices=["debug", "release"])
    ap.add_argument("--cases", type=pathlib.Path, default=None,
                    help="default: conformance/cases beside the runner's tools/")
    ap.add_argument("--toolchain-version", default=None)
    ap.add_argument("--jobs", type=int, default=os.cpu_count() or 1,
                    help="cases at once (default: the processors)")
    ap.add_argument("--only", type=pathlib.Path, default=None,
                    help="a file naming the cases to run, one label per line (the rest are skipped)")
    args = ap.parse_args()

    runner = load_runner(args.runner.resolve())
    cases_root = args.cases or args.runner.resolve().parent.parent / "conformance" / "cases"
    version = (tuple(int(p) for p in args.toolchain_version.split("."))
               if args.toolchain_version else None)
    lyric5 = args.lyric5 or shutil.which("lyric5")
    if not lyric5:
        print("need --lyric5 or 'lyric5' on PATH", file=sys.stderr)
        return 2
    lyric5 = pathlib.Path(lyric5).resolve()

    cases = runner.find_cases(cases_root)
    if not cases:
        print("no cases found", file=sys.stderr)
        return 2
    wanted = None
    if args.only:
        wanted = {line.strip() for line in args.only.read_text(encoding="utf-8").splitlines() if line.strip()}

    # The headers first, in order: a malformed one ends the run before anything is built, as the
    # runner does; and what 'since:'/'until:' skip is printed before the pool starts.
    todo = []
    skipped = 0
    for case in cases:
        spec = runner.parse_header(runner.header_of(case))
        label = case.relative_to(cases_root).as_posix() + ("/" if case.is_dir() else "")
        if wanted is not None and label not in wanted:
            skipped += 1
            continue
        if spec["since"] and version and spec["since"] > version:
            skipped += 1
            print(f"SKIP {label} (since {'.'.join(map(str, spec['since']))})")
            continue
        if spec["until"] and version and version >= spec["until"]:
            skipped += 1
            print(f"SKIP {label} (until {'.'.join(map(str, spec['until']))})")
            continue
        todo.append((case, spec, label))

    failed = 0
    lock = threading.Lock()
    with tempfile.TemporaryDirectory() as tmp:
        root = pathlib.Path(tmp)

        def one(index, case, spec, label):
            workdir = root / f"{index:04d}"
            workdir.mkdir()
            ok, reason = runner.run_case(case, spec, lyric5, args.profile, workdir)
            # Its directory goes at once: 1200 built programs would fill a tmpfs.
            shutil.rmtree(workdir, ignore_errors=True)
            return label, ok, reason

        with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, args.jobs)) as pool:
            futures = [pool.submit(one, i, case, spec, label) for i, (case, spec, label) in enumerate(todo)]
            for future in concurrent.futures.as_completed(futures):
                label, ok, reason = future.result()
                with lock:
                    if ok:
                        print(f"PASS {label}", flush=True)
                    else:
                        failed += 1
                        print(f"FAIL {label}: {reason}", flush=True)

    ran = len(todo)
    tail = f", {skipped} skipped" if skipped else ""
    print(f"\n{ran - failed}/{ran} passed ({args.profile}){tail}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
