import fitz

d = fitz.open('backend/src/Accounting.Infrastructure/Pdf/Templates/pnd51_main.pdf')
p = d[1]  # page 2
pageH = p.rect.height

# group button widgets by field name; record rect + on-state(s)
groups = {}
for w in (p.widgets() or []):
    ft = w.field_type_string
    if ft not in ('CheckBox', 'RadioButton'):
        continue
    states = w.button_states() if hasattr(w, 'button_states') else None
    groups.setdefault(w.field_name, []).append((w.rect, states, w.field_value))

# nearest label text left/above each widget for human mapping
spans = []
for b in p.get_text('dict')['blocks']:
    for l in b.get('lines', []):
        for s in l.get('spans', []):
            t = s['text'].strip()
            if t and not set(t) <= {'.', ' '}:
                x0, y0, x1, y1 = s['bbox']
                spans.append((x0, y0, x1, y1, t))

def nearest_label(r):
    cy = (r.y0 + r.y1) / 2
    cand = [s for s in spans if abs((s[1]+s[3])/2 - cy) < 6 and s[2] < r.x0 + 2]
    cand.sort(key=lambda s: r.x0 - s[2])
    return ' '.join(c[4] for c in cand[:3][::-1])

out = []
for name, widgets in groups.items():
    # sort top->bottom, left->right (mirrors RdAcroFormFiller widget order)
    ws = sorted(widgets, key=lambda t: (pageH - t[0].y1, t[0].x0))
    out.append(f"\n=== {name}  ({len(ws)} widget(s)) ===")
    for i, (r, states, val) in enumerate(ws):
        lbl = nearest_label(r)
        out.append(f"  idx{i}  rect=[{r.x0:.0f},{r.y0:.0f},{r.x1:.0f},{r.y1:.0f}]  on={states}  <= {lbl}")

open('pnd51_p2radios.txt', 'w', encoding='utf-8').write('\n'.join(out))
print('wrote', sum(len(v) for v in groups.values()), 'button widgets,', len(groups), 'groups -> pnd51_p2radios.txt')
