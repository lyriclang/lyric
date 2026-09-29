import re, os

ROOT = r"C:/Users/Olivier/CLionProjects/lyric"
DIRS = ["examples", "stdlib", "tests"]

files = []
for d in DIRS:
    p = os.path.join(ROOT, d)
    for dp, dn, fn in os.walk(p):
        norm = dp.replace("\\", "/")
        if "/bin/" in norm + "/" or "/obj/" in norm + "/":
            continue
        for f in fn:
            if f.endswith(".lyr"):
                files.append(os.path.join(dp, f))

fields_of = {}
variant_names = set()

decl_re = re.compile(r'^\s*(?:pub\s+)?(struct|class)\s+([A-Z]\w*)\s*(?:<[^>]*>)?\s*(?:::\s*\[[^\]]*\]\s*)?\{')
enum_re = re.compile(r'^\s*(?:pub\s+)?enum\s+([A-Z]\w*)\s*(?:<[^>]*>)?\s*(?:::\s*\[[^\]]*\]\s*)?\{')
field_re = re.compile(r'^\s*(?:pub\s+)?(\w+)\s*:')


def block(lines, i):
    depth = lines[i].count("{") - lines[i].count("}")
    body, j = [], i + 1
    while j < len(lines) and depth > 0:
        depth += lines[j].count("{") - lines[j].count("}")
        if depth > 0:
            body.append(lines[j])
        j += 1
    return body, j


for path in files:
    lines = open(path, encoding="utf-8", errors="replace").read().split("\n")
    i = 0
    while i < len(lines):
        m = decl_re.match(lines[i])
        if m:
            body, nxt = block(lines, i)
            fields_of.setdefault(m.group(2), set()).update(
                {fm.group(1) for b in body if (fm := field_re.match(b))})
            i = nxt
            continue
        m = enum_re.match(lines[i])
        if m:
            body, nxt = block(lines, i)
            k = 0
            while k < len(body):
                vm = re.match(r'^\s*([A-Z]\w*)\s*(\{|\(|,|$)', body[k])
                if vm:
                    variant_names.add(vm.group(1))
                    if vm.group(2) == "{":
                        vbody, vnxt = block(body, k)
                        fields_of.setdefault(vm.group(1), set()).update(
                            {fm.group(1) for b in vbody if (fm := field_re.match(b))})
                        k = vnxt
                        continue
                k += 1
            i = nxt
            continue
        i += 1

arm_field = re.compile(r'(?:^|[|(\[,]\s*|=>\s*)\s*([A-Z]\w*)\s*\{([^{}]*)\}\s*(?:if\b[^=]*)?=>')
let_field = re.compile(r'\b(?:let|var)\s+([A-Z]\w*)\s*\{([^{}]*)\}\s*(?::[^=]*)?=')
bare = re.compile(r'(?:^|\|)\s*([A-Z]\w*)\s*(?:if\b[^=]*)?=>')

total_fp = omitting = unknown = 0
omit_sites, complete_sites, bindings = [], [], []
variant_arms = 0

for path in files:
    rel = os.path.relpath(path, ROOT).replace("\\", "/")
    for n, line in enumerate(open(path, encoding="utf-8", errors="replace").read().split("\n"), 1):
        for rx in (arm_field, let_field):
            for m in rx.finditer(line):
                name, inner = m.group(1), m.group(2)
                total_fp += 1
                named = set()
                for part in inner.split(","):
                    part = part.strip()
                    if not part:
                        continue
                    fm = re.match(r'(\.\.|\w+)', part)
                    if fm:
                        named.add(fm.group(1))
                decl = fields_of.get(name)
                if decl is None:
                    unknown += 1
                elif len(named - {"_"}) < len(decl):
                    omitting += 1
                    omit_sites.append(f"{rel}:{n}: {name} {{{inner}}} (declared {sorted(decl)})")
                else:
                    complete_sites.append(f"{rel}:{n}: {name} {{{inner}}}")
        if "=>" in line:
            for m in bare.finditer(line):
                if m.group(1) in variant_names:
                    variant_arms += 1
                else:
                    bindings.append(f"{rel}:{n}: {m.group(1)} =>")

print("files scanned (no bin/obj):", len(files))
print("FIELD PATTERNS:", total_fp, "| complete:", len(complete_sites),
      "| OMITTING:", omitting, "| unknown decl:", unknown)
for s in complete_sites:
    print("   ", s)
for s in omit_sites:
    print("  !!", s)
print("BARE PascalCase arms:", variant_arms + len(bindings),
      "| known variant:", variant_arms, "| binding candidates:", len(bindings))
for s in bindings:
    print("  !!", s)
