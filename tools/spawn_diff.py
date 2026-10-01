#!/usr/bin/env python3
"""Diff two CS all.spawn files: spawn objects (by object name: custom_data changes, added/removed) and patrol points
(by path name + index: name / position / vertex changes). usage: spawn_diff.py retail.spawn mod.spawn out.json"""
import struct, sys, json
def load(p):
    d = open(p, 'rb').read()
    def ch(a, end):
        while a < end:
            c, s = struct.unpack_from('<II', d, a); yield c, a + 8, s; a += 8 + s
    top = {c: (a, s) for c, a, s in ch(0, len(d))}
    objs, paths = {}, {}
    a, s = top[1]; parts = {c: (x, y) for c, x, y in ch(a, a + s)}
    la, ls = parts[1]
    for _, oa, os_ in ch(la, la + ls):
        sub = {c: (x, y) for c, x, y in ch(oa, oa + os_)}
        ba, bs = sub[1]; body = {c: (x, y) for c, x, y in ch(ba, ba + bs)}
        sa, ss = body[0]
        r = sa + 4
        def rs():
            nonlocal r
            e = d.index(b'\0', r); v = d[r:e].decode('latin-1'); r = e + 1; return v
        section = rs(); name = rs()
        r += 1 + 1 + 12 + 12 + 8 + 2 + 2 + 2 + 2
        cd = struct.unpack_from('<H', d, r)[0]; r += 2 + cd + 2
        r += 2 + 2 + 4 + 4 + 4 + 4
        custom = rs()
        objs.setdefault(name, []).append((section, custom))
    a, s = top[3]; parts = {c: (x, y) for c, x, y in ch(a, a + s)}
    la, ls = parts[1]
    for _, pa, ps in ch(la, la + ls):
        sub = {c: (x, y) for c, x, y in ch(pa, pa + ps)}
        na, ns = sub[0]; pname = d[na:na + ns - 1].decode('latin-1')
        ga, gs = sub[1]; g = {c: (x, y) for c, x, y in ch(ga, ga + gs)}
        va, vs = g[1]; pts = {}
        for vi, xa, xs in ch(va, va + vs):
            v = {c: (x, y) for c, x, y in ch(xa, xa + xs)}
            qa, qs = v[1]; e = d.index(b'\0', qa)
            pts[vi] = (d[qa:e].decode('latin-1'),) + struct.unpack_from('<3fIIH', d, e + 1)
        paths[pname] = pts
    return objs, paths
ro, rp = load(sys.argv[1]); mo, mp = load(sys.argv[2])
out = {'custom': [], 'removed': [], 'added': [], 'points': [], 'dup_names': []}
for n, v in ro.items():
    if len(v) > 1: out['dup_names'].append(n); continue
    if n not in mo: out['removed'].append(n); continue
    if len(mo[n]) == 1 and mo[n][0][1] != v[0][1]: out['custom'].append({'name': n, 'section': v[0][0], 'old': v[0][1], 'new': mo[n][0][1]})
out['added'] = [n for n in mo if n not in ro]
for pn, pts in rp.items():
    if pn not in mp: continue
    for i, old in pts.items():
        new = mp[pn].get(i)
        if new and new != old: out['points'].append({'path': pn, 'point': i, 'old': old, 'new': new})
json.dump(out, open(sys.argv[3], 'w'), indent=1)
print({k: len(v) for k, v in out.items()}, 'paths only in mod', len(set(mp) - set(rp)), 'removed paths', len(set(rp) - set(mp)))
