import { test, expect, type Page, type Route } from '@playwright/test';

/**
 * SuperAdmin AI pages against a fake backend (page.route). Nothing here talks to a real provider.
 * The fake keeps state, so a mutation followed by the page's refetch shows what the backend would answer.
 */

const session = {
  userId: 'qa-platform', tenantId: 'qa-platform', name: 'Operador de teste', role: 'super_admin', login: 'qa-platform',
  modules: [], activeModules: [], companyName: 'Plataforma de teste', storeId: null, storeIds: [], type: 'platform',
};

type Provider = Record<string, unknown> & { id: string; provider: string; name: string; isEnabled: boolean; implemented: boolean };

function initialProviders(): Provider[] {
  return [
    { id: 'p-claude', name: 'Claude', provider: 'Claude', status: 'available', statusMessage: 'Pronto para uso.',
      implemented: true, requiresKey: true, allowedByDeployment: true, isEnabled: true, isDefault: true, priority: 10,
      modelId: 'claude-modelo-e2e', hasApiKey: true, apiKeyLastFour: 'abcd', apiKeyUpdatedAt: '2026-10-29T12:00:00Z',
      monthlyTokenLimit: null, inputUsdPerMillionTokens: 3, outputUsdPerMillionTokens: 15, lastTest: null },
    { id: 'p-rules', name: 'RuleBased', provider: 'RuleBased', status: 'available', statusMessage: 'Motor local, sempre disponível.',
      implemented: true, requiresKey: false, allowedByDeployment: true, isEnabled: true, isDefault: false, priority: 90,
      modelId: null, hasApiKey: false, apiKeyLastFour: null, apiKeyUpdatedAt: null,
      monthlyTokenLimit: null, inputUsdPerMillionTokens: 0, outputUsdPerMillionTokens: 0, lastTest: null },
    { id: 'p-openai', name: 'OpenAI', provider: 'OpenAI', status: 'implementation_missing', statusMessage: 'O OpenAI ainda não foi implementado nesta versão.',
      implemented: false, requiresKey: true, allowedByDeployment: true, isEnabled: false, isDefault: false, priority: 50,
      modelId: null, hasApiKey: false, apiKeyLastFour: null, apiKeyUpdatedAt: null,
      monthlyTokenLimit: null, inputUsdPerMillionTokens: 0, outputUsdPerMillionTokens: 0, lastTest: null },
  ];
}

const playgroundOk = {
  tenantId: 't-e2e', provider: 'Claude', modelId: 'claude-modelo-e2e', forced: true, fallbackUsed: false,
  attempts: [{ provider: 'Claude', modelId: 'claude-modelo-e2e', succeeded: true, failureReason: null, durationMs: 640 }],
  elapsedMs: 655, prompt: { type: 'extraction', version: '1.0', hash: 'hash-e2e' },
  extraction: {
    amount: { value: 850, confidence: 0.97, status: 'AutoFilled' }, date: { value: '2026-05-07', confidence: 0.9, status: 'AutoFilled' },
    payee: { value: 'João Carlos da Silva', confidence: 0.8, status: 'NeedsAttention' }, account: { value: null, confidence: 0, status: 'RequiresInput' },
    inputTokens: 380, outputTokens: 90, costUsd: 0.00249,
  },
  interpretation: { direction: 'Outflow', dirSource: 'Extraction', nature: 'Expense', natureSource: 'Rule', categoryId: null, catSource: 'None',
    contextType: null, contextId: null, ctxSource: 'None', accountId: null, accSource: 'None' },
};

interface Fake { providers: Provider[]; keysReceived: string[]; patches: unknown[]; playgroundBodies: unknown[]; failClaude: boolean }

function recompute(p: Provider): Provider {
  if (!p.implemented) return p;
  if (!p.isEnabled) return { ...p, status: 'disabled', statusMessage: 'Desativado pelo operador.' };
  return { ...p, status: 'available', statusMessage: p.provider === 'RuleBased' ? 'Motor local, sempre disponível.' : 'Pronto para uso.' };
}

