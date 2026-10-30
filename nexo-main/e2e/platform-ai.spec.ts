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

type CostItem = {
  tenantId: string; tenantName: string; costUsd: number; totalCalls: number; llmCalls: number; monthSpentUsd: number;
  monthReservedUsd: number; softLimitUsd: number | null; hardLimitUsd: number | null; softLimitHit: boolean; hardLimitHit: boolean;
};
type Prompt = {
  id: string; promptType: string; version: string; hash: string; isActive: boolean; description: string;
  createdBy: string; createdAt: string; contentPreview: string;
};

const tenants = [
  { id: 't-acme', companyName: 'Acme Ltda', slug: 'acme', status: 'Active', email: 'acme@teste.dev', taxId: '1', createdAt: '2026-01-01T00:00:00Z', modules: [], stores: [], userCount: 1 },
  { id: 't-nova', companyName: 'Empresa Nova', slug: 'nova', status: 'Active', email: 'nova@teste.dev', taxId: '2', createdAt: '2026-01-01T00:00:00Z', modules: [], stores: [], userCount: 1 },
];

function initialCosts(): CostItem[] {
  return [{ tenantId: 't-acme', tenantName: 'Acme Ltda', costUsd: 4.2, totalCalls: 200, llmCalls: 90, monthSpentUsd: 3,
    monthReservedUsd: 0, softLimitUsd: null, hardLimitUsd: null, softLimitHit: false, hardLimitHit: false }];
}

function initialPrompts(): Prompt[] {
  return [{ id: 'pv-1', promptType: 'extraction', version: '1.0.0', hash: 'aaaabbbbccccdddd', isActive: true, description: 'Inicial',
    createdBy: 'Sistema', createdAt: '2026-10-01T12:00:00Z', contentPreview: 'Você extrai valores...' }];
}

interface Fake {
  providers: Provider[]; keysReceived: string[]; patches: unknown[]; playgroundBodies: unknown[]; failClaude: boolean;
  costs: CostItem[]; limitBodies: unknown[]; prompts: Prompt[]; promptBodies: unknown[]; activations: string[];
}

function recompute(p: Provider): Provider {
  if (!p.implemented) return p;
  if (!p.isEnabled) return { ...p, status: 'disabled', statusMessage: 'Desativado pelo operador.' };
  return { ...p, status: 'available', statusMessage: p.provider === 'RuleBased' ? 'Motor local, sempre disponível.' : 'Pronto para uso.' };
}

