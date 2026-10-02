#!/usr/bin/env python3
"""check_trade_items.py CONFIG_DIR: item names in trade lists (misc/trade*.ltx) that are not sections of any config."""
import sys, os, re
root = sys.argv[1]; secs = set(); trade = []
for dp, _, files in os.walk(root):
    for f in files:
        if not f.endswith('.ltx'): continue
        p = os.path.join(dp, f)
        t = open(p, 'rb').read().decode('cp1251', 'replace')
        secs.update(s.lower() for s in re.findall(r'^\s*\[([^\]]+)\]', t, re.M))
        if 'trade' in f.lower() and '/misc' in dp.replace('\\', '/'): trade.append((p, t))
for p, t in sorted(trade):
    sec = None
    for n, line in enumerate(t.splitlines(), 1):
        m = re.match(r'\s*\[([^\]]+)\]', line)
        if m: sec = m.group(1); continue
        body = line.split(';', 1)[0].strip()
        if not body or sec is None or sec.lower() in ('trader',) or body.startswith('#'): continue
        key = body.split('=', 1)[0].strip().lower()
        if re.match(r'^[a-z0-9_.\-]+$', key) and key not in secs and not key.startswith(('buy_', 'sell_', 'discounts')):
            print(f'{os.path.relpath(p, root)}:{n}: [{sec}] {key}')