async function prepare(page: Page, options: { failClaude?: boolean } = {}): Promise<Fake> {
  const fake: Fake = { providers: initialProviders(), keysReceived: [], patches: [], playgroundBodies: [], failClaude: !!options.failClaude };
  await page.addInitScript(auth => {
    localStorage.setItem('nexo:session', JSON.stringify(auth)); localStorage.setItem('nexo:access_token', 'qa-only');
  }, session);

  const json = (route: Route, body: unknown, status = 200) =>
    route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

  await page.route('http://localhost:5000/api/**', async route => {
    const req = route.request();
    const path = new URL(req.url()).pathname.replace(/^\/api/, '');
    const method = req.method();
    if (path === '/auth/me') return json(route, session);
    if (path === '/platform/auth/security')
      return json(route, { mfa: { enabled: true, setupPending: false, recoveryCodesLeft: 8 }, currentSessionMfaVerified: true, sessions: [] });

    if (path === '/platform/interpreter/providers' && method === 'GET') {
      const available = fake.providers.filter(p => p.status === 'available')
        .sort((a, b) => Number(b.isDefault) - Number(a.isDefault) || Number(a.priority) - Number(b.priority));
      return json(route, { providers: fake.providers, secretStorageAvailable: true, automaticOrder: available.map(p => p.provider) });
    }
    const one = /^\/platform\/interpreter\/providers\/([^/]+)(\/api-key|\/test)?$/.exec(path);
    if (one) {
      const idx = fake.providers.findIndex(p => p.id === one[1]);
      if (idx < 0) return json(route, { error: 'Provedor não encontrado.', code: 'not_found', requestId: 'req-404' }, 404);
      const current = fake.providers[idx];
      if (one[2] === '/api-key' && method === 'PUT') {
        const { apiKey } = req.postDataJSON() as { apiKey: string };
        fake.keysReceived.push(apiKey);
        fake.providers[idx] = recompute({ ...current, hasApiKey: true, apiKeyLastFour: apiKey.slice(-4), apiKeyUpdatedAt: '2026-10-30T10:00:00Z' });
        return json(route, fake.providers[idx]);
      }
      if (one[2] === '/test' && method === 'POST') {
        const at = '2026-10-30T10:05:00Z';
        fake.providers[idx] = { ...current, lastTest: { status: 'ok', message: 'Conexão confirmada.', at } };
        return json(route, { status: 'ok', message: 'Conexão confirmada.', modelId: current.modelId, latencyMs: 321, testedAt: at });
      }
      if (!one[2] && method === 'PATCH') {
        const body = req.postDataJSON() as Record<string, unknown>;
        fake.patches.push(body);
        fake.providers[idx] = recompute({ ...current, ...body } as Provider);
        return json(route, fake.providers[idx]);
      }
    }
    if (path === '/platform/interpreter/playground' && method === 'POST') {
      const body = req.postDataJSON() as { forceProvider: string | null };
      fake.playgroundBodies.push(body);
      if (fake.failClaude && body.forceProvider === 'Claude')
        return json(route, { error: 'O Claude limitou as requisições. Tente de novo em instantes.', code: 'ai_provider_failed',
          details: ['rate_limited'], requestId: 'req-e2e-503' }, 503);
      return json(route, { ...playgroundOk, forced: body.forceProvider !== null });
    }
    return json(route, []);
  });
  return fake;
}

const card = (page: Page, name: string) => page.getByRole('region', { name: `Provedor ${name}` });

test('Provedores: Claude disponível com modelo e OpenAI indisponível nesta versão', async ({ page }) => {
  await prepare(page);
  await page.goto('/platform/ai/providers');
  const claude = card(page, 'Claude');
  await expect(claude.getByText('Disponível', { exact: true })).toBeVisible();
  await expect(claude.getByText('claude-modelo-e2e').first()).toBeVisible();
  await expect(claude.getByText('****abcd')).toBeVisible();
  await expect(page.getByText('Claude → RuleBased')).toBeVisible();
  const openAi = card(page, 'OpenAI');
  await expect(openAi.getByText('Não disponível nesta versão', { exact: true })).toBeVisible();
  await expect(openAi.getByRole('button', { name: /Testar conexão/ })).toBeDisabled();
  await expect(openAi.getByRole('switch', { name: 'Ativar OpenAI' })).toBeDisabled();
});

