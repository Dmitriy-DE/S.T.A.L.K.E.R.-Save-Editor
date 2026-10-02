#!/usr/bin/env python3
"""check_condfuncs.py GAMEDATA_DIR [CONFIG_SUBDIR]: condlist functions used in .ltx logic that xr_conditions.script /
xr_effects.script do not define. X-Ray aborts ('function ... is not defined in xr_conditions') when such a line is evaluated."""
import sys, os, re
root = sys.argv[1]; sub = sys.argv[2] if len(sys.argv) > 2 else 'configs'
def defs(name):
    t = open(os.path.join(root, 'scripts', name), 'rb').read().decode('cp1251', 'replace')
    return set(re.findall(r'^\s*function\s+(\w+)\s*\(', t, re.M))
cond, eff = defs('xr_conditions.script'), defs('xr_effects.script')
for dp, _, files in os.walk(os.path.join(root, sub)):
    for f in sorted(files):
        if not f.endswith('.ltx'): continue
        path = os.path.join(dp, f)
        for n, line in enumerate(open(path, 'rb').read().decode('cp1251', 'replace').splitlines(), 1):
            body = line.split(';', 1)[0]
            if '=' not in body or body.lstrip().startswith('['): continue
            value = body.split('=', 1)[1]
            for blk in re.findall(r'\{([^{}]*)\}', value):
                for fn in re.findall(r'(?:^|\s)[=!](\w+)', blk):
                    if fn not in cond: print(f'{os.path.relpath(path, root)}:{n}: condition {fn}: {line.strip()[:150]}')
            for blk in re.findall(r'%([^%]*)%', value):
                for fn in re.findall(r'(?:^|\s)=(\w+)', blk):
                    if fn not in eff: print(f'{os.path.relpath(path, root)}:{n}: effect {fn}: {line.strip()[:150]}')
