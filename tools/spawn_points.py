#!/usr/bin/env python3
"""SRP all.spawn waypoint alterations that keep the record size (position / level_vertex_id / game_vertex_id / flags):
walk the retail patrol chunks (path: 0=name, 1=graph{0=count, 1=vertices{i: 0=id, 1=point}, 2=edges}) and emit exact
byte replacements. usage: spawn_points.py Alterations.txt all.spawn out.json"""
import re, struct, sys, json
alt = open(sys.argv[1], 'rb').read().decode('cp1252')
d = open(sys.argv[2], 'rb').read()
cur, want = None, {}
for line in alt.splitlines():
    m = re.match(r'^\[([^\]]+)\]\s*$', line)
    if m: cur = m.group(1); continue
    m = re.match(r'^p(\d+):(position|level_vertex_id|game_vertex_id|flags)\s*=\s*(\S+)', line)
    if m and cur: want.setdefault(cur, {}).setdefault(int(m.group(1)), {})[m.group(2)] = m.group(3)
def chunks(a, end):
    while a < end:
        c, s = struct.unpack_from('<II', d, a); yield c, a + 8, s; a += 8 + s
res = []
for path, pts in want.items():
    key = path.encode() + b'\0'
    hits = [m.start() for m in re.finditer(re.escape(key), d) if struct.unpack_from('<II', d, m.start() - 8) == (0, len(key))]
    if len(hits) != 1: print('SKIP', path, 'name chunks', len(hits)); continue
    g = hits[0] + len(key)
    c, s = struct.unpack_from('<II', d, g); assert c == 1
    verts = {}
    for c1, a1, s1 in chunks(g + 8, g + 8 + s):
        if c1 == 1:
            for c2, a2, s2 in chunks(a1, a1 + s1):
                sub = {x: (a, z) for x, a, z in chunks(a2, a2 + s2)}
                verts[c2] = sub[1]
    for idx, props in sorted(pts.items()):
        a, z = verts[idx]
        e = d.index(b'\0', a); name = d[a:e]
        x, y, zz, fl, lv, gv = struct.unpack_from('<3fIIH', d, e + 1)
        nx = tuple(float(v) for v in props['position'].split(',')) if 'position' in props else (x, y, zz)
        new = name + b'\0' + struct.pack('<3fIIH', *nx, int(props.get('flags', fl)), int(props.get('level_vertex_id', lv)), int(props.get('game_vertex_id', gv)))
        old = d[a:a + z]
        assert len(old) == len(new)
        print(f'{path} p{idx} {name.decode()} ({x:.3f},{y:.3f},{zz:.3f}) lv={lv} gv={gv} -> ({nx[0]:.3f},{nx[1]:.3f},{nx[2]:.3f}) lv={props.get("level_vertex_id", lv)} gv={props.get("game_vertex_id", gv)} unique={d.count(old)}')
        res.append(dict(path=path, point=idx, old=old.decode('latin-1'), new=new.decode('latin-1')))
json.dump(res, open(sys.argv[3], 'w'))
