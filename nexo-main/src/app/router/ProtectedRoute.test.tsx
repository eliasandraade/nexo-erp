import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { ProtectedRoute } from "./ProtectedRoute";
import { apiClient, getAccessToken, SUBSCRIPTION_REQUIRED_CODE, TOKEN_KEYS } from "@/services/api-client";
import type { AuthSession } from "@/modules/auth/types";

const useAuthMock = vi.fn();
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => useAuthMock() }));

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
    localStorage.clear();
    sessionStorage.clear();
    localStorage.setItem(TOKEN_KEYS.access, "tenant-access");
    vi.stubGlobal("fetch", vi.fn(async () => new Response(
      JSON.stringify({ error: "x", code: SUBSCRIPTION_REQUIRED_CODE, details: [] }),
      { status: 402, headers: { "Content-Type": "application/json" } },
    )));
  });

  afterEach(() => vi.unstubAllGlobals());

  it.each(["diretoria", "gerente"])("%s vai para a tela de assinatura, sem deslogar", async (role) => {
    renderAt(role);

    await blockedWrite();

    expect(screen.getByTestId("where").textContent).toBe("/assinatura?motivo=somente-leitura");
    expect(getAccessToken()).toBe("tenant-access");
  });

  // a tela de assinatura é só da gerência: levar os outros papéis até lá só os devolveria à tela inicial
  it.each(["vendedor", "estoquista", "cozinha"])("%s fica onde está, e a mensagem da chamada explica", async (role) => {
    renderAt(role);

    await blockedWrite();

    expect(screen.getByTestId("where").textContent).toBe("/clientes/novo");
    expect(getAccessToken()).toBe("tenant-access");
  });
});
