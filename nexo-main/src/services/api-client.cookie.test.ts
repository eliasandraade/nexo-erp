import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/**
 * The client in the two builds: VITE_SESSION_MODE=cookie, and the default that must stay as it was. The real API is in e2e/auth.e2e.spec.ts;
 * here, what a browser test cannot pin down call by call: which request carries credentials, what is left in the storage after each answer, and
 * the order of things when answers come late.
 */

const API = "http://localhost:5000/api";
const ACCESS = "nexo:access_token";
const REFRESH = "nexo:refresh_token";
const SESSION = "nexo:session";
const SIGNED_OUT = "nexo:signed_out";
const MODE_HEADER = "X-Orken-Session";

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });
const empty = (status = 204) => new Response(null, { status });

/** A signed token, as far as the client reads one: only the payload matters here. */
function jwt(claims: Record<string, unknown>): string {
  const encode = (value: unknown) => btoa(JSON.stringify(value)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${encode({ alg: "HS256" })}.${encode({ exp: Math.floor(Date.now() / 1000) + 900, ...claims })}.assinatura`;
}

const USER = "11111111-1111-1111-1111-111111111111";
const tenantSession = (userId = USER) => JSON.stringify({ type: "tenant", userId, name: "Pessoa" });
const platformSession = () => JSON.stringify({ type: "platform", userId: USER, name: "Plataforma" });
const accessOf = (tag: string, userId = USER) => jwt({ userId, tag });

// A BroadcastChannel of a single page: "another tab" is another instance of it, with no module behind it.
class FakeChannel {
  static open: FakeChannel[] = [];
  onmessage: ((e: MessageEvent) => void) | null = null;
  constructor(public readonly name: string) { FakeChannel.open.push(this); }
  postMessage(data: unknown) {
    for (const other of FakeChannel.open)
      if (other !== this && other.name === this.name) other.onmessage?.({ data } as MessageEvent);
  }
  close() { FakeChannel.open = FakeChannel.open.filter((c) => c !== this); }
}

const otherTab = () => new FakeChannel("orken-session");

const fetchMock = vi.fn();
const call = (index: number) => {
  const [url, init] = fetchMock.mock.calls[index] as [string, RequestInit & { headers?: Record<string, string> }];
  return { url, init, headers: init.headers ?? {} };
};
const urls = () => fetchMock.mock.calls.map(([url]) => url as string);

async function load(mode: "cookie" | "default" = "cookie") {
  vi.resetModules();
  vi.stubEnv("VITE_SESSION_MODE", mode === "cookie" ? "cookie" : "");
  return import("./api-client");
}

beforeEach(() => {
  localStorage.clear();
  sessionStorage.clear();
  FakeChannel.open = [];
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("BroadcastChannel", FakeChannel);
});

afterEach(() => {
  vi.unstubAllEnvs();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe("the default build stays as it was", () => {
  it("sends no credentials and no mode header on any call", async () => {
    const { apiClient } = await load("default");
    fetchMock.mockResolvedValueOnce(json({ accessToken: "a", refreshToken: "r", session: {} }));
    fetchMock.mockResolvedValueOnce(json([]));

    await apiClient.post("/auth/login", { login: "x", password: "y" });
    await apiClient.get("/products");

    for (const index of [0, 1]) {
      expect(call(index).init).not.toHaveProperty("credentials");
      expect(call(index).init).not.toHaveProperty("keepalive");
      expect(call(index).headers).not.toHaveProperty(MODE_HEADER);
    }
  });

  it("keeps the tokens in localStorage, and hasSession is having a token", async () => {
    const { setTokens, hasSession, getAccessToken, clearTokens } = await load("default");
    expect(hasSession()).toBe(false);

    setTokens("acesso", "renovacao");

    expect(localStorage.getItem(ACCESS)).toBe("acesso");
    expect(localStorage.getItem(REFRESH)).toBe("renovacao");
    expect(getAccessToken()).toBe("acesso");
    expect(hasSession()).toBe(true);

    clearTokens();
    expect(hasSession()).toBe(false);
    expect(localStorage.getItem(SIGNED_OUT)).toBeNull();
  });

  it("a session in the storage with no token is no session", async () => {
    const { hasSession } = await load("default");
    localStorage.setItem(SESSION, tenantSession());

    expect(hasSession()).toBe(false);
  });

  it("renews with the token in the body, without credentials", async () => {
    const { apiClient, setTokens, getAccessToken } = await load("default");
    setTokens("velho", "renovacao");
    fetchMock.mockResolvedValueOnce(json({ code: "x" }, 401));
    fetchMock.mockResolvedValueOnce(json({ accessToken: "novo", refreshToken: "renovacao2" }));
    fetchMock.mockResolvedValueOnce(json([]));

    await apiClient.get("/products");

    expect(call(1).url).toBe(`${API}/auth/refresh`);
    expect(call(1).init.body).toBe(JSON.stringify({ refreshToken: "renovacao" }));
    expect(call(1).init).not.toHaveProperty("credentials");
    expect(call(1).headers).not.toHaveProperty(MODE_HEADER);
    expect(getAccessToken()).toBe("novo");
    expect(localStorage.getItem(REFRESH)).toBe("renovacao2");
  });

  it("opens no channel between tabs", async () => {
    await load("default");

    expect(FakeChannel.open).toHaveLength(0);
  });
});

describe("the cookie build", () => {
  describe("who carries credentials", () => {
    for (const path of [
      "/auth/login", "/auth/mfa/verify", "/auth/verify-email?token=abc", "/auth/switch-store", "/auth/logout", "/auth/sessions/revoke-all",
      "/auth/mfa/enable", "/platform/auth/login", "/platform/auth/mfa/verify", "/platform/auth/logout", "/platform/auth/sessions/revoke-all",
      "/platform/auth/mfa/enable", "/platform/auth/mfa/disable", "/users/abc/change-password",
    ]) {
      it(`${path} does`, async () => {
        const { apiClient, setTokens } = await load();
        setTokens(accessOf("a"), undefined);
        fetchMock.mockResolvedValueOnce(json({}));

        await apiClient.post(path, {});

        expect(call(0).init.credentials).toBe("include");
        expect(call(0).headers[MODE_HEADER]).toBe("cookie");
      });
    }

    for (const path of ["/products", "/auth/me", "/auth/sessions", "/auth/mfa", "/auth/mfa/setup", "/auth/mfa/disable", "/auth/impersonation/end", "/auth/register", "/auth/forgot-password", "/auth/reset-password", "/platform/auth/security", "/users/abc"]) {
      it(`${path} does not, and goes with the Bearer header`, async () => {
        const { apiClient, setTokens } = await load();
        const access = accessOf("a");
        setTokens(access, undefined);
        fetchMock.mockResolvedValueOnce(json({}));

        await apiClient.post(path, {});

        expect(call(0).init).not.toHaveProperty("credentials");
        expect(call(0).headers).not.toHaveProperty(MODE_HEADER);
        expect(call(0).headers.Authorization).toBe(`Bearer ${access}`);
      });
    }
  });

  describe("where the tokens stay", () => {
    it("the access token only in memory, the refresh token nowhere, whatever the answer brings", async () => {
      const { setTokens, getAccessToken } = await load();

      setTokens("acesso", "um-refresh-que-nao-devia-vir");

      expect(getAccessToken()).toBe("acesso");
      expect(localStorage.getItem(ACCESS)).toBeNull();
      expect(localStorage.getItem(REFRESH)).toBeNull();
      expect(JSON.stringify({ ...localStorage })).not.toContain("acesso");
      expect(JSON.stringify({ ...sessionStorage })).not.toContain("acesso");
    });

    it("a reload loses the access token and keeps the session", async () => {
      const first = await load();
      first.setTokens("acesso", undefined);
      localStorage.setItem(SESSION, tenantSession());

      const reloaded = await load();

      expect(reloaded.getAccessToken()).toBeNull();
      expect(reloaded.hasSession()).toBe(true);
    });

    it("no session in the storage and no token is no session", async () => {
      const { hasSession } = await load();

      expect(hasSession()).toBe(false);
    });
  });

  describe("the first call after a reload", () => {
    it("renews through the cookie before calling the API", async () => {
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const access = accessOf("novo");
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/products");

      expect(urls()).toEqual([`${API}/auth/refresh`, `${API}/products`]);
      const refresh = call(0).init;
      expect(refresh.credentials).toBe("include");
      expect(refresh.keepalive).toBe(true);
      expect(refresh.body).toBeUndefined();
      expect(call(0).headers[MODE_HEADER]).toBe("cookie");
      expect(call(1).headers.Authorization).toBe(`Bearer ${access}`);
    });

    it("renews once, however many calls start at the same time", async () => {
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const access = accessOf("novo");
      fetchMock.mockImplementation(async (url: string) => (url.endsWith("/auth/refresh") ? json({ accessToken: access }) : json([])));

      await Promise.all([apiClient.get("/a"), apiClient.get("/b"), apiClient.post("/c", {})]);

      expect(urls().filter((u) => u.endsWith("/auth/refresh"))).toHaveLength(1);
    });

    it("does not renew to call what needs no access token", async () => {
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      fetchMock.mockImplementation(async () => json({}));

      await apiClient.post("/auth/login", {});
      await apiClient.post("/auth/forgot-password", {});

      expect(urls()).toEqual([`${API}/auth/login`, `${API}/auth/forgot-password`]);
    });

    it("the uploads wait for the token too, and an upload never carries credentials", async () => {
      const opened: string[] = [];
      class FakeXhr {
        upload = {};
        withCredentials = false;
        status = 200; statusText = "OK"; responseText = "{}";
        onload: (() => void) | null = null;
        private readonly sent: Record<string, string> = {};
        open() { /* the URL is not what this test looks at */ }
        setRequestHeader(name: string, value: string) { this.sent[name] = value; }
        send() {
          expect(this.withCredentials).toBe(false);
          opened.push(this.sent.Authorization);
          this.onload?.();
        }
      }
      vi.stubGlobal("XMLHttpRequest", FakeXhr);
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const access = accessOf("novo");
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));

      await apiClient.upload("POST", "/arquivos", new FormData());

      expect(opened).toEqual([`Bearer ${access}`]);
    });

    it("a form post waits for the token too", async () => {
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const access = accessOf("novo");
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));
      fetchMock.mockResolvedValueOnce(json({}));

      await apiClient.postForm("/arquivos", new FormData());

      expect(call(1).headers.Authorization).toBe(`Bearer ${access}`);
      expect(call(1).init).not.toHaveProperty("credentials");
    });

    it("a download waits for the token too, and carries no credentials", async () => {
      await load();
      localStorage.setItem(SESSION, tenantSession());
      const { downloadFile } = await import("./pdf.api");
      const access = accessOf("novo");
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));
      fetchMock.mockResolvedValueOnce(new Response("%PDF", { status: 200 }));
      vi.stubGlobal("URL", Object.assign(URL, { createObjectURL: () => "blob:x", revokeObjectURL: () => undefined }));

      await downloadFile("/relatorios/1.pdf", "1.pdf", "Falha");

      expect(call(1).url).toBe(`${API}/relatorios/1.pdf`);
      expect(call(1).headers.Authorization).toBe(`Bearer ${access}`);
      expect(call(1).init).not.toHaveProperty("credentials");
    });

    it("the token of the hub comes from the cookie when the tab has none yet", async () => {
      const { getFreshAccessToken } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const access = accessOf("novo");
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));

      await expect(getFreshAccessToken()).resolves.toBe(access);
    });

    it("the token of the hub is null with no session, and nothing is asked", async () => {
      const { getFreshAccessToken } = await load();

      await expect(getFreshAccessToken()).resolves.toBeNull();
      expect(fetchMock).not.toHaveBeenCalled();
    });
  });

  describe("a refusal ends the session, anything else keeps it", () => {
    it("401 on the renewal ends it, once, whatever the number of calls waiting", async () => {
      const { apiClient, onSessionEnded, hasSession } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const reasons: string[] = [];
      onSessionEnded((r) => reasons.push(r));
      fetchMock.mockResolvedValue(json({ code: "session_ended" }, 401));

      const results = await Promise.allSettled([apiClient.get("/a"), apiClient.get("/b")]);

      expect(results.every((r) => r.status === "rejected")).toBe(true);
      expect(reasons).toEqual(["session_ended"]);
      expect(hasSession()).toBe(false);
      expect(localStorage.getItem(SESSION)).toBeNull();
      expect(localStorage.getItem(SIGNED_OUT)).toBe("1");
      expect(urls().filter((u) => u.endsWith("/auth/refresh"))).toHaveLength(1);
    });

    it("a 401 session_revoked says the access changed", async () => {
      const { apiClient, onSessionEnded } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const reasons: string[] = [];
      onSessionEnded((r) => reasons.push(r));
      fetchMock.mockResolvedValue(json({ code: "session_revoked" }, 401));

      await expect(apiClient.get("/a")).rejects.toMatchObject({ status: 401, code: "session_revoked" });

      expect(reasons).toEqual(["session_revoked"]);
    });

    it("a platform session that ended says so", async () => {
      const { apiClient, onSessionEnded } = await load();
      localStorage.setItem(SESSION, platformSession());
      const reasons: string[] = [];
      onSessionEnded((r) => reasons.push(r));
      fetchMock.mockResolvedValue(json({ code: "session_ended" }, 401));

      await expect(apiClient.get("/platform/x")).rejects.toMatchObject({ status: 401 });

      expect(reasons).toEqual(["platform_session_ended"]);
    });

    for (const [name, answer] of [
      ["429", () => json({ code: "rate_limited" }, 429, { "Retry-After": "30" })],
      ["503", () => json({ code: "session_renewal_unavailable" }, 503, { "Retry-After": "15" })],
      ["a proxy page", () => new Response("<html>", { status: 502 })],
    ] as const) {
      it(`${name} keeps the session, the call fails with the temporary error and nothing renews again before the wait`, async () => {
        const { apiClient, hasSession, SESSION_RENEWAL_UNAVAILABLE_CODE, onSessionEnded } = await load();
        localStorage.setItem(SESSION, tenantSession());
        const reasons: string[] = [];
        onSessionEnded((r) => reasons.push(r));
        fetchMock.mockResolvedValueOnce(answer());

        await expect(apiClient.get("/a")).rejects.toMatchObject({ code: SESSION_RENEWAL_UNAVAILABLE_CODE });
        await expect(apiClient.get("/b")).rejects.toMatchObject({ code: SESSION_RENEWAL_UNAVAILABLE_CODE });

        expect(hasSession()).toBe(true);
        expect(reasons).toEqual([]);
        expect(fetchMock).toHaveBeenCalledTimes(1);
      });
    }

    it("a request cancelled because the page is going away leaves no wait for the next page", async () => {
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      vi.spyOn(console, "error").mockImplementation(() => undefined);
      fetchMock.mockImplementationOnce(async () => {
        globalThis.dispatchEvent(new Event("pagehide"));
        throw new TypeError("Failed to fetch");
      });

      await expect(apiClient.get("/a")).rejects.toBeInstanceOf(Error);

      expect(localStorage.getItem("nexo:renewal_wait")).toBeNull();
      globalThis.dispatchEvent(new Event("pageshow"));
    });

    it("no connection keeps the session", async () => {
      const { apiClient, hasSession, SESSION_RENEWAL_UNAVAILABLE_CODE } = await load();
      localStorage.setItem(SESSION, tenantSession());
      vi.spyOn(console, "error").mockImplementation(() => undefined);
      fetchMock.mockRejectedValueOnce(new TypeError("Failed to fetch"));

      await expect(apiClient.get("/a")).rejects.toMatchObject({ code: SESSION_RENEWAL_UNAVAILABLE_CODE });

      expect(hasSession()).toBe(true);
    });
  });

  describe("a 409: another tab, or a page reloaded during its renewal, just replaced the cookie", () => {
    it("the token of the other tab is enough", async () => {
      const { apiClient, hasSession } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const tab = otherTab();
      const access = accessOf("da-outra-aba");
      fetchMock.mockImplementationOnce(async () => {
        setTimeout(() => tab.postMessage({ type: "token", accessToken: access }), 20);
        return json({ code: "refresh_already_rotated" }, 409);
      });
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/a");

      expect(hasSession()).toBe(true);
      expect(call(1).headers.Authorization).toBe(`Bearer ${access}`);
      expect(urls().filter((u) => u.endsWith("/auth/refresh"))).toHaveLength(1);
    });

    it("with no word from any tab it tries again, and by then the browser has the new cookie", async () => {
      const { apiClient, getAccessToken } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const access = accessOf("novo");
      fetchMock.mockResolvedValueOnce(json({ code: "refresh_already_rotated" }, 409));
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/a");

      expect(getAccessToken()).toBe(access);
      expect(urls().filter((u) => u.endsWith("/auth/refresh"))).toHaveLength(2);
    });

    it("still a 409 after the wait ends the session, as in the normal mode", async () => {
      const { apiClient, hasSession, onSessionEnded, ROTATION_WAIT_MS } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const reasons: string[] = [];
      onSessionEnded((r) => reasons.push(r));
      fetchMock.mockImplementation(async () => json({ code: "refresh_already_rotated" }, 409));
      const started = Date.now();

      await expect(apiClient.get("/a")).rejects.toMatchObject({ status: 401 });

      expect(Date.now() - started).toBeGreaterThanOrEqual(ROTATION_WAIT_MS - 50);
      expect(hasSession()).toBe(false);
      expect(reasons).toEqual(["session_ended"]);
    }, 10_000);
  });

  describe("migration of a session still in localStorage", () => {
    const seedOldSession = () => {
      localStorage.setItem(ACCESS, "acesso-antigo");
      localStorage.setItem(REFRESH, "renovacao-antiga");
      localStorage.setItem(SESSION, tenantSession());
    };

    it("sends the token once, with the header, and only a 200 deletes both", async () => {
      const { apiClient, getAccessToken } = await load();
      seedOldSession();
      const access = accessOf("novo");
      fetchMock.mockImplementationOnce(async () => {
        // still there while the answer is on its way
        expect(localStorage.getItem(REFRESH)).toBe("renovacao-antiga");
        expect(localStorage.getItem(ACCESS)).toBe("acesso-antigo");
        return json({ accessToken: access });
      });
      fetchMock.mockResolvedValueOnce(json([]));
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/a");
      await apiClient.get("/b");

      const refresh = call(0);
      expect(refresh.url).toBe(`${API}/auth/refresh`);
      expect(refresh.init.body).toBe(JSON.stringify({ refreshToken: "renovacao-antiga" }));
      expect(refresh.headers[MODE_HEADER]).toBe("cookie");
      expect(refresh.init.credentials).toBe("include");
      expect(localStorage.getItem(ACCESS)).toBeNull();
      expect(localStorage.getItem(REFRESH)).toBeNull();
      expect(getAccessToken()).toBe(access);
      expect(urls().filter((u) => u.endsWith("/auth/refresh"))).toHaveLength(1);
    });

    it("without a verdict both stay, and the old token goes on working", async () => {
      const { apiClient, getAccessToken, hasSession } = await load();
      seedOldSession();
      fetchMock.mockResolvedValueOnce(json({ code: "session_renewal_unavailable" }, 503, { "Retry-After": "15" }));
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/a");

      expect(localStorage.getItem(REFRESH)).toBe("renovacao-antiga");
      expect(localStorage.getItem(ACCESS)).toBe("acesso-antigo");
      expect(hasSession()).toBe(true);
      expect(getAccessToken()).toBe("acesso-antigo");
      expect(call(1).headers.Authorization).toBe("Bearer acesso-antigo");
    });

    it("a refusal ends the session and takes both with it", async () => {
      const { apiClient, hasSession } = await load();
      seedOldSession();
      fetchMock.mockResolvedValueOnce(json({ code: "invalid_refresh" }, 401));

      await expect(apiClient.get("/a")).rejects.toMatchObject({ status: 401 });

      expect(localStorage.getItem(REFRESH)).toBeNull();
      expect(localStorage.getItem(ACCESS)).toBeNull();
      expect(hasSession()).toBe(false);
    });

    it("a tab that lost the race goes on through the cookie the winner got", async () => {
      const { apiClient, getAccessToken } = await load();
      seedOldSession();
      const access = accessOf("novo");
      fetchMock.mockImplementationOnce(async () => {
        // the other tab finished: it deleted the tokens and the browser has the cookie
        localStorage.removeItem(REFRESH);
        localStorage.removeItem(ACCESS);
        return json({ code: "refresh_already_rotated" }, 409);
      });
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/a");

      expect(call(1).init.body).toBeUndefined();
      expect(getAccessToken()).toBe(access);
    });
  });

  describe("signing out", () => {
    it("goes by the cookie alone and outlives the tab", async () => {
      const { apiClient, setTokens } = await load();
      setTokens(accessOf("a"), undefined);
      fetchMock.mockResolvedValueOnce(empty());

      await apiClient.post("/auth/logout");

      expect(call(0).init.credentials).toBe("include");
      expect(call(0).init.keepalive).toBe(true);
      expect(call(0).headers).not.toHaveProperty("Authorization");
      expect(call(0).init.body).toBeUndefined();
    });

    it("from a tab with no token yet asks for none first", async () => {
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      fetchMock.mockResolvedValueOnce(empty());

      await apiClient.post("/auth/logout");

      expect(urls()).toEqual([`${API}/auth/logout`]);
    });

    it("a late 401 of a call sent before it renews nothing", async () => {
      const { apiClient, setTokens, clearTokens } = await load();
      localStorage.setItem(SESSION, tenantSession());
      setTokens(accessOf("a"), undefined);
      let answer: (r: Response) => void = () => undefined;
      fetchMock.mockImplementationOnce(() => new Promise((resolve) => { answer = resolve; }));
      const inFlight = apiClient.get("/a");
      await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

      clearTokens();
      answer(json({ code: "x" }, 401));

      await expect(inFlight).rejects.toMatchObject({ status: 401 });
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });

    it("the answer of a renewal that was on its way does not bring the session back", async () => {
      const { apiClient, clearTokens, getAccessToken, hasSession } = await load();
      localStorage.setItem(SESSION, tenantSession());
      let answer: (r: Response) => void = () => undefined;
      fetchMock.mockImplementationOnce(() => new Promise((resolve) => { answer = resolve; }));
      const inFlight = apiClient.get("/a");
      await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

      clearTokens();
      answer(json({ accessToken: accessOf("tarde") }));

      await expect(inFlight).rejects.toMatchObject({ status: 401 });
      expect(getAccessToken()).toBeNull();
      expect(hasSession()).toBe(false);
    });

    it("leaves the mark that stops any renewal from opening the session again", async () => {
      const first = await load();
      localStorage.setItem(SESSION, tenantSession());
      first.setTokens(accessOf("a"), undefined);
      first.clearTokens();

      const reloaded = await load();

      await expect(reloaded.renewFromCookieWithoutSession()).resolves.toBe(false);
      expect(fetchMock).not.toHaveBeenCalled();
    });
  });

  describe("nothing in the storage while the cookie lives", () => {
    it("one renewal says the session is there", async () => {
      const { renewFromCookieWithoutSession, getAccessToken } = await load();
      const access = accessOf("novo");
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));

      await expect(renewFromCookieWithoutSession()).resolves.toBe(true);

      expect(getAccessToken()).toBe(access);
      expect(localStorage.getItem(SIGNED_OUT)).toBeNull();
    });

    it("a refusal leaves the mark, tells no other tab, and the next visit asks nothing", async () => {
      const tab = otherTab();
      const heard: unknown[] = [];
      tab.onmessage = (e) => heard.push(e.data);
      const { renewFromCookieWithoutSession } = await load();
      fetchMock.mockResolvedValueOnce(json({ code: "invalid_refresh" }, 401));

      await expect(renewFromCookieWithoutSession()).resolves.toBe(false);
      await expect(renewFromCookieWithoutSession()).resolves.toBe(false);

      expect(localStorage.getItem(SIGNED_OUT)).toBe("1");
      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(heard).toEqual([]);
    });

    it("no answer leaves no mark: the next visit asks again", async () => {
      const { renewFromCookieWithoutSession } = await load();
      vi.spyOn(console, "error").mockImplementation(() => undefined);
      fetchMock.mockRejectedValueOnce(new TypeError("Failed to fetch"));

      await expect(renewFromCookieWithoutSession()).resolves.toBe(false);

      expect(localStorage.getItem(SIGNED_OUT)).toBeNull();
    });

    it("a tab with its token in memory goes on renewing after the storage was cleared", async () => {
      const { apiClient, setTokens, hasSession } = await load();
      localStorage.setItem(SESSION, tenantSession());
      setTokens(accessOf("a"), undefined);
      localStorage.clear();
      expect(hasSession()).toBe(true);
      const access = accessOf("novo");
      fetchMock.mockResolvedValueOnce(json({ code: "x" }, 401));
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/a");

      expect(call(2).headers.Authorization).toBe(`Bearer ${access}`);
    });

    it("a sign-in removes the mark", async () => {
      const { setTokens, clearTokens } = await load();
      clearTokens();
      expect(localStorage.getItem(SIGNED_OUT)).toBe("1");

      setTokens("acesso", undefined);

      expect(localStorage.getItem(SIGNED_OUT)).toBeNull();
    });
  });

  describe("the other tabs", () => {
    it("a renewal hands the new token over", async () => {
      const tab = otherTab();
      const heard: unknown[] = [];
      tab.onmessage = (e) => heard.push(e.data);
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const access = accessOf("novo");
      fetchMock.mockResolvedValueOnce(json({ accessToken: access }));
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/a");

      expect(heard).toEqual([{ type: "token", accessToken: access }]);
    });

    it("a token from another tab is used at once, with no renewal", async () => {
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const access = accessOf("da-outra-aba");
      otherTab().postMessage({ type: "token", accessToken: access });
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/a");

      expect(urls()).toEqual([`${API}/a`]);
      expect(call(0).headers.Authorization).toBe(`Bearer ${access}`);
    });

    it("a sign-out elsewhere empties this tab and the app is told", async () => {
      const { getAccessToken, setTokens, onSignedOutElsewhere } = await load();
      setTokens(accessOf("a"), undefined);
      const told = vi.fn();
      onSignedOutElsewhere(told);

      otherTab().postMessage({ type: "signed-out" });

      expect(getAccessToken()).toBeNull();
      expect(told).toHaveBeenCalledOnce();
    });

    it("a sign-in or another store elsewhere replaces the token and the app is told", async () => {
      const { getAccessToken, onSessionReplaced } = await load();
      const told: string[] = [];
      onSessionReplaced((s) => told.push(s));
      const access = accessOf("loja-2");

      otherTab().postMessage({ type: "session", accessToken: access, session: tenantSession() });

      expect(getAccessToken()).toBe(access);
      expect(told).toEqual([tenantSession()]);
    });

    it("ending the session here tells them, and only when there was one", async () => {
      const tab = otherTab();
      const heard: unknown[] = [];
      tab.onmessage = (e) => heard.push(e.data);
      const { clearTokens, setTokens } = await load();

      clearTokens();
      expect(heard).toEqual([]);

      setTokens(accessOf("a"), undefined);
      clearTokens();
      expect(heard).toEqual([{ type: "signed-out" }]);
    });

    it("an answer that came after a sign-in elsewhere does not replace that session", async () => {
      const { apiClient, getAccessToken } = await load();
      localStorage.setItem(SESSION, tenantSession());
      let answer: (r: Response) => void = () => undefined;
      fetchMock.mockImplementationOnce(() => new Promise((resolve) => { answer = resolve; }));
      const inFlight = apiClient.get("/a");
      await vi.waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
      const access = accessOf("outra-sessao");

      otherTab().postMessage({ type: "session", accessToken: access, session: tenantSession() });
      answer(json({ accessToken: accessOf("velha") }));
      fetchMock.mockResolvedValueOnce(json([]));
      await inFlight;

      expect(getAccessToken()).toBe(access);
    });
  });

  describe("navigator.locks", () => {
    /** One lock per name, as a browser does across tabs: this file has only one page, so the line is simulated. */
    function fakeLocks() {
      let tail: Promise<unknown> = Promise.resolve();
      const requested: string[] = [];
      const locks = {
        request: (name: string, _options: unknown, work: () => Promise<unknown>) => {
          requested.push(name);
          const run = tail.then(() => work());
          tail = run.catch(() => undefined);
          return run;
        },
      };
      vi.stubGlobal("navigator", { ...globalThis.navigator, locks });
      return { requested, hold: (work: () => Promise<unknown>) => locks.request("orken-session-renewal", {}, work) };
    }

    it("a renewal goes through the lock", async () => {
      const { requested } = fakeLocks();
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      fetchMock.mockResolvedValueOnce(json({ accessToken: accessOf("novo") }));
      fetchMock.mockResolvedValueOnce(json([]));

      await apiClient.get("/a");

      expect(requested).toEqual(["orken-session-renewal"]);
    });

    it("a tab in line that hears the new token does not renew", async () => {
      const { hold } = fakeLocks();
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      const access = accessOf("da-outra-aba");
      let release = () => {};
      void hold(() => new Promise<void>((resolve) => { release = resolve; }));
      fetchMock.mockResolvedValueOnce(json([]));

      const call1 = apiClient.get("/a");
      await new Promise((r) => setTimeout(r, 10));
      otherTab().postMessage({ type: "token", accessToken: access });
      release();
      await call1;

      expect(urls()).toEqual([`${API}/a`]);
    });

    it("a call inside the lock that needs a renewal does not wait for itself", async () => {
      fakeLocks();
      const { apiClient, withSessionLock } = await load();
      localStorage.setItem(SESSION, tenantSession());
      fetchMock.mockResolvedValueOnce(json({ accessToken: accessOf("novo") }));
      fetchMock.mockResolvedValueOnce(json({}));

      await withSessionLock(() => apiClient.post("/auth/switch-store", { storeId: "x" }));

      expect(urls()).toEqual([`${API}/auth/refresh`, `${API}/auth/switch-store`]);
    });

    it("a lock that never comes does not stop the renewal", async () => {
      vi.useFakeTimers({ toFake: ["setTimeout", "Date"], shouldAdvanceTime: true });
      vi.stubGlobal("navigator", {
        ...globalThis.navigator,
        locks: {
          request: (_name: string, options: { signal: AbortSignal }) => new Promise((_resolve, reject) => {
            options.signal.addEventListener("abort", () => reject(options.signal.reason));
          }),
        },
      });
      const { apiClient } = await load();
      localStorage.setItem(SESSION, tenantSession());
      fetchMock.mockResolvedValueOnce(json({ accessToken: accessOf("novo") }));
      fetchMock.mockResolvedValueOnce(json([]));

      const pending = apiClient.get("/a");
      await vi.advanceTimersByTimeAsync(10_001);
      await pending;

      expect(urls()).toEqual([`${API}/auth/refresh`, `${API}/a`]);
    });
  });

  describe("the cookie now belongs to someone else", () => {
    it("the tab does not go on working as another person", async () => {
      const { apiClient, hasSession, onSessionEnded } = await load();
      localStorage.setItem(SESSION, tenantSession(USER));
      const reasons: string[] = [];
      onSessionEnded((r) => reasons.push(r));
      fetchMock.mockResolvedValueOnce(json({ accessToken: accessOf("outra", "22222222-2222-2222-2222-222222222222") }));

      await expect(apiClient.get("/a")).rejects.toMatchObject({ status: 401 });

      expect(hasSession()).toBe(false);
      expect(reasons).toEqual(["session_ended"]);
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });
  });

  describe("an impersonation tab", () => {
    const impersonate = (api: Awaited<ReturnType<typeof load>>) =>
      api.beginImpersonation("acesso-da-empresa", tenantSession(), { tenant: "Empresa", user: "Pessoa", expiresAt: null });

    it("never sends credentials or the header, not even on a session call", async () => {
      const api = await load();
      impersonate(api);
      fetchMock.mockImplementation(async () => json({}));

      await api.apiClient.get("/auth/me");
      await api.apiClient.post("/auth/logout");
      await api.apiClient.post("/auth/impersonation/end");

      for (const index of [0, 1, 2]) {
        expect(call(index).init).not.toHaveProperty("credentials");
        expect(call(index).headers).not.toHaveProperty(MODE_HEADER);
        expect(call(index).headers.Authorization).toBe("Bearer acesso-da-empresa");
      }
    });

    it("never renews through the cookie: a 401 ends this tab only", async () => {
      const api = await load();
      localStorage.setItem(SESSION, platformSession());
      impersonate(api);
      const reasons: string[] = [];
      api.onSessionEnded((r) => reasons.push(r));
      fetchMock.mockResolvedValueOnce(json({ code: "impersonation_ended" }, 401));

      await expect(api.apiClient.get("/a")).rejects.toMatchObject({ status: 401 });

      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(reasons).toEqual(["impersonation_ended"]);
      expect(localStorage.getItem(SESSION)).toBe(platformSession());
      expect(localStorage.getItem(SIGNED_OUT)).toBeNull();
    });

    it("ignores the channel of the administrator's tabs", async () => {
      const api = await load();
      impersonate(api);
      const told = vi.fn();
      api.onSignedOutElsewhere(told);
      api.onSessionReplaced(told);

      otherTab().postMessage({ type: "token", accessToken: "do-administrador" });
      otherTab().postMessage({ type: "session", accessToken: "do-administrador", session: platformSession() });
      otherTab().postMessage({ type: "signed-out" });

      expect(api.getAccessToken()).toBe("acesso-da-empresa");
      expect(told).not.toHaveBeenCalled();
    });

    it("ending it tells the other tabs nothing", async () => {
      const tab = otherTab();
      const heard: unknown[] = [];
      tab.onmessage = (e) => heard.push(e.data);
      const api = await load();
      impersonate(api);

      api.clearTokens();

      expect(heard).toEqual([]);
    });

    it("the token of the hub is its own, with no renewal", async () => {
      const api = await load();
      impersonate(api);

      await expect(api.getFreshAccessToken()).resolves.toBe("acesso-da-empresa");
      expect(fetchMock).not.toHaveBeenCalled();
    });
  });
});
