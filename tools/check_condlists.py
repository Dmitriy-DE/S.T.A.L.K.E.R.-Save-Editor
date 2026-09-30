#!/usr/bin/env python3
"""Find malformed X-Ray condlists in .ltx logic (nested or unbalanced {} / %). usage: check_condlists.py FILE..."""
import sys
for path in sys.argv[1:]:
    for n, line in enumerate(open(path, 'rb').read().decode('cp1251', 'replace').splitlines(), 1):
        body = line.split(';', 1)[0]
        if '=' not in body or body.lstrip().startswith('['): continue
        value = body.split('=', 1)[1]
        if '{' not in value and '%' not in value and '}' not in value: continue
        depth, pct, bad = 0, 0, None
        for ch in value:
            if ch == '{':
                if depth or pct % 2: bad = 'nested {'; break
                depth += 1
            elif ch == '}':
                if not depth: bad = 'stray }'; break
                depth -= 1
            elif ch == '%':
                if depth: bad = '% inside {}'; break
                pct += 1
        if not bad and depth: bad = 'unclosed {'
        if not bad and pct % 2: bad = 'unbalanced %'
        if bad: print(f'{path}:{n}: {bad}: {line.strip()[:160]}')