test('Provedores: trocar chave mostra só ****últimos4', async ({ page }) => {
  const fake = await prepare(page);
  const typed = 'sk-e2e-0123456789ABCDEFGH-9z8y';
  await page.goto('/platform/ai/providers');
  await card(page, 'Claude').getByRole('button', { name: /Trocar chave/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByText(/AES-256-GCM/)).toBeVisible();
  const input = dialog.getByLabel(/Nova chave/);
  await expect(input).toHaveValue('');
  await input.fill(typed);
  await dialog.getByRole('button', { name: /Salvar chave/ }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);
  await expect(card(page, 'Claude').getByText('****9z8y')).toBeVisible();
  expect(fake.keysReceived).toEqual([typed]);
  expect(await page.content()).not.toContain(typed);
});

test('Provedores: testar conexão mostra OK, modelo e latência', async ({ page }) => {
  await prepare(page);
  await page.goto('/platform/ai/providers');
  const claude = card(page, 'Claude');
  await claude.getByRole('button', { name: /Testar conexão/ }).click();
  const status = claude.getByRole('status');
  await expect(status).toContainText('Teste de conexão: OK');
  await expect(status).toContainText('claude-modelo-e2e');
  await expect(status).toContainText('321 ms');
});

test('Provedores: desativar atualiza o status', async ({ page }) => {
  const fake = await prepare(page);
  await page.goto('/platform/ai/providers');
  const claude = card(page, 'Claude');
  await claude.getByRole('switch', { name: 'Ativar Claude' }).click();
  await expect(claude.getByText('Desativado', { exact: true }).first()).toBeVisible();
  await expect(claude.getByText('Desativado pelo operador.')).toBeVisible();
  await expect(page.getByText('Modo automático usa: RuleBased')).toBeVisible();
  expect(fake.patches).toEqual([{ isEnabled: false }]);
});

test('Playground: Claude forçado mostra o resultado com modelo, tokens e custo', async ({ page }) => {
  const fake = await prepare(page);
  await page.goto('/platform/ai/playground');
  await page.getByRole('radio', { name: 'Claude' }).click();
  await expect(page.getByRole('radio', { name: 'OpenAI' })).toBeDisabled();
  await page.getByLabel('Texto do movimento').fill('Pix enviado em 07/05/2026 R$ 850,00');
  await page.getByRole('button', { name: /Analisar/ }).click();
  const result = page.getByTestId('playground-result');
  await expect(result).toContainText('Executado por Claude');
  await expect(result).toContainText('claude-modelo-e2e');
  await expect(result).toContainText('Tokens: 380 entrada / 90 saída');
  await expect(result).toContainText('Custo: US$ 0,00249');
  expect(fake.playgroundBodies).toEqual([{ text: 'Pix enviado em 07/05/2026 R$ 850,00', forceProvider: 'Claude', tenantId: null }]);
});

test('Playground: falha do Claude forçado vira mensagem controlada', async ({ page }) => {
  await prepare(page, { failClaude: true });
  await page.goto('/platform/ai/playground');
  await page.getByRole('radio', { name: 'Claude' }).click();
  await page.getByLabel('Texto do movimento').fill('Recebimento de cliente R$2500');
  await page.getByRole('button', { name: /Analisar/ }).click();
  const alert = page.getByRole('alert');
  await expect(alert).toContainText('O provedor falhou');
  await expect(alert).toContainText('O Claude limitou as requisições.');
  await expect(alert).toContainText('ai_provider_failed');
  await expect(alert).toContainText('rate_limited');
  await expect(alert).toContainText('req-e2e-503');
  await expect(page.getByTestId('playground-result')).toHaveCount(0);
});
