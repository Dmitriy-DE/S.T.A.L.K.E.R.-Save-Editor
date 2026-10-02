#!/usr/bin/env python3
"""ee_diff.py RETAIL_DIR EE_DIR [SUBDIR]: what GSC changed in the Enhanced Edition scripts/configs compared with retail.
Retail text is cp1251, EE is UTF-8; comments and whitespace-only changes are ignored. Prints changed lines per file."""
import sys, os, difflib, re
def load(path):
    raw = open(path, 'rb').read()
    for enc in ('utf-8', 'cp1251'):
        try: text = raw.decode(enc); break
        except UnicodeDecodeError: continue
    else: text = raw.decode('latin-1')
    out = []
    for line in text.replace('\r', '').split('\n'):
        code = re.sub(r'--.*$', '', line).strip() if path.endswith('.script') else line.strip()
        code = re.sub(r'\s+', ' ', code)
        if code: out.append(code)
    return out
retail, ee = sys.argv[1], sys.argv[2]; sub = sys.argv[3] if len(sys.argv) > 3 else 'scripts'
only = sys.argv[4:] or None
for root, _, files in os.walk(os.path.join(retail, sub)):
    for name in sorted(files):
        rel = os.path.relpath(os.path.join(root, name), retail)
        if only and name not in only: continue
        other = os.path.join(ee, rel)
        if not os.path.exists(other): continue
        a, b = load(os.path.join(retail, rel)), load(other)
        if a == b: continue
        diff = [l for l in difflib.unified_diff(a, b, lineterm='', n=0) if l[:1] in '+-' and l[:3] not in ('+++', '---')]
        print(f"== {rel} ({len(diff)})")
        if only or len(diff) <= int(os.environ.get('MAXLINES', '12')):
            for l in diff: print('   ' + l[:220])
