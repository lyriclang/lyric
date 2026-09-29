"""Verdeckt der Optimierer heute noch weitere Lowering-Fehler?

Setzt CompilerOptions.Optimize testweise auf false — dann verifiziert der Verifier das
GELOWERTE IR statt des optimierten — und kompiliert damit die ganze stdlib (über die
stdlib-Tests) sowie die Beispiele. Jeder Verifier-Befund, der dabei auftaucht, ist heute im
Normalbetrieb unsichtbar.
"""
import os, shutil, subprocess

root = '/home/Olivier/dev/projects/lyric/.claude/worktrees/agent-aa7b5e912a78e6f2d'
src = root + '/src/Lyric.Frontend/Compiler/SourceCompiler.cs'
backup = root + '/probes/SourceCompiler.original.cs'
env = dict(os.environ, DOTNET_ROOT='/home/Olivier/.dotnet',
           LYRIC_STDLIB=root + '/stdlib',
           LYRIC_TEST=root + '/src/Lyrtest/bin/Debug/net10.0/lyrtest')
dotnet = '/home/Olivier/.dotnet/dotnet'

site = 'public bool Optimize { get; init; } = true;'
neutral = 'public bool Optimize { get; init; } = false;'

shutil.copy(src, backup)
original = open(backup).read()
assert site in original

def build(project):
    r = subprocess.run([dotnet, 'build', root + '/src/' + project, '-c', 'Debug'],
                       env=env, capture_output=True, text=True, cwd=root)
    assert ' error ' not in r.stdout, r.stdout[-500:]

try:
    open(src, 'w').write(original.replace(site, neutral, 1))
    build('Lyric.Cli')
    build('Lyrtest')
    print('(CompilerOptions.Optimize = false — the verifier now sees lowered IR)\n')

    # 1. Die ganze stdlib plus ihre Tests.
    r = subprocess.run([root + '/src/Lyric.Cli/bin/Debug/net10.0/lyric', 'test'],
                       cwd=root + '/stdlib-tests', env=env, capture_output=True, text=True)
    blob = r.stdout + r.stderr
    findings = [l.strip() for l in blob.split('\n') if ': #' in l or 'malformed IR' in l]
    tail = [l for l in blob.strip().split('\n') if 'test(s)' in l]
    print('stdlib + stdlib-tests: %s' % (findings[0] if findings else (tail[-1] if tail else 'no summary')))
    for f in findings[1:6]:
        print('   ', f)

    # 2. Die Beispiele des Repositoriums, einzeln.
    examples = []
    for base, _, files in os.walk(root + '/examples'):
        for f in files:
            if f.endswith('.lyr'):
                examples.append(os.path.join(base, f))
    broken = []
    for path in sorted(examples):
        r = subprocess.run([root + '/src/Lyric.Cli/bin/Debug/net10.0/lyrc', 'build', path,
                            '-o', path + '.probe.lyrbc'], env=env, capture_output=True, text=True)
        out = r.stdout + r.stderr
        if 'malformed IR' in out or ': #' in out:
            broken.append((os.path.relpath(path, root),
                           [l.strip() for l in out.split('\n') if ': #' in l][:1]))
        if os.path.exists(path + '.probe.lyrbc'):
            os.remove(path + '.probe.lyrbc')
    print('\nexamples: %d compiled, %d with a verifier finding' % (len(examples), len(broken)))
    for name, detail in broken:
        print('   ', name, detail)
finally:
    shutil.copy(backup, src)
    build('Lyric.Cli')
    build('Lyrtest')
    os.remove(backup)
    print('\n(original restored and rebuilt)')
