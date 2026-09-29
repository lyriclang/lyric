"""Kaschiert der INLINER den Defekt bei einer Klassenmethode?

Gleiche Klasse, zwei Körper: einer trivial (der Inliner frisst ihn), einer nicht. Gemessen
über `lyric run` (optimiert) — und derselbe Quelltext zusätzlich über den Test-Pfad, der
mit optimize:false lowert.
"""
import os, shutil, subprocess

root = '/home/Olivier/dev/projects/lyric/.claude/worktrees/agent-aa7b5e912a78e6f2d'
src = root + '/src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs'
backup = root + '/probes/FL.original2.cs'
env = dict(os.environ, DOTNET_ROOT='/home/Olivier/.dotnet', LYRIC_STDLIB=root + '/stdlib')
dotnet = '/home/Olivier/.dotnet/dotnet'
lyric = root + '/src/Lyric.Cli/bin/Debug/net10.0/lyric'

site = ('var supplied = MaterializeArguments(declaration, expr.Arguments, member.Member, expr.Span,\n'
        '            InstanceSubstitution(owner));')
neutral = ('var supplied = MaterializeArguments(declaration, expr.Arguments, member.Member, expr.Span,\n'
           '            ((Dictionary<string, LyrType>?)null));')

head = '''module probe;
import std.io.console { println };

class Box<T> {
    value: T,

'''
tail = '''}

fn main(): int {
    let slot: ?int = 7;
    let boxed = Box<?int> { value = slot };
    println(boxed.or(3) ?? -1);
    return 0;
}
'''

bodies = {
    'trivial body (inlinable)': '''    pub fn or(fallback: T): T {
        return this.value;
    }
''',
    'body with a loop (not inlined)': '''    pub fn or(fallback: T): T {
        var i = 0;
        var held = fallback;
        while (i < 3) {
            held = this.value;
            i = i + 1;
        }
        return held;
    }
''',
}

shutil.copy(src, backup)
original = open(backup).read()

try:
    open(src, 'w').write(original.replace(site, neutral, 1))
    build = subprocess.run([dotnet, 'build', root + '/src/Lyric.Cli', '-c', 'Debug'],
                           env=env, capture_output=True, text=True, cwd=root)
    assert ' error ' not in build.stdout, build.stdout[-500:]
    print('(LowerGenericMethodCall neutralised; a CLASS receiver bound to a local)\n')

    for name, body in bodies.items():
        path = os.path.join(root, 'probes', 'inl_%s.lyr' % name.split()[0])
        open(path, 'w').write(head + body + tail)
        r = subprocess.run([lyric, 'run', path], env=env, capture_output=True, text=True, cwd=root)
        blob = r.stdout + r.stderr
        finding = [l.strip() for l in blob.split('\n') if ': #' in l or 'store of' in l]
        print('%-34s via lyric run (optimised): %s'
              % (name, finding[0] if finding else 'GREEN'))
finally:
    shutil.copy(backup, src)
    subprocess.run([dotnet, 'build', root + '/src/Lyric.Cli', '-c', 'Debug'],
                   env=env, capture_output=True, text=True, cwd=root)
    os.remove(backup)
    print('\n(original restored and rebuilt)')
