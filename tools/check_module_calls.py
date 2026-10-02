#!/usr/bin/env python3
"""check_module_calls.py GAMEDATA_DIR [CONFIG_SUBDIR]: calls of script functions that the script does not define:
`module.func(` in scripts, and <action>/<precondition>/functor references `module.func` in xml and ltx.
X-Ray stops with 'attempt to call field ... (a nil value)' or 'Cannot find saved game ... functor' when one is reached."""
import sys, os, re, collections
root = sys.argv[1]; sub = sys.argv[2] if len(sys.argv) > 2 else 'configs'
sd = os.path.join(root, 'scripts'); src, defs = {}, {}
for f in os.listdir(sd):
    if f.endswith('.script'):
        t = open(os.path.join(sd, f), 'rb').read().decode('cp1251', 'replace')
        t = re.sub(r'--\[\[.*?\]\]', '', t, flags=re.S)
        src[f[:-7]] = t
        d = set(re.findall(r'^\s*function\s+([A-Za-z_]\w*)\s*\(', t, re.M))
        d |= set(re.findall(r'^([A-Za-z_]\w*)\s*=', t, re.M))
        d |= set(re.findall(r'^\s*class\s*"(\w+)"', t, re.M))
        d |= set(re.findall(r'^\s*local\s+function\s+(\w+)', t, re.M))  # not callable from outside, kept to cut noise
        defs[f[:-7]] = d
call = re.compile(r'(?<![\w\.:"\'\\])([a-z_]\w*)\.([A-Za-z_]\w*)\s*\(')
for m, t in sorted(src.items()):
    for n, line in enumerate(t.split('\n'), 1):
        code = line.split('--', 1)[0]
        for mod, fn in call.findall(code):
            if mod in defs and mod != m and fn not in defs[mod] and not re.search(r'\blocal\s+' + mod + r'\b', t):
                print(f'scripts/{m}.script:{n}: {mod}.{fn}: {code.strip()[:130]}')
ref = re.compile(r'<(?:action|precondition)>\s*([a-z_]\w*)\.(\w+)\s*<')
for dp, _, files in os.walk(os.path.join(root, sub)):
    for f in sorted(files):
        if not f.endswith('.xml'): continue
        p = os.path.join(dp, f)
        t = re.sub(r'<!--.*?-->', '', open(p, 'rb').read().decode('cp1251', 'replace'), flags=re.S)
        for n, line in enumerate(t.split('\n'), 1):
            for mod, fn in ref.findall(line):
                if mod not in defs: print(f'{os.path.relpath(p, root)}:{n}: no script {mod}: {line.strip()[:120]}')
                elif fn not in defs[mod]: print(f'{os.path.relpath(p, root)}:{n}: {mod}.{fn} not defined: {line.strip()[:120]}')
