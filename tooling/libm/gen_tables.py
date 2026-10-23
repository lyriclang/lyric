"""The tables of musl's math data files as C# arrays with the exact bits (design 09 D7, 13 M10 S12):
python3 tooling/libm/gen_tables.py <musl>/src/math src/Lyric.Frontend/Ir/Interp/LibmTables.cs"""
import re, struct, sys

M = sys.argv[1].rstrip('/') + '/'

def bits(d):
    return struct.unpack('<q', struct.pack('<d', d))[0]

def parse_number(tok):
    tok = tok.strip().rstrip(',').strip()
    neg = tok.startswith('-')
    if neg:
        tok = tok[1:].strip()
    if tok.lower().startswith('0x') and ('p' in tok.lower()):
        v = float.fromhex(tok)
    elif tok.lower().startswith('0x'):
        # an integer: the bits themselves
        return ('int', int(tok, 16) * (-1 if neg else 1))
    else:
        v = float(tok)
    return ('double', -v if neg else v)

def numbers_in(text):
    """Every numeric token in a block of C initializers, in order."""
    text = re.sub(r'/\*.*?\*/', ' ', text, flags=re.S)
    text = re.sub(r'//.*', ' ', text)
    toks = re.findall(r'-?\s*(?:0[xX][0-9a-fA-F]+(?:\.[0-9a-fA-F]*)?(?:[pP][+-]?\d+)?|\d+\.\d*(?:[eE][+-]?\d+)?|\d+(?:[eE][+-]?\d+)?)', text)
    return [parse_number(t) for t in toks]

def expressions_in(text):
    """Every entry of a block of C initializers that are expressions — pow's polynomial is written
    `0x1.555555555556p-2 * -2` — evaluated, in order."""
    text = re.sub(r'/\*.*?\*/', ' ', text, flags=re.S)
    text = re.sub(r'//.*', ' ', text)
    out = []
    for entry in text.strip().strip('{}').split(','):
        entry = entry.strip()
        if not entry:
            continue
        entry = re.sub(r'0[xX][0-9a-fA-F.]+[pP][+-]?\d+', lambda mm: repr(float.fromhex(mm.group(0))), entry)
        out.append(float(eval(entry, {})))
    return out

def block(src, name):
    """The initializer block of the named struct member or array (`name = {`), as text."""
    i = re.search(re.escape(name) + r'\s*=', src).start()  # not '.poly1' for '.poly'
    j = src.index('{', i)
    depth = 0
    k = j
    while True:
        if src[k] == '{': depth += 1
        elif src[k] == '}':
            depth -= 1
            if depth == 0: break
        k += 1
    return src[j:k + 1]

def cs_double(v):
    b = bits(v)
    if b < 0:
        return 'BitConverter.Int64BitsToDouble(unchecked((long)0x%016xUL))' % (b & 0xffffffffffffffff)
    return 'BitConverter.Int64BitsToDouble(0x%016xL)' % b

def cs_doubles(name, values):
    out = ['    public static readonly double[] %s = [' % name]
    for v in values:
        out.append('        %s,' % cs_double(v))
    out.append('    ];')
    return '\n'.join(out)

def cs_ulongs(name, values):
    out = ['    public static readonly ulong[] %s = [' % name]
    for v in values:
        out.append('        0x%016xUL,' % (v & 0xffffffffffffffff))
    out.append('    ];')
    return '\n'.join(out)

parts = []

# exp_data: scalars, poly[4], exp2 (unused), tab[256] of uint64
src = open(M + 'exp_data.c').read()
body = src[src.index('__exp_data = {'):]
body = re.sub(r'#if EXP_USE_TOINT_NARROW.*?#else', '', body, flags=re.S).replace('#endif', '')
# the scalar fields in order: invln2N, shift, negln2hiN, negln2loN, poly[4], exp2_shift, exp2_poly[5], tab
body = re.sub(r'/\*.*?\*/', ' ', body, flags=re.S)
body = re.sub(r'//.*', ' ', body)
head = body[:body.index('.tab')]
nums = numbers_in(head.replace('N', ' N ') if False else head)
# the scalars use expressions like 0x1.71547652b82fep0 * N etc.: evaluate with N = 128
N = 128
def eval_field(name):
    m = re.search(r'\.' + name + r'\s*=\s*([^,]+?)\s*,\s*\n', body)
    expr = m.group(1)
    expr = re.sub(r'0[xX][0-9a-fA-F.]+[pP][+-]?\d+', lambda mm: repr(float.fromhex(mm.group(0))), expr)
    return eval(expr, {'N': N})
scal = {k: eval_field(k) for k in ['invln2N', 'shift', 'negln2hiN', 'negln2loN', 'exp2_shift']}
poly = numbers_in(block(body, '.poly'))
assert len(poly) == 4, len(poly)
exp2_poly = numbers_in(block(body, '.exp2_poly'))
tab = numbers_in(block(body, '.tab'))
assert len(tab) == 256, len(tab)
parts.append('    // exp_data.c (N = 128)')
for k, v in scal.items():
    parts.append('    public static readonly double Exp_%s = %s;' % (k, cs_double(v)))
parts.append(cs_doubles('Exp_poly', [v for _, v in poly]))
parts.append(cs_ulongs('Exp_tab', [v if t == 'int' else bits(v) for t, v in tab]))

