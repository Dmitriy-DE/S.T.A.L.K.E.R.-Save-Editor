#!/usr/bin/env python3
"""Recompute enhancedEditionSha256 for CS fixes from a full EE dump: a fix gets EE hashes only if every patch anchor
is unique in the EE file. usage: ee_variants.py game-fixes.json EE_DUMP_DIR [fix-id ...]"""
import json, hashlib, os, sys
J, EE, only = sys.argv[1], sys.argv[2], set(sys.argv[3:])
d = json.load(open(J, encoding='utf-8'))
for f in d['definitions']:
    if f['game'] != 'ClearSky' or (only and f['id'] not in only) or f.get('overlays'): continue
    hashes = []
    for p in f['textPatches']:
        path = os.path.join(EE, p['relativePath'][len('gamedata/'):])
        if not os.path.exists(path): hashes = None; break
        raw = open(path, 'rb').read()
        if raw.decode('latin-1').count(p['expectedText']) != 1: hashes = None; break
        hashes.append(hashlib.sha256(raw).hexdigest())
    old = d['enhancedEditionSha256'].get(f['id'])
    if hashes: d['enhancedEditionSha256'][f['id']] = hashes
    else: d['enhancedEditionSha256'].pop(f['id'], None)
    if old != (hashes or None): print(f"{f['id']}: EE {'yes' if hashes else 'no'} (was {'yes' if old else 'no'})")
json.dump(d, open(J, 'w', encoding='utf-8'), ensure_ascii=False, indent=2); open(J, 'a').write('\n')
