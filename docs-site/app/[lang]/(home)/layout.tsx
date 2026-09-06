import { HomeLayout } from 'fumadocs-ui/layouts/home';
import type { ReactNode } from 'react';
import { baseOptions } from '@/lib/layout.shared';
import { isLocale } from '@/lib/i18n';

export default async function Layout({
  params,
  children,
}: {
  params: Promise<{ lang: string }>;
  children: ReactNode;
}) {
  const { lang } = await params;
  const locale = isLocale(lang) ? lang : 'zh';

  return <HomeLayout {...baseOptions(locale)}>{children}</HomeLayout>;
}
