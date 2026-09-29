"""Kann meine Korpusmessung überhaupt rot werden?

Die Kontrolle zur Schlussmessung: Optimize=false UND die Instanz-Substitution entfernt, dann
eine Datei mit dem bekannten Defekt durch GENAU DIE ZWEI Messpfade schicken, über die das
Korpus geprüft wurde — `lyric test` (stdlib-tests) und `lyrc build` (examples). Meldet einer
der beiden nichts, war die Messung auf diesem Pfad blind.
"""
import os, shutil, subprocess

root = '/home/Olivier/dev/projects/lyric/.claude/worktrees/agent-aa7b5e912a78e6f2d'
compiler = root + '/src/Lyric.Frontend/Compiler/SourceCompiler.cs'
lowerer = root + '/src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs'
env = dict(os.environ, DOTNET_ROOT='/home/Olivier/.dotnet', LYRIC_STDLIB=root + '/stdlib',
           LYRIC_TEST=root + '/src/Lyrtest/bin/Debug/net10.0/lyrtest')
dotnet = '/home/Olivier/.dotnet/dotnet'

opt_site = 'public bool Optimize { get; init; } = true;'
sub_site = ('var supplied = MaterializeArguments(declaration, expr.Arguments, member.Member, expr.Span,\n'
            '            InstanceSubstitution(owner));')
sub_neutral = ('var supplied = MaterializeArguments(declaration, expr.Arguments, member.Member, expr.Span,\n'
               '            ((Dictionary<string, LyrType>?)null));')

# Eine Datei, die den Defekt sicher auslöst: Klassenmethode mit nicht einbettbarem Körper.
defect = '''class Box<T> {
    value: T,

    pub fn or(fallback: T): T {
        var i = 0;
        var held = fallback;
        while (i < 3) {
            held = this.value;
            i = i + 1;
        }
        return held;
    }
}
'''

test_file = root + '/stdlib-tests/tests/zz_control_tests.lyr'
example_file = root + '/examples/zz_control.lyr'

backups = {}
for f in (compiler, lowerer):
    backups[f] = f + '.ctlbak'
    shutil.copy(f, backups[f])

def build(project):
    r = subprocess.run([dotnet, 'build', root + '/src/' + project, '-c', 'Debug'],
                       env=env, capture_output=True, text=True, cwd=root)
    assert ' error ' not in r.stdout, r.stdout[-400:]

try:
    open(compiler, 'w').write(open(backups[compiler]).read()
                              .replace(opt_site, 'public bool Optimize { get; init; } = false;', 1))
    src = open(backups[lowerer]).read()
    assert sub_site in src
    open(lowerer, 'w').write(src.replace(sub_site, sub_neutral, 1))
    build('Lyric.Cli')
    build('Lyrtest')
    print('(Optimize=false AND the instance substitution removed — both paths must now report)\n')

    # Pfad A: lyric test über stdlib-tests
    open(test_file, 'w').write('''import std.test { Test, assertTrue };

''' + defect + '''
@Test
pub fn control(): void {
    let slot: ?int = 7;
    let boxed = Box<?int> { value = slot };
    assertTrue(boxed.or(3) != null, "control");
}
''')
    r = subprocess.run([root + '/src/Lyric.Cli/bin/Debug/net10.0/lyric', 'test'],
                       cwd=root + '/stdlib-tests', env=env, capture_output=True, text=True)
    blob = r.stdout + r.stderr
    hit = [l.strip() for l in blob.split('\n') if ': arg ' in l or 'store of' in l or 'malformed' in l]
    print('path A  `lyric test` (stdlib-tests): %s' % (hit[0] if hit else 'NO FINDING — blind!'))

    # Pfad B: lyrc build über eine Beispieldatei
    open(example_file, 'w').write('''module control;
import std.io.console { println };

''' + defect + '''
fn main(): int {
    let slot: ?int = 7;
    let boxed = Box<?int> { value = slot };
    println(boxed.or(3) ?? -1);
    return 0;
}
''')
    r = subprocess.run([root + '/src/Lyric.Cli/bin/Debug/net10.0/lyrc', 'build', example_file,
                        '-o', example_file + '.lyrbc'], env=env, capture_output=True, text=True)
    blob = r.stdout + r.stderr
    hit = [l.strip() for l in blob.split('\n') if ': arg ' in l or 'store of' in l or 'malformed' in l]
    print('path B  `lyrc build` (examples):     %s' % (hit[0] if hit else 'NO FINDING — blind!'))
finally:
    for f, b in backups.items():
        shutil.copy(b, f)
        os.remove(b)
    for f in (test_file, example_file, example_file + '.lyrbc'):
        if os.path.exists(f):
            os.remove(f)
    build('Lyric.Cli')
    build('Lyrtest')
    print('\n(sources restored, rebuilt, control files removed)')
