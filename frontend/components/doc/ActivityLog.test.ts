import { describe, it, expect } from 'vitest';
import { activityHeadline } from './ActivityLog';

// S12 (R6-parity) — "ส่งแล้ว → ส่งแล้ว" was showing whenever action and toStatus
// localize to the same label (e.g. quotation send: action=Sent, toStatus=Sent).
describe('activityHeadline', () => {
  it('collapses to a single label when action and toStatus localize the same', () => {
    expect(activityHeadline('ส่งแล้ว', 'ส่งแล้ว')).toBe('ส่งแล้ว');
  });

  it('keeps the arrow when action and toStatus differ', () => {
    expect(activityHeadline('สร้างเอกสาร', 'ฉบับร่าง')).toBe('สร้างเอกสาร → ฉบับร่าง');
  });

  it('falls back to the action label when there is no toStatus', () => {
    expect(activityHeadline('สร้างเอกสาร', null)).toBe('สร้างเอกสาร');
  });
});
