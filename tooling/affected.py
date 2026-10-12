#!/usr/bin/env python3
"""The tests a change reaches (tooling/affected.toml): a filter for `dotnet test` and a list of
conformance cases for tooling/conformance/run_parallel.py --only.

    affected.py --base origin/main --out affected [--spec spec-repo --old-pin <sha>]

writes `affected/tests.filter` (empty: everything runs) and, unless everything runs,
`affected/cases.txt` (the labels; `cases.txt` absent: the whole suite). The diff is
`<base>...HEAD`, the merge base's side. With `--spec` and `--old-pin`, the cases the pin moved
over — added or changed between the old pin and the checked-out specification — always run.
`--files` names the changed files instead of asking git (the tests of this script).
"""

import argparse
import fnmatch
import pathlib
import re
import subprocess
import sys
import tomllib

ROOT = pathlib.Path(__file__).resolve().parent.parent


def changed_files(base):
    out = subprocess.run(["git", "diff", "--name-only", f"{base}...HEAD"], cwd=ROOT, capture_output=True, text=True, check=True)
    return [line.strip() for line in out.stdout.splitlines() if line.strip()]


def moved_cases(spec, old_pin):
    """The labels of the cases added or changed since the old pin, as the suite labels them."""
    out = subprocess.run(["git", "diff", "--name-only", old_pin, "HEAD", "--", "conformance/cases"],
                         cwd=spec, capture_output=True, text=True, check=True)
    labels = set()
    for line in out.stdout.splitlines():
        path = pathlib.Path(line.strip())
        if not line.strip():
            continue
        rel = path.relative_to("conformance/cases")
        # a file beside the chapters (the cases' README) is no case; a file below a chapter's
        # directory, or one in the chapter that is no .lyr, belongs to a package case: the
        # directory under the chapter that holds (or held) the lyric.toml
        if len(rel.parts) < 2:
            continue
        if len(rel.parts) > 2 or rel.suffix != ".lyr":
            labels.add(rel.parts[0] + "/" + rel.parts[1] + "/")
        else:
            labels.add(rel.as_posix())
    return labels


def prelude_modules():
    text = (ROOT / "stdlib5" / "std" / "prelude.lyr").read_text(encoding="utf-8")
    return set(re.findall(r"^\s*pub import (std\.[\w.]+)", text, re.M))


def test_class_of(path):
    """tests/Lyric5.Tests/<Name>.cs: the class it declares, or None for a helper."""
    text = (ROOT / path).read_text(encoding="utf-8") if (ROOT / path).exists() else ""
    m = re.search(r"public (?:sealed )?class (\w+Tests)\b", text)
    return m.group(1) if m else None


def labels_of(cases_root):
    """Every case label of the suite, with its text: for the chapter and import selections."""
    manifests = sorted(m.parent for m in cases_root.rglob("lyric.toml"))
    packages = [p for p in manifests if not any(outer in p.parents for outer in manifests)]
    result = {}
    for p in packages:
        text = "".join(f.read_text(encoding="utf-8", errors="replace") for f in p.rglob("*.lyr"))
        result[p.relative_to(cases_root).as_posix() + "/"] = text
    for f in cases_root.rglob("*.lyr"):
        if any(pkg in f.parents for pkg in packages):
            continue
        result[f.relative_to(cases_root).as_posix()] = f.read_text(encoding="utf-8", errors="replace")
    return result


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--base", default="origin/main")
    ap.add_argument("--files", nargs="*", default=None, help="the changed files, instead of git's")
    ap.add_argument("--out", type=pathlib.Path, required=True)
    ap.add_argument("--spec", type=pathlib.Path, default=None, help="the specification's checkout (its cases)")
    ap.add_argument("--old-pin", default=None, help="spec.pin before the change: the cases it moved over always run")
    ap.add_argument("--table", type=pathlib.Path, default=ROOT / "tooling" / "affected.toml")
    args = ap.parse_args()

    table = tomllib.loads(args.table.read_text(encoding="utf-8"))
    files = args.files if args.files is not None else changed_files(args.base)

    tests = set(table["regression"]["tests"])
    cases = set()          # labels and prefixes
    modules = set()        # touched std modules, for "imports"
    everything = False
    reasons = []
    for file in files:
        if any(file.startswith(p) for p in table["ignore"]):
            continue
        if any(file.startswith(p) for p in table["full"]):
            everything = True
            reasons.append(f"{file}: the harness — everything")
            continue
        if file.startswith("tests/Lyric5.Tests/") and file.endswith(".cs"):
            cls = test_class_of(file)
            if cls:
                tests.add(cls)
                reasons.append(f"{file}: {cls}")
            else:
                tests.add("all5")
                reasons.append(f"{file}: a helper — every Lyric5 class")
            continue
        hit = False
        for rule in table["rule"]:
            if any(file.startswith(p) for p in rule["paths"]):
                hit = True
                tests.update(rule["tests"])
                cases.update(rule["cases"])
                if "imports" in rule["cases"]:
                    m = re.match(r"stdlib5/std/(.+)\.lyr$", file)
                    if m:
                        modules.add("std." + m.group(1).replace("/", "."))
                reasons.append(f"{file}: {', '.join(rule['tests'])}; cases {', '.join(rule['cases']) or '-'}")
                break   # the first rule that names the path: the table lists the narrower prefix first
        if not hit:
            everything = True
            reasons.append(f"{file}: no rule — everything")

    args.out.mkdir(parents=True, exist_ok=True)
    for reason in reasons:
        print(reason)

    if everything or "all5" in tests and "4x" in tests:
        (args.out / "tests.filter").write_text("", encoding="utf-8")
        print("tests: everything")
    else:
        parts = []
        if "4x" in tests:
            parts.append("FullyQualifiedName~Lyric.Tests.")
        if "all5" in tests:
            parts.append("FullyQualifiedName~Lyric5.Tests.")
        else:
            parts += [f"FullyQualifiedName~Lyric5.Tests.{cls}." for cls in sorted(t for t in tests if t not in ("4x", "all5"))]
        (args.out / "tests.filter").write_text("|".join(parts), encoding="utf-8")
        print(f"tests: {len(parts)} filters")

    if args.spec is None:
        print("cases: no specification given — not written")
        return 0
    cases_root = args.spec / "conformance" / "cases"
    if everything or "all" in cases:
        (args.out / "cases.txt").unlink(missing_ok=True)
        print("cases: everything")
        return 0
    suite = labels_of(cases_root)
    prelude = prelude_modules()
    if modules & prelude:
        (args.out / "cases.txt").unlink(missing_ok=True)
        print(f"cases: everything — a prelude module changed ({', '.join(sorted(modules & prelude))})")
        return 0
    chosen = set()
    regression = ROOT / table["regression"]["cases"]
    chosen.update(l.strip() for l in regression.read_text(encoding="utf-8").splitlines() if l.strip() and not l.startswith("#"))
    if args.old_pin:
        chosen.update(moved_cases(args.spec, args.old_pin))
    for label, text in suite.items():
        if any(label.startswith(p) for p in cases if p != "imports"):
            chosen.add(label)
        elif modules and any(re.search(r"\b" + re.escape(m) + r"\b", text) for m in modules):
            chosen.add(label)
    chosen = {label for label in chosen if label in suite}
    (args.out / "cases.txt").write_text("\n".join(sorted(chosen)) + "\n", encoding="utf-8")
    print(f"cases: {len(chosen)} of {len(suite)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
