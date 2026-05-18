import { cookies } from 'next/headers';
import { getRequestConfig } from 'next-intl/server';

// No locale routing (no /[locale] segment). Locale comes from a cookie; TH is primary.
// next-intl v3 + Next 15 — getRequestConfig must return `locale` explicitly when the
// i18n middleware isn't used (Context7: next-intl app-router без locale routing).
export const SUPPORTED_LOCALES = ['th', 'en'] as const;
export type AppLocale = (typeof SUPPORTED_LOCALES)[number];
export const DEFAULT_LOCALE: AppLocale = 'th';

export default getRequestConfig(async () => {
  const store = await cookies();
  const raw = store.get('locale')?.value;
  const locale: AppLocale =
    raw === 'en' || raw === 'th' ? raw : DEFAULT_LOCALE;

  return {
    locale,
    messages: (await import(`../messages/${locale}.json`)).default,
    timeZone: 'Asia/Bangkok',
  };
});
