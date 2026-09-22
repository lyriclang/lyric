"""Welcher Aufrufpfad erzeugt welches Finding?

Neutralisiert die Instanz-Substitution an GENAU EINER Stelle, baut, lässt den Vier-Wege-Test
laufen und schreibt die Findings auf. Stellt danach das Original wieder her.
"""
import os, shutil, subprocess

root = '/home/Olivier/dev/projects/lyric/.claude/worktrees/agent-aa7b5e912a78e6f2d'
src = root + '/src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs'
backup = root + '/probes/FunctionLowerer.original.cs'
env = dict(os.environ, DOTNET_ROOT='/home/Olivier/.dotnet')
dotnet = '/home/Olivier/.dotnet/dotnet'

# (Name, exakter Ausdruck im Quelltext) — jede Stelle einzeln.
sites = [
    ('instance-method (LowerGenericMethodCall)',
     'var supplied = MaterializeArguments(declaration, expr.Arguments, member.Member, expr.Span,\n            InstanceSubstitution(owner));'),
    ('static-method (LowerGenericStaticCall)',
     'var args = MaterializeArguments(declaration, expr.Arguments, member.Member, expr.Span,\n            InstanceSubstitution(owner));'),
    ('constraint path (generic receiver)',
     'var supplied = MaterializeArguments(declaration, expr.Arguments, member.Member, expr.Span,\n            concrete is GenericInstance receiverInstance ? InstanceSubstitution(receiverInstance) : null);'),
]

shutil.copy(src, backup)
original = open(backup).read()

try:
    for name, needle in sites:
        assert needle in original, 'not found: ' + name
        # Nur die Substitution dieser einen Stelle auf null setzen.
        head, _, tail = needle.rpartition('\n            ')
        neutral = head + '\n            ((Dictionary<string, LyrType>?)null));'
        if not neutral.endswith('));'):
            neutral = head + '\n            ((Dictionary<string, LyrType>?)null));'
        open(src, 'w').write(original.replace(needle, neutral, 1))

        build = subprocess.run([dotnet, 'build', root + '/src/Lyric.Cli', '-c', 'Debug'],
                               env=env, capture_output=True, text=True, cwd=root)
        if ' error ' in build.stdout:
            print('%-42s BUILD FAILED' % name)
            print('   ', [l for l in build.stdout.split('\n') if ' error ' in l][:1])
            continue

        test = subprocess.run([dotnet, 'test', root + '/tests/Lyric.Tests.Ir', '-c', 'Debug',
                               '--filter', 'FullyQualifiedName~An_argument_widens'],
                              env=env, capture_output=True, text=True, cwd=root)
        blob = test.stdout + test.stderr
        findings = [l.strip() for l in blob.split('\n') if ': arg ' in l or ': #' in l]
        if 'Passed!' in blob:
            print('%-42s no finding (test stays green)' % name)
        else:
            print('%-42s %d finding(s):' % (name, len(findings)))
            for f in findings:
                print('      ', f)
finally:
    shutil.copy(backup, src)
    subprocess.run([dotnet, 'build', root + '/src/Lyric.Cli', '-c', 'Debug'],
                   env=env, capture_output=True, text=True, cwd=root)
    os.remove(backup)
    print('\n(original restored and rebuilt)')
