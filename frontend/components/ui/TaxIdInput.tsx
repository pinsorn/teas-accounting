'use client';

import { useState } from 'react';
import { formatTaxId } from '@/lib/utils';

// component-patterns.md §3 — Thai 13-digit, client-side validity, formatted display.
// Mod-11 checksum mirrors the backend domain rule (ThaiTaxId) so the UI rejects early.
export function isValidThaiTaxId(id: string): boolean {
  if (!/^\d{13}$/.test(id)) return false;
  let sum = 0;
  for (let i = 0; i < 12; i++) sum += Number(id[i]) * (13 - i);
  const check = (11 - (sum % 11)) % 10;
  return check === Number(id[12]);
}

export function TaxIdInput({
  value,
  onChange,
  label,
}: {
  value: string;
  onChange: (v: string) => void;
  label?: string;
}) {
  const [touched, setTouched] = useState(false);
  const digits = value.replace(/\D/g, '').slice(0, 13);
  const valid = digits.length === 0 || isValidThaiTaxId(digits);

  return (
    <label className="form-control">
      {label && <span className="label-text">{label}</span>}
      <input
        inputMode="numeric"
        className={`input input-bordered ${touched && !valid ? 'input-error' : ''}`}
        value={digits}
        maxLength={13}
        onChange={(e) => onChange(e.target.value.replace(/\D/g, '').slice(0, 13))}
        onBlur={() => setTouched(true)}
        placeholder="เลขประจำตัวผู้เสียภาษี 13 หลัก"
      />
      {digits.length === 13 && valid && (
        <span className="label-text-alt font-mono">{formatTaxId(digits)}</span>
      )}
      {touched && !valid && (
        <span className="label-text-alt text-error">เลขประจำตัวผู้เสียภาษีไม่ถูกต้อง</span>
      )}
    </label>
  );
}
