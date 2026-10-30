import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import * as aiApi from "../../api/build-ai.api";
import * as twinApi from "../../api/build-twin.api";
import { AI_DISABLED_MESSAGE } from "../../lib/ai";
import { RiskTab } from "./RiskTab";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../../api/build-ai.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-ai.api")>()),
  fetchSuggestions: vi.fn(),
  fetchDelayRisk:   vi.fn(),
  fetchCostRisk:    vi.fn(),
  explainRisk:      vi.fn(),
}));
vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  fetchBuildSettings: vi.fn(),
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const delay: aiApi.BuildRiskAnalysisDto = {
  projectId: "p1", kind: "delay", asOf: "2026-10-30", level: "high", methodology: "build-risk/1",
  metrics: { spi: 0.72, actualPercent: 36, plannedPercent: 50, delayedStages: 1, forecastSlipDays: 21, lateMaterials: 0,
    pendingChangeOrders: 0, openCriticalNonConformities: 1 },
  items: [
    { key: "stage_delayed:s1", kind: "stage_delayed", severity: "medium", title: "Alvenaria atrasada", evidence: "Fim planejado 10/10, 60%",
      metrics: { delayDays: 20 }, refs: ["s1"] },
    { key: "spi_low", kind: "spi_low", severity: "high", title: "Ritmo abaixo do plano", evidence: "SPI 0,72", metrics: { spi: 0.72 }, refs: [] },
  ],
};
const cost: aiApi.BuildRiskAnalysisDto = {
  projectId: "p1", kind: "cost", asOf: "2026-10-30", level: "medium", methodology: "build-risk/1",
  metrics: { budget: 100000, realized: 50000, earnedValue: 40000, cpi: 0.8, eac: 125000, vac: -25000, approvedChangeOrders: 0,
    budgetWithChangeOrders: 100000, unlinkedMaterialsEstimated: 0 },
  items: [],
};

const settings = (ai: boolean) => ({ tenantId: "t", build3DEnabled: true, buildAiEnabled: ai, buildVisionEnabled: false,
  buildPredictEnabled: true, customized: true, updatedByName: null, updatedAt: null });

beforeEach(() => {
  vi.mocked(aiApi.fetchDelayRisk).mockResolvedValue(delay);
  vi.mocked(aiApi.fetchCostRisk).mockResolvedValue(cost);
  vi.mocked(aiApi.fetchSuggestions).mockResolvedValue([]);
  vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings(true));
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("RiskTab", () => {
  it("renders the deterministic metrics and the items by severity, saying the numbers are not from the AI", async () => {
    wrap(<RiskTab projectId="p1" />);
    const metrics = await screen.findByTestId("risk-delay-metrics");
    expect(metrics).toHaveTextContent("SPI (prazo)0,72");
    expect(metrics).toHaveTextContent("+21 dias");
    expect(metrics).toHaveTextContent("NCs críticas abertas1");
    const delaySection = screen.getByTestId("risk-delay");
    expect(delaySection).toHaveTextContent("análise determinística do Orken (build-risk/1) — calculados, sem IA");
    const titles = within(delaySection).getAllByRole("listitem").map((li) => li.textContent);
    expect(titles[0]).toContain("Ritmo abaixo do plano");   // high first
    expect(titles[1]).toContain("Alvenaria atrasada");
    expect(titles[1]).toContain("delayDays: 20");

    const costMetrics = await screen.findByTestId("risk-cost-metrics");
    expect(costMetrics).toHaveTextContent("CPI (custo)0,80");
    expect(costMetrics.textContent).toMatch(/Custo final estimado \(EAC\)R\$\s125\.000,00/);
    expect(screen.getByTestId("risk-cost")).toHaveTextContent("Nenhum risco identificado pela análise.");
  });

  it("'Explicar com IA' → explanation suggestion with summary, per-item text and actions", async () => {
    vi.mocked(aiApi.explainRisk).mockResolvedValue({
      id: "r1", projectId: "p1", capability: "DelayRisk", status: "Pending", source: "Llm", subjectId: null, confidence: 0.7,
      output: { kind: "delay", summary: "A obra está atrás do plano.", discardedRefs: 0,
        items: [{ key: "spi_low", explanation: "O avanço real é 72% do planejado." }],
        recommendedActions: [{ text: "Reforçar a equipe de alvenaria", itemKeys: ["stage_delayed:s1"] }] },
      inputRefs: { kind: "delay", methodology: "build-risk/1", itemKeys: ["spi_low"] }, applied: null, provider: "Claude", model: "m",
      promptType: "build-delay-risk", promptVersion: null, promptVersionId: null, promptHash: "abcdabcdabcdabcd", inputTokens: 10,
      outputTokens: 10, costMicros: 0, latencyMs: 1, createdByName: "Ana", createdAt: "2026-10-30T10:00:00Z", decidedByName: null,
      decidedAt: null, decisionNote: null, expiresAt: new Date(Date.now() + 86_400_000).toISOString(),
    });
    wrap(<RiskTab projectId="p1" />);
    const section = await screen.findByTestId("risk-delay");
    await screen.findByTestId("risk-delay-metrics");
    const btn = within(section).getByRole("button", { name: /Explicar com IA/ });
    await waitFor(() => expect(btn).toBeEnabled());
    fireEvent.click(btn);
    const view = await within(section).findByTestId("risk-explanation");
    expect(aiApi.explainRisk).toHaveBeenCalledWith("p1", "delay");
    expect(view).toHaveTextContent("A obra está atrás do plano.");
    expect(view).toHaveTextContent("Ritmo abaixo do plano");
    expect(view).toHaveTextContent("O avanço real é 72% do planejado.");
    expect(view).toHaveTextContent("Reforçar a equipe de alvenaria");
    expect(within(section).getByRole("button", { name: /Registrar ciência/ })).toBeInTheDocument();
  });

  it("AI off: numbers still shown, 'Explicar com IA' disabled with the support message", async () => {
    vi.mocked(twinApi.fetchBuildSettings).mockResolvedValue(settings(false));
    wrap(<RiskTab projectId="p1" />);
    await screen.findByTestId("risk-delay-metrics");
    expect((await screen.findAllByText(AI_DISABLED_MESSAGE)).length).toBe(2);
    for (const b of screen.getAllByRole("button", { name: /Explicar com IA/ })) expect(b).toBeDisabled();
  });

  it("an unexpected payload does not break the tab", async () => {
    vi.mocked(aiApi.fetchDelayRisk).mockResolvedValue([] as unknown as aiApi.BuildRiskAnalysisDto);
    wrap(<RiskTab projectId="p1" />);
    expect(await within(await screen.findByTestId("risk-delay")).findByText("Análise indisponível.")).toBeInTheDocument();
  });
});
