import { createHmac, randomInt, randomUUID } from "node:crypto";
import { test as base, expect, type APIRequestContext, type Browser, type BrowserContext, type Page, type Request, type Response } from "@playwright/test";

/**
 * Authentication against the real API (playwright.auth.config.ts, e2e/auth-host): the real login page, route guards and API
 * client talking to the real controllers, middleware, CORS policy, rate limiter and PostgreSQL. A test only replaces a
 * response where the server cannot produce the case on demand (no network, a server echoing the input), and says so.
 *
 * Each test creates its own accounts through the company's API, with synthetic values, and is its own client address for the
 * rate limiter, so a test never inherits a lockout, a revoked session or a spent limit from another one.
 *
 * The whole file runs twice, once against each bundle (AUTH_E2E_SESSION_MODE, playwright.auth.config.ts): the proof of equivalence of
 * docs/security/sessao-auditoria-e-migracao.md, section 6. A scenario is the same in both modes; what differs is where the tokens are and
 * how many renewals it takes, because in the cookie mode each page load starts with no access token. Those differences are named here,
 * and the tests that only exist in one mode say why.
 */

const COOKIE_MODE = process.env.AUTH_E2E_SESSION_MODE === "cookie";
const APP_ORIGIN = `http://127.0.0.1:${COOKIE_MODE ? 5198 : 5197}`;
const HOST_ORIGIN = "http://127.0.0.1:5196";
const API = `${HOST_ORIGIN}/api`;

const ACCESS_TOKEN_KEY = "nexo:access_token";
const REFRESH_TOKEN_KEY = "nexo:refresh_token";
const SESSION_KEY = "nexo:session";
const NO_SESSION = { access: null, refresh: null, session: null };

const SESSION_COOKIE = "__Host-nexo_rt";
const MODE_HEADER = "x-orken-session";
const SESSION_CHANNEL = "orken-session";
/** Cookie mode: a page load has no access token, so its first call renews. */
const RENEWALS_PER_LOAD = COOKIE_MODE ? 1 : 0;
/** Cookie mode: the first visit of a browser to a page that needs a session asks once whether the cookie has one. */
const FIRST_VISIT_RENEWALS = COOKIE_MODE ? 1 : 0;
/** Cookie mode: a page load has no token that could be refused: it hears of the end of a session from the renewal itself. */
const REFUSED_ME_ON_LOAD = COOKIE_MODE ? 0 : 1;

const WRONG_CREDENTIALS = "Login ou senha incorretos.";
const ACCOUNT_BLOCKED = "Conta bloqueada. Entre em contato com o suporte.";
const SESSION_ENDED_URL = /\/login\?motivo=sessao-encerrada$/;
const SESSION_REVOKED_URL = /\/login\?motivo=sessao-revogada$/;
const SESSION_REVOKED_NOTICE = "Seu acesso foi alterado e, por segurança, sua sessão foi encerrada. Entre de novo.";

type Role = "diretoria" | "gerente" | "vendedor" | "estoquista";
type Status = "active" | "inactive" | "blocked";

interface Account { id: string; login: string; email: string; password: string; fullName: string; role: Role }
interface ApiCall { method: string; path: string; status: number }

// ── Test world ─────────────────────────────────────────────────────────────────────────────────────────────────────────

/** A value nobody has used: the accounts of this suite never carry anything real. */
const syntheticValue = () => `Sintetica-${randomUUID()}`;

/** A private network address nobody else in the run has: the sign-in limiter partitions on it (X-Forwarded-For). */
const clientAddress = () => `10.${randomInt(256)}.${randomInt(256)}.${randomInt(1, 255)}`;

/** A browser of another person or place, with its own client address. */
async function anotherBrowser(browser: Browser): Promise<BrowserContext> {
  return browser.newContext({ baseURL: APP_ORIGIN, extraHTTPHeaders: { "X-Forwarded-For": clientAddress() } });
}

/** The company's administrator, acting through the real API. */
class Company {
  constructor(private readonly api: APIRequestContext, private readonly bearer: string) {}

  async createUser(role: Role): Promise<Account> {
    const tag = randomUUID().replaceAll("-", "").slice(0, 12);
    const account = { login: `e2e${tag}`, email: `e2e${tag}@nexo.test`, password: syntheticValue(), fullName: `Pessoa ${tag}`, role };
    const response = await this.api.post(`${API}/users`, {
      headers: { Authorization: this.bearer },
      data: { ...account, phone: null, notes: null },
    });
    expect(response.status(), await response.text()).toBe(201);
    return { ...account, id: (await response.json()).id };
  }

  /** Same call as the users screen. A new role, or blocking or deactivating someone, ends that person's sessions. */
  async update(account: Account, change: { role?: Role; status?: Status }): Promise<void> {
    const response = await this.api.put(`${API}/users/${account.id}`, {
      headers: { Authorization: this.bearer },
      data: { fullName: account.fullName, email: account.email, role: change.role ?? account.role, phone: null, notes: null, status: change.status ?? "active" },
    });
    expect(response.ok(), await response.text()).toBe(true);
  }
}

/** A person with a company of their own: for what changes a whole company, like a module or another store. */
interface OwnCompany { account: Account; tenantId: string; storeId: string; company: Company }

/** The accounts of the platform are not created by any screen of the suite. */
interface PlatformAccount { email: string; password: string }

/** A context whose session was seeded in localStorage on purpose: the check that no token stays there does not apply. */
const keepsOldTokens = new WeakSet<BrowserContext>();

/** The last access token the API gave a context, and the reading of the answers still under way. */
const issuedAccess = new WeakMap<BrowserContext, string>();
const readingAnswers = new WeakMap<BrowserContext, Promise<unknown>>();

const issuesSession = (r: Response) =>
  r.request().method() !== "OPTIONS" && /\/api\/(auth\/(login|mfa\/verify|verify-email|refresh|switch-store)|platform\/auth\/(login|mfa\/verify|refresh))(\?|$)/.test(r.url());

const JWT_SHAPED = /eyJ[\w-]+\.[\w-]+\.[\w-]+/;

/** Everything a script of the page can read: no token there, in the cookie mode. */
const whatScriptsCanRead = (page: Page) => page.evaluate(() => ({
  local: { ...localStorage },
  session: { ...sessionStorage },
  cookie: document.cookie,
}));

/** What the page asked of the API (preflights aside) and what is still on the way. */
class ApiLog {
  calls: ApiCall[] = [];
  pending = new Set<Request>();
  count = (path: string, status?: number) => this.calls.filter((c) => c.path === path && (status === undefined || c.status === status)).length;
  refused = () => this.calls.filter((c) => c.status === 401).length;
  reset = () => { this.calls.length = 0; };
}

