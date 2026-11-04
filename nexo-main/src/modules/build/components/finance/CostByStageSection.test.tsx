import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CostByStageSection } from "./CostByStageSection";
import * as progressApi from "../../api/build-progress.api";

vi.mock("../../api/build-progress.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-progress.api")>()),
  fetchCostByStage: vi.fn(),
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

// the DOM text is whitespace-normalized (the currency NBSP becomes a space)
const brl = (v: number) => v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" }).replace(/\s/g, " ");

describe("CostByStageSection", () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("lists budgeted × realized × variance per stage in order, 'Sem etapa' last, with the totals", async () => {
    vi.mocked(progressApi.fetchCostByStage).mockResolvedValue({
      projectId: "p1", totalBudgeted: 30000, totalRealized: 32000, totalVariance: -2000,
      stages: [
        { stageId: null, stageName: "", order: null, budgeted: 0, realized: 2000, variance: -2000, movementCount: 1 },
        { stageId: "s2", stageName: "Estrutura", order: 1, budgeted: 20000, realized: 25000, variance: -5000, movementCount: 3 },
        { stageId: "s1", stageName: "Fundação", order: 0, budgeted: 10000, realized: 5000, variance: 5000, movementCount: 0 },
      ],
    });
    wrap(<CostByStageSection projectId="p1" />);

    const table = await screen.findByRole("table");
    expect(progressApi.fetchCostByStage).toHaveBeenCalledWith("p1");
    const rows = within(table).getAllByRole("row");
    // header, 3 stages, total
    expect(rows).toHaveLength(5);
    expect(rows[1]).toHaveTextContent("Fundação");
    expect(rows[1]).not.toHaveTextContent("lançamento");
    expect(rows[2]).toHaveTextContent("Estrutura(3 lançamentos)");
    expect(rows[3]).toHaveTextContent("Sem etapa(1 lançamento)");
    const variance = (row: HTMLElement) => within(row).getAllByRole("cell")[3];
    expect(variance(rows[2])).toHaveTextContent(brl(-5000));
    expect(variance(rows[2])).toHaveClass("text-red-600");
    expect(variance(rows[1])).toHaveTextContent(brl(5000));
    expect(variance(rows[1])).toHaveClass("text-emerald-600");
    expect(within(rows[3]).getAllByRole("cell")[1]).toHaveTextContent(brl(0));
    expect(rows[4]).toHaveTextContent(`Total${brl(30000)}${brl(32000)}${brl(-2000)}`);
  });

  it("an error or an unexpected body shows the error message", async () => {
    vi.mocked(progressApi.fetchCostByStage).mockResolvedValue({} as progressApi.BuildCostByStageDto);
    wrap(<CostByStageSection projectId="p1" />);
    expect(await screen.findByText("Erro ao carregar o custo por etapa.")).toBeInTheDocument();
    cleanup();

    vi.mocked(progressApi.fetchCostByStage).mockRejectedValue(new Error("500"));
    wrap(<CostByStageSection projectId="p1" />);
    expect(await screen.findByText("Erro ao carregar o custo por etapa.")).toBeInTheDocument();
  });
});
