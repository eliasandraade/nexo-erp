import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { createCheckout, createPortal, getCheckoutAvailability, listSubscriptions } from "@/services/billing.api";
import AssinaturaPage from "./AssinaturaPage";

vi.mock("@/services/billing.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/billing.api")>()),
  listSubscriptions: vi.fn(),
  getCheckoutAvailability: vi.fn(),
  createCheckout: vi.fn(),
  createPortal: vi.fn(),
}));

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

// quem testou o varejo: esse módulo não tem preço, então não há o que comprar para ele
const trialDoVarejo = {
  moduleKey: "varejo", status: "Trialing", planType: "Trial", currentPeriodStart: null, currentPeriodEnd: null,
  cancelAtPeriodEnd: false, stripeSubscriptionId: null,
};

const assinaturaViva = {
  moduleKey: "build", status: "Active", planType: "Monthly", currentPeriodStart: "2026-11-01T00:00:00Z",
  currentPeriodEnd: "2026-12-01T00:00:00Z", cancelAtPeriodEnd: false, stripeSubscriptionId: "sub_viva",
};

const SUPORTE = "mailto:suporte@orken.com.br";
const AVISO_BLOQUEIO = /contratação de novos módulos indisponível/i;
const botaoAssinar = () => screen.queryAllByRole("button", { name: /^Assinar/ });

function renderAt(path: string) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter initialEntries={[path]}>
        <AssinaturaPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(listSubscriptions).mockResolvedValue([trialDoVarejo]);
  vi.mocked(getCheckoutAvailability).mockResolvedValue({ newCheckoutsEnabled: true });
});

