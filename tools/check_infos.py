#!/usr/bin/env python3
"""check_infos.py GAMEDATA_DIR [CONFIG_SUBDIR]: info portions that logic tests with {+name} but nothing ever gives
(no %+name% in any .ltx, no <give_info> in dialogs, no mention in a script). Usually a misspelt name: the condition
can never become true."""
import sys, os, re, collections
root = sys.argv[1]; sub = sys.argv[2] if len(sys.argv) > 2 else 'configs'
given, tested, scripts = set(), collections.defaultdict(list), ''
for f in os.listdir(os.path.join(root, 'scripts')):
    if f.endswith('.script'): scripts += open(os.path.join(root, 'scripts', f), 'rb').read().decode('cp1251', 'replace')
for dp, _, files in os.walk(os.path.join(root, sub)):
    for f in files:
        path = os.path.join(dp, f)
        if f.endswith('.xml'):
            t = open(path, 'rb').read().decode('cp1251', 'replace')
            given.update(re.findall(r'<give_info>\s*([\w\.]+)\s*</give_info>', t))
        elif f.endswith('.ltx'):
            for n, line in enumerate(open(path, 'rb').read().decode('cp1251', 'replace').splitlines(), 1):
                body = line.split(';', 1)[0]
                for blk in re.findall(r'%([^%]*)%', body): given.update(re.findall(r'(?:^|\s)\+([\w\.]+)', blk))
                for blk in re.findall(r'\{([^{}]*)\}', body):
                    for name in re.findall(r'(?:^|\s)\+([\w\.]+)', blk): tested[name].append((os.path.relpath(path, root), n, line.strip()))
for name in sorted(tested):
    if name in given or re.search(r'\b' + re.escape(name) + r'\b', scripts): continue
    p, n, line = tested[name][0]
    print(f'{name} ({len(tested[name])}x): {p}:{n}: {line[:140]}')
