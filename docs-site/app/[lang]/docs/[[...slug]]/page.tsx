import { notFound } from 'next/navigation';
import {
  DocsBody,
  DocsDescription,
  DocsPage,
  DocsTitle,
} from 'fumadocs-ui/page';
import type { TOCItemType } from 'fumadocs-core/toc';
import type { MDXContent } from 'mdx/types';
import { source } from '@/lib/source';
import { getMDXComponents } from '@/components/mdx';
import { isLocale } from '@/lib/i18n';

type MdxPageData = {
  body: MDXContent;
  toc: TOCItemType[];
  full?: boolean;
};

export function generateStaticParams() {
  return source.generateParams();
}

export async function generateMetadata({
  params,
}: {
  params: Promise<{ lang: string; slug?: string[] }>;
}) {
  const { lang, slug } = await params;
  const locale = isLocale(lang) ? lang : 'zh';
  const page = source.getPage(slug ?? [], locale);

  if (!page) notFound();

  return {
    title: `${page.data.title} | CodexSwitch Docs`,
    description: page.data.description,
  };
}

export default async function Page({
  params,
}: {
  params: Promise<{ lang: string; slug?: string[] }>;
}) {
  const { lang, slug } = await params;
  const locale = isLocale(lang) ? lang : 'zh';
  const page = source.getPage(slug ?? [], locale);

  if (!page) notFound();

  const data = page.data as typeof page.data & MdxPageData;
  const MDX = data.body;

  return (
    <DocsPage toc={data.toc} full={data.full}>
      <DocsTitle>{data.title}</DocsTitle>
      <DocsDescription>{data.description}</DocsDescription>
      <DocsBody>
        <MDX components={getMDXComponents()} />
      </DocsBody>
    </DocsPage>
  );
}
