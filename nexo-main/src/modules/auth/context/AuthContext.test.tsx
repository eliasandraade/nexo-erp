import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, cleanup, render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { AuthProvider, useAuth } from "./AuthContext";
import LoginPage from "../pages/LoginPage";
import { toast } from "sonner";
import { apiClient, beginImpersonation, setTokens, TOKEN_KEYS } from "@/services/api-client";

const json = (body: unknown, status = 200, headers: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json", ...headers } });

const REVOKED = () => json({ error: "Sua sessão foi encerrada. Entre de novo.", code: "session_revoked" }, 401);
const REFRESH_REFUSED = () => json({ error: "Invalid or expired refresh token.", code: "session_ended" }, 401);
const SESSION = { userId: "u1", tenantId: "t1", name: "Ana", role: "gerente", login: "ana", email: "ana@x.dev", activeModules: [], storeIds: [], type: "tenant" };

function Where() {
  const location = useLocation();
  const { session } = useAuth();
  return <p data-testid="where">{`${location.pathname}${location.search}|${session ? "com sessão" : "sem sessão"}`}</p>;
}

function renderApp(path = "/usuarios") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>
          <Routes><Route path="*" element={<Where />} /></Routes>
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const where = () => screen.getByTestId("where").textContent;

describe("AuthProvider: a sessão que o servidor encerrou", () => {
  let fetchMock: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
    fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    setTokens("tenant-access", "tenant-refresh");
    localStorage.setItem(TOKEN_KEYS.session, JSON.stringify({ ...SESSION, modules: [] }));
  });

  afterEach(() => {
    cleanup();
    vi.unstubAllGlobals();
  });

  it("no meio do uso: leva ao login dizendo o motivo, sem deixar a pessoa numa página quebrada", async () => {
    fetchMock.mockResolvedValueOnce(json(SESSION));
    renderApp();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    fetchMock.mockResolvedValueOnce(REVOKED()).mockResolvedValueOnce(REFRESH_REFUSED());
    await act(async () => {
      await expect(apiClient.get("/users")).rejects.toMatchObject({ status: 401, code: "session_revoked" });
    });

    await waitFor(() => expect(where()).toBe("/login?motivo=sessao-revogada|sem sessão"));
    expect(fetchMock).toHaveBeenCalledTimes(3);
  });

  it("ao abrir o app: a validação em segundo plano não apaga o motivo", async () => {
    fetchMock.mockResolvedValueOnce(REVOKED()).mockResolvedValueOnce(REFRESH_REFUSED());

    renderApp();

    await waitFor(() => expect(where()).toBe("/login?motivo=sessao-revogada|sem sessão"));
    // a validação termina depois do aviso: a página continua a mesma
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });
    expect(where()).toBe("/login?motivo=sessao-revogada|sem sessão");
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("outra aba descartou a sessão compartilhada: esta também sai, sem precisar de um erro primeiro", async () => {
    fetchMock.mockResolvedValueOnce(json(SESSION));
    renderApp();
    await waitFor(() => expect(where()).toBe("/usuarios|com sessão"));

    act(() => {
      localStorage.removeItem(TOKEN_KEYS.access);
      globalThis.dispatchEvent(new StorageEvent("storage", { key: TOKEN_KEYS.access, newValue: null, storageArea: localStorage }));
    });

    await waitFor(() => expect(where()).toBe("/login?motivo=sessao-encerrada|sem sessão"));
  });

  it.each([
    ["429", () => json({ code: "rate_limited" }, 429, { "Retry-After": "600" }), /Muitas renovações/],
    ["503", () => json({ code: "session_renewal_unavailable" }, 503, { "Retry-After": "15" }), /não conseguiu renovar/],
    ["sem conexão", () => { throw new TypeError("Failed to fetch"); }, /Sem conexão/],
  ])("renovação sem veredito ao abrir o app (%s): a sessão fica, a pessoa fica na página e fica sabendo", async (_, answer, notice) => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
    const warned = vi.spyOn(toast, "warning");
    fetchMock.mockResolvedValueOnce(json({}, 401)).mockImplementationOnce(async () => answer());

    renderApp();

    await waitFor(() => expect(warned).toHaveBeenCalledTimes(1));
    expect(warned.mock.calls[0][0]).toMatch(notice);
    expect(warned.mock.calls[0][0]).toMatch(/Sua sessão continua ativa/);
    await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });
    expect(where()).toBe("/usuarios|com sessão");
    expect(localStorage.getItem(TOKEN_KEYS.refresh)).toBe("tenant-refresh");
    expect(fetchMock).toHaveBeenCalledTimes(2);
    vi.restoreAllMocks();
  });

  it("uma aba de acesso da plataforma ignora o que acontece com a sessão do administrador", async () => {
    fetchMock.mockResolvedValue(json(SESSION));
    beginImpersonation("imp-access", JSON.stringify({ ...SESSION, modules: [] }), { tenant: "Padaria", user: "Ana", expiresAt: null });
    renderApp();
    await waitFor(() => expect(where()).toBe("/usuarios|com sessão"));

    act(() => {
      globalThis.dispatchEvent(new StorageEvent("storage", { key: TOKEN_KEYS.access, newValue: null, storageArea: localStorage }));
    });

    expect(where()).toBe("/usuarios|com sessão");
  });

  it("quem não entrou não é levado a lugar nenhum", () => {
    localStorage.clear();
    renderApp("/esqueci-a-senha");

    act(() => {
      globalThis.dispatchEvent(new StorageEvent("storage", { key: TOKEN_KEYS.access, newValue: null, storageArea: localStorage }));
    });

    expect(where()).toBe("/esqueci-a-senha|sem sessão");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("LoginPage: o aviso de sessão encerrada", () => {
  const renderLogin = (path: string) => render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={[path]}>
        <AuthProvider>
          <LoginPage />
        </AuthProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );

  beforeEach(() => {
    localStorage.clear();
    sessionStorage.clear();
  });

  afterEach(cleanup);

  it("explica a revogação sem falar em erro", () => {
    renderLogin("/login?motivo=sessao-revogada");
    expect(screen.getByText("Seu acesso foi alterado e, por segurança, sua sessão foi encerrada. Entre de novo.")).toBeTruthy();
  });

  it("mantém o aviso genérico para os demais casos", () => {
    renderLogin("/login?motivo=sessao-encerrada");
    expect(screen.getByText("Sua sessão foi encerrada. Entre de novo.")).toBeTruthy();
  });
});
