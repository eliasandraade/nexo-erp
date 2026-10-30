import { test, expect, type Page, type Route } from '@playwright/test';

/**
 * SuperAdmin security against a fake backend (context.route, so a tab opened by the page is faked too):
 * two-step verification setup and the impersonation hand-off (reason, new tab, tab-scoped token).
 */

const session = {
  userId: 'qa-platform', tenantId: '', name: 'Operador de teste', role: 'super_admin', login: 'qa@orken.com.br',
  modules: [], activeModules: [], companyName: 'Orken Platform', storeId: null, storeIds: [], type: 'platform',
};

const tenantSession = {
  userId: 'u1', tenantId: 't1', name: 'Ana Lima', role: 'diretoria', login: 'ana', email: 'ana@sol.com',
  activeModules: ['varejo'], storeId: 's1', storeIds: ['s1'], companyName: 'Padaria Sol', type: 'tenant',
};

const tenant = {
  id: 't1', companyName: 'Padaria Sol LTDA', tradeName: 'Padaria Sol', taxId: '00.000.000/0001-00', email: 'p@sol.com',
  status: 'Active', slug: 'padaria-sol', createdAt: '2026-01-01T00:00:00Z', storeCount: 1, userCount: 1, activeModules: ['varejo'],
  subscriptions: [], stores: [],
  users: [{ id: 'u1', name: 'Ana Lima', email: 'ana@sol.com', login: 'ana', role: 'Diretoria', status: 'Active', lastAccessAt: null }],
};

interface Fake { mfaEnabled: boolean; sessionsEnded: boolean; impersonateBodies: unknown[]; impersonationEnded: number }

const json = (route: Route, body: unknown, status = 200) =>
  route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) });

async function prepare(page: Page): Promise<Fake> {
  const fake: Fake = { mfaEnabled: false, sessionsEnded: false, impersonateBodies: [], impersonationEnded: 0 };
  await page.addInitScript(auth => {
    if (!localStorage.getItem('nexo:session')) {
      localStorage.setItem('nexo:session', JSON.stringify(auth));
      localStorage.setItem('nexo:access_token', 'qa-only');
      localStorage.setItem('nexo:refresh_token', 'p.qa-refresh');
    }
  }, session);

  await page.context().route('http://localhost:5000/api/**', async route => {
    const req = route.request();
    const path = new URL(req.url()).pathname.replace(/^\/api/, '');
    const method = req.method();
    const bearer = req.headers()['authorization'] ?? '';

    if (bearer === 'Bearer imp-token') {
      if (path === '/auth/impersonation/end') { fake.impersonationEnded++; return route.fulfill({ status: 204 }); }
      if (path === '/auth/me') return json(route, tenantSession);
      return json(route, []);
    }
    if (fake.sessionsEnded && path.startsWith('/platform'))
      return json(route, { error: 'Sessão da plataforma encerrada. Entre de novo.', code: 'platform_session_ended' }, 401);

    if (path === '/auth/me') return json(route, session);
    if (path === '/platform/auth/security' && method === 'GET')
      return json(route, {
        mfa: { enabled: fake.mfaEnabled, setupPending: false, recoveryCodesLeft: fake.mfaEnabled ? 8 : 0 },
        currentSessionMfaVerified: false,
        sessions: [{ id: 's-current', createdAt: '2026-10-30T10:00:00Z', lastUsedAt: '2026-10-30T10:30:00Z', expiresAt: '2026-11-06T10:00:00Z',
          mfaVerified: false, ipAddress: '10.0.0.1', userAgent: 'Mozilla/5.0 (Windows NT 10.0) Chrome/130.0', current: true }],
      });
    if (path === '/platform/auth/mfa/setup' && method === 'POST')
      return json(route, { secret: 'JBSWY3DPEHPK3PXP', otpauthUri: 'otpauth://totp/Orken:qa?secret=JBSWY3DPEHPK3PXP&issuer=Orken' });
    if (path === '/platform/auth/mfa/enable' && method === 'POST') {
      const { code } = req.postDataJSON() as { code: string };
      if (code !== '123456') return json(route, { error: 'Código inválido.', code: 'invalid_code' }, 422);
      fake.mfaEnabled = true;
      fake.sessionsEnded = true;
      return json(route, { recoveryCodes: ['aaaa-1111', 'bbbb-2222', 'cccc-3333', 'dddd-4444', 'eeee-5555', 'ffff-6666', 'gggg-7777', 'hhhh-8888'] });
    }
    if (path === '/platform/tenants/t1' && method === 'GET') return json(route, tenant);
    if (path === '/platform/tenants/t1/impersonate' && method === 'POST') {
      const body = req.postDataJSON() as { reason?: string };
      fake.impersonateBodies.push(body);
      if (!body.reason || body.reason.trim().length < 10)
        return json(route, { error: 'Diga o motivo do acesso.', code: 'impersonation_reason_required' }, 422);
      return json(route, { accessToken: 'imp-token', refreshToken: null, expiresAt: new Date(Date.now() + 30 * 60_000).toISOString(), session: tenantSession });
    }
    return json(route, []);
  });
  return fake;
}

