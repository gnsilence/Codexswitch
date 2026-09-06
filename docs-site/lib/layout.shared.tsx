import type { BaseLayoutProps } from 'fumadocs-ui/layouts/shared';
import { i18n, i18nConfig } from '@/lib/i18n';
import { uiTranslations } from 'fumadocs-ui/i18n';

export const translations = i18n
  .translations()
  .extend(uiTranslations())
  .add('ui', {
    zh: {
      displayName: '简体中文',
      search: '搜索文档',
      searchNoResult: '没有找到结果',
      toc: '本页目录',
      lastUpdate: '最后更新',
      chooseLanguage: '选择语言',
      nextPage: '下一页',
      previousPage: '上一页',
    },
    en: {
      displayName: 'English',
    },
  });

export function baseOptions(locale: string): BaseLayoutProps {
  const isZh = locale === 'zh';

  return {
    i18n: i18nConfig,
    nav: {
      title: 'CodexSwitch Docs',
      url: `/${locale}`,
    },
    githubUrl: 'https://github.com/AIDotNet/CodexSwitch',
    links: [
      {
        text: isZh ? '快速开始' : 'Quick Start',
        url: `/${locale}/docs/start/quick-start`,
        active: 'nested-url',
      },
      {
        text: isZh ? '故障排查' : 'Troubleshooting',
        url: `/${locale}/docs/operations/troubleshooting`,
        active: 'nested-url',
      },
      {
        text: 'GitHub',
        url: 'https://github.com/AIDotNet/CodexSwitch',
        external: true,
      },
    ],
  };
}
