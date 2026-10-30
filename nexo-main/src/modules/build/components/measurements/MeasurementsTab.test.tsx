import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MeasurementsTab } from "./MeasurementsTab";
import * as buildApi from "../../api/build.api";
import * as progressApi from "../../api/build-progress.api";
import * as commercialApi from "../../api/build-commercial.api";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchStages: vi.fn(),
}));
vi.mock("../../api/build-progress.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-progress.api")>()),
  fetchCostByStage: vi.fn(),
}));
vi.mock("../../api/build-commercial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-commercial.api")>()),
  fetchMeasurements:  vi.fn(),
  createMeasurement:  vi.fn(),
  approveMeasurement: vi.fn(),
}));

const stage = (id: string, name: string, order: number, progressPercent: number): buildApi.BuildStageDto => ({
  id, projectId: "p1", name, description: null, order, status: "InProgress", plannedStartDate: null,
  plannedEndDate: null, actualStartDate: null, actualEndDate: null, progressPercent,
});

const measurement = (over: Partial<commercialApi.BuildMeasurementDto> = {}): commercialApi.BuildMeasurementDto => ({
  id: "me1", projectId: "p1", number: 1, periodStart: "2026-10-01", periodEnd: "2026-10-31", status: "Submitted",
  notes: null, submittedAt: null, approvedByName: null, approvedAt: null, rejectedAt: null, rejectedByName: null,
  rejectionReason: null, totalAmount: 5000, createdByName: "Ana",
  items: [{ id: "i1", stageId: "s1", stageName: "Fundação", previousPercent: 40, measuredPercent: 60, deltaPercent: 20,
    amount: 5000, suggestedAmount: 4000 }],
  createdAt: "2026-10-31T10:00:00Z", updatedAt: "2026-10-31T10:00:00Z", ...over,
});

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("MeasurementsTab", () => {
  beforeEach(() => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([stage("s1", "Fundação", 0, 40), stage("s2", "Alvenaria", 1, 0)]);
    vi.mocked(progressApi.fetchCostByStage).mockResolvedValue({
      projectId: "p1", totalBudgeted: 30000, totalRealized: 0, totalVariance: 30000,
      stages: [{ stageId: "s1", stageName: "Fundação", order: 0, budgeted: 20000, realized: 0, variance: 20000, movementCount: 0 }],
    });
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("says that approving updates the obra progress", async () => {
    vi.mocked(commercialApi.fetchMeasurements).mockResolvedValue([measurement()]);
    vi.mocked(commercialApi.approveMeasurement).mockResolvedValue(measurement({ status: "Approved" }));
    renderTab(<MeasurementsTab projectId="p1" />);

    expect(await screen.findByText(/Aprovar uma medição atualiza o progresso da obra/)).toBeInTheDocument();
    fireEvent.click(await screen.findByRole("button", { name: /Aprovar/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/progresso da obra é atualizado/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Aprovar e atualizar o progresso" }));
    await waitFor(() => expect(commercialApi.approveMeasurement).toHaveBeenCalledWith("me1"));
  });

  it("prefills the current % and rejects a % below it or above 100", async () => {
    vi.mocked(commercialApi.fetchMeasurements).mockResolvedValue([]);
    renderTab(<MeasurementsTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Nova medição/ }));
    const dialog = await screen.findByRole("dialog");
    const fundacao = await within(dialog).findByLabelText("% medido — Fundação");
    expect(fundacao).toHaveValue("40");
    const save = within(dialog).getByRole("button", { name: "Salvar rascunho" });

    fireEvent.change(fundacao, { target: { value: "30" } });
    expect(within(dialog).getByRole("alert")).toHaveTextContent("Não pode ser menor que o atual (40%).");
    expect(save).toBeDisabled();

    fireEvent.change(fundacao, { target: { value: "120" } });
    expect(within(dialog).getByRole("alert")).toHaveTextContent("Não pode passar de 100%.");
    expect(save).toBeDisabled();
    expect(commercialApi.createMeasurement).not.toHaveBeenCalled();
  });

  it("shows the suggested amount and sends only the stages that advanced", async () => {
    vi.mocked(commercialApi.fetchMeasurements).mockResolvedValue([]);
    vi.mocked(commercialApi.createMeasurement).mockResolvedValue(measurement({ status: "Draft" }));
    renderTab(<MeasurementsTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Nova medição/ }));
    const dialog = await screen.findByRole("dialog");
    const fundacao = await within(dialog).findByLabelText("% medido — Fundação");
    await waitFor(() => expect(progressApi.fetchCostByStage).toHaveBeenCalled());
    fireEvent.change(fundacao, { target: { value: "60" } });
    // 20 points of a R$ 20.000 stage
    expect(await within(dialog).findByText("R$ 4.000,00")).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("Início do período"), { target: { value: "2026-10-01" } });
    fireEvent.change(within(dialog).getByLabelText("Fim do período"), { target: { value: "2026-10-31" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar rascunho" }));

    await waitFor(() => expect(commercialApi.createMeasurement).toHaveBeenCalledWith("p1", {
      periodStart: "2026-10-01", periodEnd: "2026-10-31", notes: null,
      items: [{ stageId: "s1", measuredPercent: 60, amount: null }],
    }));
  });
});
