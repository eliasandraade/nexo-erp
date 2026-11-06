import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, beginImpersonation, getAccessToken, getImpersonation, IMPERSONATION_KEY, setStoredSession, setTokens } from "@/services/api-client";
import {
  disableMfa,
  getCurrentSession,
  login,
  logout,
  register,
  requestPasswordReset,
  resendVerification,
  resetPassword,
  revokeAllSessions,
  switchStore,
  verifyEmail,
  verifyMfa,
} from "./authService";

// Written against fetch and the public token functions, never a storage key or a refresh token in a body: where the tokens live
// may change, and what these flows do for the person must not.
const API = "http://localhost:5000/api";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const SESSION_DTO = {
  userId: "u1", tenantId: "t1", name: "Ana", role: "gerente", login: "ana", email: "ana@x.dev",
  activeModules: ["varejo"], storeId: "s2", storeIds: ["s1", "s2"], companyName: "Loja", type: "tenant",
};
const SIGNED_IN = { accessToken: "new-access", refreshToken: "new-refresh", session: SESSION_DTO };

describe("authService flows", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  const lastCall = () => {
    const [url, init] = fetchMock.mock.calls.at(-1) as [string, RequestInit];
    return { url, method: init.method, body: init.body ? JSON.parse(init.body as string) : undefined };
  };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => vi.unstubAllGlobals());

  describe("the stored session", () => {
    it("a stored session that cannot be read counts as no session, instead of breaking the app at boot", () => {
      setStoredSession("{not json");

      expect(getCurrentSession()).toBeNull();
    });
  });

  describe("login", () => {
    it("with the second step on, gives back the challenge and signs nobody in yet", async () => {
      fetchMock.mockResolvedValueOnce(json({ mfaRequired: true, mfaChallenge: "challenge-1" }));

      const result = await login({ login: "ana", password: "x" });

      expect(result).toEqual({ success: false, error: "", mfaChallenge: "challenge-1" });
      expect(getAccessToken()).toBeNull();
      expect(getCurrentSession()).toBeNull();
    });

    it("from a tab whose platform access ended, the tab becomes a normal one again", async () => {
      beginImpersonation("imp-token", JSON.stringify({ userId: "x" }), { tenant: "T", user: "U", expiresAt: null });
      sessionStorage.setItem(IMPERSONATION_KEY, JSON.stringify({ tenant: "T", user: "U", expiresAt: null, ended: true }));
      fetchMock.mockResolvedValueOnce(json(SIGNED_IN));

      const result = await login({ login: "ana", password: "x" });

      expect(result.success).toBe(true);
      expect(getImpersonation()).toBeNull();
      expect(getAccessToken()).toBe("new-access");
    });

    it("keeps an active platform access untouched when the sign-in is refused", async () => {
      beginImpersonation("imp-token", JSON.stringify({ userId: "x" }), { tenant: "T", user: "U", expiresAt: null });
      fetchMock.mockResolvedValueOnce(json({ error: "Invalid login or password." }, 401));

      const result = await login({ login: "ana", password: "x" });

      expect(result).toEqual({ success: false, error: "Login ou senha incorretos." });
      expect(getImpersonation()).not.toBeNull();
    });

    it("a platform account signs in as a platform session, and missing lists default to empty", async () => {
      fetchMock.mockResolvedValueOnce(json({
        accessToken: "a", refreshToken: "r",
        session: { userId: "p1", tenantId: "", name: "ops", role: "super_admin", login: "ops", email: "ops@x.dev", type: "platform" },
      }));

      const result = await login({ login: "ops", password: "x" });

      if (!result.success) throw new Error("expected a session");
      expect(result.session.type).toBe("platform");
      expect(result.session.modules).toEqual([]);
      expect(result.session.storeIds).toEqual([]);
      expect(result.session.isNewAccount).toBe(false);
    });
  });

  describe("logout", () => {
    it("tells the server and drops the local session", async () => {
      setTokens("old-access", "old-refresh");
      fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

      await logout();

      expect(lastCall().url).toBe(`${API}/auth/logout`);
      expect(lastCall().method).toBe("POST");
      expect(getAccessToken()).toBeNull();
    });

    it("drops the local session even when the server cannot be reached", async () => {
      setTokens("old-access", "old-refresh");
      fetchMock.mockRejectedValueOnce(new TypeError("Failed to fetch"));

      await expect(logout()).resolves.toBeUndefined();

      expect(getAccessToken()).toBeNull();
    });
  });

  describe("switchStore", () => {
    it("the new token and session take effect at once", async () => {
      setTokens("old-access", "old-refresh");
      fetchMock.mockResolvedValueOnce(json(SIGNED_IN));

      const session = await switchStore("s2");

      expect(lastCall().url).toBe(`${API}/auth/switch-store`);
      expect(lastCall().body.storeId).toBe("s2");
      expect(session.storeId).toBe("s2");
      expect(getAccessToken()).toBe("new-access");
      expect(getCurrentSession()?.storeId).toBe("s2");
    });

    it("refused, it changes nothing and the screen gets the reason", async () => {
      setTokens("old-access", "old-refresh");
      setStoredSession(JSON.stringify({ userId: "u1", storeId: "s1" }));
      fetchMock.mockResolvedValueOnce(json({ error: "Sem acesso a esta loja.", code: "store_forbidden" }, 403));

      const error = await switchStore("s9").catch((e: unknown) => e);

      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).code).toBe("store_forbidden");
      expect(getAccessToken()).toBe("old-access");
      expect(getCurrentSession()?.storeId).toBe("s1");
    });
  });

  describe("register", () => {
    it("sends what the API reads and creates no session: the e-mail has to be verified first", async () => {
      fetchMock.mockResolvedValueOnce(json({ message: "verification_email_sent" }));

      const result = await register({ name: "Ana", email: "ana@x.dev", password: "segredo" });

      expect(result).toEqual({ success: true });
      expect(lastCall().body).toEqual({ name: "Ana", email: "ana@x.dev", password: "segredo" });
      expect(getAccessToken()).toBeNull();
    });

    it("an address already in use is said in the person's language", async () => {
      fetchMock.mockResolvedValueOnce(json({ error: "Este e-mail já está cadastrado.", code: "email_already_registered" }, 409));

      const result = await register({ name: "Ana", email: "ana@x.dev", password: "segredo" });

      expect(result).toEqual({ success: false, error: "Este e-mail já está cadastrado." });
    });

    it("any other refusal shows the server's reason", async () => {
      fetchMock.mockResolvedValueOnce(json({ error: "A senha deve ter no mínimo 6 caracteres." }, 400));

      const result = await register({ name: "Ana", email: "ana@x.dev", password: "x" });

      expect(result).toEqual({ success: false, error: "A senha deve ter no mínimo 6 caracteres." });
    });
  });

  describe("verifyEmail", () => {
    it("signs the person in and escapes the token in the address", async () => {
      fetchMock.mockResolvedValueOnce(json(SIGNED_IN));

      const result = await verifyEmail("a+b/c=");

      expect(lastCall().url).toBe(`${API}/auth/verify-email?token=a%2Bb%2Fc%3D`);
      expect(result.success).toBe(true);
      expect(getAccessToken()).toBe("new-access");
      expect(getCurrentSession()?.userId).toBe("u1");
      expect(localStorage.getItem("nexo:onboarding:u1")).toBeNull();
    });

    it("a new account is marked for the onboarding", async () => {
      fetchMock.mockResolvedValueOnce(json({ ...SIGNED_IN, session: { ...SESSION_DTO, isNewAccount: true } }));

      await verifyEmail("token");

      expect(localStorage.getItem("nexo:onboarding:u1")).toBe("true");
    });

    it.each([
      ["a link the server refuses", () => json({ error: "Link inválido ou expirado." }, 400)],
      ["no connection", () => { throw new TypeError("Failed to fetch"); }],
    ])("%s never rejects and never signs in: the page shows its error state", async (_name, answer) => {
      fetchMock.mockImplementationOnce(async () => answer());

      await expect(verifyEmail("token")).resolves.toEqual({ success: false, error: "Link inválido ou expirado." });

      expect(getAccessToken()).toBeNull();
      expect(getCurrentSession()).toBeNull();
    });
  });

  describe("second step of the sign-in", () => {
    it("stores the session like the login does", async () => {
      fetchMock.mockResolvedValueOnce(json(SIGNED_IN));

      const session = await verifyMfa("challenge-1", "123456");

      expect(lastCall().url).toBe(`${API}/auth/mfa/verify`);
      expect(lastCall().body).toEqual({ challenge: "challenge-1", code: "123456", recoveryCode: null });
      expect(session.userId).toBe("u1");
      expect(getAccessToken()).toBe("new-access");
    });

    it("a wrong code rejects with the server's reason and stores nothing", async () => {
      fetchMock.mockResolvedValueOnce(json({ error: "Código inválido.", code: "invalid_code" }, 401));

      const error = await verifyMfa("challenge-1", "000000").catch((e: unknown) => e);

      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).code).toBe("invalid_code");
      expect(getAccessToken()).toBeNull();
      expect(fetchMock).toHaveBeenCalledTimes(1);
    });
  });

  // The field names here are what the API reads: renamed on one side only, these flows stop working with no error on screen.
  describe("recovery and sessions", () => {
    it.each([
      ["resend the verification", () => resendVerification("a@b.dev"), "/auth/resend-verification", { email: "a@b.dev" }],
      ["ask for a password reset", () => requestPasswordReset("a@b.dev"), "/auth/forgot-password", { email: "a@b.dev" }],
      ["reset the password", () => resetPassword("tok", "nova-senha"), "/auth/reset-password", { token: "tok", newPassword: "nova-senha" }],
      ["end every session", () => revokeAllSessions(), "/auth/sessions/revoke-all", { confirm: true }],
      ["turn the second step off", () => disableMfa("senha", "123456"), "/auth/mfa/disable", { password: "senha", code: "123456" }],
    ])("%s reaches the right route with the right fields", async (_name, call, route, body) => {
      fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

      await call();

      expect(lastCall().url).toBe(`${API}${route}`);
      expect(lastCall().method).toBe("POST");
      expect(lastCall().body).toEqual(body);
    });

    it("a refused password reset rejects, so the page can say the link expired", async () => {
      fetchMock.mockResolvedValueOnce(json({ error: "Link inválido ou expirado." }, 400));

      await expect(resetPassword("tok", "nova-senha")).rejects.toBeInstanceOf(ApiError);
    });
  });
});
