import re, os, sys, json

ROOT = r"C:/Users/Olivier/CLionProjects/lyric"
DIRS = ["examples", "stdlib", "tests"]

files = []
for d in DIRS:
    p = os.path.join(ROOT, d)
    if not os.path.isdir(p):
        continue
    for dp, dn, fn in os.walk(p):
        for f in fn:
            if f.endswith(".lyr"):
                files.append(os.path.join(dp, f))

# ---- collect declared field sets: struct decls, class decls, enum struct-variants
fields_of = {}   # Name -> set(field names)  (last wins; names are globally fairly unique)
variant_names = set()
enum_of_variant = {}

decl_re = re.compile(r'^\s*(?:pub\s+)?(struct|class)\s+([A-Z]\w*)\s*(?:<[^>]*>)?\s*(?:::\s*\[[^\]]*\]\s*)?\{')
enum_re = re.compile(r'^\s*(?:pub\s+)?enum\s+([A-Z]\w*)\s*(?:<[^>]*>)?\s*(?:::\s*\[[^\]]*\]\s*)?\{')
field_re = re.compile(r'^\s*(?:pub\s+)?(\w+)\s*:')

def block(lines, i):
    """return (body_lines, next_index) for a brace block opened on line i"""
    depth = lines[i].count("{") - lines[i].count("}")
    body = []
    j = i + 1
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
            fs = set()
            for b in body:
                fm = field_re.match(b)
                if fm:
                    fs.add(fm.group(1))
            fields_of.setdefault(m.group(2), set()).update(fs)
            i = nxt
            continue
        m = enum_re.match(lines[i])
        if m:
            ename = m.group(1)
            body, nxt = block(lines, i)
            k = 0
            while k < len(body):
                vm = re.match(r'^\s*([A-Z]\w*)\s*(\{|\(|,|$)', body[k])
                if vm:
                    variant_names.add(vm.group(1))
                    enum_of_variant.setdefault(vm.group(1), set()).add(ename)
                    if vm.group(2) == "{":
                        vbody, vnxt = block(body, k)
                        fs = set()
                        for b in vbody:
                            fm = field_re.match(b)
                            if fm:
                                fs.add(fm.group(1))
                        fields_of.setdefault(vm.group(1), set()).update(fs)
                        k = vnxt
                        continue
                k += 1
            i = nxt
            continue
        i += 1

# ---- find field patterns in pattern position
arm_field = re.compile(r'(?:^|[|(\[,]\s*|=>\s*)\s*([A-Z]\w*)\s*\{([^{}]*)\}\s*(?:if\b[^=]*)?=>')
let_field = re.compile(r'\b(?:let|var)\s+([A-Z]\w*)\s*\{([^{}]*)\}\s*(?::[^=]*)?=')

total_fp = 0
omitting = 0
omit_sites = []
complete_sites = []
unknown = 0

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
                    if part == "..":
                        named.add("..")
                        continue
                    fm = re.match(r'(\w+)', part)
                    if fm:
                        named.add(fm.group(1))
                decl = fields_of.get(name)
                if decl is None:
                    unknown += 1
                    continue
                if len(named - {"_"}) < len(decl):
                    omitting += 1
                    omit_sites.append(f"{rel}:{n}: {name} {{{inner}}}  (declared {sorted(decl)})")
                else:
                    complete_sites.append(f"{rel}:{n}")

# ---- bare PascalCase names in arm position that name no known variant
bare = re.compile(r'(?:^|\|)\s*([A-Z]\w*)\s*(?:if\b[^=]*)?=>')
bindings = []
variant_arms = 0
for path in files:
    rel = os.path.relpath(path, ROOT).replace("\\", "/")
    for n, line in enumerate(open(path, encoding="utf-8", errors="replace").read().split("\n"), 1):
        if "=>" not in line:
            continue
        for m in bare.finditer(line):
            nm = m.group(1)
            if nm in variant_names:
                variant_arms += 1
            else:
                bindings.append(f"{rel}:{n}: {nm} =>")

print("files scanned:", len(files))
print("declared types with fields:", len(fields_of))
print("known variant names:", len(variant_names))
print()
print("FIELD PATTERNS in pattern position:", total_fp)
print("  complete (name every field):", len(complete_sites))
print("  OMITTING at least one field:", omitting)
print("  declaration not found (generic/foreign):", unknown)
print()
for s in omit_sites[:40]:
    print("   ", s)
print()
print("BARE PascalCase arms:", variant_arms + len(bindings))
print("  naming a known variant:", variant_arms)
print("  naming NO known variant (binding candidates):", len(bindings))
for s in bindings[:40]:
    print("   ", s)
