import { describe, it, expect } from 'vitest';
import { bathText } from './bath-text';

// Sprint 13j-FE C1 — required coverage: 0, 1, 21, 100, 1000, 1234.56,
// 1000000, 10000000 (Answer-Sana-Backend29 §4 acceptance).
describe('bathText', () => {
  it('zero', () => expect(bathText(0)).toBe('ศูนย์บาทถ้วน'));
  it('one', () => expect(bathText(1)).toBe('หนึ่งบาทถ้วน'));
  it('twenty-one (เอ็ด/ยี่สิบ rules)', () => expect(bathText(21)).toBe('ยี่สิบเอ็ดบาทถ้วน'));
  it('one hundred', () => expect(bathText(100)).toBe('หนึ่งร้อยบาทถ้วน'));
  it('one thousand', () => expect(bathText(1000)).toBe('หนึ่งพันบาทถ้วน'));
  it('1234.56 (with สตางค์)', () =>
    expect(bathText(1234.56)).toBe('หนึ่งพันสองร้อยสามสิบสี่บาทห้าสิบหกสตางค์'));
  it('one million', () => expect(bathText(1000000)).toBe('หนึ่งล้านบาทถ้วน'));
  it('ten million', () => expect(bathText(10000000)).toBe('สิบล้านบาทถ้วน'));
});
