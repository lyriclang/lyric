"""The gate against what it must refuse and keep, with a stand-in for rrsync. MirrorGateTests runs this."""
import json, os, shutil, stat, subprocess, sys, tempfile

GATE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'setup', 'gate.py')
root = tempfile.mkdtemp()
for d in ('incoming', 'public/dev', 'public/stable', 'public/staging'):
    os.makedirs(os.path.join(root, d))
fake = os.path.join(root, 'rrsync')
with open(fake, 'w') as f:
    f.write('#!/bin/sh\necho "rrsync $*"\n')
os.chmod(fake, 0o755)
failures = 0


def run(command):
    env = {'LYRIC_MIRROR_ROOT': root, 'LYRIC_MIRROR_RRSYNC': fake, 'SSH_ORIGINAL_COMMAND': command}
    return subprocess.run([sys.executable, '-I', GATE], env=env, capture_output=True, text=True)


def check(name, ok):
    global failures
    print(('ok  ' if ok else 'FAIL') + ' ' + name)
    failures += 0 if ok else 1


def upload(id_, files):
    d = os.path.join(root, 'incoming', id_)
    os.makedirs(d, exist_ok=True)
    for n, c in files.items():
        with open(os.path.join(d, n), 'w') as f:
            f.write(c)


def index(channel, *versions):
    return json.dumps({'channel': channel, 'sequence': 1, 'versions': [
        {'version': v, 'date': 'd', 'archives': {'x': {'file': a, 'size': s, 'sha256': '0' * 64}}} for v, a, s in versions]})


pub = lambda *p: os.path.join(root, 'public', *p)

r = run('rsync --server -logDtpr . x/')
check('rsync goes to rrsync, write-only, no delete, under incoming', 'rrsync -wo -no-del ' + root + '/incoming' in r.stdout)
for bad in ('', 'sh', 'ls /', 'publish', 'publish dev', 'publish etc x', 'publish dev ../x', 'publish dev x y', 'rsync --sender .', 'publish dev x; rm -rf /',
            'publish dev ../public', 'publish staging ../public', 'publish staging ..'):
    r = run(bad)
    check(f'refused: {bad!r}', r.returncode == 1 and 'this key can only upload and publish' in r.stderr)
check('and the channel still stands', os.path.isdir(pub('dev')))
check('no upload of that name', 'no upload named' in run('publish dev none').stderr)

A1 = 'lyric-a-linux-x64.tar.gz'
upload('u1', {A1: 'aaaa', A1 + '.minisig': 's', 'SHA256SUMS': 'x', 'dev.json': index('dev', ('1', A1, 4)), 'dev.json.minisig': 'sig1'})
r = run('publish dev u1')
check('first publish', r.returncode == 0)
check('archive in the channel, index at the top', os.path.isfile(pub('dev', A1)) and os.path.isfile(pub('dev.json')) and open(pub('dev.json.minisig')).read() == 'sig1')
check('upload gone', not os.path.exists(os.path.join(root, 'incoming', 'u1')))
check('files readable by the web server', stat.S_IMODE(os.stat(pub('dev', A1)).st_mode) == 0o644)

before = open(pub('dev.json')).read()
upload('u2', {'dev.json': index('dev', ('2', 'lyric-missing-linux-x64.tar.gz', 4)), 'dev.json.minisig': 's2'})
r = run('publish dev u2')
check('an index naming what is not there is refused', r.returncode != 0 and 'neither uploaded nor published' in r.stderr)
check('and the channel is as it was', open(pub('dev.json')).read() == before)
shutil.rmtree(os.path.join(root, 'incoming', 'u2'))

upload('u3', {A1: 'aa', 'dev.json': index('dev', ('2', A1, 4)), 'dev.json.minisig': 's'})
check('a size that differs is refused', 'bytes' in run('publish dev u3').stderr and open(pub('dev.json')).read() == before)
shutil.rmtree(os.path.join(root, 'incoming', 'u3'))

