import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, waitFor, within } from "@testing-library/react";
import { toast } from "sonner";
import { opportunity, renderTax } from "../test/render";
import { ENGINE_SCENARIOS } from "../test/engineFixture";
import TaxCalculationDetailPage from "./TaxCalculationDetailPage";
import * as api from "../api/tax.api";
import { ApiError } from "@/services/api-client";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  getTaxCalculation: vi.fn(),
  getTaxOpportunity: vi.fn(),
  detectOpportunitiesAgain: vi.fn(),
  listSplitExpectations: vi.fn(),
  createSplitExpectations: vi.fn(),
  verifyTaxCalculation: vi.fn(),
}));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
Element.prototype.scrollIntoView ??= () => {};
Element.prototype.hasPointerCapture ??= () => false;
Element.prototype.releasePointerCapture ??= () => {};

const [scenario] = ENGINE_SCENARIOS;

const assessment = (over: Partial<api.TaxComplianceAssessmentDto> = {}): api.TaxComplianceAssessmentDto => ({
  id: "a1", status: "Declared", periodFrom: "2026-01-01", periodTo: "2026-06-30", scope: "Estabelecimento", obligationsChecked: [],
  evidence: null, assessedBy: "Contabilidade", assessedAt: "2026-07-01T12:00:00Z", notes: null, limitations: null, pnctParticipation: false,
  recordedByName: "Diretora", createdAt: "2026-07-01T12:00:00Z", results: [], supersedesAssessmentId: null, ...over,
} as api.TaxComplianceAssessmentDto);

const calculation = (over: Partial<api.TaxCalculationDto> = {}): api.TaxCalculationDto => ({
  id: "calc-1", transactionId: "t1", source: "Manual", sourceId: null, direction: "Outbound", description: "Venda de exemplo",
  referenceDate: "2026-06-15", profileVersionId: "pv2", profileVersionNumber: 2, engineVersion: "orken-tax-engine/1.0.0",
  inputHash: "in-hash", ruleSetHash: "rules-hash", outputHash: "out-hash", totalBase: 840, totalTax: 8.4, reused: false,
  createdAt: "2026-06-15T12:00:00Z", input: scenario.input, output: scenario.output, opportunityIds: [], advisories: [],
  coverage: { status: "Supported", policyVersion: "cobertura/1", findings: [], assumptions: [], supported: true },
  complianceAssessment: null, currentComplianceAssessment: null, governance: null, ...over,
} as api.TaxCalculationDto);

const expectation = (over: Partial<api.SplitExpectationDto> = {}): api.SplitExpectationDto => ({
  id: "x1", calculationId: "calc-1", tax: "Cbs", procedure: "Standard", expected: 7.56, actual: 0, difference: -7.56, status: "Pending",
  settlements: [], createdAt: "2026-06-15T12:00:00Z", operationDate: "2026-06-15", ageDays: 12, ...over,
});
const page = (items: api.SplitExpectationDto[]): api.Page<api.SplitExpectationDto> => ({ items, total: items.length, skip: 0, take: 50 });

function show(c: api.TaxCalculationDto = calculation()) {
  vi.mocked(api.getTaxCalculation).mockResolvedValue(c);
  return renderTax(<TaxCalculationDetailPage />, { path: "/tributos/calculos/:id", route: `/tributos/calculos/${c.id}` });
}

