import json
geo = json.load(open('pnd51_comb_geo.json', encoding='utf-8'))
out = {}
for v in geo.values():
    # key by field name (unique across pages on this form: Text1/2/3 = page1, Text4 = page2)
    out[v['name']] = v['centers']
json.dump(out, open('backend/src/Accounting.Infrastructure/Pdf/Templates/pnd51_cells.json', 'w', encoding='utf-8'),
          ensure_ascii=False, indent=0)
print('fields:', len(out))
print('Text1.1:', out.get('Text1.1'))
print('Text1.25:', out.get('Text1.25'), 'Text1.26:', out.get('Text1.26'))
