import Link from 'next/link';
import {
  Activity,
  ArrowRight,
  Bot,
  Boxes,
  ChartNoAxesCombined,
  CircleCheck,
  Code2,
  FileCog,
  GitBranch,
  KeyRound,
  Network,
  ShieldCheck,
  Terminal,
  Wrench,
} from 'lucide-react';
import { isLocale, type Locale } from '@/lib/i18n';

const copy = {
  zh: {
    eyebrow: '本地 AI Provider 工作台',
    title: 'CodexSwitch',
    lead:
      '把 Codex 接到一个本地、可切换、可观察的 Responses 代理，统一 provider、协议转换、OAuth、Claude Code 和用量统计。',
    primary: '快速开始',
    secondary: '查看 Provider',
    terminalTitle: '本地代理端点',
    quickActions: [
      {
        title: 'Quick Start',
        desc: '启动应用，激活 provider，让 Codex 指向本地端点。',
        href: '/zh/docs/start/quick-start',
        icon: Terminal,
      },
      {
        title: 'Provider Setup',
        desc: '接入 Responses、Chat 或 Anthropic Messages 上游。',
        href: '/zh/docs/guides/setup-openai-responses',
        icon: Boxes,
      },
      {
        title: 'Troubleshooting',
        desc: '按代理、模型、认证和 release 问题逐层排查。',
        href: '/zh/docs/operations/troubleshooting',
        icon: Wrench,
      },
    ],
    whatTitle: 'What is CodexSwitch?',
    what:
      'CodexSwitch 是一个跨平台 Avalonia 桌面应用和本地 Kestrel 代理。Codex 把它当成 OpenAI Responses 端点，CodexSwitch 再根据活动 provider、模型路由和协议适配器把请求发送到不同上游。',
    flowTitle: 'How it works',
    flow: ['Codex client', 'Local /v1 proxy', 'Provider routing', 'Protocol adapter', 'Usage log'],
    capabilitiesTitle: 'Key capabilities',
    capabilities: [
      ['Provider switching', '在桌面 UI 中切换活动 provider。', Boxes],
      ['Protocol adapters', 'Responses、Chat Completions、Anthropic Messages 互转。', Network],
      ['Codex OAuth', '登录 ChatGPT Codex 后端并支持多账号元数据。', KeyRound],
      ['Claude Code', '写入可恢复的 Claude Code 本地设置。', Bot],
      ['Usage dashboard', '记录 JSONL 用量并展示 provider、模型和趋势。', ChartNoAxesCombined],
      ['Managed config', '写入 `.codex` / `.claude` 并保留 `.bak`。', FileCog],
    ],
    startTitle: 'Start here',
    startLinks: [
      ['安装', '/zh/docs/start/installation'],
      ['快速开始', '/zh/docs/start/quick-start'],
      ['源码运行', '/zh/docs/start/run-from-source'],
      ['文档目录', '/zh/docs/start/docs-directory'],
    ],
    learnTitle: 'Learn more',
    learnLinks: [
      ['模型与路由', '/zh/docs/core-concepts/models-and-routes'],
      ['协议', '/zh/docs/core-concepts/protocols'],
      ['安全模型', '/zh/docs/operations/security-model'],
      ['Contributing', '/zh/docs/contributing'],
    ],
    footerCta: '进入完整文档',
  },
  en: {
    eyebrow: 'Local AI provider workstation',
    title: 'CodexSwitch',
    lead:
      'Connect Codex to a local, switchable, observable Responses proxy for providers, protocol conversion, OAuth, Claude Code, and usage accounting.',
    primary: 'Quick Start',
    secondary: 'View Providers',
    terminalTitle: 'Local proxy endpoint',
    quickActions: [
      {
        title: 'Quick Start',
        desc: 'Launch the app, activate a provider, and point Codex at the local endpoint.',
        href: '/en/docs/start/quick-start',
        icon: Terminal,
      },
      {
        title: 'Provider Setup',
        desc: 'Connect Responses, Chat-compatible, or Anthropic Messages upstreams.',
        href: '/en/docs/guides/setup-openai-responses',
        icon: Boxes,
      },
      {
        title: 'Troubleshooting',
        desc: 'Debug proxy, model, auth, routing, and release issues by layer.',
        href: '/en/docs/operations/troubleshooting',
        icon: Wrench,
      },
    ],
    whatTitle: 'What is CodexSwitch?',
    what:
      'CodexSwitch is a cross-platform Avalonia desktop app plus a local Kestrel proxy. Codex treats it like an OpenAI Responses endpoint; CodexSwitch routes requests through active providers, model rules, and protocol adapters.',
    flowTitle: 'How it works',
    flow: ['Codex client', 'Local /v1 proxy', 'Provider routing', 'Protocol adapter', 'Usage log'],
    capabilitiesTitle: 'Key capabilities',
    capabilities: [
      ['Provider switching', 'Switch the active upstream provider from the desktop UI.', Boxes],
      ['Protocol adapters', 'Bridge Responses, Chat Completions, and Anthropic Messages.', Network],
      ['Codex OAuth', 'Sign in to the ChatGPT Codex backend with account metadata.', KeyRound],
      ['Claude Code', 'Write reversible local Claude Code settings.', Bot],
      ['Usage dashboard', 'Record JSONL usage and show provider, model, and trend views.', ChartNoAxesCombined],
      ['Managed config', 'Write `.codex` / `.claude` files with `.bak` restores.', FileCog],
    ],
    startTitle: 'Start here',
    startLinks: [
      ['Installation', '/en/docs/start/installation'],
      ['Quick Start', '/en/docs/start/quick-start'],
      ['Run From Source', '/en/docs/start/run-from-source'],
      ['Docs Directory', '/en/docs/start/docs-directory'],
    ],
    learnTitle: 'Learn more',
    learnLinks: [
      ['Models And Routes', '/en/docs/core-concepts/models-and-routes'],
      ['Protocols', '/en/docs/core-concepts/protocols'],
      ['Security Model', '/en/docs/operations/security-model'],
      ['Contributing', '/en/docs/contributing'],
    ],
    footerCta: 'Open full docs',
  },
} satisfies Record<Locale, unknown>;

