// Sprint 13d P5 — resolve backend validation i18n keys → localized text.
// FluentValidation rules emit keys (e.g. "validation.required") instead of
// hardcoded English; the frontend owns the wording. Keep keys in sync with
// the backend validators (see Report-Backend21 for the migration status —
// validators are being converted incrementally).

type Locale = 'th' | 'en';

const DICT: Record<Locale, Record<string, string>> = {
  th: {
    'validation.required': 'กรุณากรอกข้อมูลในช่องนี้',
    'validation.maxLength': 'ข้อมูลยาวเกินกำหนด',
    'validation.code.format': 'รหัสต้องเป็นตัวอักษรพิมพ์ใหญ่หรือตัวเลข ไม่เกิน 20 ตัว',
    'validation.email': 'รูปแบบอีเมลไม่ถูกต้อง',
    'validation.unknown': 'ข้อมูลไม่ถูกต้อง',
  },
  en: {
    'validation.required': 'This field is required',
    'validation.maxLength': 'Value is too long',
    'validation.code.format': 'Code must be uppercase letters/digits, ≤20 chars',
    'validation.email': 'Invalid email format',
    'validation.unknown': 'Invalid value',
  },
};

export function currentLocale(): Locale {
  if (typeof document === 'undefined') return 'th';
  const m = document.cookie.match(/(?:^|; )locale=([^;]+)/);
  return m?.[1] === 'en' ? 'en' : 'th';
}

const UNKNOWN: Record<Locale, string> = {
  th: 'ข้อมูลไม่ถูกต้อง',
  en: 'Invalid value',
};

/** Resolve one key (pass through any non-key literal unchanged). */
export function resolveValidationKey(key: string, locale = currentLocale()): string {
  if (!key) return UNKNOWN[locale];
  const hit = DICT[locale][key];
  if (hit) return hit;
  // legacy literal message (validator not yet migrated) — show as-is;
  // an unrecognized validation.* key → generic localized fallback.
  return key.startsWith('validation.') ? UNKNOWN[locale] : key;
}
