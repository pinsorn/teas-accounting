// Sprint 13j-FE — Thai baht amount-in-words ("บาทถ้วน" / "สตางค์").
// Ported from design/claude-design/components.jsx bathText(). Used by
// PaperDocument totals. QuestPDF (13j-PDF) will mirror this output, so any
// change here must be reflected there.

const DIGITS = ['ศูนย์', 'หนึ่ง', 'สอง', 'สาม', 'สี่', 'ห้า', 'หก', 'เจ็ด', 'แปด', 'เก้า'];
const POS = ['', 'สิบ', 'ร้อย', 'พัน', 'หมื่น', 'แสน', 'ล้าน'];

function group(s: string): string {
  let out = '';
  const len = s.length;
  for (let i = 0; i < len; i++) {
    const d = Number(s.charAt(i));
    const p = len - i - 1;
    if (d === 0) continue;
    if (p === 1 && d === 1) out += 'สิบ';
    else if (p === 1 && d === 2) out += 'ยี่สิบ';
    else if (p === 0 && d === 1 && len > 1) out += 'เอ็ด';
    else out += (DIGITS[d] ?? '') + (POS[p] ?? '');
  }
  return out;
}

export function bathText(n: number): string {
  const baht = Math.floor(n);
  const satang = Math.round((n - baht) * 100);
  let result: string;
  if (baht === 0) result = 'ศูนย์';
  else {
    const s = String(baht);
    if (s.length > 6) {
      const mil = s.slice(0, -6);
      const rest = s.slice(-6);
      result = group(mil) + 'ล้าน' + (rest.replace(/^0+/, '') ? group(rest) : '');
    } else result = group(s);
  }
  result += 'บาท';
  if (satang === 0) result += 'ถ้วน';
  else result += group(String(satang).padStart(2, '0')) + 'สตางค์';
  return result;
}
