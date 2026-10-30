import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor } from "@testing-library/react";
import { toast } from "sonner";
import { opportunity, renderTax } from "../test/render";
import { ENGINE_SCENARIOS } from "../test/engineFixture";
import TaxSimulationsPage from "./TaxSimulationsPage";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";
import { downloadFile } from "@/services/pdf.api";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@/services/pdf.api", () => ({ downloadFile: vi.fn().mockResolvedValue(undefined) }));
vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  listTaxSimulations: vi.fn(),
  getTaxSimulation: vi.fn(),
  createTaxSimulation: vi.fn(),
  compareSuppliers: vi.fn(),
}));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const saved: api.TaxSimulationSummaryDto[] = [
  { id: "s1", name: "Mudar para Lucro Presumido", scenarios: 2, createdAt: "2026-10-20T12:00:00Z", createdByName: "Diretora Exemplo" },
  { id: "s2", name: "Desconto incondicional", scenarios: 1, createdAt: "2026-10-21T12:00:00Z", createdByName: null },
];
const page = (items: api.TaxSimulationSummaryDto[], total = items.length): api.Page<api.TaxSimulationSummaryDto> => ({ items, total, skip: 0, take: 20 });
const simulation = (over: Partial<api.TaxSimulationDto> = {}): api.TaxSimulationDto => ({
  id: "s1", name: "Mudar para Lucro Presumido", engineVersion: "orken-tax-engine/1.0.0", createdAt: "2026-10-20T12:00:00Z",
  scenarios: ENGINE_SCENARIOS, assumptions: "Preços mantidos", caveats: "Sem revisão profissional", createdByName: "Diretora Exemplo",
  governance: opportunity().governance, ...over,
});

const one = (selector: string) => document.querySelector<HTMLInputElement>(selector)!;
const all = (selector: string) => Array.from(document.querySelectorAll<HTMLInputElement>(selector));

/** Fills the "como é hoje" operation with one valid line. */
function fillBaseline() {
  fireEvent.change(one('[id^="sim-base-l-line-"][id$="-desc"]'), { target: { value: "Camiseta" } });
  fireEvent.change(one('[id^="sim-base-l-line-"][id$="-gross"]'), { target: { value: "100,00" } });
}

