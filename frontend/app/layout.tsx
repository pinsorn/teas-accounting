import type { Metadata, Viewport } from 'next';
import { Inter, Sarabun, JetBrains_Mono, Noto_Sans_Thai } from 'next/font/google';
import { NextIntlClientProvider } from 'next-intl';
import { getLocale, getMessages } from 'next-intl/server';
import { ThemeProvider } from '@/components/providers/theme-provider';
import { QueryProvider } from '@/components/providers/query-provider';
import { ConfirmProvider } from '@/hooks/useConfirm';
import { Toaster } from 'sonner';
import { BRAND_PRIMARY, BRAND_DARK } from '@/lib/brand';
import './globals.css';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-inter',
  display: 'swap',
});

const sarabun = Sarabun({
  subsets: ['thai', 'latin'],
  weight: ['300', '400', '500', '600', '700'],
  style: ['normal', 'italic'],
  variable: '--font-sarabun',
  display: 'swap',
});

const notoSansThai = Noto_Sans_Thai({
  subsets: ['thai', 'latin'],
  weight: ['400', '500', '600', '700', '800'],
  variable: '--font-noto-thai',
  display: 'swap',
});

const jetbrains = JetBrains_Mono({
  subsets: ['latin'],
  variable: '--font-jetbrains',
  display: 'swap',
});

export const metadata: Metadata = {
  title: {
    default: 'TEAS — ระบบบัญชี Enterprise',
    template: '%s | TEAS',
  },
  description: 'Thailand Enterprise Accounting System — VAT-compliant, e-Tax ready',
  applicationName: 'TEAS',
  authors: [{ name: 'TEAS Team' }],
  keywords: ['accounting', 'thailand', 'vat', 'e-tax', 'ภาษี', 'ใบกำกับภาษี'],
};

export const viewport: Viewport = {
  themeColor: [
    { media: '(prefers-color-scheme: light)', color: BRAND_PRIMARY },
    { media: '(prefers-color-scheme: dark)',  color: BRAND_DARK },
  ],
  width: 'device-width',
  initialScale: 1,
};

export default async function RootLayout({ children }: { children: React.ReactNode }) {
  const locale = await getLocale();
  const messages = await getMessages();

  return (
    <html
      lang={locale}
      suppressHydrationWarning
      className={`${inter.variable} ${sarabun.variable} ${jetbrains.variable} ${notoSansThai.variable}`}
      data-theme="teas-orange"
    >
      <body className="font-ui antialiased">
        <NextIntlClientProvider locale={locale} messages={messages}>
          <ThemeProvider
            attribute="data-theme"
            defaultTheme="teas-orange"
            enableSystem={false}
            themes={['teas-orange', 'teas', 'teas-dark']}
          >
            <QueryProvider>
              <ConfirmProvider>
                {children}
              </ConfirmProvider>
              <Toaster
                richColors
                position="top-right"
                toastOptions={{
                  className: 'font-sans',
                  style: { fontFamily: 'inherit' },
                }}
              />
            </QueryProvider>
          </ThemeProvider>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
