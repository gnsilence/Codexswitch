import { createI18nMiddleware } from 'fumadocs-core/i18n/middleware';
import { i18nConfig } from '@/lib/i18n';

export default createI18nMiddleware(i18nConfig);

export const config = {
  matcher: ['/((?!api|_next/static|_next/image|favicon.ico|llms.txt|llms-full.txt).*)'],
};
