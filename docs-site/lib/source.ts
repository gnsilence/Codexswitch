import { docs } from 'collections/server';
import { loader } from 'fumadocs-core/source';
import { createElement } from 'react';
import { icons } from 'lucide-react';
import { i18nConfig } from '@/lib/i18n';

export const source = loader({
  i18n: i18nConfig,
  baseUrl: '/docs',
  source: docs.toFumadocsSource(),
  url(slugs, locale) {
    return `/${locale ?? i18nConfig.defaultLanguage}/docs/${slugs.join('/')}`;
  },
  icon(icon) {
    if (!icon) return;
    if (icon in icons) {
      return createElement(icons[icon as keyof typeof icons]);
    }
  },
});
