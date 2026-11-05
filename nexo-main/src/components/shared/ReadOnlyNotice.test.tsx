import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ReadOnlyNotice } from "./ReadOnlyNotice";
import type { AuthSession } from "@/modules/auth/types";
import type { UserRole } from "@/modules/users/types";

const useAuthMock = vi.fn();
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => useAuthMock() }));

function session(role: UserRole, modules: string[], readOnlyModules: string[] | undefined, type: "tenant" | "platform" = "tenant"): AuthSession {
  return {
    userId: "u1", tenantId: "t1", name: "Pessoa", role, login: "pessoa", email: "p@orken.test", modules, readOnlyModules,
    storeIds: [], companyName: "ACME", type, isNewAccount: false,
  };
}

function renderWith(s: AuthSession | null) {
  useAuthMock.mockReturnValue({ session: s });
  return render(<MemoryRouter><ReadOnlyNotice /></MemoryRouter>);
}

describe("ReadOnlyNotice", () => {
  beforeEach(() => vi.clearAllMocks());

  it.each(["diretoria", "gerente"] as UserRole[])("%s vê o aviso e o link para a assinatura", (role) => {
    renderWith(session(role, [], ["varejo"]));

    expect(screen.getByRole("status").textContent).toMatch(/somente leitura/i);
    expect(screen.getByRole("status").textContent).toMatch(/consultar e exportar/i);
    expect(screen.getByRole("link", { name: "Ver assinatura" }).getAttribute("href")).toBe("/assinatura?motivo=somente-leitura");
  });

  // a tela de assinatura é só da gerência: um link só devolveria os outros papéis à tela inicial
  it.each(["vendedor", "estoquista", "cozinha"] as UserRole[])("%s vê o aviso, sem link", (role) => {
    renderWith(session(role, [], ["varejo"]));

    expect(screen.getByRole("status").textContent).toMatch(/somente leitura/i);
    expect(screen.queryByRole("link")).toBeNull();
  });

  it("com um módulo ativo e outro vencido, diz qual ficou somente leitura e não fala da empresa inteira", () => {
    renderWith(session("diretoria", ["build"], ["varejo", "service"]));

    const text = screen.getByRole("status").textContent ?? "";
    expect(text).toMatch(/somente leitura/i);
    expect(text).toContain("Orken Store");
    expect(text).toContain("Orken Service");
    expect(text).not.toContain("Orken Build");
    expect(text).not.toMatch(/sua empresa está sem assinatura/i);
  });

  it("empresa sem módulo nenhum também está em somente leitura", () => {
    renderWith(session("diretoria", [], []));

    expect(screen.getByRole("status").textContent).toMatch(/somente leitura/i);
  });

  it("uma sessão guardada antes desta mudança, sem a lista de vencidos, não quebra", () => {
    renderWith(session("diretoria", ["varejo"], undefined));

    expect(screen.queryByRole("status")).toBeNull();
  });

  it("não aparece com módulo válido, para a plataforma, nem sem sessão", () => {
    const { unmount } = renderWith(session("diretoria", ["varejo"], []));
    expect(screen.queryByRole("status")).toBeNull();
    unmount();

    renderWith(session("diretoria", [], [], "platform")).unmount();
    expect(screen.queryByRole("status")).toBeNull();

    renderWith(null);
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("não promete compra", () => {
    renderWith(session("diretoria", [], ["varejo"]));

    expect(screen.getByRole("status").textContent).not.toMatch(/\b(assine|contrate|compre|comprar|planos)\b/i);
  });
});
