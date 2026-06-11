import fitz, json
COMB = 1 << 24
src = 'docs/RD-Forms/pnd51/pnd51_020768.pdf'

def vbounds(page, rect):
    y0, y1 = rect.y0, rect.y1
    x0, x1 = rect.x0 - 2, rect.x1 + 2
    ymid = (y0 + y1) / 2
    xs = []
    for dr in page.get_drawings():
        for it in dr['items']:
            if it[0] == 'l':
                a, b = it[1], it[2]
                if abs(a.x - b.x) < 0.8 and min(a.y, b.y) <= ymid <= max(a.y, b.y) and x0 <= a.x <= x1:
                    xs.append(a.x)
            elif it[0] == 're':
                r = it[1]
                if r.y0 - 1 <= ymid <= r.y1 + 1:
                    if x0 - 1 <= r.x0 <= x1 + 1: xs.append(r.x0)
                    if x0 - 1 <= r.x1 <= x1 + 1: xs.append(r.x1)
    xs.sort()
    merged = []
    for x in xs:
        if not merged or x - merged[-1] > 1.5:
            merged.append(x)
    return merged

def centers(b):
    segs = [(b[i], b[i + 1]) for i in range(len(b) - 1)]
    segs = [(a, c) for a, c in segs if c - a > 2]
    if not segs:
        return []
    mx = max(c - a for a, c in segs)
    return [round((a + c) / 2, 1) for a, c in segs if (c - a) >= 0.62 * mx]

d = fitz.open(src)
geo = {}
summary = []
for pi, page in enumerate(d):
    for w in (page.widgets() or []):
        if w.field_type == fitz.PDF_WIDGET_TYPE_TEXT:
            ml = w.text_maxlen or 0
            b = vbounds(page, w.rect)
            if not b:
                continue
            # box field = has BOTH a left and a right vertical border (underline fields have neither)
            has_l = any(abs(x - w.rect.x0) < 3 for x in b)
            has_r = any(abs(x - w.rect.x1) < 3 for x in b)
            if not (has_l and has_r):
                continue
            c = centers(b)
            if not c:
                continue
            geo[f'{pi}:{w.field_name}'] = {
                'page': pi, 'name': w.field_name, 'maxlen': ml, 'cells': len(c),
                'centers': c, 'y': round((w.rect.y0 + w.rect.y1) / 2, 1),
                'h': round(w.rect.y1 - w.rect.y0, 1)}
            summary.append((pi, w.field_name, ml, len(c)))

json.dump(geo, open('pnd51_comb_geo.json', 'w'), ensure_ascii=False)
print('comb fields:', len(summary))
print('maxlen!=cells:', sum(1 for _, _, m, c in summary if m != c))
# show the page-1 taxid to confirm 13 cells
for k, v in geo.items():
    if v['name'] == 'Text1.1':
        print('Text1.1 cells=', v['cells'], 'centers=', v['centers'])
for s in summary[:6]:
    print(s)