const test = base.extend<{
  company: Company;
  apiCalls: ApiLog;
  /** Opens another device: an API client with its own client address. */
  device: () => Promise<APIRequestContext>;
}>({
  context: async ({ context }, provide) => {
    await context.setExtraHTTPHeaders({ "X-Forwarded-For": clientAddress() });
    const uncaught: string[] = [];
    const refreshTokensInBodies: string[] = [];
    context.on("weberror", (e) => uncaught.push(String(e.error())));
    context.on("response", (r) => {
      if (!r.url().includes("/api/") || r.request().method() === "OPTIONS" || !(r.headers()["content-type"] ?? "").includes("json")) return;
      const reading = r.text().then((text) => {
        if (/"refreshToken":\s*"/.test(text)) refreshTokensInBodies.push(new URL(r.url()).pathname);
        if (issuesSession(r) && r.ok()) {
          const access = (JSON.parse(text) as { accessToken?: string }).accessToken;
          if (access) issuedAccess.set(context, access);
        }
      }).catch(() => undefined);   // the page went away with the answer
      // An answer the page never finished reading (it reloaded) may never report back: nothing waits for it for long.
      const atMost = new Promise((resolve) => setTimeout(resolve, 3000));
      readingAnswers.set(context, Promise.all([readingAnswers.get(context), Promise.race([reading, atMost])]));
    });
    await provide(context);
    expect(uncaught, "uncaught errors in the page").toEqual([]);
    if (!COOKIE_MODE) return;

    // These hold for every test of the cookie mode, whatever it was about.
    await readingAnswers.get(context);
    expect(refreshTokensInBodies, "a refresh token in the body of an answer to a page").toEqual([]);
    if (keepsOldTokens.has(context)) return;
    for (const page of context.pages()) {
      if (page.isClosed() || !page.url().startsWith("http")) continue;
      const readable = JSON.stringify(await whatScriptsCanRead(page).catch(() => ({})));
      expect(readable, `a token where a script can read it, at ${page.url()}`).not.toMatch(JWT_SHAPED);
      expect(readable).not.toContain(ACCESS_TOKEN_KEY);
      expect(readable).not.toContain(REFRESH_TOKEN_KEY);
      expect(readable).not.toContain(SESSION_COOKIE);
    }
  },
  apiCalls: async ({ context }, provide) => {
    const log = new ApiLog();
    const isApiCall = (r: Request) => r.url().startsWith(`${API}/`) && r.method() !== "OPTIONS";
    context.on("request", (r) => {
      // a new document: whatever the old one was waiting for may never report back
      if (r.isNavigationRequest()) log.pending.clear();
      if (isApiCall(r)) log.pending.add(r);
    });
    context.on("requestfinished", (r) => log.pending.delete(r));
    context.on("requestfailed", (r) => log.pending.delete(r));
    context.on("response", (r) => {
      if (isApiCall(r.request())) log.calls.push({ method: r.request().method(), path: new URL(r.url()).pathname.replace(/^\/api/, ""), status: r.status() });
    });
    await provide(log);
  },
  device: async ({ playwright }, provide) => {
    const opened: APIRequestContext[] = [];
    await provide(async () => {
      const client = await playwright.request.newContext({ extraHTTPHeaders: { "X-Forwarded-For": clientAddress() } });
      opened.push(client);
      return client;
    });
    await Promise.all(opened.map((c) => c.dispose()));
  },
  company: async ({ device }, provide) => {
    const api = await device();
    const setup = await (await api.get(`${HOST_ORIGIN}/__test/setup`)).json();
    const login = await api.post(`${API}/auth/login`, { data: { login: setup.adminLogin, password: setup.adminPassword } });
    expect(login.status(), await login.text()).toBe(200);
    await provide(new Company(api, `Bearer ${(await login.json()).accessToken}`));
  },
});

// ── Page helpers ───────────────────────────────────────────────────────────────────────────────────────────────────────

const loginField = (page: Page) => page.getByLabel("Login ou e-mail");
const passwordField = (page: Page) => page.getByLabel("Senha", { exact: true });
const enterButton = (page: Page) => page.getByRole("button", { name: "Entrar", exact: true });
const signOutButton = (page: Page) => page.getByRole("button", { name: "Sair da conta" });
const formError = (page: Page) => page.getByRole("alert");
const isLoginCall = (r: Response) => r.url() === `${API}/auth/login` && r.request().method() === "POST";

async function submitLogin(page: Page, login: string, password: string): Promise<void> {
  await loginField(page).fill(login);
  await passwordField(page).fill(password);
  await enterButton(page).click();
}

/** A real sign-in through the form; a manager of this company (two areas) lands on the choice of area. */
async function signIn(page: Page, account: Account): Promise<void> {
  await page.goto("/login");
  await submitLogin(page, account.login, account.password);
  await expect(page).not.toHaveURL(/\/login/);
}

/** Signed in, on the dashboard, with the background validation of that page load already answered. */
async function signInToDashboard(page: Page, account: Account): Promise<void> {
  await signIn(page, account);
  const validated = page.waitForResponse(`${API}/auth/me`);
  await page.goto("/dashboard");
  expect((await validated).status()).toBe(200);
  await expect(signOutButton(page)).toBeVisible();
}

/** What is in localStorage. In the cookie mode no token is ever there: `NO_SESSION` means the same thing in both modes. */
const storedSession = (page: Page) =>
  page.evaluate(([access, refresh, session]) => ({
    access: localStorage.getItem(access),
    refresh: localStorage.getItem(refresh),
    session: localStorage.getItem(session),
  }), [ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY, SESSION_KEY]);

const setStored = (page: Page, key: string, value: string) =>
  page.evaluate(([k, v]) => localStorage.setItem(k, v), [key, value]);

const sessionCookie = async (context: BrowserContext) => (await context.cookies(HOST_ORIGIN)).find((c) => c.name === SESSION_COOKIE);

/**
 * The tokens of the session, wherever this mode keeps them. In the cookie mode the refresh token is read through the browser's cookies, which a
 * page cannot do, and the access token is the last one the API gave this context, since a variable of the page is out of reach.
 */
async function sessionTokens(page: Page): Promise<{ access: string | null; refresh: string | null; session: string | null }> {
  const stored = await storedSession(page);
  if (!COOKIE_MODE) return stored;
  const context = page.context();
  await readingAnswers.get(context);
  return { access: issuedAccess.get(context) ?? null, refresh: (await sessionCookie(context))?.value ?? null, session: stored.session };
}

/** Puts this refresh token where the session of this mode keeps it. */
async function setRefreshToken(page: Page, token: string): Promise<void> {
  if (!COOKIE_MODE) return setStored(page, REFRESH_TOKEN_KEY, token);
  await page.evaluate((url) => fetch(url, { mode: "no-cors", credentials: "include" }), `${HOST_ORIGIN}/__test/session-cookie?value=${encodeURIComponent(token)}`);
  await expect.poll(async () => (await sessionCookie(page.context()))?.value).toBe(token);
}

/**
 * Swaps the access token of the tabs for the same token, correctly signed, past its expiry. Returns it. In the cookie mode the token is in a
 * variable of each tab: it goes the way a renewal in another tab would, through the channel of the session.
 */
async function expireAccessToken(page: Page, api: APIRequestContext): Promise<string> {
  const { access } = await sessionTokens(page);
  const response = await api.post(`${HOST_ORIGIN}/__test/tokens/expired`, { data: { accessToken: access } });
  const { accessToken } = await response.json();
  if (!COOKIE_MODE) {
    await setStored(page, ACCESS_TOKEN_KEY, accessToken);
    return accessToken;
  }
  await page.evaluate(async ([channel, token]) => {
    const tabs = new BroadcastChannel(channel);
    tabs.postMessage({ type: "token", accessToken: token });
    tabs.close();
    await new Promise((resolve) => setTimeout(resolve, 50));
  }, [SESSION_CHANNEL, accessToken]);
  issuedAccess.set(page.context(), accessToken);
  return accessToken;
}

/** The claims of a token, as anyone holding it can read them. */
const claimsOf = (token: string) => JSON.parse(Buffer.from(token.split(".")[1], "base64url").toString()) as Record<string, string>;

/** Answer of a replaced response on a call with credentials. */
const crossOriginHeaders = (extra: Record<string, string> = {}) => ({
  "Access-Control-Allow-Origin": APP_ORIGIN,
  "Access-Control-Allow-Credentials": "true",
  ...extra,
});

/** A new company, of a person who registers and follows the e-mail link: the same path as a real one, only the e-mail is skipped. */
async function newOwnCompany(api: APIRequestContext, modules: string[] = []): Promise<OwnCompany> {
  const email = `e2e${randomUUID().replaceAll("-", "").slice(0, 12)}@nexo.test`;
  const password = syntheticValue();
  const registered = await api.post(`${API}/auth/register`, { data: { name: "Empresa de teste", email, password } });
  expect(registered.status(), await registered.text()).toBe(200);
  const link = await api.post(`${HOST_ORIGIN}/__test/links/verification`, { data: { email } });
  const { token } = await link.json();
  const verified = await api.get(`${API}/auth/verify-email?token=${token}`);
  expect(verified.status(), await verified.text()).toBe(200);
  const body = await verified.json();
  const session = body.session as { tenantId: string; storeId: string; userId: string; login: string; name: string };
  if (modules.length > 0) {
    const setup = await (await api.get(`${HOST_ORIGIN}/__test/setup`)).json();
    const platform = await api.post(`${API}/platform/auth/login`, { data: { email: setup.platformEmail, password: setup.platformPassword } });
    const headers = { Authorization: `Bearer ${(await platform.json()).accessToken}` };
    for (const moduleKey of modules) {
      const granted = await api.post(`${API}/platform/tenants/${session.tenantId}/modules`, { headers, data: { moduleKey, expiresAt: null, notes: "e2e" } });
      expect(granted.ok(), await granted.text()).toBe(true);
    }
  }
  return {
    account: { id: session.userId, login: session.login, email, password, fullName: session.name, role: "diretoria" },
    tenantId: session.tenantId,
    storeId: session.storeId,
    company: new Company(api, `Bearer ${body.accessToken}`),
  };
}

/** An administrator of the platform only for this test: ending every session of the one in the seed would reach other tests. */
async function newPlatformAccount(api: APIRequestContext): Promise<PlatformAccount> {
  const response = await api.post(`${HOST_ORIGIN}/__test/platform-users`, { data: {} });
  expect(response.status(), await response.text()).toBe(200);
  return response.json();
}

/** More tabs of the same browser on a page, each with the app up. */
async function openTabs(context: BrowserContext, count: number, path: string): Promise<Page[]> {
  const tabs: Page[] = [];
  for (let i = 0; i < count; i++) {
    const tab = await context.newPage();
    await tab.goto(path);
    await expect(signOutButton(tab)).toBeVisible();
    tabs.push(tab);
  }
  return tabs;
}

/** A call of the session as a browser would make it, by hand: the API client of the test framework has no Origin of its own. */
function sessionCall(cookie: string | null, origin: string | null, mode: string | null = "cookie"): Record<string, string> {
  const headers: Record<string, string> = {};
  if (cookie) headers.Cookie = `${SESSION_COOKIE}=${cookie}`;
  if (origin) headers.Origin = origin;
  if (mode) headers[MODE_HEADER] = mode;
  return headers;
}

/** A sign-in in the cookie mode from an API client: the value of the cookie, read off the answer. */
async function cookieSignIn(api: APIRequestContext, account: { login: string; password: string }): Promise<string> {
  const response = await api.post(`${API}/auth/login`, {
    headers: sessionCall(null, APP_ORIGIN),
    data: { login: account.login, password: account.password },
  });
  expect(response.status(), await response.text()).toBe(200);
  const cookie = response.headersArray().find((h) => h.name.toLowerCase() === "set-cookie" && h.value.startsWith(`${SESSION_COOKIE}=`));
  expect(cookie).toBeDefined();
  return cookie!.value.split(";")[0].slice(SESSION_COOKIE.length + 1);
}

/**
 * The session is over and the page has stopped. There is no fixed wait to prove the "stopped": the calls under way are let
 * to finish, the counts are taken, and a full sign-in, several round trips to the server, happens before they are read
 * again. A client that keeps retrying or bouncing between pages adds to them in that time, or never lets the form work.
 */
async function expectSettledOnLogin(page: Page, api: ApiLog, account: Account, url: RegExp, home: string): Promise<void> {
  await expect(page).toHaveURL(url);
  await expect(loginField(page)).toBeEditable();
  expect(await storedSession(page)).toEqual(NO_SESSION);
  await expect.poll(() => api.pending.size).toBe(0);
  const refreshes = api.count("/auth/refresh");
  const refused = api.refused();
  expect(refreshes, "one renewal attempt at most").toBeLessThanOrEqual(1);

  await submitLogin(page, account.login, account.password);
  await expect(page).toHaveURL(`${APP_ORIGIN}${home}`);
  expect(api.count("/auth/refresh"), "no renewal after the session ended").toBe(refreshes);
  expect(api.refused(), "no call refused after the session ended").toBe(refused);
}

/** Turns the second step on, through the API, as the person would on the profile page. */
async function enableMfa(api: APIRequestContext, account: Account): Promise<{ secret: string; recoveryCodes: string[] }> {
  const login = await api.post(`${API}/auth/login`, { data: { login: account.login, password: account.password } });
  const headers = { Authorization: `Bearer ${(await login.json()).accessToken}` };
  const setup = await api.post(`${API}/auth/mfa/setup`, { headers, data: { password: account.password } });
  expect(setup.status(), await setup.text()).toBe(200);
  const { secret } = await setup.json();
  const enabled = await api.post(`${API}/auth/mfa/enable`, { headers, data: { code: totp(secret) } });
  expect(enabled.status(), await enabled.text()).toBe(200);
  return { secret, recoveryCodes: (await enabled.json()).recoveryCodes };
}

/** RFC 6238, as an authenticator app computes it: SHA-1, 30 s, 6 digits. */
function totp(base32Secret: string, stepOffset = 0): string {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const bits = [...base32Secret.replaceAll("=", "")].map((c) => alphabet.indexOf(c).toString(2).padStart(5, "0")).join("");
  const key = Buffer.from((bits.match(/.{8}/g) ?? []).map((byte) => Number.parseInt(byte, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30_000) + stepOffset));
  const digest = createHmac("sha1", key).update(counter).digest();
  const offset = digest[digest.length - 1] & 0xf;
  return String((digest.readUInt32BE(offset) & 0x7fffffff) % 1_000_000).padStart(6, "0");
}

// ── Tests ──────────────────────────────────────────────────────────────────────────────────────────────────────────────

test.describe("Login", () => {
  test("Login with valid credentials succeeds and stores session", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await page.goto("/login");
    await submitLogin(page, user.login, user.password);

    // A manager of a company with two areas (the seed gives it varejo and build) chooses one first.
    await expect(page).toHaveURL(`${APP_ORIGIN}/workspaces`);
    await expect(page.getByRole("heading", { name: "Onde vamos trabalhar?" })).toBeVisible();
    const stored = await sessionTokens(page);
    expect(stored.access).toBeTruthy();
    expect(stored.refresh).toBeTruthy();
    expect(JSON.parse(stored.session ?? "{}")).toMatchObject({ login: user.login, name: user.fullName, role: "gerente", type: "tenant" });
    expect(apiCalls.count("/auth/login", 200)).toBe(1);

    await page.getByRole("button", { name: /Orken Store/ }).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    await expect(signOutButton(page)).toBeVisible();
  });

  for (const { role, home } of [
    { role: "vendedor", home: "/pdv" },
    { role: "estoquista", home: "/estoque" },
  ] as const) {
    test(`Login lands the role ${role} on ${home}`, async ({ page, company }) => {
      const user = await company.createUser(role);
      await page.goto("/login");
      await submitLogin(page, user.login, user.password);
      await expect(page).toHaveURL(`${APP_ORIGIN}${home}`);
    });
  }

  test("Login lands a platform operator on the platform", async ({ page, device }) => {
    const setup = await (await (await device()).get(`${HOST_ORIGIN}/__test/setup`)).json();
    await page.goto("/login");
    await submitLogin(page, setup.platformEmail, setup.platformPassword);
    await expect(page).toHaveURL(`${APP_ORIGIN}/platform`);
    expect(JSON.parse((await storedSession(page)).session ?? "{}")).toMatchObject({ type: "platform" });
  });

  test("Login with invalid password fails with error message", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await page.goto("/login");
    await submitLogin(page, user.login, `errada-${user.password}`);

    await expect(formError(page)).toHaveText(WRONG_CREDENTIALS);
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);
    expect(apiCalls.count("/auth/login", 401)).toBe(1);
    // a wrong password has no session to renew
    await expect.poll(() => apiCalls.count("/auth/refresh")).toBe(FIRST_VISIT_RENEWALS);

    // the form is still usable: the right password goes through
    await passwordField(page).fill(user.password);
    await enterButton(page).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/workspaces`);
  });

  test("Login with non-existent user fails gracefully", async ({ page }) => {
    await page.goto("/login");
    await submitLogin(page, `ninguem${randomInt(1_000_000)}`, syntheticValue());

    // the same answer as a wrong password: the page does not tell which accounts exist
    await expect(formError(page)).toHaveText(WRONG_CREDENTIALS);
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);
  });

  test("Empty fields are refused without calling the API", async ({ page, apiCalls }) => {
    await page.goto("/login");
    await enterButton(page).click();
    await expect(formError(page)).toHaveText("Informe o login ou e-mail.");

    await loginField(page).fill("alguem");
    await enterButton(page).click();
    await expect(formError(page)).toHaveText("Informe a senha.");
    expect(apiCalls.count("/auth/login")).toBe(0);
  });

  // Where the refresh token lives, stated so nobody takes one mode for the other. Without the header of the mode the API writes no cookie at
  // all, and the token is in localStorage. With it, the token is in a cookie no script reads, and the body does not carry it.
  test("The refresh token is where the mode says, and the old cookies are gone", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    await page.goto("/login");
    const [response] = await Promise.all([page.waitForResponse(isLoginCall), submitLogin(page, user.login, user.password)]);
    expect(response.status()).toBe(200);

    const setCookies = (await response.headersArray())
      .filter((h) => h.name.toLowerCase() === "set-cookie")
      .map((h) => h.value);
    expect(setCookies.filter((c) => c.startsWith("nexo_")), "the cookies of older versions").toEqual([]);
    await expect(page).not.toHaveURL(/\/login/);
    const body = await response.json();

    if (!COOKIE_MODE) {
      expect(setCookies).toEqual([]);
      expect(await context.cookies(), "cookies the browser kept").toEqual([]);
      const stored = await storedSession(page);
      expect(stored.access).toBe(body.accessToken);
      expect(stored.refresh).toBe(body.refreshToken);
      return;
    }

    expect(setCookies).toHaveLength(1);
    expect(body).not.toHaveProperty("refreshToken");
    // measured here, since it is what the whole mode stands on: this browser stores a __Host- cookie sent to http://127.0.0.1
    const cookie = await sessionCookie(context);
    expect(cookie).toMatchObject({ httpOnly: true, secure: true, sameSite: "Lax", path: "/", domain: "127.0.0.1" });
    expect(cookie!.expires * 1000).toBeGreaterThan(Date.now() + 6 * 24 * 3600 * 1000);
    expect(await page.evaluate(() => document.cookie)).toBe("");
    const stored = await storedSession(page);
    expect(stored.access).toBeNull();
    expect(stored.refresh).toBeNull();
    expect(stored.session).toBeTruthy();
    expect(await sessionCookie(context)).toBeDefined();
  });

  test("Login error messages do not contain user-supplied XSS", async ({ page }) => {
    const payload = `"><img src=x onerror="window.__xss=1"><script>window.__xss=1</script>`;
    let dialogs = 0;
    page.on("dialog", (d) => { dialogs++; void d.dismiss(); });

    // the real API never echoes the input, so against it this could only pass
    await page.goto("/login");
    await submitLogin(page, payload, syntheticValue());
    await expect(formError(page)).toHaveText(WRONG_CREDENTIALS);

    // the risk is a server message that does echo it: replaced response, and the page must show it as plain text
    await page.route(`${API}/auth/login`, (route) => route.fulfill({
      status: 400,
      headers: crossOriginHeaders(),
      contentType: "application/json",
      body: JSON.stringify({ error: payload }),
    }));
    await enterButton(page).click();
    await expect(formError(page)).toHaveText(payload);
    await expect(page.locator("img[src='x']")).toHaveCount(0);
    expect(await page.evaluate(() => (window as unknown as { __xss?: number }).__xss)).toBeUndefined();
    expect(dialogs).toBe(0);
  });

  test("Network errors display user-friendly message", async ({ page }) => {
    // no server answer to wait for: the connection itself fails
    await page.route("**/api/auth/login", (route) => route.abort("connectionrefused"));
    await page.goto("/login");
    await submitLogin(page, "alguem", syntheticValue());

    await expect(formError(page)).toHaveText("Erro ao conectar com o servidor.");
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
  });
});