test('Segurança: ativa a verificação em duas etapas, mostra os códigos e manda entrar de novo', async ({ page }) => {
  await prepare(page);
  await page.goto('/platform/seguranca');

  await expect(page.getByText('Ações administrativas exigem login com verificação em duas etapas.')).toBeVisible();
  await expect(page.getByText('Esta sessão', { exact: true })).toBeVisible();

  await page.getByRole('button', { name: 'Ativar verificação em duas etapas' }).click();
  await page.getByLabel('Sua senha').fill('senha-forte-qa');
  await page.getByRole('button', { name: 'Continuar' }).click();

  await expect(page.getByText('JBSWY3DPEHPK3PXP', { exact: true })).toBeVisible();
  await expect(page.getByAltText('QR code para o aplicativo autenticador')).toBeVisible();
  await page.getByLabel(/código de 6 números/).fill('123456');
  await page.getByRole('button', { name: 'Ativar', exact: true }).click();

  const codes = page.getByRole('list', { name: 'Códigos de recuperação' });
  await expect(codes.getByRole('listitem')).toHaveCount(8);
  await expect(codes).toContainText('aaaa-1111');
  // the server ended every session: the dead tokens are gone, yet the codes stay on screen
  expect(await page.evaluate(() => localStorage.getItem('nexo:access_token'))).toBeNull();

  const again = page.getByRole('button', { name: 'Entrar de novo' });
  await expect(again).toBeDisabled();
  await page.getByLabel('Guardei os códigos em local seguro').check();
  await again.click();

  await expect(page).toHaveURL(/\/login\?motivo=mfa$/);
  await expect(page.getByText(/Verificação em duas etapas ativada/)).toBeVisible();
});

test('Entrar como cliente: pede o motivo, abre a aba e entrega o token só para ela', async ({ page }) => {
  const fake = await prepare(page);
  await page.goto('/platform/tenants/t1');

  await page.getByRole('button', { name: 'Entrar como cliente' }).click();
  const dialog = page.getByRole('dialog');
  const enter = dialog.getByRole('button', { name: 'Entrar como cliente' });
  await dialog.getByLabel('Motivo do acesso').fill('curto');
  await expect(dialog.getByText('5/300')).toBeVisible();
  await expect(enter).toBeDisabled();

  await dialog.getByLabel('Motivo do acesso').fill('Chamado #42: nota fiscal rejeitada');
  const [tab] = await Promise.all([page.waitForEvent('popup'), enter.click()]);

  await expect(tab.getByText(/Acesso da plataforma/)).toBeVisible();
  await expect(tab.getByRole('status').filter({ hasText: 'Acesso da plataforma' })).toContainText('Ana Lima');
  expect(fake.impersonateBodies).toEqual([{ reason: 'Chamado #42: nota fiscal rejeitada' }]);

  // the token lives only in the new tab's sessionStorage; the admin's own session is untouched
  const tabStorage = await tab.evaluate(() => ({
    marker: sessionStorage.getItem('orken:impersonation'),
    token: sessionStorage.getItem('nexo:access_token'),
  }));
  expect(tabStorage.token).toBe('imp-token');
  expect(JSON.parse(tabStorage.marker!)).toMatchObject({ tenant: 'Padaria Sol', user: 'Ana Lima', ended: false });
  const shared = await page.evaluate(() => ({ ...localStorage }));
  expect(shared['nexo:access_token']).toBe('qa-only');
  expect(shared['nexo:refresh_token']).toBe('p.qa-refresh');
  expect(JSON.parse(shared['nexo:session']).type).toBe('platform');
  expect(Object.keys(shared).some(k => k.includes('impersonat'))).toBe(false);
  expect(Object.values(shared).some(v => v.includes('imp-token'))).toBe(false);

  // leaving ends the access on the server and closes the tab; the platform tab keeps working
  await Promise.all([tab.waitForEvent('close'), tab.getByRole('button', { name: 'Sair do acesso' }).click()]);
  expect(fake.impersonationEnded).toBe(1);
  expect(await page.evaluate(() => localStorage.getItem('nexo:access_token'))).toBe('qa-only');
  await expect(page.getByRole('heading', { name: 'Padaria Sol' })).toBeVisible();
});
