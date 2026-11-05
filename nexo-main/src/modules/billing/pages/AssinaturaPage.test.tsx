import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import AssinaturaPage from "./AssinaturaPage";

vi.mock("@/services/billing.api", () => ({
  listSubscriptions: vi.fn(async () => [
    // quem testou o varejo: esse módulo não tem preço, então não há o que comprar para ele
    { moduleKey: "varejo", status: "Trialing", planType: "Trial", currentPeriodStart: null, currentPeriodEnd: null, cancelAtPeriodEnd: false },
  ]),
  createCheckout: vi.fn(),
  createPortal: vi.fn(),
}));

function renderAt(path: string) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={[path]}>
        <AssinaturaPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("AssinaturaPage, modo somente leitura", () => {
  beforeEach(() => vi.clearAllMocks());

  it("explica o bloqueio e mantém o caminho de contato, sem prometer a compra do que não está à venda", async () => {
    renderAt("/assinatura?motivo=somente-leitura");

    const notice = await screen.findByRole("status");
    expect(notice.textContent).toMatch(/somente leitura/i);
    expect(notice.textContent).toMatch(/ver e exportar/i);
    expect(notice.textContent).not.toMatch(/varejo/i);
    expect(screen.getByRole("link", { name: "Falar com o suporte" }).getAttribute("href")).toBe("mailto:suporte@orken.com.br");
    expect(await screen.findByText("Gestão de Obras")).toBeTruthy();
    expect(screen.queryByText(/varejo/i)).toBeNull();
  });

  it("sem o motivo na URL, a tela segue como sempre", async () => {
    renderAt("/assinatura");

    expect(await screen.findByText("Gestão de Obras")).toBeTruthy();
    expect(screen.queryByRole("status")).toBeNull();
  });
});