test.describe("Session", () => {
  // Replaces "Cookies persist across page navigation". The risk is the same in both modes: losing the session on the way to another page.
  test("Session persists across page navigation", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await sessionTokens(page);
    apiCalls.reset();

    await page.getByRole("link", { name: "Produtos" }).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/produtos`);
    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    expect((await sessionTokens(page)).refresh, "a link inside the app renews nothing").toBe(before.refresh);
    await page.goto("/usuarios");
    await expect(page.getByRole("heading", { name: "Usuários" })).toBeVisible();

    expect(apiCalls.count("/auth/refresh")).toBe(RENEWALS_PER_LOAD);
    const after = await sessionTokens(page);
    if (!COOKIE_MODE) expect(after).toMatchObject({ access: before.access, refresh: before.refresh });
    else expect(after.refresh).not.toBe(before.refresh);
  });

  // Replaces "SSR hydration preserves auth state": the app has no server rendering; a full page load is the same risk.
  test("A full page reload keeps the session", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await sessionTokens(page);
    apiCalls.reset();

    await page.reload();

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    await expect.poll(() => apiCalls.count("/auth/me", 200)).toBe(1);
    expect(apiCalls.count("/auth/refresh", 200)).toBe(RENEWALS_PER_LOAD);
    expect(apiCalls.refused(), "the first call does not go without a token to be refused").toBe(0);
    if (!COOKIE_MODE) expect((await storedSession(page)).access).toBe(before.access);
    else expect((await sessionTokens(page)).access).not.toBe(before.access);
  });

  test("Signed-in user opening the login page goes to their own destination", async ({ page, company }) => {
    const user = await company.createUser("vendedor");
    await signIn(page, user);

    await page.goto("/login");

    await expect(page).toHaveURL(`${APP_ORIGIN}/pdv`);
    await expect(loginField(page)).toHaveCount(0);
  });

  test("API requests carry the access token of the session", async ({ page, company }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);

    // The frontend authenticates with the header: no credentials on this call in either mode.
    const apiRequest = page.waitForRequest((r) => r.url() === `${API}/auth/me` && r.method() === "GET");
    await page.reload();
    const sent = await apiRequest;

    const { access } = await sessionTokens(page);
    expect(await sent.headerValue("authorization")).toBe(`Bearer ${access}`);
    const headers = await sent.allHeaders();
    expect(headers.cookie).toBeUndefined();
    expect(headers[MODE_HEADER]).toBeUndefined();
  });

  test("Login in one tab is reflected in another tab", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);

    // Pages of one context are tabs of one browser: they share the stored session.
    const otherTab = await context.newPage();
    await otherTab.goto("/dashboard");

    await expect(signOutButton(otherTab)).toBeVisible();
    await expect(otherTab).toHaveURL(`${APP_ORIGIN}/dashboard`);
  });

  test("Logout in one tab affects other tabs", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const otherTab = await context.newPage();
    await otherTab.goto("/dashboard");
    await expect(signOutButton(otherTab)).toBeVisible();

    await signOutButton(page).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);

    // no reload: the other tab hears it (the shared storage; in the cookie mode, the channel of the session)
    await expect(otherTab).toHaveURL(SESSION_ENDED_URL);
    await expect(loginField(otherTab)).toBeEditable();
  });
});

test.describe("Route guards", () => {
  test("Accessing protected route without session redirects to login", async ({ page, apiCalls }) => {
    for (const route of ["/dashboard", "/usuarios", "/produtos", "/pdv", "/estoque", "/perfil", "/build", "/platform"]) {
      await test.step(route, async () => {
        await page.goto(route);
        await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
        await expect(loginField(page)).toBeEditable();
      });
    }
    // nothing was fetched on behalf of a session that does not exist; in the cookie mode, once, whether the cookie has one
    expect(apiCalls.calls).toEqual(COOKIE_MODE ? [{ method: "POST", path: "/auth/refresh", status: 401 }] : []);
  });

  test("A forged session in storage does not open a protected route", async ({ page, apiCalls }) => {
    await page.goto("/login");
    await setStored(page, SESSION_KEY, JSON.stringify({ userId: "x", tenantId: "x", name: "Forjada", role: "diretoria", login: "x", modules: ["varejo"], storeIds: [], type: "tenant" }));
    await setStored(page, ACCESS_TOKEN_KEY, syntheticValue());

    await page.goto("/dashboard");

    await expect(page).toHaveURL(/\/login/);
    await expect(loginField(page)).toBeEditable();
    expect(await storedSession(page)).toEqual(NO_SESSION);
    expect(apiCalls.count("/auth/me", 401)).toBe(REFUSED_ME_ON_LOAD);
  });

  for (const { role, home, denied } of [
    { role: "vendedor", home: "/pdv", denied: ["/dashboard", "/usuarios", "/estoque", "/financeiro"] },
    { role: "estoquista", home: "/estoque", denied: ["/dashboard", "/usuarios", "/pdv"] },
  ] as const) {
    test(`The role ${role} does not get the routes of other roles`, async ({ page, company, device }) => {
      const user = await company.createUser(role);
      await signIn(page, user);

      for (const route of denied) {
        await test.step(route, async () => {
          await page.goto(route);
          await expect(page).toHaveURL(`${APP_ORIGIN}${home}`);
        });
      }
      await expect(page.getByRole("heading", { name: "Usuários" })).toHaveCount(0);

      // and the server refuses the data of those screens to the same token
      const { access } = await sessionTokens(page);
      const usersList = await (await device()).get(`${API}/users`, { headers: { Authorization: `Bearer ${access}` } });
      expect(usersList.status()).toBe(403);
    });
  }

  test("A company user does not get the platform area", async ({ page, company }) => {
    const user = await company.createUser("vendedor");
    await signIn(page, user);

    await page.goto("/platform");

    await expect(page).toHaveURL(`${APP_ORIGIN}/pdv`);
  });
});

test.describe("Logout", () => {
  test("Logout clears session and redirects to login", async ({ page, company, device }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await sessionTokens(page);

    const [logout] = await Promise.all([
      page.waitForResponse((r) => r.url() === `${API}/auth/logout` && r.request().method() === "POST"),
      signOutButton(page).click(),
    ]);

    expect(logout.status()).toBe(204);
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);
    expect(await sessionCookie(page.context()), "the browser removed it: the attributes of the removal are right").toBeUndefined();

    // the way back does not reopen it, nor does the address of a protected page
    await page.goBack();
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    await page.goto("/dashboard");
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);

    // the session ended on the server: its refresh token no longer renews anything
    const renewal = await (await device()).post(`${API}/auth/refresh`, { data: { refreshToken: before.refresh } });
    expect(renewal.status()).toBe(401);
  });

  test("Signing out while the session is still being validated stays signed out", async ({ page, company }) => {
    const user = await company.createUser("gerente");
    await signIn(page, user);
    // a slow API: the validation of this page load only gets its (real) answer after the person has signed out
    let release = () => {};
    const held = new Promise<void>((resolve) => { release = resolve; });
    await page.route(`${API}/auth/me`, async (route) => {
      await held;
      await route.continue();
    });
    const validated = page.waitForResponse(`${API}/auth/me`);
    await page.goto("/dashboard");

    await signOutButton(page).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    release();

    // the access token lives on until it expires, so the server confirms it: that must not bring the session back
    expect((await validated).status()).toBe(200);
    await (await validated).finished();
    // a full round trip on the form: a page sent back into the app has no form to answer with
    await submitLogin(page, user.login, `errada-${user.password}`);
    await expect(formError(page)).toHaveText(WRONG_CREDENTIALS);
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);
  });
});

test.describe("Token refresh", () => {
  test("Refresh token prevents constant re-login", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await sessionTokens(page);
    const expired = await expireAccessToken(page, await device());
    apiCalls.reset();

    await page.reload();

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    await expect.poll(async () => (await sessionTokens(page)).access).not.toBe(expired);
    const after = await sessionTokens(page);
    expect(after.access).not.toBe(before.access);
    expect(after.refresh).not.toBe(before.refresh);
    await expect.poll(() => apiCalls.count("/auth/me", 200)).toBe(1);
    expect(apiCalls.count("/auth/me", 401)).toBe(REFUSED_ME_ON_LOAD);
    expect(apiCalls.count("/auth/refresh", 200)).toBe(1);
  });

  test("Session remains valid when token is refreshed in background", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const expired = await expireAccessToken(page, await device());
    apiCalls.reset();

    // the token runs out while the person is on a page: the next screen's calls renew it and still bring the data
    await page.getByRole("link", { name: "Produtos" }).click();

    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    await expect(page.getByText("Erro ao carregar produtos")).toHaveCount(0);
    await expect.poll(() => apiCalls.calls.some((c) => c.path.startsWith("/products") && c.status === 200)).toBe(true);
    await expect(page).toHaveURL(`${APP_ORIGIN}/produtos`);
    expect((await sessionTokens(page)).access).not.toBe(expired);
    // however many calls found the token expired, the session renews once
    expect(apiCalls.count("/auth/refresh", 200)).toBe(1);
  });
});

test.describe("Session ended by the server", () => {
  test("Expired session sends the user back to login", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const api = await device();
    const { refresh } = await sessionTokens(page);
    await expireAccessToken(page, api);
    // signing out on another device of the same session: the refresh token is refused from now on
    const stillValid = await signInOnDevice(api, user);
    await api.post(`${API}/auth/logout`, { headers: { Authorization: `Bearer ${stillValid}` }, data: { refreshToken: refresh } });
    apiCalls.reset();

    await page.reload();

    await expect(page).toHaveURL(SESSION_ENDED_URL);
    await expect(page.getByText("Sua sessão foi encerrada. Entre de novo.")).toBeVisible();
    expect(apiCalls.count("/auth/refresh", 401)).toBe(1);
    await expectSettledOnLogin(page, apiCalls, user, SESSION_ENDED_URL, "/workspaces");
  });

  // Replaces the test of the same risk that deleted a cookie the app does not use and read the answer of a request of its
  // own, which said nothing about the app: here the app's own call meets the 401, in the middle of the work.
  test("401 responses trigger re-login flow", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    await expireAccessToken(page, await device());
    await setRefreshToken(page, syntheticValue());
    apiCalls.reset();

    await page.getByRole("link", { name: "Produtos" }).click();

    await expect(page).toHaveURL(SESSION_ENDED_URL);
    expect(apiCalls.count("/auth/refresh", 401)).toBe(1);
    await expectSettledOnLogin(page, apiCalls, user, SESSION_ENDED_URL, "/workspaces");
  });

  // A thief who renews first leaves the victim with a spent token. The victim only ever sees a race (409), never the reuse: without telling the
  // server, the victim would give up alone and the copy would go on. It is the one moment the server can hear of it.
  test("A token someone else spent ends the session for them too", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const api = await device();
    const { refresh } = await sessionTokens(page);
    const stolen = await api.post(`${API}/auth/refresh`, { data: { refreshToken: refresh } });
    expect(stolen.status(), await stolen.text()).toBe(200);
    const theirs = (await stolen.json()).refreshToken as string;
    await expireAccessToken(page, api);
    apiCalls.reset();

    const signedOut = page.waitForResponse((r) => r.url() === `${API}/auth/logout` && r.request().method() === "POST");
    await page.reload();

    expect((await signedOut).status()).toBe(204);
    await expect(page).toHaveURL(SESSION_ENDED_URL);
    expect(apiCalls.count("/auth/refresh", 409)).toBeGreaterThanOrEqual(1);
    const theirRenewal = await api.post(`${API}/auth/refresh`, { data: { refreshToken: theirs } });
    expect(theirRenewal.status(), "the copy ended with the family").toBe(401);
  });

  test("Revoked session (401 session_revoked) goes to login with the reason and no loop", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    apiCalls.reset();

    // the administrator changes the person's role: every session of theirs ends at once
    await company.update(user, { role: "vendedor" });
    const refused = page.waitForResponse((r) => r.url().startsWith(`${API}/`) && r.status() === 401);
    await page.getByRole("link", { name: "Produtos" }).click();

    expect((await (await refused).json()).code).toBe("session_revoked");
    await expect(page).toHaveURL(SESSION_REVOKED_URL);
    await expect(page.getByText(SESSION_REVOKED_NOTICE)).toBeVisible();
    await expect(signOutButton(page)).toHaveCount(0);
    expect(apiCalls.count("/auth/refresh", 401)).toBe(1);
    // signing in again works and already carries the new role
    await expectSettledOnLogin(page, apiCalls, user, SESSION_REVOKED_URL, "/pdv");
  });

  test("Revoked session found on opening the app goes to login with the reason and no loop", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    await company.update(user, { role: "vendedor" });
    apiCalls.reset();

    await page.reload();

    await expect(page).toHaveURL(SESSION_REVOKED_URL);
    await expect(page.getByText(SESSION_REVOKED_NOTICE)).toBeVisible();
    expect(apiCalls.count("/auth/me", 401)).toBe(REFUSED_ME_ON_LOAD);
    expect(apiCalls.count("/auth/refresh", 401)).toBe(1);
    await expectSettledOnLogin(page, apiCalls, user, SESSION_REVOKED_URL, "/pdv");
  });

  test("A user blocked while signed in loses the session and cannot come back", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    apiCalls.reset();

    await company.update(user, { status: "blocked" });
    await page.getByRole("link", { name: "Produtos" }).click();

    await expect(page).toHaveURL(SESSION_REVOKED_URL);
    await submitLogin(page, user.login, user.password);
    await expect(formError(page)).toHaveText(ACCOUNT_BLOCKED);
    expect(await storedSession(page)).toEqual(NO_SESSION);
    expect(apiCalls.count("/auth/refresh")).toBeLessThanOrEqual(1);
  });
});

/**
 * Only a refusal from the server ends a session: a 401, or a 409 after the wait for the tab that won the race. When the renewal
 * itself gets no verdict (429, 5xx or no connection), the session stays, the screens fail with an error the person can read, and
 * the next renewal waits for what the server asked for. Keeping the session means failing the screens until then, so the limit
 * had to stop being reachable in normal use first: the renewal has its own policy (docs/security/sessao-auditoria-e-migracao.md,
 * section 4), with room for a store behind one address, and what remains over it is abuse or a client in a loop.
 */
test.describe("Failures that say nothing about the session", () => {
  const STILL_ACTIVE = /Sua sessão continua ativa/;
  const WAIT_KEY = "nexo:renewal_wait";
  const CEILING = 900; // RateLimiting:AuthRefresh:PermitLimit, the production default
  const SESSION_BUDGET = 300; // RateLimiting:AuthRefresh:FamilyBudget, the production default
  /** The first call of a page load: the renewal in the cookie mode, where the tab has no token yet. */
  const FIRST_CALL_ON_LOAD = COOKIE_MODE ? `${API}/auth/refresh` : `${API}/auth/me`;

  /** Plain requests from one address: an API client of the test framework records each call, which takes minutes for this many. */
  async function floodRenewals(address: string, total: number): Promise<void> {
    for (let sent = 0; sent < total; sent += 50) {
      const answers = await Promise.all(Array.from({ length: Math.min(50, total - sent) }, () => fetch(`${API}/auth/refresh`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-Forwarded-For": address },
        body: JSON.stringify({ refreshToken: syntheticValue() }),
      })));
      await Promise.all(answers.map((answer) => answer.text()));
    }
  }

  /** How much longer the page is waiting to renew again, in milliseconds. */
  const renewalWaitLeft = (page: Page) => page.evaluate((key) => {
    const stored = localStorage.getItem(key);
    return stored ? (JSON.parse(stored) as { until: number }).until - Date.now() : 0;
  }, WAIT_KEY);

  test("A network failure on opening the app keeps the session", async ({ page, company }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await sessionTokens(page);
    // no server answer to wait for: the connection itself fails
    await page.route(FIRST_CALL_ON_LOAD, (route) => route.abort("connectionrefused"));

    const failed = page.waitForEvent("requestfailed", (r) => r.url() === FIRST_CALL_ON_LOAD);
    await page.reload();
    await failed;

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    expect(await sessionTokens(page)).toMatchObject({ access: before.access, refresh: before.refresh });
  });

  test("A server error on opening the app keeps the session", async ({ page, company }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await sessionTokens(page);
    // replaced response: the real API has no way to fail on demand
    await page.route(FIRST_CALL_ON_LOAD, (route) => route.fulfill({
      status: 500,
      headers: crossOriginHeaders(),
      contentType: "application/json",
      body: JSON.stringify({ error: "Erro interno.", requestId: "e2e" }),
    }));

    const answered = page.waitForResponse(FIRST_CALL_ON_LOAD);
    await page.reload();
    await answered;

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    expect(await sessionTokens(page)).toMatchObject({ access: before.access, refresh: before.refresh });
  });

  test("A 429 on the token renewal keeps the session", async ({ context, page, company, device, apiCalls }) => {
    test.slow(); // the flood below is a thousand real requests through the whole API
    const user = await company.createUser("gerente");
    const address = clientAddress();
    await context.setExtraHTTPHeaders({ "X-Forwarded-For": address });
    await signInToDashboard(page, user);
    const before = await sessionTokens(page);

    // the real limiter: a flood from the same address (a store's network) spends the ceiling of the renewal
    await floodRenewals(address, CEILING);
    const expired = await expireAccessToken(page, await device());
    apiCalls.reset();

    const limited = page.waitForResponse(`${API}/auth/refresh`);
    await page.reload();
    const answer = await limited;

    expect(answer.status()).toBe(429);
    expect(Number(await answer.headerValue("retry-after"))).toBeGreaterThan(0);
    // The app does not read the body of a 429, and the browser tools never hand over an unread answer with no-store: the same limiter, asked directly.
    const refused = await fetch(`${API}/auth/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Forwarded-For": address },
      body: JSON.stringify({ refreshToken: syntheticValue() }),
    });
    expect((await refused.json()).code).toBe("rate_limited");

    // the session stays, and the person is told why the screens are not loading
    await expect(signOutButton(page)).toBeVisible();
    await expect(page.getByText(STILL_ACTIVE)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    expect(await sessionTokens(page)).toMatchObject({ access: expired, refresh: before.refresh, session: before.session });

    // nothing asks again during the wait: not another screen, not a reload, not another tab
    await page.getByRole("link", { name: "Produtos" }).click();
    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    await page.reload();
    await expect(signOutButton(page)).toBeVisible();
    const otherTab = await context.newPage();
    await otherTab.goto("/dashboard");
    await expect(signOutButton(otherTab)).toBeVisible();
    await expect.poll(() => apiCalls.pending.size).toBe(0);
    expect(apiCalls.count("/auth/refresh"), "one renewal, whatever else the app did").toBe(1);
    expect(await sessionTokens(page)).toMatchObject({ refresh: before.refresh });
    await expect(page).toHaveURL(`${APP_ORIGIN}/produtos`);
  });

  test("A session over its own renewal budget gets a 429 and stays, and another session behind the same address renews", async ({ context, page, company, device, playwright, apiCalls }) => {
    test.slow(); // the budget of a session, one real renewal at a time
    const user = await company.createUser("gerente");
    const address = clientAddress();
    await context.setExtraHTTPHeaders({ "X-Forwarded-For": address });
    await signInToDashboard(page, user);

    // a copy of this session renewing in a loop, each time with the token it just got
    const sameNetwork = await playwright.request.newContext({ extraHTTPHeaders: { "X-Forwarded-For": address } });
    let { refresh } = await sessionTokens(page);
    // in the cookie mode the page load of the sign-in already spent one
    for (let renewal = 0; renewal < SESSION_BUDGET - RENEWALS_PER_LOAD; renewal++) {
      const renewed = await sameNetwork.post(`${API}/auth/refresh`, { data: { refreshToken: refresh } });
      expect(renewed.status(), await renewed.text()).toBe(200);
      refresh = (await renewed.json()).refreshToken as string;
    }
    await setRefreshToken(page, refresh!);
    await expireAccessToken(page, await device());
    apiCalls.reset();

    const limited = page.waitForResponse(`${API}/auth/refresh`);
    await page.reload();
    const answer = await limited;

    expect(answer.status()).toBe(429);
    expect(Number(await answer.headerValue("retry-after"))).toBeGreaterThan(0);
    await expect(signOutButton(page)).toBeVisible();
    await expect(page.getByText(STILL_ACTIVE)).toBeVisible();
    expect((await sessionTokens(page)).refresh).toBe(refresh);

    // the budget is of that session: someone else in the same store goes on
    const other = await company.createUser("gerente");
    const signedIn = await (await device()).post(`${API}/auth/login`, { data: { login: other.login, password: other.password } });
    const renewal = await sameNetwork.post(`${API}/auth/refresh`, { data: { refreshToken: (await signedIn.json()).refreshToken } });
    expect(renewal.status(), await renewal.text()).toBe(200);
    await sameNetwork.dispose();
  });

  test("A network failure on the token renewal keeps the session, and it renews when the network is back", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await sessionTokens(page);
    const expired = await expireAccessToken(page, await device());
    await page.route(`${API}/auth/refresh`, (route) => route.abort("connectionrefused"));

    const failed = page.waitForEvent("requestfailed", (r) => r.url() === `${API}/auth/refresh`);
    await page.reload();
    await failed;

    await expect(signOutButton(page)).toBeVisible();
    await expect(page.getByText(STILL_ACTIVE)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    expect(await sessionTokens(page)).toMatchObject({ access: expired, refresh: before.refresh, session: before.session });

    // the network comes back: once the short wait is over, the next screen renews against the real API, with no new sign-in
    await page.unroute(`${API}/auth/refresh`);
    await expect.poll(() => renewalWaitLeft(page)).toBeLessThanOrEqual(0);
    apiCalls.reset();
    await page.getByRole("link", { name: "Produtos" }).click();

    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    await expect.poll(() => apiCalls.calls.some((c) => c.path.startsWith("/products") && c.status === 200)).toBe(true);
    expect(apiCalls.count("/auth/refresh", 200)).toBe(1);
    const after = await sessionTokens(page);
    expect(after.access).not.toBe(expired);
    expect(after.refresh).not.toBe(before.refresh);
    expect(await page.evaluate((key) => localStorage.getItem(key), WAIT_KEY)).toBeNull();
    await expect(page).toHaveURL(`${APP_ORIGIN}/produtos`);
  });

  test("A server error on the token renewal keeps the session and Retry-After is respected", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await sessionTokens(page);
    await expireAccessToken(page, await device());
    // replaced response, once: the real API has no way to fail on demand. It says 3 seconds; then the real API answers.
    const retryAfterSeconds = 3;
    const seen: number[] = [];
    await page.route(`${API}/auth/refresh`, async (route) => {
      if (route.request().method() !== "POST") return route.continue();
      seen.push(Date.now());
      if (seen.length > 1) return route.continue();
      return route.fulfill({
        status: 503,
        headers: crossOriginHeaders({ "Access-Control-Expose-Headers": "Retry-After", "Retry-After": String(retryAfterSeconds) }),
        contentType: "application/json",
        body: JSON.stringify({ error: "Indisponível.", code: "session_renewal_unavailable", retryAfterSeconds }),
      });
    });
    apiCalls.reset();

    const answered = page.waitForResponse(`${API}/auth/refresh`);
    await page.reload();
    expect((await answered).status()).toBe(503);

    await expect(signOutButton(page)).toBeVisible();
    await expect(page.getByText(STILL_ACTIVE)).toBeVisible();
    expect((await sessionTokens(page)).refresh).toBe(before.refresh);

    // during the wait, a screen fails without asking again
    await page.getByRole("link", { name: "Produtos" }).click();
    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    await expect.poll(() => apiCalls.pending.size).toBe(0);
    if (await renewalWaitLeft(page) > 0) expect(seen).toHaveLength(1);

    // after it, the next call renews against the real API
    await expect.poll(() => renewalWaitLeft(page)).toBeLessThanOrEqual(0);
    await page.reload();
    await expect.poll(() => apiCalls.count("/auth/refresh", 200)).toBe(1);
    expect(seen).toHaveLength(2);
    expect(seen[1] - seen[0], "not before the time the server asked for").toBeGreaterThanOrEqual(retryAfterSeconds * 1000);
    await expect(signOutButton(page)).toBeVisible();
    expect((await sessionTokens(page)).refresh).not.toBe(before.refresh);
  });
});