export default async function HomePage({
  params,
}: {
  params: Promise<{ lang: string }>;
}) {
  const { lang } = await params;
  const locale = isLocale(lang) ? lang : 'zh';
  const t = copy[locale];
  const docsUrl = `/${locale}/docs`;
  const providersUrl = `/${locale}/docs/core-concepts/providers`;

  return (
    <main className="min-h-screen bg-background text-foreground">
      <section className="cs-shell-grid relative overflow-hidden border-b">
        <div className="cs-focus-line absolute inset-x-0 top-0 h-1" />
        <div className="mx-auto grid min-h-[calc(100svh-64px)] max-w-7xl items-center gap-12 px-5 py-14 md:grid-cols-[1.05fr_0.95fr] md:px-8 lg:px-10">
          <div className="max-w-3xl">
            <p className="mb-4 text-sm font-medium uppercase tracking-normal text-muted-foreground">
              {t.eyebrow}
            </p>
            <h1 className="text-5xl font-semibold tracking-normal md:text-7xl">
              {t.title}
            </h1>
            <p className="mt-6 max-w-2xl text-lg leading-8 text-muted-foreground md:text-xl">
              {t.lead}
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <Link
                href={`/${locale}/docs/start/quick-start`}
                className="inline-flex h-11 items-center gap-2 rounded-md bg-primary px-5 text-sm font-medium text-primary-foreground"
              >
                {t.primary}
                <ArrowRight className="size-4" />
              </Link>
              <Link
                href={providersUrl}
                className="inline-flex h-11 items-center gap-2 rounded-md border px-5 text-sm font-medium"
              >
                {t.secondary}
                <Boxes className="size-4" />
              </Link>
            </div>
          </div>

          <div className="rounded-lg border bg-background/92 p-5 shadow-sm backdrop-blur">
            <div className="flex items-center justify-between border-b pb-3">
              <div>
                <p className="text-sm font-medium">{t.terminalTitle}</p>
                <p className="text-xs text-muted-foreground">Responses-compatible</p>
              </div>
              <span className="inline-flex items-center gap-1 rounded-md border px-2 py-1 text-xs text-[var(--cs-status)]">
                <CircleCheck className="size-3" />
                127.0.0.1
              </span>
            </div>
            <pre className="mt-4 overflow-x-auto rounded-md bg-muted p-4 text-sm"><code>{`GET  /health
GET  /v1/models
POST /v1/responses
POST /v1/messages

base_url = "http://127.0.0.1:12785/v1"`}</code></pre>
            <div className="mt-5 grid gap-3">
              {t.quickActions.map((item) => {
                const Icon = item.icon;
                return (
                  <Link
                    key={item.href}
                    href={item.href}
                    className="cs-hover-link flex items-start gap-3 rounded-md border bg-background p-3 hover:border-primary/40 hover:bg-muted/40"
                  >
                    <Icon className="mt-0.5 size-4 text-primary" />
                    <span>
                      <span className="block text-sm font-medium">{item.title}</span>
                      <span className="mt-1 block text-sm text-muted-foreground">
                        {item.desc}
                      </span>
                    </span>
                  </Link>
                );
              })}
            </div>
          </div>
        </div>
      </section>

      <section className="border-b">
        <div className="mx-auto grid max-w-7xl gap-10 px-5 py-14 md:grid-cols-[0.85fr_1.15fr] md:px-8 lg:px-10">
          <div>
            <h2 className="text-3xl font-semibold tracking-normal">{t.whatTitle}</h2>
            <p className="mt-4 leading-7 text-muted-foreground">{t.what}</p>
          </div>
          <div>
            <h2 className="text-3xl font-semibold tracking-normal">{t.flowTitle}</h2>
            <div className="mt-6 grid gap-3">
              {t.flow.map((label, index) => (
                <div key={label} className="flex items-center gap-3">
                  <div className="flex size-9 items-center justify-center rounded-md border bg-muted text-sm font-medium">
                    {index + 1}
                  </div>
                  <div className="h-px flex-1 bg-border" />
                  <div className="min-w-44 rounded-md border bg-background px-3 py-2 text-sm">
                    {label}
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      </section>

      <section className="border-b">
        <div className="mx-auto max-w-7xl px-5 py-14 md:px-8 lg:px-10">
          <h2 className="text-3xl font-semibold tracking-normal">{t.capabilitiesTitle}</h2>
          <div className="mt-8 grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {t.capabilities.map(([title, desc, Icon]) => (
              <div key={title as string} className="rounded-md border bg-background p-4">
                <Icon className="size-5 text-primary" />
                <h3 className="mt-4 font-medium">{title as string}</h3>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">
                  {desc as string}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      <section>
        <div className="mx-auto grid max-w-7xl gap-8 px-5 py-14 md:grid-cols-2 md:px-8 lg:px-10">
          <LinkGroup title={t.startTitle} links={t.startLinks} />
          <LinkGroup title={t.learnTitle} links={t.learnLinks} />
          <div className="md:col-span-2">
            <Link
              href={docsUrl}
              className="cs-hover-link inline-flex items-center gap-2 rounded-md border px-4 py-3 text-sm font-medium hover:border-primary/40 hover:bg-muted/40"
            >
              <Activity className="size-4 text-primary" />
              {t.footerCta}
              <ArrowRight className="size-4" />
            </Link>
          </div>
        </div>
      </section>
    </main>
  );
}

function LinkGroup({
  title,
  links,
}: {
  title: string;
  links: string[][];
}) {
  return (
    <div>
      <h2 className="text-2xl font-semibold tracking-normal">{title}</h2>
      <div className="mt-5 divide-y rounded-md border">
        {links.map(([label, href]) => (
          <Link
            key={href}
            href={href}
            className="cs-hover-link flex items-center justify-between px-4 py-3 text-sm hover:bg-muted/40"
          >
            <span>{label}</span>
            <ArrowRight className="size-4 text-muted-foreground" />
          </Link>
        ))}
      </div>
    </div>
  );
}
