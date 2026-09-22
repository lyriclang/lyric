"""Verifizieren die IR-erzeugenden Testsuiten dieses Branches auch unoptimiert sauber?

pattern-lambda hat das für ihren Baum gemessen; dies ist die Entsprechung für meinen, der zwei
Lowering-Fixes und die neue stdlib mitbringt. `ModuleLowerer.Lower` lowert dann per Standard
unoptimiert, sodass der Verifier das Ergebnis des LOWERINGS prüft statt das des Optimierers.
Erwartet: keine Verifier-Befunde; Abweichungen nur dort, wo ein Test Inlining voraussetzt.
"""
import os, re, shutil, subprocess

root = '/home/Olivier/dev/projects/lyric/.claude/worktrees/agent-aa7b5e912a78e6f2d'
src = root + '/src/Lyric.Frontend/Ir/Lowering/ModuleLowerer.cs'
backup = root + '/probes/ModuleLowerer.original.cs'
env = dict(os.environ, DOTNET_ROOT='/home/Olivier/.dotnet')
dotnet = '/home/Olivier/.dotnet/dotnet'

site = 'DiagnosticEngine de, bool? verify = null, bool optimize = true, bool libraryRoots = false)'
neutral = 'DiagnosticEngine de, bool? verify = null, bool optimize = false, bool libraryRoots = false)'

suites = ['Lyric.Tests.Ir', 'Lyric.Tests.Sema', 'Lyric.Tests.Vm', 'Lyric.Tests.Bytecode']

shutil.copy(src, backup)
original = open(backup).read()
assert site in original

def run(label):
    for suite in suites:
        r = subprocess.run([dotnet, 'test', root + '/tests/' + suite, '-c', 'Debug'],
                           env=env, capture_output=True, text=True, cwd=root)
        blob = r.stdout + r.stderr
        summary = [l.strip() for l in blob.split('\n') if 'Passed!' in l or 'Failed!' in l]
        verifier = [l.strip() for l in blob.split('\n')
                    if 'malformed IR' in l or 'InternalCompilationException' in l]
        failed = re.findall(r'Failed\s+(\S+)', blob)
        line = summary[0].split(' - ')[0].strip() if summary else 'no summary'
        print('%-8s %-22s %s' % (label, suite.replace('Lyric.Tests.', ''), line))
        if verifier:
            print('         VERIFIER FINDINGS: %s' % verifier[0][:110])
        elif failed:
            print('         failures (no verifier finding): %s' % ', '.join(sorted(set(failed))[:4]))

try:
    open(src, 'w').write(original.replace(site, neutral, 1))
    b = subprocess.run([dotnet, 'build', root + '/Lyric.slnx', '-c', 'Debug'],
                       env=env, capture_output=True, text=True, cwd=root)
    assert ' error ' not in b.stdout, b.stdout[-400:]
    print('(ModuleLowerer.Lower default: optimize = false)\n')
    run('unopt')
finally:
    shutil.copy(backup, src)
    subprocess.run([dotnet, 'build', root + '/Lyric.slnx', '-c', 'Debug'],
                   env=env, capture_output=True, text=True, cwd=root)
    os.remove(backup)
    print('\n(original restored and rebuilt)')
