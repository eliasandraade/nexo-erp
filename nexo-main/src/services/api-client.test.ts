import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import {
  apiClient,
  ApiError,
  beginImpersonation,
  clearTokens,
  getAccessToken,
  getImpersonation,
  getRefreshToken,
  getStoredSession,
  IMPERSONATION_KEY,
  onSessionEnded,
  setStoredSession,
  setTokens,
  TOKEN_KEYS,
  type SessionEndReason,
} from "./api-client";

const API = "http://localhost:5000/api";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

/** The administrator's own (platform) session in localStorage. */
function signInAdmin() {
  localStorage.setItem(TOKEN_KEYS.access, "admin-access");
  localStorage.setItem(TOKEN_KEYS.refresh, "p.admin-refresh");
  localStorage.setItem(TOKEN_KEYS.session, JSON.stringify({ type: "platform", name: "Admin" }));
}

const adminStorage = () => ({
  access: localStorage.getItem(TOKEN_KEYS.access),
  refresh: localStorage.getItem(TOKEN_KEYS.refresh),
  session: localStorage.getItem(TOKEN_KEYS.session),
});

const authHeader = (call: unknown[]) => (call[1] as RequestInit).headers as Record<string, string>;

describe("api-client", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let ended: SessionEndReason[];
  let unsubscribe: () => void;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    ended = [];
    unsubscribe = onSessionEnded(r => ended.push(r));
  });

  afterEach(() => {
    unsubscribe();
    vi.unstubAllGlobals();
  });

  describe("impersonation mode (tab-scoped)", () => {
    beforeEach(() => {
      signInAdmin();
      beginImpersonation("imp-access", JSON.stringify({ type: "tenant", name: "Ana" }),
        { tenant: "Padaria", user: "Ana", expiresAt: "2026-10-30T12:30:00Z" });
    });

    it("reads and writes the token and the session only in sessionStorage", () => {
      expect(getAccessToken()).toBe("imp-access");
      expect(getStoredSession()).toContain("Ana");
      expect(getRefreshToken()).toBeNull();

      setTokens("imp-access-2", "should-be-ignored");
      setStoredSession(JSON.stringify({ name: "Ana 2" }));

      expect(sessionStorage.getItem(TOKEN_KEYS.access)).toBe("imp-access-2");
      expect(sessionStorage.getItem(TOKEN_KEYS.refresh)).toBeNull();
      expect(adminStorage()).toEqual({
        access: "admin-access",
        refresh: "p.admin-refresh",
        session: JSON.stringify({ type: "platform", name: "Admin" }),
      });
    });

    it("clearTokens clears only this tab's keys and keeps the administrator's localStorage session", () => {
      clearTokens();
      expect(sessionStorage.getItem(TOKEN_KEYS.access)).toBeNull();
      expect(sessionStorage.getItem(TOKEN_KEYS.session)).toBeNull();
      expect(getImpersonation()?.ended).toBe(true);
      // the ended tab never falls back to the admin's token
      expect(getAccessToken()).toBeNull();
      expect(adminStorage().access).toBe("admin-access");
      expect(adminStorage().refresh).toBe("p.admin-refresh");
    });

    it("sends the impersonation token and never tries to refresh on 401", async () => {
      fetchMock.mockResolvedValueOnce(json({ error: "O acesso terminou.", code: "impersonation_ended" }, 401));

      await expect(apiClient.get("/products")).rejects.toMatchObject({ status: 401, code: "impersonation_ended" });

      expect(fetchMock).toHaveBeenCalledTimes(1);   // no /auth/refresh call
      expect(authHeader(fetchMock.mock.calls[0]).Authorization).toBe("Bearer imp-access");
      expect(ended).toEqual(["impersonation_ended"]);
      expect(sessionStorage.getItem(TOKEN_KEYS.access)).toBeNull();
      expect(adminStorage().access).toBe("admin-access");
      expect(adminStorage().refresh).toBe("p.admin-refresh");
    });
  });

  describe("refresh", () => {
    it("rotates the pair on 401 and retries the request once", async () => {
      signInAdmin();
      fetchMock
        .mockResolvedValueOnce(json({}, 401))
        .mockResolvedValueOnce(json({ accessToken: "admin-access-2", refreshToken: "p.admin-refresh-2" }))
        .mockResolvedValueOnce(json({ ok: true }));

      await expect(apiClient.get("/platform/stats")).resolves.toEqual({ ok: true });

      expect(fetchMock.mock.calls[1][0]).toBe(`${API}/auth/refresh`);
      expect(JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string)).toEqual({ refreshToken: "p.admin-refresh" });
      expect(authHeader(fetchMock.mock.calls[2]).Authorization).toBe("Bearer admin-access-2");
      expect(adminStorage().refresh).toBe("p.admin-refresh-2");
    });

    it("on 409 refresh_already_rotated uses the pair the other tab stored and retries instead of logging out", async () => {
      signInAdmin();
      fetchMock
        .mockResolvedValueOnce(json({}, 401))
        .mockImplementationOnce(async () => {
          // the winner tab stores the new pair while this tab's refresh is refused
          localStorage.setItem(TOKEN_KEYS.access, "winner-access");
          localStorage.setItem(TOKEN_KEYS.refresh, "p.winner-refresh");
          return json({ error: "Renovada em outra aba.", code: "refresh_already_rotated" }, 409);
        })
        .mockResolvedValueOnce(json({ ok: true }));

      await expect(apiClient.get("/platform/stats")).resolves.toEqual({ ok: true });

      expect(fetchMock).toHaveBeenCalledTimes(3);
      expect(authHeader(fetchMock.mock.calls[2]).Authorization).toBe("Bearer winner-access");
      expect(adminStorage().access).toBe("winner-access");
      expect(adminStorage().refresh).toBe("p.winner-refresh");
      expect(ended).toEqual([]);
    });

    it("waits for the winner tab when its new pair arrives after the 409", async () => {
      signInAdmin();
      fetchMock
        .mockResolvedValueOnce(json({}, 401))
        .mockImplementationOnce(async () => {
          setTimeout(() => {
            localStorage.setItem(TOKEN_KEYS.access, "late-access");
            localStorage.setItem(TOKEN_KEYS.refresh, "p.late-refresh");
          }, 150);
          return json({ code: "refresh_already_rotated" }, 409);
        })
        .mockResolvedValueOnce(json({ ok: true }));

      await expect(apiClient.get("/platform/stats")).resolves.toEqual({ ok: true });
      expect(authHeader(fetchMock.mock.calls[2]).Authorization).toBe("Bearer late-access");
    });

    it("retries with the token another tab already renewed, without refreshing again", async () => {
      signInAdmin();
      fetchMock
        .mockImplementationOnce(async () => {
          localStorage.setItem(TOKEN_KEYS.access, "renewed-elsewhere");
          return json({}, 401);
        })
        .mockResolvedValueOnce(json({ ok: true }));

      await expect(apiClient.get("/platform/stats")).resolves.toEqual({ ok: true });
      expect(fetchMock).toHaveBeenCalledTimes(2);
      expect(authHeader(fetchMock.mock.calls[1]).Authorization).toBe("Bearer renewed-elsewhere");
    });

    it("a refused platform refresh ends the session and tells the app", async () => {
      signInAdmin();
      fetchMock
        .mockResolvedValueOnce(json({}, 401))
        .mockResolvedValueOnce(json({ error: "Sessão encerrada.", code: "session_ended" }, 401));

      await expect(apiClient.get("/platform/stats")).rejects.toBeInstanceOf(ApiError);
      expect(adminStorage()).toEqual({ access: null, refresh: null, session: null });
      expect(ended).toEqual(["platform_session_ended"]);
    });
  });

  it("401 platform_session_ended clears the tokens and ends the session without refreshing", async () => {
    signInAdmin();
    fetchMock.mockResolvedValueOnce(json({ error: "Sessão da plataforma encerrada.", code: "platform_session_ended" }, 401));

    await expect(apiClient.get("/platform/tenants")).rejects.toMatchObject({ status: 401, code: "platform_session_ended" });

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(adminStorage()).toEqual({ access: null, refresh: null, session: null });
    expect(ended).toEqual(["platform_session_ended"]);
  });

  it("a wrong password on sign-in keeps the session and never refreshes", async () => {
    signInAdmin();
    fetchMock.mockResolvedValueOnce(json({ error: "Credenciais inválidas." }, 401));
    await expect(apiClient.post("/auth/login", { login: "x", password: "y" })).rejects.toMatchObject({ status: 401 });
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(adminStorage().access).toBe("admin-access");
  });

  it("keeps the marker key name the banner and the router rely on", () => {
    expect(IMPERSONATION_KEY).toBe("orken:impersonation");
  });
});
