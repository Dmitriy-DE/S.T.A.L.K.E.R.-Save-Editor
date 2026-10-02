#!/usr/bin/env python3
"""lua_globals.py SCRIPTS_DIR: globals that a script reads but nothing defines (typos such as 'st' for 'self.st').
Uses `luac5.1 -l -p`. A name is reported when it is read in at most MAXFILES files (default 1), never assigned in any
script, and is not a script (module) name. Engine globals are used in many files, so they drop out."""
import sys, os, re, subprocess, collections
d = sys.argv[1]; maxfiles = int(os.environ.get('MAXFILES', '1'))
reads, sets, where = collections.defaultdict(set), set(), collections.defaultdict(list)
mods = {f[:-7] for f in os.listdir(d) if f.endswith('.script')}
src = {}
for f in os.listdir(d):
    if f.endswith('.script'):
        src[f] = open(os.path.join(d, f), 'rb').read().decode('cp1251', 'replace').split('\n')
        for m in re.finditer(r'class\s*\(?\s*"(\w+)"', '\n'.join(src[f])): sets.add(m.group(1))
for f in sorted(os.listdir(d)):
    if not f.endswith('.script'): continue
    out = subprocess.run(['luac5.1', '-l', '-l', '-p', os.path.join(d, f)], capture_output=True, text=True, errors='replace').stdout
    for m in re.finditer(r'\[(\d+)\]\s+(GETGLOBAL|SETGLOBAL)\s+\S+\s+\S+\s+; (\S+)', out):
        line, op, name = int(m.group(1)), m.group(2), m.group(3)
        if op == 'SETGLOBAL': sets.add(name)
        else: reads[name].add(f); where[(name, f)].append(line)
for name in sorted(reads):
    if name in sets or name in mods or len(reads[name]) > maxfiles: continue
    if re.match(r'^(C[A-Z]|DIK_|cse_|[A-Z][A-Z_0-9]+$)', name): continue
    for f in sorted(reads[name]):
        for ln in sorted(set(where[(name, f)]))[:3]: print(f"{f}:{ln}: {name}: {src[f][ln-1].strip()[:150]}")
