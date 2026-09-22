"""Bricht eine KLASSE im Instanz-Pfad — und wovon hängt es ab?

Neutralisiert nur LowerGenericMethodCall und misst drei Klassenformen, die sich genau in
den zwei Merkmalen unterscheiden, in denen sich meine Testklasse und pattern-lambdas Probe
unterscheiden: Interface-Konformanz und eine statische Methode daneben.
"""
import os, shutil, subprocess

root = '/home/Olivier/dev/projects/lyric/.claude/worktrees/agent-aa7b5e912a78e6f2d'
src = root + '/src/Lyric.Frontend/Ir/Lowering/FunctionLowerer.cs'
backup = root + '/probes/FL.original.cs'
env = dict(os.environ, DOTNET_ROOT='/home/Olivier/.dotnet',
           LYRIC_STDLIB=root + '/stdlib')
dotnet = '/home/Olivier/.dotnet/dotnet'
lyric = root + '/src/Lyric.Cli/bin/Debug/net10.0/lyric'

site = ('var supplied = MaterializeArguments(declaration, expr.Arguments, member.Member, expr.Span,\n'
        '            InstanceSubstitution(owner));')
neutral = ('var supplied = MaterializeArguments(declaration, expr.Arguments, member.Member, expr.Span,\n'
           '            ((Dictionary<string, LyrType>?)null));')

variants = {
    'a plain class, no conformance, no static': '''module probe;
import std.io.console { println };

class Box<T> {
    value: T,

    pub fn or(fallback: T): T {
        return this.value;
    }
}

fn main(): int {
    let slot: ?int = 7;
    println(Box<?int> { value = slot }.or(3) ?? -1);
    return 0;
}
''',
    'b class WITH conformance, no static': '''module probe;
import std.io.console { println };

interface Keeper<T> {
    fn or(fallback: T): T;
}

class Box<T> :: [Keeper<T>] {
    value: T,

    pub fn or(fallback: T): T {
        return this.value;
    }
}

fn main(): int {
    let slot: ?int = 7;
    println(Box<?int> { value = slot }.or(3) ?? -1);
    return 0;
}
''',
    'c class WITH conformance AND a static method': '''module probe;
import std.io.console { println };

interface Keeper<T> {
    fn or(fallback: T): T;
}

class Box<T> :: [Keeper<T>] {
    value: T,

    pub static fn of(v: T): Box<T> {
        return Box<T> { value = v };
    }

    pub fn or(fallback: T): T {
        return this.value;
    }
}

fn main(): int {
    let slot: ?int = 7;
    println(Box<?int> { value = slot }.or(3) ?? -1);
    return 0;
}
''',
    'd enum (the control)': '''module probe;
import std.io.console { println };

enum Holder<T> {
    Full(T),
    Empty;

    pub fn or(fallback: T): T {
        return match (this) {
            Full(v) => v,
            Empty => fallback,
        };
    }
}

fn main(): int {
    let slot: ?int = 7;
    println(Holder<?int>.Full(slot).or(3) ?? -1);
    return 0;
}
''',
}

shutil.copy(src, backup)
original = open(backup).read()
assert site in original

try:
    open(src, 'w').write(original.replace(site, neutral, 1))
    build = subprocess.run([dotnet, 'build', root + '/src/Lyric.Cli', '-c', 'Debug'],
                           env=env, capture_output=True, text=True, cwd=root)
    assert ' error ' not in build.stdout, build.stdout[-600:]
    print('(LowerGenericMethodCall neutralised)\n')

    for name, source in sorted(variants.items()):
        path = os.path.join(root, 'probes', 'var_%s.lyr' % name[0])
        open(path, 'w').write(source)
        r = subprocess.run([lyric, 'run', path], env=env, capture_output=True, text=True, cwd=root)
        blob = r.stdout + r.stderr
        finding = [l.strip() for l in blob.split('\n') if ': #' in l or 'store of' in l]
        print('%-46s %s' % (name, finding[0] if finding else 'GREEN (no finding)'))
finally:
    shutil.copy(backup, src)
    subprocess.run([dotnet, 'build', root + '/src/Lyric.Cli', '-c', 'Debug'],
                   env=env, capture_output=True, text=True, cwd=root)
    os.remove(backup)
    print('\n(original restored and rebuilt)')
