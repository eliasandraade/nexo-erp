import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiError } from "@/services/api-client";
import { BuildProgressTab } from "./BuildProgressTab";
import * as progressApi from "../../api/build-progress.api";

vi.mock("../../api/build-progress.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-progress.api")>()),
  fetchProjectProgress:   vi.fn(),
  fetchProjectProgressAt: vi.fn(),
  fetchScheduleRevisions: vi.fn(),
  freezeScheduleBaseline: vi.fn(),
}));

// Wave 5 AI panels inside the tab: no suggestions, AI off (their own tests are in components/ai)
vi.mock("../../api/build-ai.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-ai.api")>()),
  fetchSuggestions: vi.fn().mockResolvedValue([]),
}));
vi.mock("../../api/build-twin.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-twin.api")>()),
  fetchBuildSettings: vi.fn().mockResolvedValue({ buildAiEnabled: false, buildVisionEnabled: false }),
}));
vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchDailyLogs: vi.fn().mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 30 }),
}));

// the chart is lazy-loaded recharts: a stub keeps the test about the numbers
vi.mock("./ProgressCurvesChart", () => ({ default: () => <div data-testid="curves-chart" /> }));

const progress: progressApi.BuildProgressDto = {
  projectId: "p1", date: "2026-10-30",
  plannedPercent: 50, actualPercent: 42.5, forecastPercent: 47.25,
  plannedDelivery: "2026-12-20", forecastDelivery: "2027-01-10",
  methodology: "weighted-stages/1", planSource: "current", planRevisionNumber: null,
  stages: [
    { stageId: "s1", name: "Fundação", order: 0, weight: 3, status: "Completed", plannedPercent: 100, actualPercent: 100,
      plannedStart: "2026-09-01", plannedEnd: "2026-09-30", actualStart: "2026-09-01", actualEnd: "2026-10-02",
      forecastEnd: "2026-10-02", delayDays: 2, flags: [] },
    { stageId: "s2", name: "Acabamento", order: 1, weight: 1, status: "Pending", plannedPercent: null, actualPercent: 0,
      plannedStart: null, plannedEnd: null, actualStart: null, actualEnd: null,
      forecastEnd: null, delayDays: null, flags: ["missing_planned_dates"] },
  ],
  curves: { planned: [{ date: "2026-10-01", percent: 30 }], actual: [{ date: "2026-10-01", percent: 25 }], forecast: [] },
  warnings: ["1 etapa sem datas previstas fica fora do planejado."],
};

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("BuildProgressTab", () => {
  beforeEach(() => {
    vi.mocked(progressApi.fetchProjectProgress).mockResolvedValue(progress);
    vi.mocked(progressApi.fetchScheduleRevisions).mockResolvedValue([]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("shows actual / planned / forecast, delivery, methodology, per-stage flags and the chart", async () => {
    renderTab(<BuildProgressTab projectId="p1" />);

    expect(await screen.findByText("42,5%")).toBeInTheDocument();
    expect(screen.getAllByText("50,0%").length).toBeGreaterThan(0);
    expect(screen.getByText("47,3%")).toBeInTheDocument();
    expect(screen.getByText(/Média ponderada das etapas pelo peso/)).toBeInTheDocument();
    expect(screen.getByText(/Plano atual \(sem linha de base congelada\)/)).toBeInTheDocument();
    expect(screen.getByText("sem datas planejadas")).toBeInTheDocument();
    expect(screen.getByText("2 dias de atraso")).toBeInTheDocument();
    expect(screen.getByText(/1 etapa sem datas previstas/)).toBeInTheDocument();
    expect(await screen.findByTestId("curves-chart")).toBeInTheDocument();
    // the overall % is computed, never an input
    expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();
    expect(await screen.findByRole("button", { name: "Congelar baseline" })).toBeInTheDocument();
  });

  it("'Como estava em' answers 404 with 'Nenhum registro até esta data'", async () => {
    vi.mocked(progressApi.fetchProjectProgressAt).mockRejectedValue(new ApiError(404, "Não há registro"));
    renderTab(<BuildProgressTab projectId="p1" />);
    await screen.findByText("42,5%");

    fireEvent.change(screen.getByLabelText("Data"), { target: { value: "2026-01-15" } });
    fireEvent.click(screen.getByRole("button", { name: "Consultar" }));

    expect(await screen.findByText("Nenhum registro até esta data.")).toBeInTheDocument();
    expect(progressApi.fetchProjectProgressAt).toHaveBeenCalledWith("p1", "2026-01-15");
  });

  it("'Como estava em' shows the snapshot found", async () => {
    vi.mocked(progressApi.fetchProjectProgressAt).mockResolvedValue({
      projectId: "p1", requestedDate: "2026-10-20", date: "2026-10-18",
      plannedPercent: 40, actualPercent: 33.3, forecastPercent: null, forecastDeliveryDate: null,
      methodology: "weighted-stages/1", updatedAt: "2026-10-18T20:00:00Z",
      stages: [{ stageId: "s1", name: "Fundação", order: 0, weight: 3, status: "InProgress", plannedPercent: 80,
        actualPercent: 70, plannedEnd: null, forecastEnd: null, delayDays: null }],
    });
    renderTab(<BuildProgressTab projectId="p1" />);
    await screen.findByText("42,5%");
    fireEvent.change(screen.getByLabelText("Data"), { target: { value: "2026-10-20" } });
    fireEvent.click(screen.getByRole("button", { name: "Consultar" }));
    expect(await screen.findByText("33,3%")).toBeInTheDocument();
    expect(screen.getByText(/o mais recente até/)).toBeInTheDocument();
  });

  it("after the baseline, a revision needs a reason", async () => {
    vi.mocked(progressApi.fetchScheduleRevisions).mockResolvedValue([{
      id: "r1", number: 1, kind: "Baseline", reason: null, createdByName: "Elias", createdAt: "2026-10-01T12:00:00Z",
      items: [{ stageId: "s1", name: "Fundação", plannedStart: "2026-09-01", plannedEnd: "2026-09-30", weight: 3 }],
    }]);
    vi.mocked(progressApi.freezeScheduleBaseline).mockResolvedValue({} as progressApi.BuildScheduleRevisionDto);
    renderTab(<BuildProgressTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Registrar revisão" }));
    const dialog = await screen.findByRole("dialog");
    const confirm = within(dialog).getByRole("button", { name: "Registrar revisão" });
    expect(confirm).toBeDisabled();

    fireEvent.change(within(dialog).getByLabelText(/Motivo/), { target: { value: "Chuvas atrasaram a fundação" } });
    fireEvent.click(confirm);
    await waitFor(() => expect(progressApi.freezeScheduleBaseline).toHaveBeenCalledWith("p1", "Chuvas atrasaram a fundação"));
  });
});
