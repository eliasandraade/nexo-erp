import { createHmac, randomInt, randomUUID } from "node:crypto";
import { test as base, expect, type APIRequestContext, type Browser, type BrowserContext, type Page, type Request, type Response } from "@playwright/test";

/**
 * Authentication against the real API (playwright.auth.config.ts, e2e/auth-host): the real login page, route guards and API
 * client talking to the real controllers, middleware, CORS policy, rate limiter and PostgreSQL. A test only replaces a
 * response where the server cannot produce the case on demand (no network, a server echoing the input), and says so.
 *
 * Each test creates its own accounts through the company's API, with synthetic values, and is its own client address for the
 * rate limiter, so a test never inherits a lockout, a revoked session or a spent limit from another one.
 */

const APP_ORIGIN = "http://127.0.0.1:5197";
const HOST_ORIGIN = "http://127.0.0.1:5196";
const API = `${HOST_ORIGIN}/api`;

const ACCESS_TOKEN_KEY = "nexo:access_token";
const REFRESH_TOKEN_KEY = "nexo:refresh_token";
const SESSION_KEY = "nexo:session";
const NO_SESSION = { access: null, refresh: null, session: null };

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
    const account = { login: `e2e${tag}`, email: `e2e${tag}@nexo.test`, password: `Sintetica-${randomUUID()}`, fullName: `Pessoa ${tag}`, role };
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

