import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  apiClient,
  ApiError,
  clearTokens,
  getAccessToken,
  getFreshAccessToken,
  getRefreshToken,
  onSessionEnded,
  setTokens,
  TOKEN_KEYS,
  type SessionEndReason,
} from "./api-client";

const API = "http://localhost:5000/api";
const RENEWAL_WAIT_KEY = "nexo:renewal_wait";
const UNAVAILABLE = "session_renewal_unavailable";

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });

const expiredCall = () => json({}, 401);
const tooMany = (seconds: number) => json({ error: "Muitas tentativas.", code: "rate_limited", retryAfterSeconds: seconds }, 429, { "Retry-After": String(seconds) });

/** The token of the app, with only the expiry the client reads. */
const tokenExpiringIn = (seconds: number) => {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + seconds }))
    .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `header.${payload}.signature`;
};

describe("a renewal without a verdict keeps the session", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let ended: SessionEndReason[];
  let unsubscribe: () => void;

  const renewals = () => fetchMock.mock.calls.filter(call => call[0] === `${API}/auth/refresh`).length;
  const sessionIsIntact = () => {
    expect(getAccessToken()).toBe("access-1");
    expect(getRefreshToken()).toBe("refresh-1");
    expect(localStorage.getItem(TOKEN_KEYS.session)).toBe("{}");
    expect(ended).toEqual([]);
  };

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-11-06T12:00:00Z"));
    vi.spyOn(Math, "random").mockReturnValue(0);
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    localStorage.clear();
    sessionStorage.clear();
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    ended = [];
    unsubscribe = onSessionEnded(reason => ended.push(reason));
    setTokens("access-1", "refresh-1");
    localStorage.setItem(TOKEN_KEYS.session, "{}");
  });

  afterEach(() => {
    unsubscribe();
    vi.useRealTimers();
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  describe("what the server or the network answered", () => {
    const answers: Array<[string, () => Promise<Response> | Response, number]> = [
      ["429 with Retry-After", () => tooMany(120), 429],
      ["429 without Retry-After", () => json({ code: "rate_limited" }, 429), 429],
      ["503 of the API", () => json({ code: UNAVAILABLE, error: "x" }, 503, { "Retry-After": "15" }), 503],
      ["500 of the API", () => json({ error: "Erro interno.", requestId: "r1" }, 500), 500],
      ["502 of the edge, not JSON", () => new Response("<html>Bad gateway</html>", { status: 502 }), 502],
      ["403 of a proxy", () => new Response("forbidden", { status: 403 }), 403],
      ["no connection", () => Promise.reject(new TypeError("Failed to fetch")), 0],
    ];

    it.each(answers)("%s: the call fails with a readable temporary error, and the session stays", async (_, answer, status) => {
      fetchMock.mockResolvedValueOnce(expiredCall()).mockImplementationOnce(answer);

      const error = await apiClient.get("/products").catch((e: unknown) => e) as ApiError;

      expect(error).toBeInstanceOf(ApiError);
      expect(error.status).toBe(status);
      expect(error.status).not.toBe(401);
      expect(error.code).toBe(UNAVAILABLE);
      expect(error.message).toMatch(/sessão continua ativa/);
      expect(error.message).not.toMatch(/fetch|<html>|Unauthorized/);
      sessionIsIntact();
      expect(fetchMock).toHaveBeenCalledTimes(2);
    });

    it("a 401 on the renewal is a verdict: the session ends once, with no loop", async () => {
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({ code: "session_ended" }, 401));

      await expect(apiClient.get("/products")).rejects.toMatchObject({ status: 401 });

      expect(getAccessToken()).toBeNull();
      expect(getRefreshToken()).toBeNull();
      expect(ended).toEqual(["session_ended"]);
      expect(fetchMock).toHaveBeenCalledTimes(2);
    });

    describe("a 409 is no verdict", () => {
      const signOuts = () => fetchMock.mock.calls.filter(call => call[0] === `${API}/auth/logout`);
      const conflict = (seconds?: number) =>
        json({ code: "refresh_already_rotated" }, 409, seconds === undefined ? {} : { "Retry-After": String(seconds) });

      /** The clock jumps, so the three seconds this tab looks at the storage for the pair of the winner go by at once. */
      async function callAndLetTheWaitGoBy() {
        vi.useFakeTimers();
        vi.setSystemTime(new Date("2026-11-06T12:00:00Z"));
        const call = apiClient.get("/products").catch((e: unknown) => e);
        await vi.advanceTimersByTimeAsync(3100);
        return call;
      }

      it("the session stays, nothing calls logout by itself, and the call fails like on a 5xx", async () => {
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(conflict(27));

        const error = await callAndLetTheWaitGoBy() as ApiError;

        expect(error).toBeInstanceOf(ApiError);
        expect(error.status, "a screen reads a 409 as a conflict of its own record").toBe(503);
        expect(error.code).toBe(UNAVAILABLE);
        expect(error.message).toMatch(/sessão continua ativa/);
        sessionIsIntact();
        expect(signOuts()).toHaveLength(0);
        expect(fetchMock).toHaveBeenCalledTimes(2);
      });

      it("no tab presents that token again until the time the server gave", async () => {
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(conflict(27));
        await callAndLetTheWaitGoBy();
        expect(renewals()).toBe(1);

        vi.setSystemTime(new Date("2026-11-06T12:00:20Z"));
        fetchMock.mockResolvedValueOnce(expiredCall());
        await expect(apiClient.get("/products")).rejects.toMatchObject({ code: UNAVAILABLE });

        expect(renewals()).toBe(1);
        sessionIsIntact();
      });

      it("the time the server gave is written down for the other tabs before anything else", async () => {
        vi.useRealTimers();
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(conflict(27));

        const call = apiClient.get("/products").catch((e: unknown) => e);
        await vi.waitFor(() => expect(localStorage.getItem(RENEWAL_WAIT_KEY)).not.toBeNull());
        const left = JSON.parse(localStorage.getItem(RENEWAL_WAIT_KEY)!).until - Date.now();
        await call;

        expect(left).toBeGreaterThan(26_000);
        expect(left).toBeLessThanOrEqual(27_000);
      }, 10_000);

      it("with no Retry-After, it waits the whole grace", async () => {
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(conflict());
        await callAndLetTheWaitGoBy();

        expect(JSON.parse(localStorage.getItem(RENEWAL_WAIT_KEY)!).until).toBeGreaterThanOrEqual(Date.parse("2026-11-06T12:00:30Z"));
        sessionIsIntact();
      });

      it("a 409 of any other kind says nothing about the session either", async () => {
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({ error: "conflito" }, 409));

        await callAndLetTheWaitGoBy();

        sessionIsIntact();
        expect(signOuts()).toHaveLength(0);
      });

      it("the new pair of another tab ends the wait at once", async () => {
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(conflict(27));
        await callAndLetTheWaitGoBy();

        // the winning tab got its answer: this is what setTokens does there
        setTokens("access-2", "refresh-2");
        expect(localStorage.getItem(RENEWAL_WAIT_KEY)).toBeNull();

        fetchMock.mockResolvedValueOnce(json({ ok: true }));
        await expect(apiClient.get("/products")).resolves.toEqual({ ok: true });
        expect(renewals()).toBe(1);
        expect(fetchMock.mock.calls.at(-1)![1].headers.Authorization).toBe("Bearer access-2");
      });

      it("a pair that lands during the wait leaves nothing behind", async () => {
        vi.useRealTimers();
        fetchMock
          .mockResolvedValueOnce(expiredCall())
          .mockImplementationOnce(async () => {
            setTimeout(() => {
              localStorage.setItem(TOKEN_KEYS.access, "access-2");
              localStorage.setItem(TOKEN_KEYS.refresh, "refresh-2");
            }, 150);
            return conflict(27);
          })
          .mockResolvedValueOnce(json({ ok: true }));

        await expect(apiClient.get("/products")).resolves.toEqual({ ok: true });

        expect(localStorage.getItem(RENEWAL_WAIT_KEY)).toBeNull();
        expect(ended).toEqual([]);
      });

      it("after the grace the same token goes again, and a renewal goes on as if nothing had happened", async () => {
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(conflict(27));
        await callAndLetTheWaitGoBy();

        vi.setSystemTime(new Date("2026-11-06T12:00:40Z"));
        fetchMock.mockResolvedValueOnce(expiredCall())
          .mockResolvedValueOnce(json({ accessToken: "access-2", refreshToken: "refresh-2" }))
          .mockResolvedValueOnce(json({ ok: true }));
        await expect(apiClient.get("/products")).resolves.toEqual({ ok: true });

        expect(JSON.parse(fetchMock.mock.calls[3][1].body)).toEqual({ refreshToken: "refresh-1" });
        expect(getRefreshToken()).toBe("refresh-2");
        expect(localStorage.getItem(RENEWAL_WAIT_KEY)).toBeNull();
      });

      it("after the grace the same token goes again, and the server's refusal ends the session", async () => {
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(conflict(27));
        await callAndLetTheWaitGoBy();

        vi.setSystemTime(new Date("2026-11-06T12:00:40Z"));
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({ code: "session_ended" }, 401));
        await expect(apiClient.get("/products")).rejects.toMatchObject({ status: 401 });

        expect(getAccessToken()).toBeNull();
        expect(getRefreshToken()).toBeNull();
        expect(ended).toEqual(["session_ended"]);
        expect(signOuts()).toHaveLength(0);
      });

      it("while it waits, signing out still reaches the server, with the token this tab has", async () => {
        fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(conflict(27));
        await callAndLetTheWaitGoBy();
        const { logout } = await import("@/modules/auth/services/authService");

        fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
        await logout();

        expect(signOuts()).toHaveLength(1);
        expect(JSON.parse(signOuts()[0][1].body)).toEqual({ refreshToken: "refresh-1" });
        expect(getRefreshToken()).toBeNull();
      });
    });

    // An answer nobody reads is a request that never ends: with no-store the browser tools never see it finish, and neither does a test waiting for them.
    it.each([
      ["401", () => json({ code: "session_ended" }, 401)],
      ["409", () => json({ code: "refresh_already_rotated" }, 409, { "Retry-After": "27" })],
      ["429", () => tooMany(120)],
    ])("the body of a %s on the renewal is read", async (_, answer) => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date("2026-11-06T12:00:00Z"));
      const refused = answer();
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(refused);

      const call = apiClient.get("/products").catch((e: unknown) => e);
      await vi.advanceTimersByTimeAsync(3100);
      await call;

      expect(refused.bodyUsed).toBe(true);
    });

    it("a plain 401 needs no such call: the server already said the session is over", async () => {
      fetchMock.mockImplementation(async (url: string) => (url === `${API}/auth/refresh` ? json({ code: "session_ended" }, 401) : expiredCall()));

      await expect(apiClient.get("/a")).rejects.toBeInstanceOf(ApiError);

      expect(fetchMock.mock.calls.filter(call => call[0] === `${API}/auth/logout`)).toHaveLength(0);
    });

    // A limit of the rotation, stated here so nobody reads "no connection keeps the session" as more than it is: if the server
    // did rotate and only the answer was lost, the new pair is gone. Past the grace the spent token is judged, and the session ends there.
    it("an answer lost after the server rotated ends the session when the server judges the token", async () => {
      vi.useFakeTimers();
      vi.setSystemTime(new Date("2026-11-06T12:00:00Z"));
      fetchMock.mockResolvedValueOnce(expiredCall()).mockRejectedValueOnce(new TypeError("Failed to fetch"));
      await expect(apiClient.get("/products")).rejects.toMatchObject({ status: 0, code: UNAVAILABLE });
      sessionIsIntact();

      vi.setSystemTime(new Date("2026-11-06T12:00:10Z"));
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({ code: "refresh_already_rotated" }, 409, { "Retry-After": "20" }));
      const next = apiClient.get("/products").catch((e: unknown) => e);
      await vi.advanceTimersByTimeAsync(3100);
      await expect(next).resolves.toMatchObject({ code: UNAVAILABLE });
      sessionIsIntact();

      vi.setSystemTime(new Date("2026-11-06T12:00:40Z"));
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({ code: "session_ended" }, 401));
      await expect(apiClient.get("/products")).rejects.toMatchObject({ status: 401 });
      expect(getAccessToken()).toBeNull();
      expect(ended).toEqual(["session_ended"]);
    });

    it("a platform session is treated the same: 429 keeps it, 401 ends it", async () => {
      clearTokens();
      setTokens("p-access", "p.refresh");
      localStorage.setItem(TOKEN_KEYS.session, "{}");
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(tooMany(30));

      await expect(apiClient.get("/platform/stats")).rejects.toMatchObject({ status: 429, code: UNAVAILABLE });
      expect(getRefreshToken()).toBe("p.refresh");
      expect(ended).toEqual([]);

      vi.setSystemTime(Date.now() + 31_000);
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({ code: "session_ended" }, 401));
      await expect(apiClient.get("/platform/stats")).rejects.toMatchObject({ status: 401 });
      expect(getRefreshToken()).toBeNull();
      expect(ended).toEqual(["platform_session_ended"]);
    });

    it("a revoked access token is the server's verdict: without a renewal the session still ends", async () => {
      fetchMock
        .mockResolvedValueOnce(json({ error: "Sua sessão foi encerrada.", code: "session_revoked" }, 401))
        .mockResolvedValueOnce(tooMany(120));

      await expect(apiClient.get("/products")).rejects.toMatchObject({ status: 401, code: "session_revoked" });

      expect(getAccessToken()).toBeNull();
      expect(ended).toEqual(["session_revoked"]);
    });

    it("the multipart calls and the uploads follow the same rule", async () => {
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(tooMany(120));
      await expect(apiClient.postForm("/storage", new FormData())).rejects.toMatchObject({ status: 429, code: UNAVAILABLE });
      sessionIsIntact();

      const xhr = vi.fn(function (this: Record<string, unknown>) {
        this.open = vi.fn();
        this.setRequestHeader = vi.fn();
        this.upload = {};
        this.send = vi.fn(() => {
          this.status = 401;
          this.statusText = "Unauthorized";
          this.responseText = "{}";
          (this.onload as () => void)();
        });
      });
      vi.stubGlobal("XMLHttpRequest", xhr);

      await expect(apiClient.upload("POST", "/storage", new FormData())).rejects.toMatchObject({ status: 429, code: UNAVAILABLE });
      sessionIsIntact();
      expect(xhr).toHaveBeenCalledTimes(1);
    });
  });

  describe("when it tries again", () => {
    it("respects Retry-After: nothing goes out before it, and the session renews right after", async () => {
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(tooMany(120));
      await expect(apiClient.get("/products")).rejects.toMatchObject({ status: 429 });
      expect(renewals()).toBe(1);

      for (const seconds of [1, 60, 119]) {
        vi.setSystemTime(new Date("2026-11-06T12:00:00Z").getTime() + seconds * 1000);
        fetchMock.mockResolvedValueOnce(expiredCall());
        await expect(apiClient.get("/products")).rejects.toMatchObject({ code: UNAVAILABLE });
      }
      expect(renewals()).toBe(1);
      sessionIsIntact();

      vi.setSystemTime(new Date("2026-11-06T12:02:01Z"));
      fetchMock
        .mockResolvedValueOnce(expiredCall())
        .mockResolvedValueOnce(json({ accessToken: "access-2", refreshToken: "refresh-2" }))
        .mockResolvedValueOnce(json([{ id: 1 }]));

      await expect(apiClient.get("/products")).resolves.toEqual([{ id: 1 }]);

      expect(renewals()).toBe(2);
      expect(getAccessToken()).toBe("access-2");
      expect(localStorage.getItem(RENEWAL_WAIT_KEY)).toBeNull();
      expect(ended).toEqual([]);
    });

    it("reads Retry-After as a date too", async () => {
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(
        json({}, 503, { "Retry-After": new Date("2026-11-06T12:01:00Z").toUTCString() }));
      await expect(apiClient.get("/products")).rejects.toMatchObject({ status: 503 });

      vi.setSystemTime(new Date("2026-11-06T12:00:59Z"));
      fetchMock.mockResolvedValueOnce(expiredCall());
      await expect(apiClient.get("/products")).rejects.toMatchObject({ code: UNAVAILABLE });
      expect(renewals()).toBe(1);

      vi.setSystemTime(new Date("2026-11-06T12:01:01Z"));
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({}, 503, { "Retry-After": "15" }));
      await expect(apiClient.get("/products")).rejects.toMatchObject({ status: 503 });
      expect(renewals()).toBe(2);
    });

    it("says how long is left, in words", async () => {
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(tooMany(600));
      await expect(apiClient.get("/products")).rejects.toThrow(/em cerca de 10 minutos/);

      vi.setSystemTime(new Date("2026-11-06T12:09:30Z"));
      fetchMock.mockResolvedValueOnce(expiredCall());
      await expect(apiClient.get("/products")).rejects.toThrow(/em 30 segundos/);
    });

    it("without Retry-After it backs off, up to a minute between attempts", async () => {
      const seen: number[] = [];
      let now = new Date("2026-11-06T12:00:00Z").getTime();
      // the app keeps calling every second for ten minutes: each failed renewal says how long the next one waits
      fetchMock.mockImplementation(async (url: string) => {
        if (url === `${API}/auth/refresh`) {
          seen.push(now);
          return new Response("Bad gateway", { status: 502 });
        }
        return expiredCall();
      });

      for (let second = 0; second < 600; second++) {
        now = new Date("2026-11-06T12:00:00Z").getTime() + second * 1000;
        vi.setSystemTime(now);
        await apiClient.get("/products").catch(() => undefined);
      }

      const gaps = seen.slice(1).map((at, i) => (at - seen[i]) / 1000);
      expect(gaps.slice(0, 5)).toEqual([2, 4, 8, 16, 32]);
      expect(Math.max(...gaps)).toBe(60);
      expect(seen.length).toBeLessThan(16);
      sessionIsIntact();
    });

    it("spreads the terminals of a store: the wait gets a little on top, never more than 5 s", async () => {
      vi.mocked(Math.random).mockReturnValue(0.5);
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(tooMany(10));
      await apiClient.get("/products").catch(() => undefined);

      vi.setSystemTime(new Date("2026-11-06T12:00:10.500Z"));
      fetchMock.mockResolvedValueOnce(expiredCall());
      await apiClient.get("/products").catch(() => undefined);
      expect(renewals()).toBe(1);

      vi.setSystemTime(new Date("2026-11-06T12:00:11.100Z"));
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(tooMany(10));
      await apiClient.get("/products").catch(() => undefined);
      expect(renewals()).toBe(2);
    });

    it("many calls at the same moment make one renewal", async () => {
      fetchMock.mockImplementation(async (url: string) => (url === `${API}/auth/refresh` ? tooMany(120) : expiredCall()));

      const results = await Promise.allSettled(Array.from({ length: 8 }, () => apiClient.get("/products")));

      expect(results.every(r => r.status === "rejected")).toBe(true);
      expect(renewals()).toBe(1);
      sessionIsIntact();
    });

    it("another tab obeys the same wait, and no tab sends a renewal during it", async () => {
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(tooMany(120));
      await apiClient.get("/products").catch(() => undefined);
      expect(renewals()).toBe(1);

      // a tab is a module of its own over the same localStorage
      vi.resetModules();
      const otherTab = await import("./api-client");
      fetchMock.mockResolvedValueOnce(expiredCall());

      await expect(otherTab.apiClient.get("/products")).rejects.toMatchObject({ status: 429, code: UNAVAILABLE });

      expect(renewals()).toBe(1);
      sessionIsIntact();
    });

    it("a wait with a date from another clock, or unreadable, does not hold the session back", async () => {
      for (const stored of [JSON.stringify({ until: Date.now() + 86_400_000 * 30, status: 429, attempts: 1 }), "{not json", JSON.stringify({ until: "soon" })]) {
        localStorage.setItem(RENEWAL_WAIT_KEY, stored);
        fetchMock.mockReset();
        fetchMock
          .mockResolvedValueOnce(expiredCall())
          .mockResolvedValueOnce(json({ accessToken: "access-1", refreshToken: "refresh-1" }))
          .mockResolvedValueOnce(json([]));

        await expect(apiClient.get("/products")).resolves.toEqual([]);
        expect(renewals()).toBe(1);
      }
    });

    it("a server asking for an absurd wait is capped at an hour", async () => {
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({}, 429, { "Retry-After": "99999999" }));
      await apiClient.get("/products").catch(() => undefined);

      vi.setSystemTime(new Date("2026-11-06T13:00:01Z"));
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({ accessToken: "access-2", refreshToken: "refresh-2" })).mockResolvedValueOnce(json([]));

      await expect(apiClient.get("/products")).resolves.toEqual([]);
    });

    it("what goes on top of a long wait stays small, and the cap holds with it", async () => {
      vi.mocked(Math.random).mockReturnValue(0.999);
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(tooMany(900));
      await apiClient.get("/products").catch(() => undefined);
      expect(JSON.parse(localStorage.getItem(RENEWAL_WAIT_KEY)!).until).toBeLessThan(new Date("2026-11-06T12:15:05.001Z").getTime());

      localStorage.removeItem(RENEWAL_WAIT_KEY);
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(json({}, 429, { "Retry-After": "99999999" }));
      await apiClient.get("/products").catch(() => undefined);
      vi.setSystemTime(new Date("2026-11-06T12:59:59Z"));
      fetchMock.mockResolvedValueOnce(expiredCall());
      await apiClient.get("/products").catch(() => undefined);

      expect(renewals()).toBe(2);
    });

    it("signing in or out ends the wait", async () => {
      fetchMock.mockResolvedValueOnce(expiredCall()).mockResolvedValueOnce(tooMany(600));
      await apiClient.get("/products").catch(() => undefined);
      expect(localStorage.getItem(RENEWAL_WAIT_KEY)).not.toBeNull();

      setTokens("access-9", "refresh-9");
      expect(localStorage.getItem(RENEWAL_WAIT_KEY)).toBeNull();

      localStorage.setItem(RENEWAL_WAIT_KEY, JSON.stringify({ until: Date.now() + 1000, status: 429, attempts: 1 }));
      clearTokens();
      expect(localStorage.getItem(RENEWAL_WAIT_KEY)).toBeNull();
    });
  });

  describe("a connection that stays open (getFreshAccessToken)", () => {
    it("keeps the token it has on a 429 and does not ask again during the wait", async () => {
      const expiring = tokenExpiringIn(5);
      setTokens(expiring, "refresh-1");
      fetchMock.mockResolvedValueOnce(tooMany(120));

      await expect(getFreshAccessToken()).resolves.toBe(expiring);
      await expect(getFreshAccessToken()).resolves.toBe(expiring);

      expect(renewals()).toBe(1);
      expect(ended).toEqual([]);
      expect(getRefreshToken()).toBe("refresh-1");
    });

    it("a 5xx does not end the session either", async () => {
      const expiring = tokenExpiringIn(5);
      setTokens(expiring, "refresh-1");
      fetchMock.mockResolvedValueOnce(new Response("Bad gateway", { status: 502 }));

      await expect(getFreshAccessToken()).resolves.toBe(expiring);

      expect(ended).toEqual([]);
      expect(getRefreshToken()).toBe("refresh-1");
    });

    it("a 401 still ends it", async () => {
      setTokens(tokenExpiringIn(5), "refresh-1");
      fetchMock.mockResolvedValueOnce(json({ code: "session_ended" }, 401));

      await expect(getFreshAccessToken()).resolves.toBeNull();

      expect(ended).toEqual(["session_ended"]);
    });
  });
});