/** A second sign-in of the same person, on another device; returns its access token. */
async function signInOnDevice(api: APIRequestContext, account: Account): Promise<string> {
  const login = await api.post(`${API}/auth/login`, { data: { login: account.login, password: account.password } });
  expect(login.status(), await login.text()).toBe(200);
  return (await login.json()).accessToken;
}

test.describe("Accounts refused at login", () => {
  for (const status of ["blocked", "inactive"] as const) {
    test(`A user with status ${status} is refused`, async ({ page, company, apiCalls }) => {
      const user = await company.createUser("gerente");
      await company.update(user, { status });
      await page.goto("/login");

      await submitLogin(page, user.login, user.password);

      await expect(formError(page)).toHaveText(ACCOUNT_BLOCKED);
      await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
      expect(await storedSession(page)).toEqual(NO_SESSION);
      expect(apiCalls.count("/auth/login", 401)).toBe(1);
    });
  }

  test("An account with an unverified e-mail is refused and told why", async ({ page, device }) => {
    const email = `e2e${randomUUID().replaceAll("-", "").slice(0, 12)}@nexo.test`;
    const password = syntheticValue();
    const registered = await (await device()).post(`${API}/auth/register`, { data: { name: "Empresa de teste", email, password } });
    expect(registered.status(), await registered.text()).toBe(200);
    await page.goto("/login");

    await submitLogin(page, email, password);

    await expect(formError(page)).toHaveText("E-mail não verificado. Verifique sua caixa de entrada.");
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);
  });
});

test.describe("Rate limit", () => {
  const LIMIT = 5; // RateLimiting:AuthLogin:PermitLimit, the production default

  test("429 of the sign-in limiter is shown, and refuses even the right password", async ({ page, browser, company }) => {
    const user = await company.createUser("gerente");
    await page.goto("/login");

    for (let attempt = 1; attempt <= LIMIT; attempt++) {
      const [answer] = await Promise.all([page.waitForResponse(isLoginCall), submitLogin(page, user.login, `errada-${attempt}`)]);
      expect(answer.status()).toBe(401);
      await expect(formError(page)).toHaveText(WRONG_CREDENTIALS);
    }

    const [limited] = await Promise.all([page.waitForResponse(isLoginCall), submitLogin(page, user.login, user.password)]);
    expect(limited.status()).toBe(429);
    expect((await limited.json()).code).toBe("rate_limited");
    expect(Number(await limited.headerValue("retry-after"))).toBeGreaterThan(0);
    await expect(formError(page)).toHaveText("Muitas tentativas. Aguarde um pouco e tente de novo.");
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);

    // the limit is this client's: the same person elsewhere gets in
    const elsewhere = await anotherBrowser(browser);
    const otherPage = await elsewhere.newPage();
    await otherPage.goto("/login");
    await submitLogin(otherPage, user.login, user.password);
    await expect(otherPage).toHaveURL(`${APP_ORIGIN}/workspaces`);
    await elsewhere.close();
  });

  test("429 of a locked account is shown, and refuses even the right password", async ({ page, company, device }) => {
    const user = await company.createUser("gerente");
    // ten wrong passwords lock the account, whichever addresses they come from
    for (let batch = 0; batch < 2; batch++) {
      const api = await device();
      for (let attempt = 0; attempt < LIMIT; attempt++) {
        await api.post(`${API}/auth/login`, { data: { login: user.login, password: syntheticValue() } });
      }
    }
    await page.goto("/login");

    const [locked] = await Promise.all([page.waitForResponse(isLoginCall), submitLogin(page, user.login, user.password)]);

    expect(locked.status()).toBe(429);
    expect((await locked.json()).code).toBe("account_locked");
    await expect(formError(page)).toContainText("Muitas tentativas com senha errada.");
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);
  });
});

