#!/usr/bin/env python3
"""split_partial.py FILE CLASS_LINE 'Suffix:start:next' ...
Moves whole members of a partial class into FILE.Suffix.cs. `start` is the line of the first member to move, `next`
the line of the first member that stays (both 1-based, as an outline prints them); doc comments and attributes above
a member travel with it. CLASS_LINE is the line of the class declaration (0 = the range holds top-level types).
The original keeps everything else; nothing is rewritten, only moved."""
import sys, re
path, class_line = sys.argv[1], int(sys.argv[2])
L = open(path, encoding='utf-8').read().split('\n')
def up(n):  # n is 1-based member line; return 0-based index of the first line that belongs to it
    i = n - 1
    while i > 0 and re.match(r'\s*(///|//|\[)', L[i - 1]): i -= 1
    return i
header_end = next(i for i, l in enumerate(L) if l.startswith('namespace ')) + 1
header = L[:header_end]
moves = []
for spec in sys.argv[3:]:
    suffix, start, nxt = spec.split(':')
    a, b = up(int(start)), up(int(nxt))
    while b > a and L[b - 1].strip() == '': b -= 1
    moves.append((suffix, a, b))
for suffix, a, b in sorted(moves, key=lambda m: -m[1]):
    body = L[a:b]
    if class_line:
        decl = L[class_line - 1]
        out = header + [''] + [decl, '{'] + body + ['}', '']
    else:
        out = header + [''] + body + ['']
    new = re.sub(r'\.cs$', '.' + suffix + '.cs', path) if class_line else re.sub(r'[^/]+\.cs$', suffix + '.cs', path)
    open(new, 'w', encoding='utf-8').write('\n'.join(out))
    del L[a:b]
    # collapse a doubled blank line left behind
    while a < len(L) and a > 0 and L[a].strip() == '' and L[a - 1].strip() == '': del L[a]
    print(new, len(body), 'lines')
open(path, 'w', encoding='utf-8').write('\n'.join(L))
