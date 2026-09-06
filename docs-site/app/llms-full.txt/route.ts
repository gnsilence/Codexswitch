export const dynamic = 'force-static';

const sections = [
  ['Start', 'Overview, installation, quick start, source builds, and a curated docs directory.'],
  ['Core Concepts', 'Local proxy, providers, model routing, protocols, managed client config, usage logs, and pricing.'],
  ['Guides', 'OpenAI Responses, OpenAI Chat, Anthropic Messages, Codex OAuth, Claude Code, auth preservation, and network failover workflows.'],
  ['Reference', 'Supported endpoints, built-in providers, config files, pricing catalog, developer commands, and changelog source.'],
  ['Operations', 'Release guide, CI flow, macOS signing, update checks, troubleshooting, and security model.'],
  ['UI System', 'CodexSwitchUI component gallery concepts: tokens, forms, navigation, overlay, feedback, and data display.'],
  ['Contributing', 'Repository architecture, testing strategy, localization, provider/protocol changes, and license status.'],
];

export function GET() {
  const pages = sections
    .map(([title, summary]) => `## ${title}\n${summary}`)
    .join('\n\n');

  const body = `# CodexSwitch Full Documentation Index

This is the first AI-readable index for the CodexSwitch Fumadocs site. It summarizes the current bilingual content tree and links readers to the rendered documentation.

Home:
- /zh
- /en

Docs:
- /zh/docs
- /en/docs

${pages}
`;

  return new Response(body, {
    headers: {
      'content-type': 'text/plain; charset=utf-8',
    },
  });
}
