import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import ModuleSelectionPage from "./ModuleSelectionPage";
import type { AuthSession } from "@/modules/auth/types";

const useAuthMock = vi.fn();
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => useAuthMock() }));
vi.mock("../WorkspaceContext", () => ({ useWorkspace: () => ({ setActive: vi.fn() }) }));

function renderWith(modules: string[], readOnlyModules: string[]) {
  const session = {
    userId: "u1", tenantId: "t1", name: "Pessoa", role: "diretoria", login: "pessoa", email: "p@orken.test", modules, readOnlyModules,
    storeIds: [], companyName: "ACME", type: "tenant", isNewAccount: false,
  } as AuthSession;
  useAuthMock.mockReturnValue({ session, logout: vi.fn() });
  render(
    <MemoryRouter initialEntries={["/workspaces"]}>
      <Routes>
        <Route path="/workspaces" element={<ModuleSelectionPage />} />
        <Route path="*" element={<p data-testid="other">outra tela</p>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ModuleSelectionPage, somente leitura", () => {
  beforeEach(() => vi.clearAllMocks());

  it("o módulo vencido entra, marcado como somente leitura, e o que a empresa nunca teve não", () => {
    renderWith(["build"], ["varejo"]);

    const store = screen.getByRole("button", { name: /Orken Store/ });
    expect(store.textContent).toMatch(/somente leitura/i);
    expect(store.textContent).toMatch(/Entrar/);
    const build = screen.getByRole("button", { name: /Orken Build/ });
    expect(build.textContent).not.toMatch(/somente leitura/i);
    expect(screen.queryByRole("button", { name: /Orken Menu/ })).toBeNull();
  });

  it("com um só módulo, vencido, vai direto para ele", () => {
    renderWith([], ["build"]);

    expect(screen.getByTestId("other")).toBeTruthy();
  });

  it("sem módulo nenhum, a tela explica e não fica vazia", () => {
    renderWith([], []);

    expect(screen.getByRole("heading", { level: 1 }).textContent).toBe("Nenhuma área liberada");
    expect(screen.getByRole("link", { name: "Falar com o suporte" })).toBeTruthy();
  });
});