async function prepare(page: Page, options: { failClaude?: boolean } = {}): Promise<Fake> {
  const fake: Fake = {
    providers: initialProviders(), keysReceived: [], patches: [], playgroundBodies: [], failClaude: !!options.failClaude,
    costs: initialCosts(), limitBodies: [], prompts: initialPrompts(), promptBodies: [], activations: [],
  };
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
    if (path === '/platform/tenants' && method === 'GET') return json(route, tenants);

    if (path === '/platform/interpreter/costs' && method === 'GET') {
      return json(route, {
        totalUsd: fake.costs.reduce((s, c) => s + c.costUsd, 0),
        monthSpentUsd: fake.costs.reduce((s, c) => s + c.monthSpentUsd, 0),
        items: fake.costs,
      });
    }
    const limits = /^\/platform\/interpreter\/costs\/([^/]+)\/limits$/.exec(path);
    if (limits && method === 'PUT') {
      const body = req.postDataJSON() as { softLimitUsd: number | null; hardLimitUsd: number | null };
      fake.limitBodies.push(body);
      const tenant = tenants.find(t => t.id === limits[1]);
      if (!tenant) return json(route, { error: 'Empresa não encontrada.', code: 'not_found' }, 404);
      const base: CostItem = fake.costs.find(c => c.tenantId === tenant.id) ?? {
        ...initialCosts()[0], tenantId: tenant.id, tenantName: tenant.companyName, costUsd: 0, totalCalls: 0, llmCalls: 0, monthSpentUsd: 0,
      };
      const used = base.monthSpentUsd + base.monthReservedUsd;
      const item: CostItem = {
        ...base, ...body,
        softLimitHit: body.softLimitUsd != null && base.monthSpentUsd >= body.softLimitUsd,
        hardLimitHit: body.hardLimitUsd != null && used >= body.hardLimitUsd,
      };
      fake.costs = [...fake.costs.filter(c => c.tenantId !== tenant.id), item];
      return json(route, item);
    }

    if (path === '/platform/interpreter/prompts' && method === 'GET') {
      const type = new URL(req.url()).searchParams.get('type');
      return json(route, fake.prompts.filter(p => p.promptType === type));
    }
    if (path === '/platform/interpreter/prompts' && method === 'POST') {
      const body = req.postDataJSON() as { promptType: string; version: string; content: string; description: string };
      fake.promptBodies.push(body);
      if (fake.prompts.some(p => p.promptType === body.promptType && p.version === body.version))
        return json(route, { error: `A versão ${body.version} do prompt ${body.promptType} já existe.`, code: 'conflict' }, 409);
      const created: Prompt = {
        id: `pv-${fake.prompts.length + 1}`, promptType: body.promptType, version: body.version, hash: 'eeeeffff00001111',
        isActive: false, description: body.description, createdBy: 'Operador de teste', createdAt: '2026-10-30T10:00:00Z',
        contentPreview: body.content.slice(0, 200),
      };
      fake.prompts.push(created);
      return json(route, { id: created.id, promptType: created.promptType, version: created.version, hash: created.hash, isActive: false });
    }
    const activation = /^\/platform\/interpreter\/prompts\/([^/]+)\/activate$/.exec(path);
    if (activation && method === 'POST') {
      const target = fake.prompts.find(p => p.id === activation[1]);
      if (!target) return json(route, { error: 'Versão não encontrada.', code: 'not_found' }, 404);
      fake.activations.push(target.id);
      fake.prompts = fake.prompts.map(p => (p.promptType === target.promptType ? { ...p, isActive: p.id === target.id } : p));
      return json(route, { activated: target.id, promptType: target.promptType, version: target.version, hash: target.hash });
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

test('Custos: limite rígido abaixo do gasto do mês aparece como atingido', async ({ page }) => {
  const fake = await prepare(page);
  await page.goto('/platform/ai/costs');
  await expect(page.getByText(/chamadas pagas são recusadas antes de acontecer/)).toBeVisible();
  const acme = page.getByRole('row', { name: 'Empresa Acme Ltda' });
  await expect(acme.getByText('Sem limite')).toBeVisible();

  await page.getByRole('button', { name: 'Editar limites de Acme Ltda' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(/Limite rígido/).fill('2');
  await dialog.getByRole('button', { name: /Salvar limites/ }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);

  await expect(acme.getByText('Limite rígido atingido')).toBeVisible();
  await expect(acme.getByText('100% do limite rígido')).toBeVisible();
  expect(fake.limitBodies).toEqual([{ softLimitUsd: null, hardLimitUsd: 2 }]);
});

test('Custos: definir limite para empresa ainda sem uso', async ({ page }) => {
  const fake = await prepare(page);
  await page.goto('/platform/ai/costs');
  await page.getByRole('button', { name: /Definir limite para empresa/ }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('combobox', { name: 'Empresa' }).click();
  await page.getByRole('option', { name: 'Empresa Nova' }).click();
  await dialog.getByLabel(/Limite suave/).fill('3');
  await dialog.getByLabel(/Limite rígido/).fill('10');
  await dialog.getByRole('button', { name: /Salvar limites/ }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);

  const nova = page.getByRole('row', { name: 'Empresa Empresa Nova' });
  await expect(nova.getByText('US$ 10,00')).toBeVisible();
  await expect(nova.getByText('0% do limite rígido')).toBeVisible();
  expect(fake.limitBodies).toEqual([{ softLimitUsd: 3, hardLimitUsd: 10 }]);
});

test('Prompts: criar e ativar uma versão de extração', async ({ page }) => {
  const fake = await prepare(page);
  await page.goto('/platform/ai/prompts');
  await expect(page.getByTestId('prompt-runtime-usage')).toContainText('Usado pelo runtime.');

  await page.getByRole('button', { name: /Nova versão/ }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Versão').fill('1.1.0');
  await dialog.getByLabel('Descrição').fill('Datas por extenso');
  await dialog.getByLabel('Conteúdo').fill('Instruções da versão nova');
  await expect(dialog.getByTestId('prompt-content-counter')).toHaveText('25 / 16.000');
  await dialog.getByRole('button', { name: /Criar versão/ }).click();
  await expect(page.getByRole('dialog')).toHaveCount(0);

  const row = page.getByRole('row', { name: 'Versão 1.1.0' });
  await expect(row.getByText('eeeeffff00001111')).toBeVisible();
  await row.getByRole('button', { name: 'Ativar versão 1.1.0' }).click();
  const confirm = page.getByRole('alertdialog');
  await expect(confirm).toContainText('O runtime passa a usar esta versão imediatamente.');
  await confirm.getByRole('button', { name: 'Ativar versão', exact: true }).click();

  await expect(row.getByText('ATIVA', { exact: true })).toBeVisible();
  await expect(page.getByRole('row', { name: 'Versão 1.0.0' }).getByText('ATIVA', { exact: true })).toHaveCount(0);
  expect(fake.promptBodies).toEqual([
    { promptType: 'extraction', version: '1.1.0', content: 'Instruções da versão nova', description: 'Datas por extenso' },
  ]);
  expect(fake.activations).toEqual(['pv-2']);

  await page.getByRole('tab', { name: 'Interpretação' }).click();
  await expect(page.getByTestId('prompt-runtime-usage')).toContainText('Ainda não usado pelo runtime');
});
