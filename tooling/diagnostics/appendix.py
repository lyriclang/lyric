#!/usr/bin/env python3
"""The specification's Appendix A from the toolchain's diagnostic catalog.

    python3 tooling/diagnostics/appendix.py [--catalog src/Lyric.Core/diagnostics.toml] > appendix-a-diagnostics.md

The appendix is generated, never edited by hand: a code's title, severity, chapter and summary come from
diagnostics.toml, grouped by the part of the toolchain that reports it. `lyric explain CODE` prints the
whole entry, with its explanation and programs. The spec pull request of every slice that changes codes
carries the regenerated file.
"""
import argparse
import pathlib
import sys
import tomllib

GROUPS = [
    ("LEX", "Lexical errors", "The lexer's: a token that cannot be formed."),
    ("PAR", "Syntax errors", "The parser's. A declaration that does not parse reports these and nothing that follows from them (16 §2)."),
    ("RES", "Names and modules", "The resolver's: what a name, an import or a module path refers to."),
    ("SEM", "Semantic errors and warnings", "The checker's: types, interfaces, errors, patterns, and the warnings."),
    ("MAC", "Macros", "A macro's own refusal, at its site."),
    ("CT", "Compile-time evaluation", "The evaluation of `comptime`, a macro or a synthesis at build time."),
    ("CG", "Code generation", "What the C backend does not take."),
    ("PKG", "Packages and manifests", "`lyric.toml`, the lock file, dependencies."),
    ("BLD", "The build", "The C toolchain, the runtime, the build script."),
    ("LNK", "The link", "What the linker says, read: a symbol nothing defines, a library it does not find, a symbol defined twice (15 §5.2 rule 12)."),
    ("CLI", "The command line", "The driver's options and verbs."),
    ("IR", "The lowering", "Retired: each refusal of the lowering has a code of its own."),
    ("ICE", "Internal compiler errors", "The toolchain's own failure: a bug to report, never the program's fault."),
    ("RT", "Runtime panics", "A panic ends the program with exit code 101 and a backtrace; the compile-time evaluation reports the same codes inside `LYR-CT0001`."),
]

SEVERITY = {"error": "E", "warning": "W", "hint": "H", "panic": "P"}


def cell(text):
    return " ".join(text.split()).replace("|", "\\|")


def main():
    ap = argparse.ArgumentParser()
    root = pathlib.Path(__file__).resolve().parents[2]
    ap.add_argument("--catalog", default=root / "src" / "Lyric.Core" / "diagnostics.toml")
    args = ap.parse_args()
    catalog = tomllib.loads(pathlib.Path(args.catalog).read_text(encoding="utf-8"))

    out = []
    out.append("# Appendix A — Diagnostic catalogue\n")
    out.append("> **Generated** from the toolchain's `diagnostics.toml` by `tooling/diagnostics/appendix.py`; not edited by hand.")
    out.append("> Source of the decisions: [11 Tooling and interop G9](https://github.com/lyriclang/lyric/blob/main/design/v5/spec/11-werkzeuge-interop.md).\n")
    out.append("Every code the reference toolchain reports, with its severity, the chapter whose rule it is, and when it is")
    out.append("reported. The **code and the severity are contract**; the message's wording is not (16 §1). `lyric explain CODE`")
    out.append("prints a code's whole entry: its explanation, a program that reports it and the program put right. A retired")
    out.append("code is no longer reported, and its number is not given again.\n")
    out.append("Severities: **E** error, **W** warning, **H** hint, **P** panic (at run time, exit code 101).\n")
    known = set()
    for number, (prefix, title, intro) in enumerate(GROUPS, start=1):
        codes = sorted(c for c in catalog if c.split("-")[1].rstrip("0123456789") == prefix)
        if not codes:
            continue
        out.append(f"## A.{number} {prefix} — {title}\n")
        out.append(intro + "\n")
        out.append("| Code | S | Chapter | Title | When |")
        out.append("|---|---|---|---|---|")
        for code in codes:
            e = catalog[code]
            title_cell = cell(e["title"]) + (" *(retired)*" if "retired" in e else "")
            when = cell(e["summary"]) + (f" Retired: {cell(e['retired'])}" if "retired" in e else "")
            out.append(f"| {code} | {SEVERITY[e['severity']]} | [{e['area']}]({e['area']}.md) | {title_cell} | {when} |")
            known.add(code)
        out.append("")
    unknown = sorted(set(catalog) - known)
    if unknown:
        print(f"codes of no group: {', '.join(unknown)}", file=sys.stderr)
        return 1
    # the lints (16 §5): every warning, by its name
    lints = sorted((e["lint"], code, e) for code, e in catalog.items() if "lint" in e)
    out.append(f"## A.{len(GROUPS) + 1} Lints\n")
    out.append("Every warning is a lint (16 §5): its name, its code, its group and its level unless said. "
               "`warnings` names them all.\n")
    out.append("| Lint | Code | Group | Level | Title |")
    out.append("|---|---|---|---|---|")
    for name, code, e in lints:
        out.append(f"| `{name}` | {code} | {e['group']} | {e.get('level', 'warn')} | {cell(e['title'])} |")
    out.append("")
    sys.stdout.write("\n".join(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