upload('u4', {'dev.json': index('stable'), 'dev.json.minisig': 's'})
check('the index of another channel is refused', "uploaded as 'dev'" in run('publish dev u4').stderr)
upload('u5', {'dev.json': '{not json', 'dev.json.minisig': 's'})
check('what is no JSON is refused', 'is no index' in run('publish dev u5').stderr)
upload('u6', {'dev.json.minisig': 's'})
check('no index, refused', 'dev.json is not in the upload' in run('publish dev u6').stderr)
upload('u7', {'dev.json': json.dumps({'channel': 'dev', 'sequence': 1, 'versions': [{'archives': {'x': {'file': '../../etc/passwd', 'size': 1}}}]}), 'dev.json.minisig': 's'})
check('a path in an archive name is refused', 'file name' in run('publish dev u7').stderr)
os.symlink('/etc', os.path.join(root, 'incoming', 'u8'))
check('a link as an upload is refused', 'no upload named' in run('publish dev u8').stderr)
os.remove(os.path.join(root, 'incoming', 'u8'))
upload('u9', {'dev.json': index('dev', ('1', A1, 4)), 'dev.json.minisig': 's'})
os.symlink('/etc/passwd', os.path.join(root, 'incoming', 'u9', 'sneak'))
check('a link inside an upload is refused', 'not a plain file' in run('publish dev u9').stderr)
for u in ('u4', 'u5', 'u6', 'u7', 'u9'):
    shutil.rmtree(os.path.join(root, 'incoming', u))

A2 = 'lyric-b-linux-x64.tar.gz'
upload('v2', {A2: 'bbbb', A2 + '.minisig': 's', 'SHA256SUMS': 'y', 'dev.json': index('dev', ('2', A2, 4), ('1', A1, 4)), 'dev.json.minisig': 'sig2'})
check('second publish keeps the first archive, still named', run('publish dev v2').returncode == 0 and os.path.isfile(pub('dev', A1)) and os.path.isfile(pub('dev', A2)))
A3 = 'lyric-c-linux-x64.tar.gz'
upload('v3', {A3: 'cccc', A3 + '.minisig': 's', 'SHA256SUMS': 'z', 'dev.json': index('dev', ('3', A3, 4), ('2', A2, 4)), 'dev.json.minisig': 'sig3'})
r = run('publish dev v3')
check('third publish removes the archive no longer named, with its signature', r.returncode == 0 and not os.path.exists(pub('dev', A1)) and not os.path.exists(pub('dev', A1 + '.minisig')) and os.path.isfile(pub('dev', A2)))
check('and leaves the sums', os.path.isfile(pub('dev', 'SHA256SUMS')))

S1 = 'lyric-5-linux-x64.tar.gz'
upload('s1', {S1: 'ssss', 'stable.json': index('stable', ('5', S1, 4)), 'stable.json.minisig': 's'})
check('stable publishes', run('publish stable s1').returncode == 0)
upload('s2', {'other': 'o', 'stable.json': index('stable'), 'stable.json.minisig': 's'})
check('stable never removes an archive', run('publish stable s2').returncode == 0 and os.path.isfile(pub('stable', S1)))

for i in range(5):
    upload(f't{i}', {'hello.txt': str(i)})
    check(f'staging {i}', run(f'publish staging t{i}').returncode == 0)
    os.utime(pub('staging', f't{i}'), (1000 + i, 1000 + i))
upload('t9', {'hello.txt': 'x'})
run('publish staging t9')
check('staging keeps three', sorted(os.listdir(pub('staging'))) == ['t3', 't4', 't9'])

os.makedirs(os.path.join(root, 'incoming', 'old'))
os.utime(os.path.join(root, 'incoming', 'old'), (1000, 1000))
upload('fresh', {'hello.txt': 'x'})
run('publish staging fresh')
check('an upload left for a day is cleared', not os.path.exists(os.path.join(root, 'incoming', 'old')))
shutil.rmtree(root)
print('failures:', failures)
sys.exit(1 if failures else 0)
