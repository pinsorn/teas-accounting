import fitz

d = fitz.open('backend/src/Accounting.Infrastructure/Pdf/Templates/pnd51_main.pdf')
p = d[1]; H = p.rect.height

spans = []
for b in p.get_text('dict')['blocks']:
    for l in b.get('lines', []):
        for s in l.get('spans', []):
            t = s['text'].strip()
            if t and not set(t) <= {'.', ' ', '(', ')'}:
                x0, y0, x1, y1 = s['bbox']
                spans.append((x0, y0, x1, y1, t))

def right_label(r):
    cy = (r.y0 + r.y1) / 2
    cand = [s for s in spans if abs((s[1] + s[3]) / 2 - cy) < 5 and s[0] >= r.x1 - 1 and s[0] < r.x1 + 170]
    cand.sort(key=lambda s: s[0])
    # stop at the next checkbox-ish gap: take spans until a big x-jump (next option)
    out, lastx = [], None
    for s in cand:
        if lastx is not None and s[0] - lastx > 60:
            break
        out.append(s[4]); lastx = s[2]
    return ' '.join(out)[:60]

groups = {}
for w in (p.widgets() or []):
    if w.field_type_string in ('CheckBox', 'RadioButton'):
        groups.setdefault(w.field_name, []).append((w.rect, (w.button_states() or {})))

out = []
for name, ws in groups.items():
    ws = sorted(ws, key=lambda t: (H - t[0].y1, t[0].x0))
    out.append(f"\n=== {name} ({len(ws)}) ===")
    for i, (r, st) in enumerate(ws):
        on = st.get('normal', ['?'])[0] if isinstance(st, dict) else '?'
        out.append(f"  #{i} on='{on}' x={r.x0:.0f} y={r.y0:.0f}  => {right_label(r)}")
open('pnd51_p2radio_right.txt', 'w', encoding='utf-8').write('\n'.join(out))
print('wrote pnd51_p2radio_right.txt')
