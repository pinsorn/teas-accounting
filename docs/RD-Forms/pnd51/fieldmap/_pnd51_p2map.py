import fitz, json
geo = json.load(open('pnd51_comb_geo.json', encoding='utf-8'))
d = fitz.open('docs/RD-Forms/pnd51/pnd51_020768.pdf')
p2 = d[1]
# all text spans on page 2
spans = []
for b in p2.get_text('dict')['blocks']:
    for l in b.get('lines', []):
        for s in l.get('spans', []):
            t = s['text'].strip()
            if t:
                x0, y0, x1, y1 = s['bbox']
                spans.append((x0, y0, x1, y1, t))

# page-2 fields, sorted top->bottom
fields = [v for v in geo.values() if v['page'] == 1]
fields.sort(key=lambda v: (v['y'], min(v['centers'])))
def clean(toks):
    # drop pure dotted-leader / whitespace tokens
    return [t for t in toks if t.strip().strip('.') != '' and not set(t) <= {'.', ' ', '\t'}]

out = []
for v in fields:
    fy = v['y']; fx0 = min(v['centers'])
    # ALL label text on the same row, left of the field's first cell, sorted left->right
    cand = [s for s in spans if abs((s[1] + s[3]) / 2 - fy) < 7 and s[2] < fx0 - 3]
    cand.sort(key=lambda s: s[0])
    label = ' '.join(clean([s[4] for s in cand]))
    out.append(f"{v['name']:10s} cells={v['cells']:2d} y={fy:6.1f} satangbox={'yes' if v['cells']<=2 else 'no'}  <= {label}")
open('pnd51_p2map.txt', 'w', encoding='utf-8').write('\n'.join(out))
print('wrote', len(out), 'page-2 field rows -> pnd51_p2map.txt')