describe("TaxCalculationDetailPage", { timeout: 30_000 }, () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(api.listSplitExpectations).mockResolvedValue(page([]));
  });
  afterEach(cleanup);

  it("says what was calculated, with the coverage, the assumptions and the advisories", async () => {
    show(calculation({
      reused: true, advisories: ["Obrigações declaradas, não verificadas."],
      coverage: { status: "Supported", policyVersion: "cobertura/1", findings: [], assumptions: ["Fornecimento único e imediato."], supported: true },
    }));

    expect(await screen.findByRole("heading", { name: "Venda de exemplo" })).toBeInTheDocument();
    expect(screen.getByText(/Venda \/ saída de 15\/06\/2026 · origem: Manual · perfil v2/)).toBeInTheDocument();
    expect(screen.getByText(/o resultado guardado foi reaproveitado/)).toBeInTheDocument();
    expect(screen.getByText("Suportada pela V1")).toBeInTheDocument();
    expect(screen.getByText("matriz cobertura/1")).toBeInTheDocument();
    expect(screen.getByText("Fornecimento único e imediato.")).toBeInTheDocument();
    expect(screen.getByText("Obrigações declaradas, não verificadas.")).toBeInTheDocument();
    expect(screen.getByText("Nenhuma oportunidade encontrada para esta operação.")).toBeInTheDocument();
    expect(screen.queryByText("Com qual aprovação das regras")).not.toBeInTheDocument();
    expect(api.getTaxCalculation).toHaveBeenCalledWith("calc-1");
  });

  it("titles a calculation without description by its direction", async () => {
    show(calculation({ description: null, direction: "Inbound" }));
    expect(await screen.findByRole("heading", { name: "Compra / entrada" })).toBeInTheDocument();
  });

  it("keeps the compliance assessment it used, even when another one is in force today", async () => {
    show(calculation({
      complianceAssessment: assessment(),
      currentComplianceAssessment: assessment({ id: "a2", status: "Verified" }),
      governance: opportunity().governance,
    }));

    expect(await screen.findByText(/Obrigações acessórias usadas: Declarado, não verificado de 01\/01\/2026 a 30\/06\/2026 \(Estabelecimento\)\./)).toBeInTheDocument();
    expect(screen.getByText(/Hoje está registrada outra avaliação das obrigações acessórias para esta data/)).toHaveTextContent(
      "(Verificado no período e escopo). Este cálculo continua com a avaliação que usou.",
    );
    expect(screen.getByText("Com qual aprovação das regras")).toBeInTheDocument();
  });

  it("says when the calculation cannot be seen or does not exist", async () => {
    vi.mocked(api.getTaxCalculation).mockRejectedValue(new ApiError(403, "proibido"));
    renderTax(<TaxCalculationDetailPage />, { path: "/tributos/calculos/:id", route: "/tributos/calculos/x" });
    expect(await screen.findByText("Seu perfil não pode ver o cálculo. Peça acesso à diretoria.")).toBeInTheDocument();
    cleanup();

    vi.mocked(api.getTaxCalculation).mockRejectedValue(new ApiError(404, "não"));
    renderTax(<TaxCalculationDetailPage />, { path: "/tributos/calculos/:id", route: "/tributos/calculos/y" });
    expect(await screen.findByText("O cálculo não encontrado(a)")).toBeInTheDocument();
  });

  it("lists the opportunities it found, each one read on its own", async () => {
    vi.mocked(api.getTaxOpportunity).mockImplementation((id) =>
      id === "op-1" ? Promise.resolve(opportunity()) : Promise.reject(new ApiError(404, "não")));
    show(calculation({ opportunityIds: ["op-1", "op-x"] }));

    const link = await screen.findByRole("link", { name: "Conceder o desconto como incondicional" });
    expect(link).toHaveAttribute("href", "/tributos/oportunidades/op-1");
    const item = link.closest("li")!;
    expect(within(item).getByText("Redução do IBS/CBS calculado — não é economia")).toBeInTheDocument();
    expect(within(item).getByText("R$ 0,50")).toBeInTheDocument();
    expect(within(item).queryByText("Economia")).not.toBeInTheDocument();
    expect(await screen.findByText("Não foi possível ler esta oportunidade.")).toBeInTheDocument();
  });

  it("shows no amount for an opportunity without an estimate", async () => {
    vi.mocked(api.getTaxOpportunity).mockResolvedValue(opportunity({ estimatedImpact: null }));
    show(calculation({ opportunityIds: ["op-1"] }));

    const item = (await screen.findByRole("link", { name: "Conceder o desconto como incondicional" })).closest("li")!;
    expect(item).not.toHaveTextContent("R$");
  });

  it("searches for alternatives again and says how many are new", async () => {
    vi.mocked(api.detectOpportunitiesAgain).mockResolvedValueOnce(["op-2", "op-3"]).mockResolvedValueOnce([]);
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Procurar de novo" }));
    await waitFor(() => expect(api.detectOpportunitiesAgain).toHaveBeenCalledWith("calc-1"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("2 alternativa(s) nova(s) encontrada(s)."));
    await waitFor(() => expect(api.getTaxCalculation).toHaveBeenCalledTimes(2));

    fireEvent.click(screen.getByRole("button", { name: "Procurar de novo" }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Nenhuma alternativa nova."));
  });

  it("explains a refused search", async () => {
    vi.mocked(api.detectOpportunitiesAgain).mockRejectedValue(new ApiError(422, "As regras mudaram.", "tax_calculation_not_reproducible"));
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Procurar de novo" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("O cálculo de origem não reproduz mais", { description: "As regras mudaram." }));
  });

  it("offers split payment only for an outbound operation", async () => {
    show(calculation({ direction: "Inbound" }));

    expect(await screen.findByText("Retenção esperada vale para operações de saída (o estabelecimento é o fornecedor).")).toBeInTheDocument();
    expect(api.listSplitExpectations).not.toHaveBeenCalled();
    expect(screen.queryByRole("button", { name: "Registrar expectativa de segregação" })).not.toBeInTheDocument();
  });

  it("records the expected segregation with the chosen procedure when there is none", async () => {
    vi.mocked(api.createSplitExpectations).mockResolvedValue([expectation()]);
    show();

    fireEvent.click(await screen.findByRole("combobox", { name: "Procedimento" }));
    fireEvent.click(await screen.findByRole("option", { name: "Simplificado" }));
    fireEvent.click(screen.getByRole("button", { name: "Registrar expectativa de segregação" }));

    await waitFor(() => expect(api.createSplitExpectations).toHaveBeenCalledWith("calc-1", "Simplified"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Expectativas gerenciais de segregação registradas."));
    expect(api.listSplitExpectations).toHaveBeenCalledWith({ calculationId: "calc-1", take: 50 });
    expect(screen.getByText(/Expectativa gerencial de segregação\. Não comprova retenção ou recolhimento\./)).toBeInTheDocument();
  });

  it("explains a refused segregation", async () => {
    vi.mocked(api.createSplitExpectations).mockRejectedValue(new ApiError(409, "Já registrada."));
    show();

    fireEvent.click(await screen.findByRole("button", { name: "Registrar expectativa de segregação" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: "Já registrada." }));
  });

  it("shows the expected retentions already recorded, instead of offering to record them again", async () => {
    vi.mocked(api.listSplitExpectations).mockResolvedValue(page([expectation()]));
    show();

    expect(await screen.findByText("12 dia(s) desde a operação")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Registrar retenção" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Registrar expectativa de segregação" })).not.toBeInTheDocument();
  });

  it("says when the retentions cannot be loaded", async () => {
    vi.mocked(api.listSplitExpectations).mockRejectedValue(new Error("rede"));
    show();
    expect(await screen.findByText("Não foi possível carregar as retenções")).toBeInTheDocument();
  });

  it("verifies that the stored calculation still reproduces, from its technical trail", async () => {
    vi.mocked(api.verifyTaxCalculation).mockResolvedValue({
      calculationId: "calc-1", reproducible: true, engineAvailable: true, engineVersionMatches: true, storedEngineVersion: "orken-tax-engine/1.0.0",
      currentEngineVersion: "orken-tax-engine/1.0.0", storedOutputHash: "out-hash", recomputedOutputHash: "out-hash", differences: [],
    });
    show();

    fireEvent.click(await screen.findByRole("button", { name: /Rastro técnico/ }));
    expect(screen.getByText("rules-hash")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Verificar reprodução/ }));
    await waitFor(() => expect(api.verifyTaxCalculation).toHaveBeenCalledWith("calc-1"));
    expect(await screen.findByText(/Reproduzido:/)).toBeInTheDocument();
  });
});
