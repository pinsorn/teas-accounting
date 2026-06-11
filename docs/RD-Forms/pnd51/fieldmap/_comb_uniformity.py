import fitz, glob, os, statistics

COMB = 1 << 24
TPL = 'backend/src/Accounting.Infrastructure/Pdf/Templates'

def vdividers(page, rect):
    """X positions of vertical line segments whose y-span overlaps rect, x within rect."""
    xs = []
    y0, y1 = rect.y0, rect.y1
    for d in page.get_drawings():
        for it in d['items']:
            if it[0] == 'l':  # line: ('l', p1, p2)
                p1, p2 = it[1], it[2]
                if abs(p1.x - p2.x) < 0.6 and abs(p1.y - p2.y) > 2:  # vertical
                    yy0, yy1 = sorted((p1.y, p2.y))
                    if yy1 > y0 - 1 and yy0 < y1 + 1 and rect.x0 - 1 <= p1.x <= rect.x1 + 1:
                        xs.append(round(p1.x, 1))
            elif it[0] == 're':  # rect borders -> two verticals
                r = it[1]
                for vx in (r.x0, r.x1):
                    if r.y1 > y0 - 1 and r.y0 < y1 + 1 and rect.x0 - 1 <= vx <= rect.x1 + 1:
                        xs.append(round(vx, 1))
    # dedupe near-equal
    xs = sorted(xs)
    uniq = []
    for x in xs:
        if not uniq or x - uniq[-1] > 1.0:
            uniq.append(x)
    return uniq

out = []
for path in sorted(glob.glob(TPL + '/*.pdf')):
    name = os.path.basename(path)
    d = fitz.open(path)
    out.append(f"\n==== {name}  ({d.page_count} page(s)) ====")
    for pno in range(d.page_count):
        page = d[pno]
        for w in (page.widgets() or []):
            if w.field_type_string not in ('Text',):
                continue
            ml = w.text_maxlen or 0
            comb = bool((w.field_flags or 0) & COMB)
            # only interesting: comb fields, or any text field with maxlen>=10 (taxid-like)
            if not comb and ml < 10:
                continue
            div = vdividers(page, w.rect)
            gaps = [round(div[i+1]-div[i], 2) for i in range(len(div)-1)]
            verdict = ''
            if len(gaps) >= 3:
                lo, hi = min(gaps), max(gaps)
                ratio = hi / lo if lo > 0 else 0
                cv = statistics.pstdev(gaps) / statistics.mean(gaps) if gaps else 0
                verdict = f"ratio={ratio:.2f} cv={cv:.2f} -> {'NON-UNIFORM' if ratio > 1.25 else 'uniform'}"
            out.append(
                f"  p{pno} {w.field_name:12s} maxLen={ml:2d} comb={int(comb)} "
                f"box=[{w.rect.x0:.1f},{w.rect.x1:.1f}] w={w.rect.x1-w.rect.x0:.1f} "
                f"dividers={len(div)} gaps={gaps}  {verdict}")
    d.close()

open('comb_uniformity.txt', 'w', encoding='utf-8').write('\n'.join(out))
print('wrote comb_uniformity.txt;', len(out), 'lines')
