import fitz, io

# Dump page-2 radio/checkbox groups in the SAME order RdAcroFormFiller.BuildRadioCells uses for
# RdRadio.WidgetIndex: widgets sorted top->bottom, left->right. pymupdf rect is top-left origin
# (y0 = top), so ascending (y0, x0) == C#'s ascending (pageH - Y2, X1). For each widget, join the
# nearest text span to its RIGHT (the printed option label) so the index->option map is deterministic.
d = fitz.open(r'U:\backend\src\Accounting.Infrastructure\Pdf\Templates\pnd51_main.pdf')
p = d[1]
spans = []
for blk in p.get_text('dict')['blocks']:
    for ln in blk.get('lines', []):
        for sp in ln.get('spans', []):
            t = sp['text'].strip()
            if t:
                x0, y0, x1, y1 = sp['bbox']
                spans.append((x0, (y0 + y1) / 2, t))

def label_right(r):
    cy = (r.y0 + r.y1) / 2
    cands = [(x0, t) for (x0, ymid, t) in spans
             if abs(ymid - cy) <= 6 and x0 >= r.x1 - 2 and x0 <= r.x1 + 130]
    cands.sort()
    return ' '.join(t for _, t in cands[:4]) if cands else '(no text right)'

groups = {}
for w in (p.widgets() or []):
    if w.field_type_string in ('CheckBox', 'RadioButton'):
        groups.setdefault(w.field_name, []).append(fitz.Rect(w.rect))

out = io.StringIO()
for name in sorted(groups, key=lambda n: int(''.join(ch for ch in n if ch.isdigit()) or 0)):
    rs = sorted(groups[name], key=lambda r: (round(r.y0, 1), round(r.x0, 1)))
    out.write(f'\n{name}  (n={len(rs)})\n')
    for i, r in enumerate(rs):
        out.write(f'   #{i}  y0={r.y0:6.1f} x0={r.x0:6.1f}  ->  {label_right(r)}\n')
open('pnd51_p2radio_cs.txt', 'w', encoding='utf-8').write(out.getvalue())

# labelled raster with the CORRECT (y0,x0) order
shape = p.new_shape()
for name, rs in groups.items():
    for i, r in enumerate(sorted(rs, key=lambda r: (round(r.y0, 1), round(r.x0, 1)))):
        shape.draw_rect(r); shape.finish(color=(1, 0, 0), width=0.8)
        tag = ''.join(ch for ch in name if ch.isdigit())
        p.insert_text((r.x0, r.y0 - 1.2), f'{tag}#{i}', fontsize=4.2, color=(0, 0, 1))
shape.commit()
p.get_pixmap(matrix=fitz.Matrix(4, 4), clip=fitz.Rect(0, 20, p.rect.width, p.rect.height * 0.52)).save('_p2_radiomap_top.png')
p.get_pixmap(matrix=fitz.Matrix(4, 4), clip=fitz.Rect(0, p.rect.height * 0.46, p.rect.width, p.rect.height)).save('_p2_radiomap_bot.png')
print('wrote pnd51_p2radio_cs.txt + _p2_radiomap_top/bot.png')