test.describe("Two-step verification", () => {
  const codeField = (page: Page) => page.getByLabel("Código do aplicativo autenticador");
  const confirm = (page: Page) => page.getByRole("button", { name: "Confirmar" });

  test("The password alone opens nothing; the code signs in", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    const { secret } = await enableMfa(await device(), user);
    await page.goto("/login");

    await submitLogin(page, user.login, user.password);

    await expect(codeField(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);

    // the code of the next step: the one that turned this on is spent, and the server accepts one step of clock drift
    await codeField(page).fill(totp(secret, 1));
    await confirm(page).click();

    await expect(page).toHaveURL(`${APP_ORIGIN}/workspaces`);
    expect((await sessionTokens(page)).access).toBeTruthy();
    expect((await sessionTokens(page)).refresh).toBeTruthy();
    expect(apiCalls.count("/auth/mfa/verify", 200)).toBe(1);
  });

  test("A wrong code is refused and the step stays open", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    const { secret } = await enableMfa(await device(), user);
    await page.goto("/login");
    await submitLogin(page, user.login, user.password);

    // a code that no step of this window produces
    const wrong = [0, 1, 2, 3].map((d) => String(d).repeat(6)).find((c) => ![-1, 0, 1].some((s) => totp(secret, s) === c))!;
    await codeField(page).fill(wrong);
    await confirm(page).click();

    await expect(formError(page)).toHaveText("Código inválido.");
    await expect(codeField(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);
    // a refused code is not a session to renew
    await expect.poll(() => apiCalls.count("/auth/refresh")).toBe(FIRST_VISIT_RENEWALS);

    await codeField(page).fill(totp(secret, 1));
    await confirm(page).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/workspaces`);
  });

  test("A recovery code signs in, once", async ({ page, company, device, browser }) => {
    const user = await company.createUser("gerente");
    const { recoveryCodes } = await enableMfa(await device(), user);
    await page.goto("/login");
    await submitLogin(page, user.login, user.password);

    await page.getByRole("button", { name: "Usar um código de recuperação" }).click();
    await page.getByLabel("Código de recuperação").fill(recoveryCodes[0]);
    await confirm(page).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/workspaces`);

    const elsewhere = await anotherBrowser(browser);
    const otherPage = await elsewhere.newPage();
    await otherPage.goto("/login");
    await submitLogin(otherPage, user.login, user.password);
    await otherPage.getByRole("button", { name: "Usar um código de recuperação" }).click();
    await otherPage.getByLabel("Código de recuperação").fill(recoveryCodes[0]);
    await confirm(otherPage).click();
    await expect(formError(otherPage)).toHaveText("Código inválido.");
    await elsewhere.close();
  });

  test("A challenge already spent sends the person back to the password", async ({ page, company, device }) => {
    const user = await company.createUser("gerente");
    const { secret } = await enableMfa(await device(), user);
    await page.goto("/login");
    const [firstStep] = await Promise.all([page.waitForResponse(isLoginCall), submitLogin(page, user.login, user.password)]);
    const { mfaChallenge } = await firstStep.json();

    // the same challenge finished elsewhere: a challenge works once
    const finished = await (await device()).post(`${API}/auth/mfa/verify`, { data: { challenge: mfaChallenge, code: totp(secret, 1) } });
    expect(finished.status(), await finished.text()).toBe(200);
    await codeField(page).fill("000000");
    await confirm(page).click();

    await expect(formError(page)).toHaveText("A verificação expirou. Entre com sua senha de novo.");
    await expect(passwordField(page)).toBeEditable();
    await expect(passwordField(page)).toHaveValue("");
    expect(await storedSession(page)).toEqual(NO_SESSION);
  });

  test("Going back from the code step returns to the password form", async ({ page, company, device }) => {
    const user = await company.createUser("gerente");
    await enableMfa(await device(), user);
    await page.goto("/login");
    await submitLogin(page, user.login, user.password);
    await expect(codeField(page)).toBeVisible();

    await page.getByRole("button", { name: "Voltar" }).click();

    await expect(enterButton(page)).toBeVisible();
    await expect(codeField(page)).toHaveCount(0);
    expect(await storedSession(page)).toEqual(NO_SESSION);
  });
});

// ── The two modes side by side (docs/security/sessao-auditoria-e-migracao.md, section 6) ──────────────────────────────────

test.describe("The mode under test", () => {
  test("The bundle is the one of this run", async ({ page, company }) => {
    const user = await company.createUser("gerente");
    await page.goto("/login");

    const sent = page.waitForRequest((r) => r.url() === `${API}/auth/login` && r.method() === "POST");
    await submitLogin(page, user.login, user.password);

    expect((await (await sent).allHeaders())[MODE_HEADER]).toBe(COOKIE_MODE ? "cookie" : undefined);
  });
});

