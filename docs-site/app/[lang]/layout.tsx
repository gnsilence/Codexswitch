import '@/app/global.css';
import { RootProvider } from 'fumadocs-ui/provider/next';
import { i18nProvider } from 'fumadocs-ui/i18n';
import type { ReactNode } from 'react';
import { isLocale } from '@/lib/i18n';
import { translations } from '@/lib/layout.shared';

export function generateStaticParams() {
  return [{ lang: 'zh' }, { lang: 'en' }];
}

export default async function LocaleLayout({
  params,
  children,
}: {
  params: Promise<{ lang: string }>;
  children: ReactNode;
}) {
  const { lang } = await params;
  const locale = isLocale(lang) ? lang : 'zh';

  return (
    <html lang={locale} suppressHydrationWarning>
      <body className="flex min-h-screen flex-col">
        <RootProvider i18n={i18nProvider(translations, locale)}>{children}</RootProvider>
      </body>
    </html>
  );
}
