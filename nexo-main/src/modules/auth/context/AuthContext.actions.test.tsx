import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { act, cleanup, render, renderHook, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { AuthProvider, useAuth } from "./AuthContext";
import { beginImpersonation, getAccessToken, setStoredSession, setTokens, TOKEN_KEYS } from "@/services/api-client";
import type { AuthSession } from "../types";

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

const DTO = { userId: "u1", tenantId: "t1", name: "Ana", role: "gerente", login: "ana", email: "ana@x.dev", activeModules: [], storeIds: ["s1", "s2"], type: "tenant" };
const STORED = { ...DTO, modules: [] };

type Auth = ReturnType<typeof useAuth>;
let auth: Auth;

function Probe() {
  auth = useAuth();
  const location = useLocation();
  return (
    <p data-testid="where">
      {`${location.pathname}${location.search}|${auth.session ? auth.session.name : "sem sessão"}|${auth.isReady ? "pronto" : "validando"}`}
    </p>
  );
}

function renderApp(client: QueryClient, path = "/usuarios") {
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>
          <Routes><Route path="*" element={<Probe />} /></Routes>
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const where = () => screen.getByTestId("where").textContent;
const cachedOfTheLastPerson = (client: QueryClient) => client.getQueryData(["customers"]);

describe("AuthProvider, what the session actions do", () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let client: QueryClient;

  const signedIn = () => {
    setTokens("tenant-access", "tenant-refresh");
    setStoredSession(JSON.stringify(STORED));
  };

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    client.setQueryData(["customers"], ["cliente da sessão anterior"]);
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  describe("at boot", () => {
    it("with no stored session it is ready at once and asks the server nothing", async () => {
      renderApp(client);

      await waitFor(() => expect(where()).toBe("/usuarios|sem sessão|pronto"));
      expect(fetchMock).not.toHaveBeenCalled();
    });

    it("a network failure keeps the stored session and never leaves the app stuck on not ready", async () => {
      signedIn();
      fetchMock.mockRejectedValueOnce(new TypeError("Failed to fetch"));

      renderApp(client);

      await waitFor(() => expect(where()).toBe("/usuarios|Ana|pronto"));
      expect(getAccessToken()).toBe("tenant-access");
    });

    it("a server error says nothing about the session either", async () => {
      signedIn();
      fetchMock.mockResolvedValueOnce(json({ error: "Erro inesperado." }, 500));

      renderApp(client);

      await waitFor(() => expect(where()).toBe("/usuarios|Ana|pronto"));
    });

    it("a platform administrator on a company route is taken to the platform", async () => {
      signedIn();
      fetchMock.mockResolvedValueOnce(json({ ...DTO, type: "platform" }));

      renderApp(client);

      await waitFor(() => expect(where()).toBe("/platform|Ana|pronto"));
    });

    it("the entry page of a platform access starts from no session at all, not the administrator's", async () => {
      signedIn();
      window.history.pushState({}, "", "/impersonate");

      try {
        renderApp(client, "/impersonate");

        await waitFor(() => expect(where()).toBe("/impersonate|sem sessão|pronto"));
        expect(fetchMock).not.toHaveBeenCalled();
      } finally {
        window.history.pushState({}, "", "/");
      }
    });
  });

  describe("login", () => {
    it("takes the session and answers its type", async () => {
      renderApp(client);
      fetchMock.mockResolvedValueOnce(json({ accessToken: "a", refreshToken: "r", session: DTO }));

      let answer: Awaited<ReturnType<Auth["login"]>> | undefined;
      await act(async () => { answer = await auth.login({ login: "ana", password: "x" }); });

      expect(answer).toEqual({ error: null, type: "tenant" });
      expect(where()).toContain("Ana");
    });

    it("refused, answers the reason and stays without a session", async () => {
      renderApp(client);
      fetchMock.mockResolvedValueOnce(json({ error: "Invalid login or password." }, 401));

      let answer: Awaited<ReturnType<Auth["login"]>> | undefined;
      await act(async () => { answer = await auth.login({ login: "ana", password: "x" }); });

      expect(answer?.error).toBe("Login ou senha incorretos.");
      expect(answer?.type).toBeNull();
      expect(where()).toContain("sem sessão");
    });

    it("with the second step on, hands back the challenge and stays without a session", async () => {
      renderApp(client);
      fetchMock.mockResolvedValueOnce(json({ mfaRequired: true, mfaChallenge: "challenge-1" }));

      let answer: Awaited<ReturnType<Auth["login"]>> & { mfaChallenge?: string } | undefined;
      await act(async () => { answer = await auth.login({ login: "ana", password: "x" }); });

      expect(answer?.mfaChallenge).toBe("challenge-1");
      expect(where()).toContain("sem sessão");
    });
  });

  describe("logout", () => {
    it("leaves for the login with no trace of the session or of its cached data", async () => {
      signedIn();
      fetchMock.mockResolvedValueOnce(json(DTO));
      renderApp(client);
      await waitFor(() => expect(where()).toContain("pronto"));
      fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

      act(() => auth.logout());

      expect(where()).toBe("/login|sem sessão|pronto");
      expect(getAccessToken()).toBeNull();
      expect(cachedOfTheLastPerson(client)).toBeUndefined();
    });

    it("works the same with the server out of reach", async () => {
      signedIn();
      fetchMock.mockResolvedValueOnce(json(DTO));
      renderApp(client);
      await waitFor(() => expect(where()).toContain("pronto"));
      fetchMock.mockRejectedValueOnce(new TypeError("Failed to fetch"));

      await act(async () => { auth.logout(); });

      expect(where()).toBe("/login|sem sessão|pronto");
      expect(getAccessToken()).toBeNull();
    });

    it("in a platform access tab ends the access on the server and keeps the administrator's own session", async () => {
      setTokens("admin-access", "admin-refresh");
      setStoredSession(JSON.stringify({ ...STORED, name: "Administrador" }));
      const adminAccess = localStorage.getItem(TOKEN_KEYS.access);
      beginImpersonation("imp-token", JSON.stringify(STORED), { tenant: "T", user: "Ana", expiresAt: null });
      fetchMock.mockResolvedValueOnce(json(DTO));
      renderApp(client);
      await waitFor(() => expect(where()).toContain("pronto"));
      fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));

      await act(async () => { auth.logout(); });

      await waitFor(() => expect(where()).toBe("/acesso-encerrado?motivo=saiu|sem sessão|pronto"));
      const [url] = fetchMock.mock.calls.at(-1) as [string];
      expect(url).toContain("/auth/impersonation/end");
      expect(localStorage.getItem(TOKEN_KEYS.access)).toBe(adminAccess);
    });

    it("in a platform access tab leaves the access even if the server cannot be told", async () => {
      beginImpersonation("imp-token", JSON.stringify(STORED), { tenant: "T", user: "Ana", expiresAt: null });
      fetchMock.mockResolvedValueOnce(json(DTO));
      renderApp(client);
      await waitFor(() => expect(where()).toContain("pronto"));
      fetchMock.mockRejectedValueOnce(new TypeError("Failed to fetch"));

      await act(async () => { auth.logout(); });

      await waitFor(() => expect(where()).toBe("/acesso-encerrado?motivo=saiu|sem sessão|pronto"));
    });
  });

  describe("dropping the session the server already ended", () => {
    it("drops it without calling the server and goes where the page says", async () => {
      signedIn();
      fetchMock.mockResolvedValueOnce(json(DTO));
      renderApp(client);
      await waitFor(() => expect(where()).toContain("pronto"));
      const callsBefore = fetchMock.mock.calls.length;

      act(() => auth.endLocalSession("/login?motivo=sessao-revogada"));

      expect(where()).toBe("/login?motivo=sessao-revogada|sem sessão|pronto");
      expect(getAccessToken()).toBeNull();
      expect(cachedOfTheLastPerson(client)).toBeUndefined();
      expect(fetchMock.mock.calls.length).toBe(callsBefore);
    });

    it("goes to the login when the page names nothing", async () => {
      signedIn();
      fetchMock.mockResolvedValueOnce(json(DTO));
      renderApp(client);
      await waitFor(() => expect(where()).toContain("pronto"));

      act(() => auth.endLocalSession());

      expect(where()).toBe("/login|sem sessão|pronto");
    });
  });

  describe("switching store", () => {
    it("takes the new session and clears the data of the old store", async () => {
      signedIn();
      fetchMock.mockResolvedValueOnce(json(DTO));
      renderApp(client);
      await waitFor(() => expect(where()).toContain("pronto"));
      fetchMock.mockResolvedValueOnce(json({ accessToken: "a2", refreshToken: "r2", session: { ...DTO, name: "Ana na loja 2", storeId: "s2" } }));

      await act(async () => { await auth.switchStore("s2"); });

      expect(where()).toContain("Ana na loja 2");
      expect(cachedOfTheLastPerson(client)).toBeUndefined();
    });

    it("refused, rejects for the screen and keeps the session and the data", async () => {
      signedIn();
      fetchMock.mockResolvedValueOnce(json(DTO));
      renderApp(client);
      await waitFor(() => expect(where()).toContain("pronto"));
      fetchMock.mockResolvedValueOnce(json({ error: "Sem acesso a esta loja." }, 403));

      await act(async () => {
        await expect(auth.switchStore("s9")).rejects.toThrow("Sem acesso a esta loja.");
      });

      expect(where()).toContain("Ana");
      expect(cachedOfTheLastPerson(client)).toEqual(["cliente da sessão anterior"]);
    });
  });

  it("the verification of the e-mail signs the person in", async () => {
    renderApp(client);
    await waitFor(() => expect(where()).toContain("pronto"));

    act(() => auth.setSessionFromVerify({ ...STORED, name: "Verificada" } as unknown as AuthSession));

    expect(where()).toContain("Verificada");
  });

  it("useAuth outside the provider says so, instead of failing later with no session", () => {
    const silent = vi.spyOn(console, "error").mockImplementation(() => undefined);

    expect(() => renderHook(() => useAuth())).toThrow("useAuth must be used inside AuthProvider");

    silent.mockRestore();
  });
});
