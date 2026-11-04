import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import AiCostsPage from "./AiCostsPage";
import * as api from "../../services/interpreterAdminApi";
import * as platformApi from "../../services/platformApi";
import { renderAi, polyfillRadix, costsOverview, costItem } from "../../test/aiFixtures";

vi.mock("../../services/interpreterAdminApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/interpreterAdminApi")>()),
  fetchAiCosts: vi.fn(),
  setTenantAiLimits: vi.fn(),
}));
vi.mock("../../services/platformApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../services/platformApi")>()),
  fetchPlatformTenants: vi.fn(),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

polyfillRadix();

const row = (name: string) => screen.getByRole("row", { name: `Empresa ${name}` });

async function openLimits(name: string) {
  fireEvent.click(await screen.findByRole("button", { name: `Editar limites de ${name}` }));
  return screen.findByRole("dialog");
}

describe("AiCostsPage", () => {
  beforeEach(() => {
    vi.mocked(api.fetchAiCosts).mockResolvedValue(costsOverview());
    vi.mocked(platformApi.fetchPlatformTenants).mockResolvedValue([]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("shows the month spend against the limits, the hit badges and what the limits really do", async () => {
    renderAi(<AiCostsPage />);

    const acme = await screen.findByRole("row", { name: "Empresa Acme Ltda" });
    expect(within(acme).getByText("Limite rígido atingido")).toBeInTheDocument();
    expect(within(acme).getByText("Limite suave atingido")).toBeInTheDocument();
    expect(within(acme).getByText("US$ 10,50")).toBeInTheDocument();
    expect(within(acme).getByText("+ US$ 0,25 em andamento")).toBeInTheDocument();
    expect(within(acme).getByText("US$ 5,00")).toBeInTheDocument();
    expect(within(acme).getByText("US$ 10,00")).toBeInTheDocument();
    // spent + in flight against the hard limit, capped at 100%
    expect(within(acme).getByRole("progressbar", { name: "Uso do mês de Acme Ltda" })).toHaveAttribute("aria-valuenow", "100");
    expect(within(acme).getByText("100% do limite rígido")).toBeInTheDocument();

    // a company with a limit and zero usage is listed; one without limit says so
    expect(within(row("Gama Serviços")).getByRole("progressbar")).toHaveAttribute("aria-valuenow", "0");
    expect(within(row("Beta Comércio")).getByText("Sem limite")).toBeInTheDocument();
    expect(within(row("Beta Comércio")).queryByText(/atingido/)).not.toBeInTheDocument();

    expect(screen.getByText("Gasto no mês (UTC)")).toBeInTheDocument();
    expect(screen.getByText(/chamadas pagas são recusadas antes de acontecer quando o gasto do mês/)).toBeInTheDocument();
    expect(screen.getByText(/no modo automático a análise cai para o leitor de regras, sem custo/)).toBeInTheDocument();
    expect(screen.getByText(/Limite suave:/).parentElement).toHaveTextContent("Limite suave: só sinaliza.");
    // no false claims left
    expect(screen.queryByText(/providers LLM ainda não ativos/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/100% do processamento é RuleBased/i)).not.toBeInTheDocument();
  });

  it("refuses a soft limit above the hard one before sending", async () => {
    renderAi(<AiCostsPage />);
    const dialog = await openLimits("Beta Comércio");

    fireEvent.change(within(dialog).getByLabelText(/Limite suave/), { target: { value: "20" } });
    fireEvent.change(within(dialog).getByLabelText(/Limite rígido/), { target: { value: "10" } });
    expect(within(dialog).getByText("O limite suave não pode ser maior que o limite rígido.")).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: /Salvar limites/ })).toBeDisabled();

    fireEvent.change(within(dialog).getByLabelText(/Limite rígido/), { target: { value: "-1" } });
    expect(within(dialog).getByText(/Limite rígido: informe um valor em US\$ maior ou igual a zero/)).toBeInTheDocument();

    fireEvent.change(within(dialog).getByLabelText(/Limite rígido/), { target: { value: "2000000" } });
    expect(within(dialog).getByText(/Limite rígido: o máximo é US\$ 1.000.000/)).toBeInTheDocument();
    expect(api.setTenantAiLimits).not.toHaveBeenCalled();
  });

  it("sets a hard limit only, keeping the soft one empty", async () => {
    vi.mocked(api.setTenantAiLimits).mockResolvedValue(costItem({ tenantId: "t-beta", tenantName: "Beta Comércio", hardLimitUsd: 25 }));
    renderAi(<AiCostsPage />);
    const dialog = await openLimits("Beta Comércio");

    fireEvent.change(within(dialog).getByLabelText(/Limite rígido/), { target: { value: "25" } });
    fireEvent.click(within(dialog).getByRole("button", { name: /Salvar limites/ }));

    await waitFor(() => expect(api.setTenantAiLimits).toHaveBeenCalledWith("t-beta", { softLimitUsd: null, hardLimitUsd: 25 }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(toast.success).toHaveBeenCalledWith("Limites de Beta Comércio salvos.");
    await waitFor(() => expect(api.fetchAiCosts).toHaveBeenCalledTimes(2));
  });

  it("sends PUT with nulls when both limits are cleared", async () => {
    vi.mocked(api.setTenantAiLimits).mockResolvedValue(costItem({ tenantId: "t-acme", tenantName: "Acme Ltda" }));
    renderAi(<AiCostsPage />);
    const dialog = await openLimits("Acme Ltda");

    expect(within(dialog).getByLabelText(/Limite suave/)).toHaveValue(5);
    expect(within(dialog).getByLabelText(/Limite rígido/)).toHaveValue(10);
    fireEvent.change(within(dialog).getByLabelText(/Limite suave/), { target: { value: "" } });
    fireEvent.change(within(dialog).getByLabelText(/Limite rígido/), { target: { value: "" } });
    fireEvent.click(within(dialog).getByRole("button", { name: /Salvar limites/ }));

    await waitFor(() => expect(api.setTenantAiLimits).toHaveBeenCalledWith("t-acme", { softLimitUsd: null, hardLimitUsd: null }));
  });

  it("explains a 403: the action needs a super admin signed in with two-step verification", async () => {
    vi.mocked(api.setTenantAiLimits).mockRejectedValue(new ApiError(403, "Forbidden"));
    renderAi(<AiCostsPage />);
    const dialog = await openLimits("Beta Comércio");

    fireEvent.change(within(dialog).getByLabelText(/Limite rígido/), { target: { value: "25" } });
    fireEvent.click(within(dialog).getByRole("button", { name: /Salvar limites/ }));

    const message = "Esta ação exige um super administrador conectado com verificação em duas etapas.";
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(message);
    expect(toast.error).toHaveBeenCalledWith(message);
    expect(screen.getByRole("dialog")).toBeInTheDocument();
  });

  it("shows the backend's 422 message", async () => {
    const msg = "O limite suave não pode passar do limite rígido.";
    vi.mocked(api.setTenantAiLimits).mockRejectedValue(new ApiError(422, msg, "ai_limit_order", "req-1", undefined, msg));
    renderAi(<AiCostsPage />);
    const dialog = await openLimits("Beta Comércio");
    fireEvent.change(within(dialog).getByLabelText(/Limite suave/), { target: { value: "1" } });
    fireEvent.click(within(dialog).getByRole("button", { name: /Salvar limites/ }));
    expect(await within(dialog).findByRole("alert")).toHaveTextContent(msg);
  });

  it("offers setting a limit for a company that is not listed yet", async () => {
    renderAi(<AiCostsPage />);
    fireEvent.click(await screen.findByRole("button", { name: /Definir limite para empresa/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByRole("combobox", { name: "Empresa" })).toBeInTheDocument();
    // nothing to save until a company is picked
    expect(within(dialog).getByLabelText(/Limite rígido/)).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: /Salvar limites/ })).toBeDisabled();
    await waitFor(() => expect(platformApi.fetchPlatformTenants).toHaveBeenCalled());
  });
});