/** What the page asked of the API (preflights aside) and what is still on the way. */
class ApiLog {
  calls: ApiCall[] = [];
  inFlight = 0;
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
    context.on("weberror", (e) => uncaught.push(String(e.error())));
    await provide(context);
    expect(uncaught, "uncaught errors in the page").toEqual([]);
  },
  apiCalls: async ({ context }, provide) => {
    const log = new ApiLog();
    const isApiCall = (r: Request) => r.url().startsWith(`${API}/`) && r.method() !== "OPTIONS";
    context.on("request", (r) => { if (isApiCall(r)) log.inFlight++; });
    context.on("requestfinished", (r) => { if (isApiCall(r)) log.inFlight--; });
    context.on("requestfailed", (r) => { if (isApiCall(r)) log.inFlight--; });
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

const storedSession = (page: Page) =>
  page.evaluate(([access, refresh, session]) => ({
    access: localStorage.getItem(access),
    refresh: localStorage.getItem(refresh),
    session: localStorage.getItem(session),
  }), [ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY, SESSION_KEY]);

const setStored = (page: Page, key: string, value: string) =>
  page.evaluate(([k, v]) => localStorage.setItem(k, v), [key, value]);

/** Swaps the access token of the page for the same token, correctly signed, past its expiry. Returns it. */
async function expireAccessToken(page: Page, api: APIRequestContext): Promise<string> {
  const { access } = await storedSession(page);
  const response = await api.post(`${HOST_ORIGIN}/__test/tokens/expired`, { data: { accessToken: access } });
  const { accessToken } = await response.json();
  await setStored(page, ACCESS_TOKEN_KEY, accessToken);
  return accessToken;
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
  await expect.poll(() => api.inFlight).toBe(0);
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
    const stored = await storedSession(page);
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
    expect(apiCalls.count("/auth/refresh")).toBe(0);

    // the form is still usable: the right password goes through
    await passwordField(page).fill(user.password);
    await enterButton(page).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/workspaces`);
  });

  test("Login with non-existent user fails gracefully", async ({ page }) => {
    await page.goto("/login");
    await submitLogin(page, `ninguem${randomInt(1_000_000)}`, "qualquer-senha");

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

  // What the app does today, stated so nobody reads the cookies as a protection of the session. The API answers with httpOnly
  // cookies, but the app calls it with fetch and no credentials, from another origin, so the browser drops them. The session
  // is the copy of the same tokens in the response body, kept in localStorage, where any script on the page can read it: the
  // 7-day refresh token included. If the app moves to the cookies, this test must change with it.
  test("Login answers with httpOnly cookies, the browser drops them and the session lives in localStorage", async ({ context, page, company }) => {
    const user = await company.createUser("gerente");
    await page.goto("/login");
    const [response] = await Promise.all([page.waitForResponse(isLoginCall), submitLogin(page, user.login, user.password)]);
    expect(response.status()).toBe(200);

    const setCookies = (await response.headersArray())
      .filter((h) => h.name.toLowerCase() === "set-cookie")
      .map((h) => h.value.toLowerCase());
    for (const name of ["nexo_access", "nexo_refresh"]) {
      const cookie = setCookies.find((value) => value.startsWith(`${name}=`));
      expect(cookie, `${name} in the login response`).toBeDefined();
      expect(cookie).toContain("httponly");
    }

    await expect(page).not.toHaveURL(/\/login/);
    const authCookies = async () => (await context.cookies()).map((c) => c.name).filter((name) => name.startsWith("nexo_"));
    expect(await authCookies(), "cookies the browser kept").toEqual([]);
    expect(await page.evaluate(() => document.cookie)).not.toContain("nexo_");

    const body = await response.json();
    const stored = await storedSession(page);
    expect(stored.access).toBe(body.accessToken);
    expect(stored.refresh).toBe(body.refreshToken);

    // control: the same call with credentials does keep them, and httpOnly hides them from the page. So the empty list above
    // comes from how the app calls the API, and not from this environment refusing the cookies.
    await page.evaluate(async ([url, login, password]) => {
      await fetch(url, { method: "POST", credentials: "include", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ login, password }) });
    }, [`${API}/auth/login`, user.login, user.password]);
    expect((await authCookies()).sort()).toEqual(["nexo_access", "nexo_refresh"]);
    expect(await page.evaluate(() => document.cookie)).not.toContain("nexo_");
  });

  test("Login error messages do not contain user-supplied XSS", async ({ page }) => {
    const payload = `"><img src=x onerror="window.__xss=1"><script>window.__xss=1</script>`;
    let dialogs = 0;
    page.on("dialog", (d) => { dialogs++; void d.dismiss(); });

    // the real API never echoes the input, so against it this could only pass
    await page.goto("/login");
    await submitLogin(page, payload, "qualquer-senha");
    await expect(formError(page)).toHaveText(WRONG_CREDENTIALS);

    // the risk is a server message that does echo it: replaced response, and the page must show it as plain text
    await page.route(`${API}/auth/login`, (route) => route.fulfill({
      status: 400,
      headers: { "Access-Control-Allow-Origin": APP_ORIGIN },
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
    await submitLogin(page, "alguem", "qualquer-senha");

    await expect(formError(page)).toHaveText("Erro ao conectar com o servidor.");
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
  });
});

test.describe("Session", () => {
  // Replaces "Cookies persist across page navigation": the app keeps its session in localStorage and sends a header; it
  // never sends the cookies. The risk is the same: losing the session on the way to another page.
  test("Session persists across page navigation", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await storedSession(page);

    await page.getByRole("link", { name: "Produtos" }).click();
    await expect(page).toHaveURL(`${APP_ORIGIN}/produtos`);
    await expect(page.getByRole("heading", { name: "Produtos" })).toBeVisible();
    await page.goto("/usuarios");
    await expect(page.getByRole("heading", { name: "Usuários" })).toBeVisible();

    expect(await storedSession(page)).toMatchObject({ access: before.access, refresh: before.refresh });
    expect(apiCalls.count("/auth/refresh")).toBe(0);
  });

  // Replaces "SSR hydration preserves auth state": the app has no server rendering; a full page load is the same risk.
  test("A full page reload keeps the session", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await storedSession(page);
    apiCalls.reset();

    await page.reload();

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    await expect.poll(() => apiCalls.count("/auth/me", 200)).toBe(1);
    expect((await storedSession(page)).access).toBe(before.access);
    expect(apiCalls.count("/auth/refresh")).toBe(0);
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
    const { access } = await storedSession(page);

    // The frontend authenticates with the header; it does not send the cookies.
    const apiRequest = page.waitForRequest((r) => r.url() === `${API}/auth/me` && r.method() === "GET");
    await page.reload();

    expect(await (await apiRequest).headerValue("authorization")).toBe(`Bearer ${access}`);
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

    // no reload: the other tab hears it from the shared storage
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
    // nothing was fetched on behalf of a session that does not exist
    expect(apiCalls.calls).toEqual([]);
  });

  test("A forged session in storage does not open a protected route", async ({ page, apiCalls }) => {
    await page.goto("/login");
    await setStored(page, SESSION_KEY, JSON.stringify({ userId: "x", tenantId: "x", name: "Forjada", role: "diretoria", login: "x", modules: ["varejo"], storeIds: [], type: "tenant" }));
    await setStored(page, ACCESS_TOKEN_KEY, "forjado");

    await page.goto("/dashboard");

    await expect(page).toHaveURL(/\/login/);
    await expect(loginField(page)).toBeEditable();
    expect(await storedSession(page)).toEqual(NO_SESSION);
    expect(apiCalls.count("/auth/me", 401)).toBe(1);
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
      const { access } = await storedSession(page);
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
    const before = await storedSession(page);

    const [logout] = await Promise.all([
      page.waitForResponse((r) => r.url() === `${API}/auth/logout` && r.request().method() === "POST"),
      signOutButton(page).click(),
    ]);

    expect(logout.status()).toBe(204);
    await expect(page).toHaveURL(`${APP_ORIGIN}/login`);
    expect(await storedSession(page)).toEqual(NO_SESSION);

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
    const before = await storedSession(page);
    const expired = await expireAccessToken(page, await device());
    apiCalls.reset();

    await page.reload();

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    await expect.poll(async () => (await storedSession(page)).access).not.toBe(expired);
    const after = await storedSession(page);
    expect(after.access).not.toBe(before.access);
    expect(after.refresh).not.toBe(before.refresh);
    await expect.poll(() => apiCalls.count("/auth/me", 200)).toBe(1);
    expect(apiCalls.count("/auth/me", 401)).toBe(1);
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
    expect((await storedSession(page)).access).not.toBe(expired);
    // however many calls found the token expired, the session renews once
    expect(apiCalls.count("/auth/refresh", 200)).toBe(1);
  });
});