# log_data: ln2hi, ln2lo, poly[5], poly1[11], tab[128]{invc, logc}, tab2[128]{chi, clo}
src = open(M + 'log_data.c').read()
body = re.sub(r'/\*.*?\*/', ' ', src[src.index('__log_data = {'):], flags=re.S)
def scalar(body, name):
    m = re.search(r'\.' + name + r'\s*=\s*([^,]+?)\s*,', body)
    return parse_number(m.group(1))[1]
parts.append('    // log_data.c (N = 128)')
parts.append('    public static readonly double Log_ln2hi = %s;' % cs_double(scalar(body, 'ln2hi')))
parts.append('    public static readonly double Log_ln2lo = %s;' % cs_double(scalar(body, 'ln2lo')))
log_poly = [v for _, v in numbers_in(block(body, '.poly'))]
log_poly1 = [v for _, v in numbers_in(block(body, '.poly1'))]
assert len(log_poly) == 5 and len(log_poly1) == 11, (len(log_poly), len(log_poly1))
parts.append(cs_doubles('Log_poly', log_poly))
parts.append(cs_doubles('Log_poly1', log_poly1))
tab = [v for _, v in numbers_in(block(body, '.tab'))]
assert len(tab) == 256, len(tab)
parts.append(cs_doubles('Log_tab', tab))
tab2 = [v for _, v in numbers_in(block(body, '.tab2'))]
assert len(tab2) == 256, len(tab2)
parts.append(cs_doubles('Log_tab2', tab2))

# log2_data: invln2hi, invln2lo, poly[6], poly1[10], tab[64], tab2[64]
src = open(M + 'log2_data.c').read()
body = re.sub(r'/\*.*?\*/', ' ', src[src.index('__log2_data = {'):], flags=re.S)
parts.append('    // log2_data.c (N = 64)')
parts.append('    public static readonly double Log2_invln2hi = %s;' % cs_double(scalar(body, 'invln2hi')))
parts.append('    public static readonly double Log2_invln2lo = %s;' % cs_double(scalar(body, 'invln2lo')))
log2_poly = [v for _, v in numbers_in(block(body, '.poly'))]
log2_poly1 = [v for _, v in numbers_in(block(body, '.poly1'))]
assert len(log2_poly) == 6 and len(log2_poly1) == 10, (len(log2_poly), len(log2_poly1))
parts.append(cs_doubles('Log2_poly', log2_poly))
parts.append(cs_doubles('Log2_poly1', log2_poly1))
tab = [v for _, v in numbers_in(block(body, '.tab'))]
assert len(tab) == 128, len(tab)
parts.append(cs_doubles('Log2_tab', tab))
tab2 = [v for _, v in numbers_in(block(body, '.tab2'))]
assert len(tab2) == 128, len(tab2)
parts.append(cs_doubles('Log2_tab2', tab2))

# pow_data: ln2hi, ln2lo, poly[7], tab[128]{invc, pad, logc, logctail}
src = open(M + 'pow_data.c').read()
body = re.sub(r'/\*.*?\*/', ' ', src[src.index('__pow_log_data = {'):], flags=re.S)
parts.append('    // pow_data.c (N = 128)')
parts.append('    public static readonly double Pow_ln2hi = %s;' % cs_double(scalar(body, 'ln2hi')))
parts.append('    public static readonly double Pow_ln2lo = %s;' % cs_double(scalar(body, 'ln2lo')))
pow_poly = expressions_in(block(body, '.poly'))
assert len(pow_poly) == 7, len(pow_poly)
parts.append(cs_doubles('Pow_poly', pow_poly))
# the entries are written A(invc, logc, logctail) and expand to {invc, 0, logc, logctail}
tab3 = [v for _, v in numbers_in(re.sub(r'#define.*', ' ', block(body, '.tab')))]
assert len(tab3) == 384, len(tab3)
tab = []
for e in range(128):
    tab += [tab3[3 * e], 0.0, tab3[3 * e + 1], tab3[3 * e + 2]]
parts.append(cs_doubles('Pow_tab', tab))

# __rem_pio2_large: init_jk[4], ipio2[] (int32), PIo2[8] doubles
src = open(M + '__rem_pio2_large.c').read()
src2 = re.sub(r'/\*.*?\*/', ' ', src, flags=re.S)
src2 = re.sub(r'(?m)^#.*$', ' ', src2)  # the LDBL_MAX_EXP guards: every entry, as the C compiles on every tier-1 target
ipio2 = [v for _, v in numbers_in(block(src2, 'ipio2[]'))]
pio2 = [v for _, v in numbers_in(block(src2, 'PIo2[]'))]
init_jk = [int(v) for _, v in numbers_in(block(src2, 'init_jk[]'))]
parts.append('    // __rem_pio2_large.c')
parts.append('    public static readonly int[] Rem_init_jk = [%s];' % ', '.join(str(v) for v in init_jk))
parts.append('    public static readonly int[] Rem_ipio2 = [\n' + '\n'.join('        0x%08x,' % v for v in ipio2) + '\n    ];')
parts.append(cs_doubles('Rem_PIo2', pio2))
print('ipio2', len(ipio2), 'PIo2', len(pio2), 'init_jk', init_jk, file=sys.stderr)

out = '''// The tables of musl's math functions, the bits exact, generated from musl 1.2.5's
// exp_data.c, log_data.c, log2_data.c, pow_data.c and __rem_pio2_large.c (MIT;
// tooling/libm/gen_tables.py). Not written by hand.
namespace Lyric.Ir.Interp;

internal static class LibmTables
{
''' + '\n\n'.join(parts) + '\n}\n'
open(sys.argv[2], 'w', encoding='utf-8', newline='\n').write(out)
print('written', len(out), 'bytes', file=sys.stderr)
