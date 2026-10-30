import { describe, it, expect, vi, beforeEach, afterEach, type MockInstance } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { toast } from "sonner";
import { ApiError, TOKEN_KEYS } from "@/services/api-client";
import { requestImpersonation } from "@/services/impersonation-handoff";
import PlatformTenantDetailPage from "./PlatformTenantDetailPage";
import { polyfillRadix } from "../test/aiFixtures";

const { impersonate, resetPw, idle, tenant } = vi.hoisted(() => ({
  impersonate: { mutateAsync: vi.fn(), isPending: false },
  resetPw: { mutate: vi.fn(), isPending: false },
  idle: () => ({ mutate: vi.fn(), mutateAsync: vi.fn(), isPending: false }),
  tenant: {
    id: "t1", companyName: "Padaria Sol LTDA", tradeName: "Padaria Sol", taxId: "00.000.000/0001-00", email: "p@sol.com",
    status: "Active", slug: "padaria-sol", createdAt: "2026-01-01T00:00:00Z", subscriptions: [], stores: [],
    users: [{ id: "u1", name: "Ana Lima", email: "ana@sol.com", login: "ana", role: "Diretoria", status: "Active", lastAccessAt: null }],
  },
}));

vi.mock("../hooks/usePlatformTenants", () => ({
  usePlatformTenant: () => ({ data: tenant, isLoading: false }),
  useSetTenantStatus: idle, useUpdateTenant: idle, useGrantModule: idle, useRevokeModule: idle,
  useImpersonate: () => impersonate,
  useTenantNotes: () => ({ data: [] }), useCreateNote: idle, useDeleteNote: idle, useToggleNotePin: idle,
  useResetUserPassword: () => resetPw,
  useUserSessions: () => ({ data: [], isLoading: false }),
  useRevokeAllSessions: idle,
  usePlanHistory: () => ({ data: [], isLoading: false }),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

polyfillRadix();

const renderPage = () => render(
  <MemoryRouter initialEntries={["/platform/tenants/t1"]}>
    <Routes><Route path="/platform/tenants/:tenantId" element={<PlatformTenantDetailPage />} /></Routes>
  </MemoryRouter>
);

const grantResult = {
  accessToken: "imp-token", refreshToken: null, expiresAt: "2026-10-30T12:30:00Z",
  session: { userId: "u1", tenantId: "t1", companyName: "Padaria Sol", name: "Ana Lima", login: "ana", email: "ana@sol.com",
    role: "diretoria", storeId: "s1", storeIds: ["s1"], activeModules: ["varejo"], type: "tenant" as const },
};

describe("PlatformTenantDetailPage — impersonation and password reset", () => {
  let openSpy: MockInstance<typeof window.open>;
  const newTab = { opener: {} as unknown, close: vi.fn() };

  beforeEach(() => {
    localStorage.clear();
    localStorage.setItem(TOKEN_KEYS.access, "admin-access");
    localStorage.setItem(TOKEN_KEYS.refresh, "p.admin-refresh");
    newTab.close.mockReset();
    newTab.opener = {};
    openSpy = vi.spyOn(window, "open").mockReturnValue(newTab as unknown as Window);
  });
  afterEach(() => { cleanup(); openSpy.mockRestore(); vi.clearAllMocks(); });

  const askToImpersonate = (reason: string) => {
    fireEvent.click(screen.getByRole("button", { name: /Entrar como cliente/ }));
    fireEvent.change(screen.getByLabelText("Motivo do acesso"), { target: { value: reason } });
    fireEvent.click(screen.getAllByRole("button", { name: "Entrar como cliente" }).at(-1)!);
  };

  it("opens the tab before asking the server and hands the token over the channel, never through localStorage", async () => {
    let resolveServer!: (v: typeof grantResult) => void;
    impersonate.mutateAsync.mockImplementation(() => new Promise(r => { resolveServer = r; }));
    renderPage();

    askToImpersonate("Chamado #42: nota fiscal rejeitada");

    expect(openSpy).toHaveBeenCalledTimes(1);
    const url = String(openSpy.mock.calls[0][0]);
    const nonce = new URL(url, "http://x").searchParams.get("k")!;
    expect(url.startsWith("/impersonate?k=")).toBe(true);
    expect(nonce).toHaveLength(32);
    expect(newTab.opener).toBeNull();
    expect(impersonate.mutateAsync).toHaveBeenCalledWith({ tenantId: "t1", reason: "Chamado #42: nota fiscal rejeitada" });
    // the window was opened in the click, before the server answered
    expect(openSpy.mock.invocationCallOrder[0]).toBeLessThan(impersonate.mutateAsync.mock.invocationCallOrder[0]);

    const received = requestImpersonation(nonce, { timeoutMs: 2000 });
    resolveServer(grantResult);
    await expect(received).resolves.toEqual({ accessToken: "imp-token", expiresAt: grantResult.expiresAt, session: grantResult.session });

    const keys = Object.keys(localStorage);
    expect(keys.some(k => k.includes("impersonat"))).toBe(false);
    expect(localStorage.getItem(TOKEN_KEYS.access)).toBe("admin-access");
    expect(localStorage.getItem(TOKEN_KEYS.refresh)).toBe("p.admin-refresh");
    await waitFor(() => expect(screen.queryByLabelText("Motivo do acesso")).not.toBeInTheDocument());
  });

  it("closes the new tab and explains when the server refuses", async () => {
    impersonate.mutateAsync.mockRejectedValue(new ApiError(403, "Forbidden"));
    renderPage();
    askToImpersonate("Chamado #43: conferir estoque");
    await waitFor(() => expect(newTab.close).toHaveBeenCalled());
    expect(toast.error).toHaveBeenCalledWith("Esta ação exige um super administrador conectado com verificação em duas etapas.");
  });

  it("says so when the browser blocks the new tab", () => {
    openSpy.mockReturnValue(null);
    renderPage();
    askToImpersonate("Chamado #44: suporte ao caixa");
    expect(impersonate.mutateAsync).not.toHaveBeenCalled();
    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining("bloqueou a nova aba"));
  });

  it("resets a password only after confirming at least 8 characters typed twice", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: "Usuários" }));
    fireEvent.click(screen.getByTitle("Redefinir senha"));
    const submit = screen.getByRole("button", { name: "Redefinir senha" });
    fireEvent.change(screen.getByLabelText("Nova senha temporária"), { target: { value: "curta" } });
    fireEvent.change(screen.getByLabelText("Repita a senha"), { target: { value: "curta" } });
    expect(submit).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Nova senha temporária"), { target: { value: "Temporaria#1" } });
    fireEvent.change(screen.getByLabelText("Repita a senha"), { target: { value: "Temporaria#2" } });
    expect(screen.getByText("As senhas não coincidem.")).toBeInTheDocument();
    expect(submit).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Repita a senha"), { target: { value: "Temporaria#1" } });
    fireEvent.click(submit);
    expect(resetPw.mutate).toHaveBeenCalledWith({ userId: "u1", newPassword: "Temporaria#1" }, expect.anything());
  });
});
