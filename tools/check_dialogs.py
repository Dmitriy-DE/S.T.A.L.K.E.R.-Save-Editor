#!/usr/bin/env python3
"""Find dialog tree errors that crash X-Ray. Research tool for our own fixes (docs/roadmap/FIX-PACKS.md).
Checks: <next> pointing at a missing phrase, duplicate phrase ids, no start phrase 0, and (with --scripts DIR)
<precondition>/<action>/<script_text> calling a module or function that does not exist.
usage: check_dialogs.py [--scripts DIR] FILE.xml..."""
import glob, os, re, sys

args = sys.argv[1:]
funcs = None
if args[:1] == ['--scripts']:
    funcs = {}
    for p in glob.glob(os.path.join(args[1], '*.script')):
        t = open(p, 'rb').read().decode('cp1251', 'replace')
        funcs[os.path.basename(p)[:-7]] = set(re.findall(r'^\s*function\s+(\w+)\s*\(', t, re.M)) | set(re.findall(r'^\s*(\w+)\s*=\s*function', t, re.M))
    args = args[2:]
for path in args:
    text = open(path, 'rb').read().decode('cp1251', 'replace')
    for m in re.finditer(r'<dialog\s+id="([^"]+)"(.*?)</dialog>', text, re.S):
        did, body = m.group(1), m.group(2)
        ids = re.findall(r'<phrase\s+id="([^"]+)"', body)
        known = set(ids)
        for dup in sorted({i for i in ids if ids.count(i) > 1}): print(f'{path}: {did}: duplicate phrase id {dup}')
        if ids and '0' not in known: print(f'{path}: {did}: no start phrase 0')
        for ph in re.finditer(r'<phrase\s+id="([^"]+)"(.*?)</phrase>', body, re.S):
            for n in re.findall(r'<next>\s*([^<\s]+)\s*</next>', ph.group(2)):
                if n not in known: print(f'{path}: {did}: phrase {ph.group(1)} -> missing {n}')
        if funcs is None: continue
        for tag in ('precondition', 'action', 'script_text'):
            for v in re.findall(rf'<{tag}>\s*([^<\s]+)\s*</{tag}>', body):
                mod, _, fn = v.partition('.')
                if fn and (mod not in funcs or fn not in funcs[mod]): print(f'{path}: {did}: {tag} {v} does not exist')
