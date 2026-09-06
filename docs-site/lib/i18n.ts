import { defineI18n, type I18nConfig } from 'fumadocs-core/i18n';

export const i18nConfig = {
  defaultLanguage: 'zh',
  languages: ['zh', 'en'],
  parser: 'dir',
} satisfies I18nConfig<'zh' | 'en'>;

export const i18n = defineI18n(i18nConfig);

export const languages = {
  zh: {
    name: '简体中文',
    locale: 'zh',
  },
  en: {
    name: 'English',
    locale: 'en',
  },
} as const;

export type Locale = keyof typeof languages;

export function isLocale(value: string): value is Locale {
  return value in languages;
}