describe("TaxSimulationsPage", { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.listTaxSimulations).mockResolvedValue(page(saved));
    vi.mocked(api.getTaxSimulation).mockResolvedValue(simulation());
  });
  afterEach(cleanup);

  it("lists the saved simulations with who ran them, searches and pages through the server", async () => {
    vi.mocked(api.listTaxSimulations).mockResolvedValue(page(saved, 45));
    renderTax(<TaxSimulationsPage />);

    expect(await screen.findByText(/2 cenários · .* · Diretora Exemplo/)).toBeInTheDocument();
    expect(screen.getByText(/^1 cenário · /)).not.toHaveTextContent("Diretora");
    expect(api.listTaxSimulations).toHaveBeenLastCalledWith("", 0, 20);

    fireEvent.click(screen.getByText("2"));
    await waitFor(() => expect(api.listTaxSimulations).toHaveBeenLastCalledWith("", 20, 20));
    fireEvent.change(screen.getByLabelText("Buscar"), { target: { value: "presumido" } });
    await waitFor(() => expect(api.listTaxSimulations).toHaveBeenLastCalledWith("presumido", 0, 20));
  });

  it("tells no simulation apart from no simulation with that name, and says when they cannot be listed", async () => {
    vi.mocked(api.listTaxSimulations).mockResolvedValue(page([]));
    renderTax(<TaxSimulationsPage />);
    expect(await screen.findByText("Nenhuma simulação ainda")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Buscar"), { target: { value: "xyz" } });
    expect(await screen.findByText("Nenhuma simulação com este nome")).toBeInTheDocument();
    cleanup();

    vi.mocked(api.listTaxSimulations).mockRejectedValue(new Error("rede"));
    renderTax(<TaxSimulationsPage />);
    expect(await screen.findByText("Não foi possível carregar as simulações")).toBeInTheDocument();
  });

  it("opens a saved simulation with its assumptions, caveats, rule approval and CSV export", async () => {
    renderTax(<TaxSimulationsPage />);

    fireEvent.click(await screen.findByRole("button", { name: /Mudar para Lucro Presumido/ }));
    expect(await screen.findByText("Preços mantidos")).toBeInTheDocument();
    expect(screen.getByText("Sem revisão profissional")).toBeInTheDocument();
    expect(screen.getByText("Premissas")).toBeInTheDocument();
    expect(screen.getByText("Ressalvas")).toBeInTheDocument();
    expect(screen.getByText("Com qual aprovação das regras (registrado quando rodou)")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Mudar para Lucro Presumido/ })).toHaveAttribute("aria-current", "true");
    expect(api.getTaxSimulation).toHaveBeenCalledWith("s1");

    fireEvent.click(screen.getByRole("button", { name: "Exportar CSV" }));
    await waitFor(() => expect(downloadFile).toHaveBeenCalledWith("/tax/simulations/s1/export", "simulacao-s1.csv", "Falha ao baixar"));

    fireEvent.click(screen.getByRole("button", { name: "Nova simulação" }));
    expect(await screen.findByText("Descreva a operação como ela é hoje e os cenários alternativos.")).toBeInTheDocument();
  });

  it("shows a simulation without assumptions, caveats or governance, and says when it cannot be opened", async () => {
    vi.mocked(api.getTaxSimulation).mockResolvedValueOnce(simulation({ assumptions: null, caveats: null, governance: null }));
    renderTax(<TaxSimulationsPage />, { route: "/?id=s1" });
    expect(await screen.findByRole("button", { name: "Exportar CSV" })).toBeInTheDocument();
    expect(screen.queryByText("Premissas")).not.toBeInTheDocument();
    expect(screen.queryByText(/Com qual aprovação das regras/)).not.toBeInTheDocument();
    cleanup();

    vi.mocked(api.getTaxSimulation).mockRejectedValueOnce(new ApiError(404, "não"));
    renderTax(<TaxSimulationsPage />, { route: "/?id=s9" });
    expect(await screen.findByText("A simulação não encontrado(a)")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Exportar CSV" })).not.toBeInTheDocument();
  });

  it("duplicates a scenario with its hypotheses and sends the assumptions and caveats", async () => {
    vi.mocked(api.createTaxSimulation).mockResolvedValue(simulation({ id: "s3", name: "Teste" }));
    renderTax(<TaxSimulationsPage />);

    fireEvent.click(await screen.findByRole("button", { name: "Simular" }));
    expect(screen.getByText("Dê um nome à simulação.")).toBeInTheDocument();
    expect(api.createTaxSimulation).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Nome da simulação"), { target: { value: " Teste " } });
    fireEvent.change(screen.getByLabelText("Premissas (opcional)"), { target: { value: "  Preços mantidos  " } });
    fireEvent.change(screen.getByLabelText("Ressalvas (opcional)"), { target: { value: "Sem revisão profissional" } });
    fillBaseline();
    fireEvent.change(one('[id^="sc-"][id$="-name"]'), { target: { value: "Presumido" } });
    fireEvent.click(screen.getByRole("button", { name: "Alíquota hipotética" }));
    fireEvent.change(one('[id^="hy-"][id$="-rate"]'), { target: { value: "1,5" } });
    fireEvent.change(one('[id^="hy-"][id$="-label"]'), { target: { value: "Estimativa 2027" } });

    fireEvent.click(screen.getByRole("button", { name: "Duplicar cenário" }));
    expect(screen.getByText("Cenários (2 de 10)")).toBeInTheDocument();
    const names = all('[id^="sc-"][id$="-name"]');
    expect(names.map((n) => n.value)).toEqual(["Presumido", "Presumido (cópia)"]);
    const rates = all('[id^="hy-"][id$="-rate"]');
    expect(rates.map((r) => r.value)).toEqual(["1,5", "1,5"]);
    expect(new Set(rates.map((r) => r.id)).size).toBe(2);

    // The copy is independent: changing it leaves the original untouched.
    fireEvent.change(rates[1], { target: { value: "2" } });
    expect(all('[id^="hy-"][id$="-rate"]')[0]).toHaveValue("1,5");

    fireEvent.click(screen.getByRole("button", { name: "Simular" }));
    await waitFor(() => expect(api.createTaxSimulation).toHaveBeenCalledTimes(1));
    const body = vi.mocked(api.createTaxSimulation).mock.calls[0][0];
    expect(body).toMatchObject({ name: "Teste", assumptions: "Preços mantidos", caveats: "Sem revisão profissional" });
    expect(body.scenarios.map((s) => [s.name, s.rateHypotheses])).toEqual([
      ["Presumido", [{ tax: "Cbs", ratePercent: 1.5, label: "Estimativa 2027" }]],
      ["Presumido (cópia)", [{ tax: "Cbs", ratePercent: 2, label: "Estimativa 2027" }]],
    ]);
    // Opening the new simulation is a navigation, then a fetch: give it more than the default second under a loaded run.
    expect(await screen.findByRole("button", { name: "Exportar CSV" }, { timeout: 5_000 })).toBeInTheDocument();
    expect(api.getTaxSimulation).toHaveBeenCalledWith("s3");
  });

  it("sends no assumptions or caveats when left empty, and removes scenarios and hypotheses", async () => {
    vi.mocked(api.createTaxSimulation).mockRejectedValueOnce(new ApiError(422, "Regras sem aprovação.", "tax_rules_not_approved"));
    renderTax(<TaxSimulationsPage />);

    await screen.findByRole("button", { name: "Simular" });
    fireEvent.change(screen.getByLabelText("Nome da simulação"), { target: { value: "Teste" } });
    fillBaseline();
    fireEvent.change(one('[id^="sc-"][id$="-name"]'), { target: { value: "A" } });
    expect(screen.getByRole("button", { name: "Remover cenário" })).toBeDisabled();
    fireEvent.click(screen.getByRole("button", { name: "Adicionar cenário" }));
    fireEvent.click(screen.getAllByRole("button", { name: "Remover cenário" })[1]);
    fireEvent.click(screen.getByRole("button", { name: "Alíquota hipotética" }));
    fireEvent.click(screen.getByRole("button", { name: "Remover alíquota hipotética" }));
    expect(all('[id^="hy-"][id$="-rate"]')).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: "Simular" }));

    await waitFor(() => expect(api.createTaxSimulation).toHaveBeenCalledWith(expect.objectContaining({
      name: "Teste", assumptions: null, caveats: null, scenarios: [expect.objectContaining({ name: "A", rateHypotheses: null })],
    })));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(expect.any(String), { description: "Regras sem aprovação." }));
  });

  it("asks for the tax profile when the server says it is missing", async () => {
    vi.mocked(api.createTaxSimulation).mockRejectedValueOnce(new ApiError(422, "Sem perfil.", "tax_profile_missing"));
    renderTax(<TaxSimulationsPage />);

    await screen.findByRole("button", { name: "Simular" });
    fireEvent.change(screen.getByLabelText("Nome da simulação"), { target: { value: "Teste" } });
    fillBaseline();
    fireEvent.change(one('[id^="sc-"][id$="-name"]'), { target: { value: "A" } });
    fireEvent.click(screen.getByRole("button", { name: "Simular" }));

    await waitFor(() => expect(api.createTaxSimulation).toHaveBeenCalled());
    await waitFor(() => expect(screen.getAllByRole("link", { name: /perfil tributário/i }).length).toBeGreaterThan(0));
    expect(toast.error).not.toHaveBeenCalled();
  });
});
