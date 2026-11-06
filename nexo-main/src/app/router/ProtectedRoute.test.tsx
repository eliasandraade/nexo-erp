import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { toast } from "sonner";
import { ProtectedRoute } from "./ProtectedRoute";
import { apiClient, getAccessToken, SUBSCRIPTION_REQUIRED_CODE, TOKEN_KEYS } from "@/services/api-client";
import type { AuthSession } from "@/modules/auth/types";

const useAuthMock = vi.fn();
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => useAuthMock() }));
vi.mock("sonner", () => ({ toast: { warning: vi.fn() } }));

const warning = vi.mocked(toast.warning);

function Where() {
  const { pathname, search } = useLocation();
  return <p data-testid="where">{pathname + search}</p>;
}

function renderAt(role: string, path = "/clientes/novo") {
  const session = { type: "tenant", role, modules: ["varejo"], trialEndsAt: null, requirePasswordChange: false } as unknown as AuthSession;
  useAuthMock.mockReturnValue({ session, isReady: true });
  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route element={<ProtectedRoute />}>
          <Route path="*" element={<Where />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

const blockedWrite = () => act(async () => {
  await apiClient.post("/customers", {}).catch(() => undefined);
});

describe("ProtectedRoute, escrita recusada por falta de assinatura", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem(TOKEN_KEYS.access, "tenant-access");
    vi.stubGlobal("fetch", vi.fn(async () => new Response(
      JSON.stringify({ error: "x", code: SUBSCRIPTION_REQUIRED_CODE, details: [] }),
      { status: 402, headers: { "Content-Type": "application/json" } },
    )));
  });

  afterEach(() => vi.unstubAllGlobals());

  // quem está consultando não perde a tela: a recusa é um aviso, e a sessão continua
  it.each(["diretoria", "gerente", "vendedor", "estoquista", "cozinha"])("%s fica na tela em que está, sem deslogar", async (role) => {
    renderAt(role);

    await blockedWrite();

    expect(screen.getByTestId("where").textContent).toBe("/clientes/novo");
    expect(getAccessToken()).toBe("tenant-access");
    expect(warning).toHaveBeenCalledTimes(1);
  });

  it.each(["diretoria", "gerente"])("%s recebe um link para a assinatura, que só abre se ele clicar", async (role) => {
    renderAt(role);

    await blockedWrite();

    const options = warning.mock.calls[0][1] as { action?: { label: string; onClick: () => void } };
    expect(options.action?.label).toBe("Ver assinatura");
    expect(screen.getByTestId("where").textContent).toBe("/clientes/novo");

    act(() => options.action?.onClick());

    expect(screen.getByTestId("where").textContent).toBe("/assinatura?motivo=somente-leitura");
  });

  // a tela de assinatura é só da gerência: um link só devolveria os outros papéis à tela inicial
  it.each(["vendedor", "estoquista", "cozinha"])("%s recebe o aviso sem link", async (role) => {
    renderAt(role);

    await blockedWrite();

    expect((warning.mock.calls[0][1] as { action?: unknown }).action).toBeUndefined();
  });

  it("recusas repetidas não empilham avisos: todas usam o mesmo id", async () => {
    renderAt("diretoria");

    await blockedWrite();
    await blockedWrite();

    expect(new Set(warning.mock.calls.map((c) => (c[1] as { id?: string }).id)).size).toBe(1);
  });
});