describe("AssinaturaPage, modo somente leitura", () => {
  it("explica o bloqueio e mantém o caminho de contato, sem prometer a compra do que não está à venda", async () => {
    renderAt("/assinatura?motivo=somente-leitura");

    const notice = await screen.findByRole("status");
    expect(notice.textContent).toMatch(/somente leitura/i);
    expect(notice.textContent).toMatch(/ver e exportar/i);
    expect(notice.textContent).not.toMatch(/varejo/i);
    expect(screen.getByRole("link", { name: "Falar com o suporte" }).getAttribute("href")).toBe(SUPORTE);
    expect(await screen.findByText("Gestão de Obras")).toBeTruthy();
    expect(screen.queryByText(/varejo/i)).toBeNull();
  });

  it("sem o motivo na URL, a tela segue como sempre", async () => {
    renderAt("/assinatura");

    expect(await screen.findByText("Gestão de Obras")).toBeTruthy();
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("com a contratação bloqueada, não diz que há módulos para contratar", async () => {
    vi.mocked(getCheckoutAvailability).mockResolvedValue({ newCheckoutsEnabled: false });

    renderAt("/assinatura?motivo=somente-leitura");

    expect(await screen.findByText(AVISO_BLOQUEIO)).toBeTruthy();
    const avisos = screen.getAllByRole("status");
    expect(avisos.map((a) => a.textContent).join(" ")).toMatch(/somente leitura/i);
    expect(avisos.map((a) => a.textContent).join(" ")).not.toMatch(/disponíveis para contratação/i);
    expect(botaoAssinar()).toHaveLength(0);
  });
});

describe("AssinaturaPage, contratação de novos módulos", () => {
  it("liberada pelo servidor, oferece assinar e abre o checkout", async () => {
    vi.mocked(listSubscriptions).mockResolvedValue([]);
    vi.mocked(createCheckout).mockResolvedValue({ sessionId: "cs_1", checkoutUrl: "#checkout-de-teste" });

    renderAt("/assinatura");

    const buttons = await screen.findAllByRole("button", { name: /^Assinar/ });
    expect(buttons).toHaveLength(2);
    expect(screen.queryByText(AVISO_BLOQUEIO)).toBeNull();
    expect(screen.getByText(/Clique em "Assinar"/)).toBeTruthy();
    expect(screen.getByRole("button", { name: "Mensal" })).toBeTruthy();
    fireEvent.click(buttons[0]);
    await waitFor(() => expect(createCheckout).toHaveBeenCalledWith("restaurante", "monthly"));
  });

  it("bloqueada pelo servidor, tira o botão e mostra o aviso com o contato de suporte, sem preço", async () => {
    vi.mocked(listSubscriptions).mockResolvedValue([]);
    vi.mocked(getCheckoutAvailability).mockResolvedValue({ newCheckoutsEnabled: false });

    renderAt("/assinatura");

    const notice = (await screen.findByText(AVISO_BLOQUEIO)).closest("[role=status]");
    expect(notice).toBeTruthy();
    expect(notice?.querySelector("a")?.getAttribute("href")).toBe(SUPORTE);
    expect(botaoAssinar()).toHaveLength(0);
    expect(screen.queryByRole("button", { name: "Mensal" })).toBeNull();
    expect(screen.queryByText(/Clique em "Assinar"/)).toBeNull();
    expect(document.body.textContent).not.toMatch(/R\$/);
    expect(await screen.findByText("Gestão de Obras")).toBeTruthy();
    expect(createCheckout).not.toHaveBeenCalled();
  });

  it("bloqueada, quem tem assinatura continua vendo o status e gerenciando no portal", async () => {
    vi.mocked(listSubscriptions).mockResolvedValue([assinaturaViva]);
    vi.mocked(getCheckoutAvailability).mockResolvedValue({ newCheckoutsEnabled: false });
    vi.mocked(createPortal).mockResolvedValue({ portalUrl: "#portal-de-teste" });

    renderAt("/assinatura");

    expect(await screen.findByText("Ativa")).toBeTruthy();
    expect(screen.getByText(AVISO_BLOQUEIO)).toBeTruthy();
    expect(botaoAssinar()).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: "Gerenciar assinatura" }));
    await waitFor(() => expect(createPortal).toHaveBeenCalledTimes(1));
  });

  it("se a leitura falha, falha fechado: sem botão e com o aviso", async () => {
    vi.mocked(getCheckoutAvailability).mockRejectedValue(new ApiError(500, "Erro interno"));

    renderAt("/assinatura");

    expect(await screen.findByText(AVISO_BLOQUEIO)).toBeTruthy();
    expect(botaoAssinar()).toHaveLength(0);
  });

  it.each([
    ["campo ausente", {}],
    ["texto em vez de booleano", { newCheckoutsEnabled: "true" }],
    ["nulo", { newCheckoutsEnabled: null }],
  ])("só o booleano true libera: %s continua bloqueado", async (_, resposta) => {
    vi.mocked(getCheckoutAvailability).mockResolvedValue(resposta as never);

    renderAt("/assinatura");

    expect(await screen.findByText(AVISO_BLOQUEIO)).toBeTruthy();
    expect(botaoAssinar()).toHaveLength(0);
  });

  it("enquanto a leitura não responde, não oferece o botão nem acusa o bloqueio", async () => {
    vi.mocked(getCheckoutAvailability).mockReturnValue(new Promise(() => {}));

    renderAt("/assinatura");

    await waitFor(() => expect(listSubscriptions).toHaveBeenCalled());
    await waitFor(() => expect(screen.queryByText("Módulos ativos e condições da assinatura.")).toBeTruthy());
    expect(botaoAssinar()).toHaveLength(0);
    expect(screen.queryByText(AVISO_BLOQUEIO)).toBeNull();
    expect(screen.queryByText("Gestão de Obras")).toBeNull();
  });

  it("o 403 checkout_disabled mostra a mensagem do servidor e fecha a contratação na tela", async () => {
    const mensagem = "A contratação está indisponível. Fale com o suporte.";
    vi.mocked(createCheckout).mockRejectedValue(new ApiError(403, mensagem, "checkout_disabled"));

    renderAt("/assinatura");

    fireEvent.click((await screen.findAllByRole("button", { name: /^Assinar/ }))[0]);

    expect(await screen.findByText(AVISO_BLOQUEIO)).toBeTruthy();
    expect(toast.error).toHaveBeenCalledWith(mensagem);
    expect(botaoAssinar()).toHaveLength(0);
  });

  it("outro erro do checkout não fecha a contratação", async () => {
    vi.mocked(createCheckout).mockRejectedValue(new ApiError(502, "Erro ao comunicar com Stripe. Tente novamente."));

    renderAt("/assinatura");

    fireEvent.click((await screen.findAllByRole("button", { name: /^Assinar/ }))[0]);

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Erro ao comunicar com Stripe. Tente novamente."));
    expect(botaoAssinar()).toHaveLength(2);
    expect(screen.queryByText(AVISO_BLOQUEIO)).toBeNull();
  });

  it("com o billing desligado (404), segue a tela de configuração, sem o aviso de contratação", async () => {
    vi.mocked(listSubscriptions).mockRejectedValue(new ApiError(404, "Billing não está habilitado."));
    vi.mocked(getCheckoutAvailability).mockRejectedValue(new ApiError(404, "Billing não está habilitado."));

    renderAt("/assinatura");

    expect(await screen.findByText("Billing em configuração")).toBeTruthy();
    expect(screen.queryByText(AVISO_BLOQUEIO)).toBeNull();
    expect(botaoAssinar()).toHaveLength(0);
  });
});
