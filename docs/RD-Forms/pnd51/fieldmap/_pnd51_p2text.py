import fitz

d = fitz.open('docs/RD-Forms/pnd51/pnd51_020768.pdf')
p2 = d[1]

# Group spans into visual lines (by rounded y), drop pure dotted-leader noise.
rows = {}
for b in p2.get_text('dict')['blocks']:
    for l in b.get('lines', []):
        for s in l.get('spans', []):
            t = s['text']
            x0, y0, x1, y1 = s['bbox']
            yc = round((y0 + y1) / 2)
            rows.setdefault(yc, []).append((x0, t))

def clean(t):
    # strip dotted leaders / lone dots
    st = t.strip()
    if st == '' or set(st) <= {'.', ' ', '\t', '_'}:
        return ''
    return st

out = []
for yc in sorted(rows):
    parts = rows[yc]
    parts.sort(key=lambda p: p[0])
    line = ' '.join(c for c in (clean(t) for _, t in parts) if c)
    if line.strip():
        out.append(f"y={yc:4d}  {line}")

open('pnd51_p2text.txt', 'w', encoding='utf-8').write('\n'.join(out))
print('wrote', len(out), 'lines -> pnd51_p2text.txt')
