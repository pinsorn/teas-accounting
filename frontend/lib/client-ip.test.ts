import { describe, it, expect } from 'vitest';
import { extractClientIp } from './client-ip';

// M4/M5 follow-up (2026-07-04) — cf-connecting-ip (Cloudflare-authoritative, unforgeable) must
// win over the client-suppliable x-forwarded-for; the fallback uses only the FIRST XFF entry.
describe('extractClientIp', () => {
  it('prefers cf-connecting-ip over x-forwarded-for', () => {
    const headers = new Headers({
      'cf-connecting-ip': '203.0.113.9',
      'x-forwarded-for': '198.51.100.1, 10.0.0.1',
    });
    expect(extractClientIp(headers)).toBe('203.0.113.9');
  });

  it('falls back to the first x-forwarded-for entry when cf-connecting-ip is absent', () => {
    const headers = new Headers({ 'x-forwarded-for': '198.51.100.1, 10.0.0.1' });
    expect(extractClientIp(headers)).toBe('198.51.100.1');
  });

  it('trims whitespace around the first x-forwarded-for entry', () => {
    const headers = new Headers({ 'x-forwarded-for': ' 198.51.100.1 , 10.0.0.1' });
    expect(extractClientIp(headers)).toBe('198.51.100.1');
  });

  it('returns empty string when neither header is present', () => {
    expect(extractClientIp(new Headers())).toBe('');
  });
});
