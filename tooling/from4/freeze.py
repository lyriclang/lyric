# Freezes Lyric 4.6's front end for `lyric fix --from-4`: the lexer, the parser, the syntax tree, the
# resolver and the checker of release/v4.6.0, with the part of Lyric.Core they need and 4.6's
# standard library, copied out of git into src/Lyric4.Frontend/. The namespaces become Lyric4.*,
# so the copy lives in one process beside the shared front end of Lyric 5; nothing else changes.
# frozen.sha256 lists every file it wrote; FrozenTests holds the directory to it. The copy is never
# edited by hand: a migration rule answers what 4.6 does, the old checker is not repaired.
#
#   python3 tooling/from4/freeze.py      (run in the repository; reads the commit from git)
import hashlib, os, re, shutil, subprocess

COMMIT = '002b7a4baca318ad54da80ebefba395011a2f830'
ROOT = subprocess.run(['git', 'rev-parse', '--show-toplevel'], capture_output=True, text=True, check=True).stdout.strip()
OUT = os.path.join(ROOT, 'src', 'Lyric4.Frontend')
EXAMPLES = os.path.join(ROOT, 'tests', 'fix-from-4', 'examples-4.6')
FROZEN = ('Core', 'Lexing', 'Parsing', 'AST', 'Resolver', 'Sema', 'Ir', 'stdlib')

FRONTEND = ['Lexing', 'Parsing', 'AST', 'Resolver', 'Sema']
# the one file of the IR the checker reads: which empty classes are host types
FRONTEND_FILES = ['Ir/Lowering/HostTypes.cs']
# what of Lyric.Core the checker reads: sources, spans, diagnostics, escapes, numbers, lyric.json
CORE = [
    'Diagnostic.cs', 'DiagnosticEngine.cs', 'DiagnosticNote.cs', 'DiagnosticsComparer.cs', 'Escapes.cs',
    'Exceptions.cs', 'FileId.cs', 'Floats.cs', 'LinePosition.cs', 'Phase.cs', 'ProjectFile.cs', 'Severity.cs',
    'SourceManager.cs', 'Span.cs', 'Unicode.cs', 'ToolchainVersion.cs', 'CliDiagnostics.cs', 'ExitCodes.cs',
]
HEADER = f'// Frozen from release/v4.6.0 ({COMMIT[:8]}) by tooling/from4/freeze.py: Lyric 4.6 as it was. Do not edit.\n'


def git(*args):
    return subprocess.run(['git', *args], cwd=ROOT, capture_output=True, check=True).stdout


def rename(text):
    text = re.sub(r'\bLyric(?=\.[A-Z])', 'Lyric4', text)
    text = re.sub(r'\bnamespace Lyric\b(?!4)', 'namespace Lyric4', text)
    text = re.sub(r'\busing Lyric;', 'using Lyric4;', text)
    return text


def write(relative, data, under=OUT):
    path = os.path.join(under, relative)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'wb') as f:
        f.write(data)
    return os.path.relpath(path, ROOT).replace(os.sep, '/'), hashlib.sha256(data).hexdigest()


for part in FROZEN:
    shutil.rmtree(os.path.join(OUT, part), ignore_errors=True)
shutil.rmtree(EXAMPLES, ignore_errors=True)

listed = []
for area in FRONTEND:
    for path in git('ls-tree', '-r', '--name-only', COMMIT, f'src/Lyric.Frontend/{area}').decode().split():
        if not path.endswith('.cs'):
            continue
        text = git('show', f'{COMMIT}:{path}').decode('utf-8-sig').replace('\r\n', '\n')
        listed.append(write(os.path.join(area, os.path.relpath(path, f'src/Lyric.Frontend/{area}')), (HEADER + rename(text)).encode()))
for name in FRONTEND_FILES:
    text = git('show', f'{COMMIT}:src/Lyric.Frontend/{name}').decode('utf-8-sig').replace('\r\n', '\n')
    listed.append(write(name, (HEADER + rename(text)).encode()))
for name in CORE:
    text = git('show', f'{COMMIT}:src/Lyric.Core/{name}').decode('utf-8-sig').replace('\r\n', '\n')
    listed.append(write(os.path.join('Core', name), (HEADER + rename(text)).encode()))
for path in git('ls-tree', '-r', '--name-only', COMMIT, 'stdlib').decode().split():
    data = git('show', f'{COMMIT}:{path}').replace(b'\r\n', b'\n')
    listed.append(write(path, data))
# the input of the acceptance run: 4.6's examples in Lyric 4, the .lyr files only (embedded-host is C#)
for path in git('ls-tree', '-r', '--name-only', COMMIT, 'examples').decode().split():
    if not path.endswith('.lyr'):
        continue
    data = git('show', f'{COMMIT}:{path}').replace(b'\r\n', b'\n')
    listed.append(write(os.path.relpath(path, 'examples'), data, EXAMPLES))

with open(os.path.join(OUT, 'frozen.sha256'), 'w', newline='\n') as f:
    f.write(f'# release/v4.6.0 {COMMIT}: every frozen file and its SHA-256, written by tooling/from4/freeze.py\n')
    for relative, digest in sorted(listed):
        f.write(f'{digest}  {relative}\n')
print(f'{len(listed)} files frozen')