test.describe("Several tabs", () => {
  test("Reloading every tab at once keeps the session, round after round", async ({ context, page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const tabs = [page, ...await openTabs(context, 3, "/dashboard")];

    for (let round = 0; round < 3; round++) {
      apiCalls.reset();
      await Promise.all(tabs.map((tab) => tab.reload()));
      for (const tab of tabs) {
        await expect(signOutButton(tab)).toBeVisible();
        await expect(tab).toHaveURL(`${APP_ORIGIN}/dashboard`);
      }
      await expect.poll(() => apiCalls.pending.size).toBe(0);
      expect(apiCalls.refused(), "nothing refused: no token spent twice and no family revoked").toBe(0);
      expect(apiCalls.count("/auth/refresh", 409), "the lock keeps the tabs from racing").toBe(0);
      expect(apiCalls.count("/auth/refresh")).toBeLessThanOrEqual(tabs.length * RENEWALS_PER_LOAD);
    }

    // the session is still good on the server: a new call goes through and a new renewal too
    await page.reload();
    await expect.poll(() => apiCalls.count("/auth/me", 200)).toBeGreaterThan(0);
    await expect(signOutButton(page)).toBeVisible();
  });

  test("A token that expired in every tab renews at once with no family revoked", async ({ context, page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const tabs = [page, ...await openTabs(context, 3, "/dashboard")];
    const expired = await expireAccessToken(page, await device());
    apiCalls.reset();

    await Promise.all(tabs.map((tab) => tab.getByRole("link", { name: "Produtos" }).click()));

    for (const tab of tabs) {
      await expect(tab.getByRole("heading", { name: "Produtos" })).toBeVisible();
      await expect(tab.getByText("Erro ao carregar produtos")).toHaveCount(0);
      await expect(tab).toHaveURL(`${APP_ORIGIN}/produtos`);
    }
    await expect.poll(() => apiCalls.pending.size).toBe(0);
    expect(apiCalls.count("/auth/refresh", 401)).toBe(0);
    expect(apiCalls.count("/auth/refresh", 200)).toBeGreaterThanOrEqual(1);
    if (COOKIE_MODE) {
      expect(apiCalls.count("/auth/refresh", 200), "the tabs in line take the token of the first one").toBe(1);
      expect(apiCalls.count("/auth/refresh", 409), "in line, no tab presents a cookie already replaced").toBe(0);
    }
    expect((await sessionTokens(page)).access).not.toBe(expired);

    // and the session goes on: each tab reloads and works
    apiCalls.reset();
    await Promise.all(tabs.map((tab) => tab.reload()));
    for (const tab of tabs) await expect(signOutButton(tab)).toBeVisible();
    expect(apiCalls.count("/auth/refresh", 401)).toBe(0);
  });

  test("A sign-in reaches a tab that was already open", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    const otherTab = await context.newPage();
    await otherTab.goto("/login");
    await expect(loginField(otherTab)).toBeEditable();

    await signIn(page, user);

    // The normal mode has never told an open tab: the storage is shared, and the tab finds the session the next time it loads.
    if (!COOKIE_MODE) await otherTab.reload();
    await expect(otherTab).not.toHaveURL(/\/login/);
    await otherTab.getByRole("button", { name: /Orken Store/ }).click();
    await expect(otherTab).toHaveURL(`${APP_ORIGIN}/dashboard`);
    await expect(signOutButton(otherTab)).toBeVisible();
  });

  test("A sign-out reaches every tab, and a reload in any of them stays out", async ({ context, page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const tabs = await openTabs(context, 2, "/dashboard");
    apiCalls.reset();

    await signOutButton(page).click();

    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    for (const tab of tabs) await expect(tab).toHaveURL(SESSION_ENDED_URL);
    await expect.poll(() => sessionCookie(context), "the answer of the sign-out removes it").toBeUndefined();
    await Promise.all([page, ...tabs].map((tab) => tab.reload()));
    for (const tab of [page, ...tabs]) await expect(loginField(tab)).toBeEditable();
    await expect.poll(() => apiCalls.pending.size).toBe(0);
    expect(apiCalls.count("/auth/refresh"), "nothing tries to renew after the sign-out").toBe(0);
  });

  test("The new store holds in the other tabs and survives the renewal", async ({ context, page, device }) => {
    const api = await device();
    const { account, tenantId, storeId } = await newOwnCompany(api);
    const added = await api.post(`${HOST_ORIGIN}/__test/stores`, { data: { tenantId, name: "Segunda Loja" } });
    const second = (await added.json()).id as string;
    await signInToDashboard(page, account);
    const [otherTab] = await openTabs(context, 1, "/dashboard");
    const sentBy = (tab: Page) => tab.waitForRequest((r) => r.url().startsWith(`${API}/`) && r.method() === "GET" && !!r.headers().authorization);
    const storeOf = (authorization: string) => claimsOf(authorization.replace("Bearer ", "")).storeId;

    await page.getByRole("button", { name: "Loja Principal" }).click();
    const [switched] = await Promise.all([
      page.waitForResponse((r) => r.url() === `${API}/auth/switch-store` && r.request().method() === "POST"),
      page.getByRole("menuitem", { name: /Segunda Loja/ }).click(),
    ]);
    expect(switched.status(), await switched.text()).toBe(200);
    const moved = await switched.json();
    expect(claimsOf(moved.accessToken).storeId).toBe(second);
    if (COOKIE_MODE) {
      expect(moved).not.toHaveProperty("refreshToken");
      // no reload: the seat of the other tab already shows it
      await expect(otherTab.getByRole("button", { name: "Segunda Loja" })).toBeVisible();
    }

    // the other tab goes on with the new store, with no reload
    const fromOtherTab = sentBy(otherTab);
    await otherTab.getByRole("link", { name: "Produtos" }).click();
    expect(storeOf((await fromOtherTab).headers().authorization)).toBe(second);
    expect(second).not.toBe(storeId);

    // the renewal keeps it, in the token and after a reload of each tab
    await expireAccessToken(otherTab, api);
    const renewal = otherTab.waitForResponse((r) => r.url() === `${API}/auth/refresh` && r.request().method() === "POST" && r.status() === 200);
    await otherTab.getByRole("link", { name: "Clientes" }).click();
    expect(claimsOf((await (await renewal).json()).accessToken).storeId).toBe(second);
    await Promise.all([page.reload(), otherTab.reload()]);
    await expect(signOutButton(page)).toBeVisible();
    expect(JSON.parse((await storedSession(otherTab)).session ?? "{}").storeId).toBe(second);
  });
});

test.describe("Access of the platform to a company", () => {
  test("It stays in its tab, sends no cookie, and leaving does not touch the administrator", async ({ context, page, device }) => {
    const api = await device();
    const { tenantId, account } = await newOwnCompany(api);
    const operator = await newPlatformAccount(api);
    await page.goto("/login");
    await submitLogin(page, operator.email, operator.password);
    await expect(page).toHaveURL(`${APP_ORIGIN}/platform`);

    await page.goto(`/platform/tenants/${tenantId}`);
    await expect(page.getByRole("button", { name: "Entrar como cliente" })).toBeVisible();
    // from here on nothing in this tab loads again: if the cookie changes, it was the other tab
    const administrator = await sessionTokens(page);
    await page.getByRole("button", { name: "Entrar como cliente" }).click();
    await page.getByLabel("Motivo do acesso").fill("Teste de sessão, sem dado real");
    const [tab] = await Promise.all([
      context.waitForEvent("page"),
      page.getByRole("dialog").getByRole("button", { name: "Entrar como cliente" }).click(),
    ]);
    await expect(tab).not.toHaveURL(/\/impersonate/);
    await expect(tab.getByText(account.fullName).first()).toBeVisible();

    // its calls carry the token of this access and nothing of the administrator
    const call = tab.waitForRequest((r) => r.url() === `${API}/auth/me` && r.method() === "GET");
    await tab.reload();
    const headers = await (await call).allHeaders();
    expect(headers.cookie, "the cookie of the administrator").toBeUndefined();
    expect(headers[MODE_HEADER]).toBeUndefined();
    expect(claimsOf(headers.authorization.replace("Bearer ", "")).tenantId).toBe(tenantId);
    // the residual risk the document accepts: this short access is in the storage of this tab, and only of this tab
    expect(await tab.evaluate((key) => sessionStorage.getItem(key), ACCESS_TOKEN_KEY)).toBeTruthy();
    expect(await tab.evaluate((key) => localStorage.getItem(key), ACCESS_TOKEN_KEY)).toBe(COOKIE_MODE ? null : administrator.access);
    keepsOldTokens.add(context);
    await tab.close();

    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}`));
    const stillThere = await sessionTokens(page);
    expect(stillThere.refresh, "nothing in that tab touched the session of the administrator").toBe(administrator.refresh);
  });

  test("Leaving the access ends that tab only", async ({ context, page, device }) => {
    const api = await device();
    const { tenantId } = await newOwnCompany(api);
    const operator = await newPlatformAccount(api);
    await page.goto("/login");
    await submitLogin(page, operator.email, operator.password);
    await expect(page).toHaveURL(`${APP_ORIGIN}/platform`);

    await page.goto(`/platform/tenants/${tenantId}`);
    await expect(page.getByRole("button", { name: "Entrar como cliente" })).toBeVisible();
    const before = await sessionTokens(page);
    await page.getByRole("button", { name: "Entrar como cliente" }).click();
    await page.getByLabel("Motivo do acesso").fill("Teste de sessão, sem dado real");
    const [tab] = await Promise.all([
      context.waitForEvent("page"),
      page.getByRole("dialog").getByRole("button", { name: "Entrar como cliente" }).click(),
    ]);
    await expect(tab).not.toHaveURL(/\/impersonate/);
    keepsOldTokens.add(context);

    const ended = tab.waitForRequest((r) => r.url() === `${API}/auth/impersonation/end` && r.method() === "POST");
    await tab.getByRole("button", { name: "Sair do acesso" }).click();
    expect((await (await ended).allHeaders()).cookie).toBeUndefined();
    // a tab opened by the platform closes by itself; if it could not, it says the access ended
    await expect.poll(() => tab.isClosed() || tab.url().includes("/acesso-encerrado")).toBe(true);

    expect((await sessionTokens(page)).refresh).toBe(before.refresh);
    await page.reload();
    await expect(page).toHaveURL(new RegExp(`/platform/tenants/${tenantId}`));
    await expect(page.getByRole("button", { name: "Entrar como cliente" })).toBeVisible();
  });
});

test.describe("The platform", () => {
  test("Its session renews, and revoking it ends it with no loop", async ({ page, device, apiCalls }) => {
    const api = await device();
    const operator = await newPlatformAccount(api);
    await page.goto("/login");
    await submitLogin(page, operator.email, operator.password);
    await expect(page).toHaveURL(`${APP_ORIGIN}/platform`);
    const { refresh } = await sessionTokens(page);
    expect(refresh).toMatch(/^p\./);

    await expireAccessToken(page, api);
    apiCalls.reset();
    await page.reload();
    await expect(page).toHaveURL(`${APP_ORIGIN}/platform`);
    await expect.poll(() => apiCalls.count("/auth/refresh", 200)).toBe(1);
    await expect.poll(() => apiCalls.count("/auth/me", 200)).toBe(1);
    const renewed = await sessionTokens(page);
    expect(renewed.refresh).toMatch(/^p\./);
    expect(renewed.refresh).not.toBe(refresh);

    // every session of this administrator ends, from another device
    const elsewhere = await api.post(`${API}/platform/auth/login`, { data: { email: operator.email, password: operator.password } });
    const revoked = await api.post(`${API}/platform/auth/sessions/revoke-all`, { headers: { Authorization: `Bearer ${(await elsewhere.json()).accessToken}` } });
    expect(revoked.status()).toBe(204);
    apiCalls.reset();

    await page.reload();

    await expect(page).toHaveURL(SESSION_ENDED_URL);
    expect(apiCalls.count("/auth/refresh")).toBeLessThanOrEqual(1);
    await expect(loginField(page)).toBeEditable();
    expect(await storedSession(page)).toEqual(NO_SESSION);
  });

  test("Two-step verification signs an administrator in through the form", async ({ page, device }) => {
    const api = await device();
    const operator = await newPlatformAccount(api);
    const login = await api.post(`${API}/platform/auth/login`, { data: { email: operator.email, password: operator.password } });
    const headers = { Authorization: `Bearer ${(await login.json()).accessToken}` };
    const setup = await api.post(`${API}/platform/auth/mfa/setup`, { headers, data: { password: operator.password } });
    const { secret } = await setup.json();
    const enabled = await api.post(`${API}/platform/auth/mfa/enable`, { headers, data: { code: totp(secret) } });
    expect(enabled.status(), await enabled.text()).toBe(200);

    await page.goto("/login");
    await submitLogin(page, operator.email, operator.password);
    await page.getByLabel("Código do aplicativo autenticador").fill(totp(secret, 1));
    await page.getByRole("button", { name: "Confirmar" }).click();

    await expect(page).toHaveURL(`${APP_ORIGIN}/platform`);
    expect((await sessionTokens(page)).refresh).toMatch(/^p\./);
    await page.reload();
    await expect(page).toHaveURL(`${APP_ORIGIN}/platform`);
    await expect(loginField(page)).toHaveCount(0);
  });

  test("Signing out of the platform ends the session on the server", async ({ page, device }) => {
    const api = await device();
    const operator = await newPlatformAccount(api);
    await page.goto("/login");
    await submitLogin(page, operator.email, operator.password);
    await expect(page).toHaveURL(`${APP_ORIGIN}/platform`);
    const { refresh } = await sessionTokens(page);

    const signedOut = page.waitForResponse((r) => r.url() === `${API}/auth/logout` && r.request().method() === "POST");
    await page.getByRole("button", { name: "Sair", exact: true }).first().click();

    expect((await signedOut).status()).toBe(204);
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    const renewal = await api.post(`${API}/platform/auth/refresh`, { data: { refreshToken: refresh } });
    expect(renewal.status()).toBe(401);
  });
});

test.describe("New password and e-mail link", () => {
  test("The link of a new password ends the sessions, and the new password gets in", async ({ page, browser, company, device }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const link = await (await device()).post(`${HOST_ORIGIN}/__test/links/password`, { data: { email: user.email } });
    const { token } = await link.json();

    const elsewhere = await anotherBrowser(browser);
    const otherPage = await elsewhere.newPage();
    await otherPage.goto(`/redefinir-senha?token=${token}`);
    const password = syntheticValue();
    await otherPage.locator("#new-password").fill(password);
    await otherPage.locator("#confirm-password").fill(password);
    await otherPage.getByRole("button", { name: /Salvar|Redefinir|Criar|Alterar/ }).click();
    await expect(otherPage).toHaveURL(/\/login\?motivo=senha-redefinida/);
    await submitLogin(otherPage, user.login, password);
    await expect(otherPage).toHaveURL(`${APP_ORIGIN}/workspaces`);
    await elsewhere.close();

    // the session that was open ended, whichever way it hears of it
    await page.reload();
    await expect(page).toHaveURL(SESSION_REVOKED_URL);
  });

  test("The link of a new e-mail signs in, and the session holds on a reload", async ({ context, page, device }) => {
    const email = `e2e${randomUUID().replaceAll("-", "").slice(0, 12)}@nexo.test`;
    const api = await device();
    await api.post(`${API}/auth/register`, { data: { name: "Empresa de teste", email, password: syntheticValue() } });
    const { token } = await (await api.post(`${HOST_ORIGIN}/__test/links/verification`, { data: { email } })).json();

    const answered = page.waitForResponse((r) => r.url().startsWith(`${API}/auth/verify-email`));
    await page.goto(`/verify-email?token=${token}`);
    const answer = await answered;

    expect(answer.status(), await answer.text()).toBe(200);
    await expect(page).not.toHaveURL(/\/verify-email/);
    if (COOKIE_MODE) {
      expect(await answer.json()).not.toHaveProperty("refreshToken");
      expect(await sessionCookie(context)).toBeDefined();
    } else {
      expect((await storedSession(page)).refresh).toBeTruthy();
    }
    await page.reload();
    await expect(page).not.toHaveURL(/\/login/);
  });

  test("A link already used signs nobody in", async ({ page, device }) => {
    const email = `e2e${randomUUID().replaceAll("-", "").slice(0, 12)}@nexo.test`;
    const api = await device();
    await api.post(`${API}/auth/register`, { data: { name: "Empresa de teste", email, password: syntheticValue() } });
    const { token } = await (await api.post(`${HOST_ORIGIN}/__test/links/verification`, { data: { email } })).json();
    expect((await api.get(`${API}/auth/verify-email?token=${token}`)).status()).toBe(200);

    await page.goto(`/verify-email?token=${token}`);

    await expect(page.getByText(/inválido|expirado/i).first()).toBeVisible();
    expect(await storedSession(page)).toEqual(NO_SESSION);
    expect(await sessionCookie(page.context())).toBeUndefined();
  });
});

test.describe("A download", () => {
  test("It goes with the token and carries no cookie", async ({ page, company, device }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    await page.goto("/relatorios");
    const exportButton = page.getByRole("button", { name: "Exportar planilha" });
    const exported = async () => {
      const sent = page.waitForRequest((r) => r.url().includes("/reports/sales/export") && r.method() === "GET");
      const [download] = await Promise.all([page.waitForEvent("download"), exportButton.click()]);
      expect(download.suggestedFilename()).toMatch(/^vendas_.*\.csv$/);
      return (await sent).allHeaders();
    };

    const headers = await exported();

    expect(headers.authorization).toMatch(/^Bearer /);
    expect(headers.cookie).toBeUndefined();
    expect(headers[MODE_HEADER]).toBeUndefined();

    // This call has no renewal of its own. In the cookie mode it gets a good token first; the normal mode has always failed here.
    if (!COOKIE_MODE) return;
    const expired = await expireAccessToken(page, await device());
    const again = await exported();
    expect(again.authorization).not.toBe(`Bearer ${expired}`);
    expect(again.cookie).toBeUndefined();
  });
});

test.describe("The hub of the kitchen", () => {
  test("It connects after a reload, reconnects with a token renewed through the session, and carries no cookie", async ({ page, device, apiCalls }) => {
    const api = await device();
    const { account } = await newOwnCompany(api, ["restaurante"]);
    await page.goto("/login");
    await submitLogin(page, account.login, account.password);
    await expect(page).not.toHaveURL(/\/login/);

    const sockets: string[] = [];
    page.on("websocket", (socket) => sockets.push(socket.url()));
    const negotiations: Promise<Record<string, string>>[] = [];
    page.on("request", (r) => {
      if (r.url().includes("/hubs/restaurant/negotiate") && r.method() === "POST") negotiations.push(r.allHeaders());
    });

    // a full load: in the cookie mode the tab starts with no token, and the factory of the connection fetches the first one
    await page.goto("/restaurante/cozinha");
    await expect(page.getByText("Tempo real")).toBeVisible();
    await expect.poll(() => sockets.length).toBeGreaterThan(0);
    await expect.poll(() => negotiations.length).toBeGreaterThan(0);
    const first = await negotiations[0];
    expect(first.authorization).toMatch(/^Bearer /);
    expect(first.cookie, "the hub authenticates with the token, not with the cookie").toBeUndefined();
    expect(first[MODE_HEADER]).toBeUndefined();

    // the token runs out and the connection drops: it comes back by itself with a new one
    const expired = await expireAccessToken(page, api);
    apiCalls.reset();
    const connected = sockets.length;
    await api.post(`${HOST_ORIGIN}/__test/hub/drop`);

    await expect.poll(() => sockets.length, { timeout: 30_000 }).toBeGreaterThan(connected);
    await expect(page.getByText("Tempo real")).toBeVisible();
    await expect.poll(() => apiCalls.count("/auth/refresh", 200)).toBeGreaterThanOrEqual(1);
    const reconnectedWith = new URL(sockets[sockets.length - 1]).searchParams.get("access_token");
    expect(reconnectedWith).toBeTruthy();
    expect(reconnectedWith).not.toBe(expired);
    expect(reconnectedWith).toBe((await sessionTokens(page)).access);
  });

  test("A session ended by the server closes the connection and the page leaves", async ({ page, device }) => {
    const api = await device();
    const { company } = await newOwnCompany(api, ["restaurante"]);
    const user = await company.createUser("gerente");
    await page.goto("/login");
    await submitLogin(page, user.login, user.password);
    await expect(page).not.toHaveURL(/\/login/);
    // the connection really open: the screen already says "Tempo real" while it is on its way
    const opening = page.waitForEvent("websocket");
    await page.goto("/restaurante/cozinha");
    await (await opening).waitForEvent("framereceived");

    await company.update(user, { role: "vendedor" });

    await expect(page).toHaveURL(SESSION_REVOKED_URL);
    await expect(page.getByText(SESSION_REVOKED_NOTICE)).toBeVisible();
  });
});

test.describe("Migration of a session still in localStorage", () => {
  test.skip(!COOKIE_MODE, "there is nothing to migrate in the normal mode: its tokens stay in localStorage, which the other tests of this run check");

  /** What the bundle of today leaves in a browser: both tokens and the session, read off a sign-in with no header. */
  async function seedOldSession(page: Page, api: APIRequestContext, account: Account): Promise<{ access: string; refresh: string }> {
    const login = await api.post(`${API}/auth/login`, { data: { login: account.login, password: account.password } });
    const body = await login.json();
    expect(body.refreshToken).toBeTruthy();
    const dto = body.session;
    const session = { ...dto, modules: dto.activeModules ?? [], readOnlyModules: dto.readOnlyModules ?? [], storeIds: dto.storeIds ?? [] };
    await page.goto("/esqueci-senha");
    await page.evaluate(([accessKey, access, refreshKey, refresh, sessionKey, json]) => {
      localStorage.setItem(accessKey, access);
      localStorage.setItem(refreshKey, refresh);
      localStorage.setItem(sessionKey, json);
    }, [ACCESS_TOKEN_KEY, body.accessToken, REFRESH_TOKEN_KEY, body.refreshToken, SESSION_KEY, JSON.stringify(session)]);
    return { access: body.accessToken, refresh: body.refreshToken };
  }

  test("It becomes a cookie with no new sign-in, and only a 200 deletes the tokens", async ({ context, page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    const api = await device();
    const old = await seedOldSession(page, api, user);
    apiCalls.reset();

    const sent = page.waitForRequest((r) => r.url() === `${API}/auth/refresh` && r.method() === "POST");
    const answered = page.waitForResponse((r) => r.url() === `${API}/auth/refresh` && r.request().method() === "POST");
    await page.goto("/dashboard");
    const request = await sent;
    const answer = await answered;

    expect(request.postDataJSON()).toEqual({ refreshToken: old.refresh });
    expect((await request.allHeaders())[MODE_HEADER]).toBe("cookie");
    expect(answer.status(), await answer.text()).toBe(200);
    expect(await answer.json()).not.toHaveProperty("refreshToken");
    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    await expect.poll(async () => (await storedSession(page)).refresh).toBeNull();
    expect((await storedSession(page)).access).toBeNull();
    expect((await storedSession(page)).session).toBeTruthy();
    const cookie = await sessionCookie(context);
    expect(cookie?.value).toBeTruthy();
    expect(cookie?.value).not.toBe(old.refresh);

    // once: the next load goes by the cookie, with no body
    const next = page.waitForRequest((r) => r.url() === `${API}/auth/refresh` && r.method() === "POST");
    await page.reload();
    expect((await next).postData()).toBeNull();
    await expect(signOutButton(page)).toBeVisible();
    expect(apiCalls.refused()).toBe(0);
  });

  test("Without a verdict both tokens stay, the session goes on, and it migrates later", async ({ context, page, company, device }) => {
    keepsOldTokens.add(context);
    const user = await company.createUser("gerente");
    const old = await seedOldSession(page, await device(), user);
    // replaced response, once: the real API has no way to fail on demand
    let attempts = 0;
    await page.route(`${API}/auth/refresh`, (route) => {
      attempts++;
      if (attempts > 1) return route.continue();
      return route.fulfill({
        status: 503,
        headers: crossOriginHeaders({ "Access-Control-Expose-Headers": "Retry-After", "Retry-After": "1" }),
        contentType: "application/json",
        body: JSON.stringify({ error: "Indisponível.", code: "session_renewal_unavailable" }),
      });
    });

    const answered = page.waitForResponse(`${API}/auth/refresh`);
    await page.goto("/dashboard");
    expect((await answered).status()).toBe(503);

    await expect(signOutButton(page)).toBeVisible();
    expect(await storedSession(page)).toMatchObject({ access: old.access, refresh: old.refresh });
    expect(await sessionCookie(context)).toBeUndefined();
    await expect.poll(() => page.evaluate(() => {
      const wait = localStorage.getItem("nexo:renewal_wait");
      return wait ? (JSON.parse(wait) as { until: number }).until - Date.now() : 0;
    })).toBeLessThanOrEqual(0);

    const migrated = page.waitForResponse((r) => r.url() === `${API}/auth/refresh` && r.status() === 200);
    await page.reload();
    await migrated;
    await expect.poll(async () => (await storedSession(page)).refresh).toBeNull();
    expect(await sessionCookie(context)).toBeDefined();
    await expect(signOutButton(page)).toBeVisible();
  });

  test("Several tabs migrating at once revoke nothing", async ({ context, page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await seedOldSession(page, await device(), user);
    const others = [await context.newPage(), await context.newPage(), await context.newPage()];
    apiCalls.reset();

    await Promise.all([page, ...others].map((tab) => tab.goto("/dashboard")));

    for (const tab of [page, ...others]) await expect(signOutButton(tab)).toBeVisible();
    await expect.poll(() => apiCalls.pending.size).toBe(0);
    expect(apiCalls.count("/auth/refresh", 401)).toBe(0);
    await expect.poll(async () => (await storedSession(page)).refresh).toBeNull();
    await page.reload();
    await expect(signOutButton(page)).toBeVisible();
  });

  test("A token in localStorage the server refuses ends the session and takes both", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await seedOldSession(page, await device(), user);
    await setStored(page, REFRESH_TOKEN_KEY, syntheticValue());
    apiCalls.reset();

    await page.goto("/dashboard");

    await expect(page).toHaveURL(SESSION_ENDED_URL);
    expect(await storedSession(page)).toEqual(NO_SESSION);
    expect(apiCalls.count("/auth/refresh", 401)).toBe(1);
  });
});

/**
 * The host of the public portals. In the cookie mode only the app of the ERP has authority over the cookie, so a page that needs a session goes there, and a
 * portal stays. The names are the ones of production; the browser of the suite gets the bundle under test for them, and the app on the other side is a stand-in.
 */
test.describe("The host of the public portals", () => {
  const WWW = `http://www.orken.com.br:${COOKIE_MODE ? 5198 : 5197}`;
  const ERP = "https://erp.orken.com.br";

  async function serveBundleAsWww(context: BrowserContext): Promise<void> {
    await context.route(`${WWW}/**`, async (route) => {
      const response = await route.fetch({ url: route.request().url().replace(WWW, APP_ORIGIN) });
      await route.fulfill({ response });
    });
    await context.route(`${ERP}/**`, (route) => route.fulfill({ status: 200, contentType: "text/html", body: "<!doctype html><title>erp</title>" }));
  }

  for (const path of ["/login?motivo=sessao-encerrada", "/dashboard", "/verify-email?token=abc", "/redefinir-senha?token=abc"]) {
    test(`${path} ${COOKIE_MODE ? "goes to the app of the ERP" : "stays where it is"}`, async ({ context, page, apiCalls }) => {
      await serveBundleAsWww(context);

      await page.goto(`${WWW}${path}`);

      if (COOKIE_MODE) {
        await expect(page).toHaveURL(`${ERP}${path}`);
      } else {
        await expect(page).toHaveURL(new RegExp(`^${WWW.replaceAll(".", "\\.")}`));
        await page.waitForLoadState("networkidle");
      }
      expect(apiCalls.count("/auth/refresh"), "no renewal from this host").toBe(0);
    });
  }

  test("A public page stays on it and asks nothing about the session", async ({ context, page, apiCalls }) => {
    await serveBundleAsWww(context);

    await page.goto(`${WWW}/agendar/empresa-que-nao-existe`);
    await page.waitForLoadState("networkidle");

    await expect(page).toHaveURL(`${WWW}/agendar/empresa-que-nao-existe`);
    expect(apiCalls.count("/auth/refresh")).toBe(0);
    expect(apiCalls.count("/auth/me")).toBe(0);
    expect((await context.cookies(HOST_ORIGIN)).filter((c) => c.name === SESSION_COOKIE)).toEqual([]);
  });
});

test.describe("A page of another site", () => {
  const SIBLING = "https://evil.orken.test:5199";
  test.use({ ignoreHTTPSErrors: true });

  /** What any page can do with no preflight, in batches. The browser adds the Origin. It cannot read the answers and does not need to. */
  const flood = (page: Page, url: string, total: number) => page.evaluate(async ({ url, total }) => {
    for (let sent = 0; sent < total; sent += 20) {
      await Promise.all(Array.from({ length: Math.min(20, total - sent) }, () =>
        fetch(url, { method: "POST", mode: "no-cors", credentials: "include", headers: { "Content-Type": "text/plain" }, body: "x" }).catch(() => undefined)));
    }
  }, { url, total });

  test("It spends nothing of the sign-in limit of whoever opened it", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    const attacker = await context.newPage();
    await attacker.goto(SIBLING);

    await flood(attacker, `${API}/auth/login`, 30);   // the limit is 5

    await page.goto("/login");
    await submitLogin(page, user.login, user.password);
    await expect(page).toHaveURL(`${APP_ORIGIN}/workspaces`);
  });

  test("It spends nothing of the renewal limit of whoever opened it", async ({ context, page, company, device, apiCalls }) => {
    test.slow(); // past the ceiling of the renewal, a thousand real requests through the whole API
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const attacker = await context.newPage();
    await attacker.goto(SIBLING);

    await flood(attacker, `${API}/auth/refresh`, 950);   // the ceiling is 900

    await expireAccessToken(page, await device());
    apiCalls.reset();
    await page.getByRole("link", { name: "Produtos" }).click();
    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    await expect.poll(() => apiCalls.count("/auth/refresh", 200)).toBe(1);
    expect(apiCalls.count("/auth/refresh", 429)).toBe(0);
  });
});

test.describe("Clearing the storage", () => {
  const clearStorage = (page: Page) => page.evaluate(() => { localStorage.clear(); sessionStorage.clear(); });

  test("In the normal mode it ends the session, and the app asks nothing", async ({ page, company, apiCalls }) => {
    test.skip(COOKIE_MODE, "the session of the cookie mode is not in the storage: the next tests");
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    await clearStorage(page);
    apiCalls.reset();

    await page.reload();

    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(apiCalls.calls).toEqual([]);
  });

  test("While the cookie lives the open tab goes on, a reload brings the session back, and the other tabs are left alone", async ({ context, page, company, apiCalls }) => {
    test.skip(!COOKIE_MODE, "the session of the normal mode is the storage: the test above");
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const [otherTab] = await openTabs(context, 1, "/dashboard");
    await clearStorage(page);

    // nothing happens to whoever is working
    await page.getByRole("link", { name: "Produtos" }).click();
    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    await expect(page.getByText("Erro ao carregar produtos")).toHaveCount(0);
    await expect(otherTab).toHaveURL(`${APP_ORIGIN}/dashboard`);
    await expect(signOutButton(otherTab)).toBeVisible();
    apiCalls.reset();

    // the next load finds nothing in the storage: one renewal says the cookie has a session, and the same page opens
    await page.reload();

    await expect(page).toHaveURL(`${APP_ORIGIN}/produtos`);
    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    expect(apiCalls.count("/auth/refresh", 200)).toBe(1);
    await expect.poll(() => apiCalls.count("/auth/me", 200)).toBeGreaterThanOrEqual(1);
    expect(apiCalls.refused()).toBe(0);
    expect(JSON.parse((await storedSession(page)).session ?? "{}")).toMatchObject({ login: user.login });
  });

  test("On the sign-in page, with the cookie alive, it goes in by itself", async ({ page, company }) => {
    test.skip(!COOKIE_MODE, "only the cookie mode has something left after the storage is gone");
    const user = await company.createUser("vendedor");
    await signIn(page, user);
    await clearStorage(page);

    await page.goto("/login");

    await expect(page).toHaveURL(`${APP_ORIGIN}/pdv`);
  });

  test("After a sign-out nothing comes back, with or without the storage", async ({ context, page, company, apiCalls }) => {
    test.skip(!COOKIE_MODE, "only the cookie mode has something that could come back");
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    await signOutButton(page).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    await expect.poll(() => sessionCookie(context), "the answer of the sign-out removes it").toBeUndefined();
    await clearStorage(page);
    apiCalls.reset();

    await page.goto("/dashboard");

    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(apiCalls.count("/auth/refresh", 200)).toBe(0);
  });

  test("A public page asks nothing about the cookie", async ({ page, apiCalls }) => {
    test.skip(!COOKIE_MODE, "only the cookie mode could ask");
    await page.goto("/agendar/empresa-que-nao-existe");
    await page.waitForLoadState("networkidle");

    expect(apiCalls.count("/auth/refresh")).toBe(0);
  });
});

test.describe("Cookie mode, what only exists there", () => {
  test.skip(!COOKIE_MODE, "the normal mode has no cookie and no renewal on a page load");

  test("Credentials go on the session calls and on nothing else", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    const seen: { path: string; cookie: boolean; mode: boolean }[] = [];
    const reading: Promise<void>[] = [];
    context.on("request", (r) => {
      if (!r.url().startsWith(`${API}/`) || r.method() === "OPTIONS") return;
      // A call that a navigation cancels before it leaves has no headers to read, and waiting for them never ends.
      const left = new Promise<null>((resolve) => setTimeout(() => resolve(null), 5000));
      reading.push(Promise.race([r.allHeaders(), left]).then((headers) => {
        if (headers) seen.push({ path: new URL(r.url()).pathname.replace(/^\/api/, ""), cookie: !!headers.cookie, mode: !!headers[MODE_HEADER] });
      }));
    });

    await signInToDashboard(page, user);
    await page.getByRole("link", { name: "Produtos" }).click();
    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    await page.goto("/usuarios");
    await expect(page.getByRole("heading", { name: "Usuários" })).toBeVisible();
    await signOutButton(page).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    await Promise.all(reading);

    const sessionCalls = new Set(["/auth/login", "/auth/refresh", "/auth/logout"]);
    expect(seen.some((call) => call.path === "/auth/me")).toBe(true);
    expect(seen.filter((call) => !sessionCalls.has(call.path)).filter((call) => call.cookie || call.mode), "a call with credentials that is not of the session").toEqual([]);
    expect(seen.filter((call) => sessionCalls.has(call.path) && !call.mode)).toEqual([]);
    expect(seen.find((call) => call.path === "/auth/refresh" && call.cookie), "the renewal goes with the cookie").toBeDefined();
    expect(seen.find((call) => call.path === "/auth/logout" && call.cookie), "the sign-out too").toBeDefined();
  });

  test("Signing out from a tab with no access token ends the session on the server", async ({ context, page, company, device }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await sessionCookie(context);
    // the tab reloaded and has no token: the renewal of this load does not get through
    await page.route(`${API}/auth/refresh`, (route) => route.abort("connectionrefused"));
    const failed = page.waitForEvent("requestfailed", (r) => r.url() === `${API}/auth/refresh`);
    await page.reload();
    await failed;
    await expect(signOutButton(page)).toBeVisible();
    await page.unroute(`${API}/auth/refresh`);

    const sent = page.waitForRequest((r) => r.url() === `${API}/auth/logout` && r.method() === "POST");
    const answered = page.waitForResponse((r) => r.url() === `${API}/auth/logout` && r.request().method() === "POST");
    await signOutButton(page).click();

    expect((await sent).headers().authorization).toBeUndefined();
    expect((await answered).status()).toBe(204);
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    await expect.poll(() => sessionCookie(context), "the answer of the sign-out removes it").toBeUndefined();

    const renewal = await (await device()).post(`${API}/auth/refresh`, { data: { refreshToken: before!.value } });
    expect(renewal.status(), "the session ended on the server").toBe(401);
  });

  test("A sign-out the server did not hear goes first when the person signs in again on the same page", async ({ context, page, company, device }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const cookie = (await sessionCookie(context))!.value;
    await page.route(`${API}/auth/logout`, (route) => route.abort("connectionrefused"));
    await signOutButton(page).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    await page.unroute(`${API}/auth/logout`);

    const answered = page.waitForResponse((r) => r.url() === `${API}/auth/logout` && r.request().method() === "POST");
    await submitLogin(page, user.login, user.password);

    expect((await answered).status()).toBe(204);
    await expect(page).toHaveURL(`${APP_ORIGIN}/workspaces`);
    expect((await sessionCookie(context))?.value, "the cookie of the new session").not.toBe(cookie);
    const renewal = await (await device()).post(`${API}/auth/refresh`, { data: { refreshToken: cookie } });
    expect(renewal.status(), "the old session ended on the server").toBe(401);
  });

  test("A sign-out the server did not hear goes again on the next load, and the session ends for real", async ({ context, page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const cookie = (await sessionCookie(context))!.value;
    await page.route(`${API}/auth/logout`, (route) => route.abort("connectionrefused"));

    await signOutButton(page).click();

    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await sessionCookie(context), "the server did not hear it: the cookie is still there").toBeDefined();
    await page.unroute(`${API}/auth/logout`);
    apiCalls.reset();

    // the connection is back: this load finishes it, and it does not bring the session back
    const answered = page.waitForResponse((r) => r.url() === `${API}/auth/logout` && r.request().method() === "POST");
    await page.reload();
    expect((await answered).status()).toBe(204);
    await expect(loginField(page)).toBeEditable();
    await expect.poll(() => sessionCookie(context), "the answer removes it").toBeUndefined();
    const renewal = await (await device()).post(`${API}/auth/refresh`, { data: { refreshToken: cookie } });
    expect(renewal.status(), "the session ended on the server").toBe(401);
    expect(apiCalls.count("/auth/refresh")).toBe(0);

    // once: the next load asks nothing
    apiCalls.reset();
    await page.goto("/dashboard");
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(apiCalls.count("/auth/logout")).toBe(0);

    await submitLogin(page, user.login, user.password);
    await expect(page).toHaveURL(`${APP_ORIGIN}/workspaces`);
  });

  // The trap of a rotating cookie: the reload cancels a request the server has already answered. The new cookie would be lost with the answer, and
  // the old one, spent, would end the session. The renewal goes with keepalive, so the answer still reaches the browser's cookies.
  test("A reload in the middle of a renewal does not lose the session", async ({ context, page, company, device, apiCalls }) => {
    const address = clientAddress();
    await context.setExtraHTTPHeaders({ "X-Forwarded-For": address });
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const api = await device();
    const before = await sessionCookie(context);
    await api.post(`${HOST_ORIGIN}/__test/hold-renewals`, { data: { address, milliseconds: 1500 } });

    // the first load sends the renewal; the API answers at once, and the answer is on its way back when the page goes away
    const sent = page.waitForRequest((r) => r.url() === `${API}/auth/refresh` && r.method() === "POST");
    await page.reload({ waitUntil: "commit" });
    await sent;
    await page.waitForTimeout(300);
    await api.post(`${HOST_ORIGIN}/__test/hold-renewals`, { data: { address, milliseconds: 0 } });
    apiCalls.reset();
    await page.reload();

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    await expect.poll(() => apiCalls.count("/auth/me", 200)).toBeGreaterThanOrEqual(1);
    expect(apiCalls.count("/auth/refresh", 401), "the spent cookie did not end the session").toBe(0);
    expect((await sessionCookie(context))?.value).not.toBe(before?.value);
  });

  test("Reloading many times in a row keeps the session", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    apiCalls.reset();

    for (let reload = 0; reload < 6; reload++) await page.reload({ waitUntil: "commit" });
    await page.reload();

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    expect(apiCalls.count("/auth/refresh", 401)).toBe(0);
    await expect.poll(() => apiCalls.count("/auth/me", 200)).toBeGreaterThanOrEqual(1);
  });

  test("A tab showing one person does not go on as another when the cookie changes hands", async ({ context, page, company, device }) => {
    const user = await company.createUser("gerente");
    const someoneElse = await company.createUser("vendedor");
    await signInToDashboard(page, user);
    // the other app of the same API (erp and www share the cookie, and no channel crosses origins): a sign-in as someone else there
    const theirCookie = await cookieSignIn(await device(), someoneElse);
    await setRefreshToken(page, theirCookie);
    await expireAccessToken(page, await device());

    await page.getByRole("link", { name: "Produtos" }).click();

    await expect(page).toHaveURL(SESSION_ENDED_URL);
    expect(await storedSession(page)).toEqual(NO_SESSION);
    expect(context.pages()).toHaveLength(1);
  });
});

