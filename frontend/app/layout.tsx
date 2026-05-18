import type { Metadata, Viewport } from 'next';
import { Inter, Sarabun, JetBrains_Mono } from 'next/font/google';
import { NextIntlClientProvider } from 'next-intl';
import { getLocale, getMessages } from 'next-intl/server';
import { ThemeProvider } from '@/components/providers/theme-provider';
import { QueryProvider } from '@/components/providers/query-provider';
import { Toaster } from 'sonner';
import './globals.css';

const inter = Inter({
  subsets: ['latin'],
  variable: '--font-inter',
  display: 'swap',
});

const sarabun = Sarabun({
  subsets: ['thai', 'latin'],
  weight: ['300', '400', '500', '600', '700'],
  variable: '--font-sarabun',
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
    { media: '(prefers-color-scheme: light)', color: '#1565C0' },
    { media: '(prefers-color-scheme: dark)',  color: '#0B1220' },
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
      className={`${inter.variable} ${sarabun.variable} ${jetbrains.variable}`}
      data-theme="teas"
    >
      <body className="font-sans antialiased">
        <NextIntlClientProvider locale={locale} messages={messages}>
          <ThemeProvider
            attribute="data-theme"
            defaultTheme="teas"
            enableSystem={false}
            themes={['teas', 'teas-dark']}
          >
            <QueryProvider>
              {children}
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
