import fitz, json
geo = json.load(open('pnd51_comb_geo.json', encoding='utf-8'))
d = fitz.open('docs/RD-Forms/pnd51/pnd51_020768.pdf')
for v in geo.values():
    pg = d[v['page']]
    h = v['h']
    fs = min(max(h - 3.0, 7.0), 10.0)
    base = v['y'] + fs * 0.33   # baseline below the vertical centre
    for cx in v['centers']:
        pg.insert_text(fitz.Point(cx - fs * 0.27, base), '0', fontsize=fs, color=(0, 0, 0))
d.save('pnd51_zerofill2.pdf')
o = fitz.open('pnd51_zerofill2.pdf')
for i, pg in enumerate(o):
    pg.get_pixmap(matrix=fitz.Matrix(2.3, 2.3)).save(f'zf_p{i+1}.png')
print('rendered', o.page_count, 'pages; fields filled:', len(geo))