test.describe("CSRF against the real API", () => {
  const SIBLING = "https://evil.orken.com.br";

  for (const [name, origin] of [["a foreign origin", "https://evil.example"], ["a sibling subdomain", SIBLING], ["no origin", null]] as const) {
    test(`From ${name} a renewal gets 403 and the session is untouched`, async ({ company, device }) => {
      const user = await company.createUser("gerente");
      const api = await device();
      const cookie = await cookieSignIn(api, user);

      const refused = await api.post(`${API}/auth/refresh`, { headers: sessionCall(cookie, origin) });

      expect(refused.status()).toBe(403);
      expect((await refused.json()).code).toBe("origin_not_allowed");
      expect(refused.headersArray().filter((h) => h.name.toLowerCase() === "set-cookie")).toEqual([]);
      // the token was not spent: a second use would be a 409
      const renewed = await api.post(`${API}/auth/refresh`, { headers: sessionCall(cookie, APP_ORIGIN) });
      expect(renewed.status(), await renewed.text()).toBe(200);
    });

    test(`From ${name} a sign-out gets 403 and ends nothing`, async ({ company, device }) => {
      const user = await company.createUser("gerente");
      const api = await device();
      const cookie = await cookieSignIn(api, user);

      const refused = await api.post(`${API}/auth/logout`, { headers: sessionCall(cookie, origin) });

      expect(refused.status()).toBe(403);
      const renewed = await api.post(`${API}/auth/refresh`, { headers: sessionCall(cookie, APP_ORIGIN) });
      expect(renewed.status(), await renewed.text()).toBe(200);
    });
  }

  test("Without the header the cookie is worth nothing", async ({ company, device }) => {
    const user = await company.createUser("gerente");
    const api = await device();
    const cookie = await cookieSignIn(api, user);

    const renewal = await api.post(`${API}/auth/refresh`, { headers: sessionCall(cookie, APP_ORIGIN, null) });
    expect(renewal.status()).toBe(400);
    const signOut = await api.post(`${API}/auth/logout`, { headers: sessionCall(cookie, APP_ORIGIN, null) });
    expect(signOut.status()).toBe(401);

    const renewed = await api.post(`${API}/auth/refresh`, { headers: sessionCall(cookie, APP_ORIGIN) });
    expect(renewed.status(), await renewed.text()).toBe(200);
  });

  test("The cookie of the access token is gone: it opens nothing", async ({ company, device }) => {
    const user = await company.createUser("gerente");
    const api = await device();
    const login = await api.post(`${API}/auth/login`, { data: { login: user.login, password: user.password } });
    const { accessToken } = await login.json();

    const withCookie = await api.get(`${API}/auth/me`, { headers: { Cookie: `nexo_access=${accessToken}` } });
    expect(withCookie.status()).toBe(401);
    const withBearer = await api.get(`${API}/auth/me`, { headers: { Authorization: `Bearer ${accessToken}` } });
    expect(withBearer.status()).toBe(200);
  });
});