test.describe("Session ended by the server", () => {
  test("Expired session sends the user back to login", async ({ page, company, device, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const api = await device();
    const { refresh } = await storedSession(page);
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
    await setStored(page, REFRESH_TOKEN_KEY, "recusado");
    apiCalls.reset();

    await page.getByRole("link", { name: "Produtos" }).click();

    await expect(page).toHaveURL(SESSION_ENDED_URL);
    expect(apiCalls.count("/auth/refresh", 401)).toBe(1);
    await expectSettledOnLogin(page, apiCalls, user, SESSION_ENDED_URL, "/workspaces");
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
    expect(apiCalls.count("/auth/me", 401)).toBe(1);
    expect(apiCalls.count("/auth/refresh", 401)).toBe(1);
    await expectSettledOnLogin(page, apiCalls, user, SESSION_REVOKED_URL, "/pdv");
  });

  test("A user blocked while signed in loses the session and cannot come back", async ({ page, company, apiCalls }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);

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
 * Only a refusal from the server ends a session. The two fixme tests below show where the app does not hold to that: when the
 * renewal itself gets no verdict (429 or no connection), api-client.ts drops the tokens and sends the person to the login.
 * The limiter makes the 429 reachable in normal use: 5 renewals per 15 minutes per address, shared by every device of a store
 * behind one address. Fixing it is not only client code: keeping the session means failing every screen until the window
 * ends, where today a new sign-in works at once (the login has its own budget), so the limit itself has to be decided first.
 */
test.describe("Failures that say nothing about the session", () => {
  test("A network failure on opening the app keeps the session", async ({ page, company }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await storedSession(page);
    // no server answer to wait for: the connection itself fails
    await page.route(`${API}/auth/me`, (route) => route.abort("connectionrefused"));

    const failed = page.waitForEvent("requestfailed", (r) => r.url() === `${API}/auth/me`);
    await page.reload();
    await failed;

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    expect(await storedSession(page)).toMatchObject({ access: before.access, refresh: before.refresh });
  });

  test("A server error on opening the app keeps the session", async ({ page, company }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await storedSession(page);
    // replaced response: the real API has no way to fail on demand
    await page.route(`${API}/auth/me`, (route) => route.fulfill({
      status: 500,
      headers: { "Access-Control-Allow-Origin": APP_ORIGIN },
      contentType: "application/json",
      body: JSON.stringify({ error: "Erro interno.", requestId: "e2e" }),
    }));

    const answered = page.waitForResponse(`${API}/auth/me`);
    await page.reload();
    await answered;

    await expect(signOutButton(page)).toBeVisible();
    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    expect(await storedSession(page)).toMatchObject({ access: before.access, refresh: before.refresh });
  });

  // Bug, left open: see the comment above the describe.
  test.fixme("A 429 on the token renewal keeps the session", async ({ context, page, company, device, playwright }) => {
    const user = await company.createUser("gerente");
    const address = clientAddress();
    await context.setExtraHTTPHeaders({ "X-Forwarded-For": address });
    await signInToDashboard(page, user);
    const before = await storedSession(page);

    // the renewal shares a limit per address: other devices behind the same address (a store's network) spend it
    const sameNetwork = await playwright.request.newContext({ extraHTTPHeaders: { "X-Forwarded-For": address } });
    for (let renewal = 0; renewal < 5; renewal++) {
      await sameNetwork.post(`${API}/auth/refresh`, { data: { refreshToken: "de-outro-dispositivo" } });
    }
    await sameNetwork.dispose();
    await expireAccessToken(page, await device());

    const limited = page.waitForResponse(`${API}/auth/refresh`);
    await page.reload();
    expect((await limited).status()).toBe(429);

    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    expect((await storedSession(page)).refresh).toBe(before.refresh);
  });

  // Bug, left open: see the comment above the describe.
  test.fixme("A network failure on the token renewal keeps the session", async ({ page, company, device }) => {
    const user = await company.createUser("gerente");
    await signInToDashboard(page, user);
    const before = await storedSession(page);
    await expireAccessToken(page, await device());
    await page.route(`${API}/auth/refresh`, (route) => route.abort("connectionrefused"));

    const failed = page.waitForEvent("requestfailed", (r) => r.url() === `${API}/auth/refresh`);
    await page.reload();
    await failed;

    await expect(page).toHaveURL(`${APP_ORIGIN}/dashboard`);
    expect((await storedSession(page)).refresh).toBe(before.refresh);
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
    const password = `Sintetica-${randomUUID()}`;
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
        await api.post(`${API}/auth/login`, { data: { login: user.login, password: `errada-${batch}-${attempt}` } });
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
    expect((await storedSession(page)).access).toBeTruthy();
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
    expect(apiCalls.count("/auth/refresh")).toBe(0);

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
