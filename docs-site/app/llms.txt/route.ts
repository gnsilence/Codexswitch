export const dynamic = 'force-static';

export function GET() {
  const body = `# CodexSwitch Docs

CodexSwitch is a cross-platform Avalonia desktop app and local OpenAI Responses-compatible proxy for switching AI providers, adapting protocols, managing Codex/Claude Code configuration, and recording local usage.

Full corpus: /llms-full.txt
English docs: /en/docs
Chinese docs: /zh/docs
Quick start: /zh/docs/start/quick-start
`;

  return new Response(body, {
    headers: {
      'content-type': 'text/plain; charset=utf-8',
    },
  });
}