/**
 * erp.orken.com.br and api.erp.orken.com.br, over TLS: other origins of one site, and a third sibling that is not ours. No bundle is needed: what is
 * under test is what the browser does with the cookie of the real API, so a page runs the same calls the client makes. It runs in both rounds of the suite.
 */
test.describe("The shape of production", () => {
  const SHAPE_APP = "https://erp.orken.test:5199";
  const SHAPE_API = "https://api.erp.orken.test:5199/api";
  const SIBLING = "https://evil.orken.test:5199";
  test.use({ ignoreHTTPSErrors: true });

  /** A call from the page, as the client makes it. Status, body and whether the browser refused to let the page see the answer. */
  const callFrom = (page: Page, url: string, init: { body?: unknown; mode?: boolean; noCors?: boolean } = {}) =>
    page.evaluate(async ({ url, init, modeHeader }) => {
      try {
        const res = await fetch(url, {
          method: "POST",
          credentials: "include",
          mode: init.noCors ? "no-cors" : "cors",
          headers: init.noCors ? {} : { "Content-Type": "application/json", ...(init.mode === false ? {} : { [modeHeader]: "cookie" }) },
          body: init.body === undefined ? undefined : JSON.stringify(init.body),
        });
        return { status: res.status, type: res.type, text: res.type === "opaque" ? "" : await res.text() };
      } catch (error) {
        return { status: -1, type: "blocked", text: String(error) };
      }
    }, { url, init, modeHeader: MODE_HEADER });

  const apiCookies = async (context: BrowserContext) => (await context.cookies(SHAPE_API)).filter((c) => c.name.includes("nexo"));

  async function signInOnTheApp(page: Page, user: Account): Promise<string> {
    await page.goto(SHAPE_APP);
    const signedIn = await callFrom(page, `${SHAPE_API}/auth/login`, { body: { login: user.login, password: user.password } });
    expect(signedIn.status, signedIn.text).toBe(200);
    expect(signedIn.text).not.toContain("refreshToken");
    const [cookie] = await apiCookies(page.context());
    return cookie.value;
  }

  test("Over TLS the cookie is stored on the host of the API, no script reads it, and it renews with SameSite=Lax", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    const first = await signInOnTheApp(page, user);

    const cookies = await apiCookies(context);
    expect(cookies).toHaveLength(1);
    expect(cookies[0]).toMatchObject({ name: SESSION_COOKIE, domain: "api.erp.orken.test", path: "/", secure: true, httpOnly: true, sameSite: "Lax" });
    expect(await page.evaluate(() => document.cookie)).toBe("");
    // the cookie is of that host alone: the app itself does not hold it
    expect((await context.cookies(SHAPE_APP)).filter((c) => c.name === SESSION_COOKIE)).toEqual([]);

    const renewed = await callFrom(page, `${SHAPE_API}/auth/refresh`);
    expect(renewed.status, renewed.text).toBe(200);
    expect(renewed.text).not.toContain("refreshToken");
    const [next] = await apiCookies(context);
    expect(next.value).not.toBe(first);

    const signedOut = await callFrom(page, `${SHAPE_API}/auth/logout`);
    expect(signedOut.status).toBe(204);
    expect(await apiCookies(context)).toEqual([]);
  });

  test("A sibling subdomain cannot renew: the browser stops it at the preflight", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    const before = await signInOnTheApp(page, user);
    const sibling = await context.newPage();
    await sibling.goto(SIBLING);

    const attempt = await callFrom(sibling, `${SHAPE_API}/auth/refresh`);

    expect(attempt.status, "CORS: the sibling is not on the list").toBe(-1);
    const [after] = await apiCookies(context);
    expect(after.value).toBe(before);
    const renewed = await callFrom(page, `${SHAPE_API}/auth/refresh`);
    expect(renewed.status, renewed.text).toBe(200);
  });

  test("A sibling subdomain that skips the preflight sends the cookie, and without the header it is worth nothing", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    const before = await signInOnTheApp(page, user);
    const address = clientAddress();
    await context.setExtraHTTPHeaders({ "X-Forwarded-For": address });
    const sibling = await context.newPage();
    await sibling.goto(SIBLING);

    const attempt = await callFrom(sibling, `${SHAPE_API}/auth/refresh`, { noCors: true });

    // same site: SameSite=Lax lets the cookie go on this POST, which is why the server has to refuse it on its own
    expect(attempt.type).toBe("opaque");
    const seen = await page.context().request.get(`${HOST_ORIGIN}/__test/cookies-on-renewal?address=${address}`);
    expect((await seen.json()).names).toContain(SESSION_COOKIE);
    const [after] = await apiCookies(context);
    expect(after.value, "not rotated").toBe(before);
    const renewed = await callFrom(page, `${SHAPE_API}/auth/refresh`);
    expect(renewed.status, renewed.text).toBe(200);
  });

  test("A sibling subdomain cannot write over the cookie", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    const before = await signInOnTheApp(page, user);
    const sibling = await context.newPage();
    await sibling.goto(SIBLING);

    // __Host- with Domain, __Host- on its own host, and another name with Domain: the three ways to reach the host of the API
    await sibling.evaluate(() => fetch("/tossed-cookies"));

    const cookies = await context.cookies(SHAPE_API);
    const session = cookies.filter((c) => c.name === SESSION_COOKIE);
    expect(session, "one cookie of that name reaches the API, and it is not the sibling's").toHaveLength(1);
    expect(session[0].value).toBe(before);
    expect(session[0].domain).toBe("api.erp.orken.test");
    expect(cookies.filter((c) => c.value === "do-vizinho").map((c) => c.name)).toEqual(["nexo_rt"]);

    // the session goes on, and the name that did get through is read by nobody
    const renewed = await callFrom(page, `${SHAPE_API}/auth/refresh`);
    expect(renewed.status, renewed.text).toBe(200);
  });
});
