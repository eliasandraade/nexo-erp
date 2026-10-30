import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import * as api from "../../api/build-forecast.api";
import { ForecastSection } from "./ForecastSection";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../../api/build-forecast.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-forecast.api")>()),
  fetchForecast:        vi.fn(),
  recomputeForecast:    vi.fn(),
  fetchForecastHistory: vi.fn(),
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const stageRow = (over: Partial<api.BuildForecastStageDto> = {}): api.BuildForecastStageDto => ({
  stageId: "s1", name: "Fundação", order: 0, weight: 2, status: "Completed", actualPercent: 100, method: "completed",
  ratePerWorkingDay: null, plannedRatePerWorkingDay: 5, dataPoints: 4, plannedStart: "2026-09-01", plannedEnd: "2026-09-30",
  forecastStart: "2026-09-01", forecastEnd: "2026-09-28", delayDays: -2, inheritedDelayDays: 0, confidence: 1, ...over,
});

const forecast = (over: Partial<api.BuildForecastDto> = {}): api.BuildForecastDto => ({
  id: "f1", projectId: "p1", date: "2026-10-30", generatedAt: "2026-10-30T09:00:00Z", source: "Scheduled", stored: true,
  methodology: "productivity/1", version: 1, forecastDelivery: "2027-02-15", plannedDelivery: "2027-01-31", delayDays: 15,
  expectedEnd: "2027-01-20", daysLateVsContract: 26, confidence: 0.62,
  stages: [
    stageRow({ stageId: "s2", name: "Alvenaria", order: 1, status: "InProgress", actualPercent: 40, method: "window", ratePerWorkingDay: 1.5,
      plannedRatePerWorkingDay: 2.25, dataPoints: 6, plannedEnd: "2026-12-10", forecastEnd: "2026-12-30", delayDays: 20, confidence: 0.7 }),
    stageRow(),
    stageRow({ stageId: "s3", name: "Cobertura", order: 2, status: "Pending", actualPercent: 0, method: "planned", ratePerWorkingDay: null,
      dataPoints: 0, plannedEnd: "2027-01-31", forecastEnd: "2027-02-15", delayDays: 15, inheritedDelayDays: 15, confidence: 0.5 }),
  ],
  factors: [
    { key: "weather:s3", kind: "weather_risk", severity: "low", title: "Chuva prevista na cobertura", evidence: "15 mm em 04/11", stageId: "s3", days: null },
    { key: "delivery", kind: "delivery_after_plan", severity: "medium", title: "Entrega depois do plano", evidence: "15 dias", stageId: null, days: 15 },
    { key: "co:1", kind: "pending_change_order", severity: "low", title: "Aditivo pendente (+10 dias)", evidence: "AD-3 enviado", stageId: null, days: 10 },
  ],
  calendar: { workingDays: ["seg", "ter", "qua", "qui", "sex", "sáb"], holidays: "feriados nacionais", windowDays: 21 },
  createdByName: null, ...over,
});

const history: api.BuildForecastHistoryItemDto[] = [
  { id: "h2", date: "2026-10-30", generatedAt: "2026-10-30T09:00:00Z", source: "Scheduled", methodology: "productivity/1", version: 1,
    forecastDelivery: "2027-02-15", plannedDelivery: "2027-01-31", delayDays: 15, confidence: 0.62, createdByName: null },
  { id: "h1", date: "2026-10-29", generatedAt: "2026-10-29T09:00:00Z", source: "Manual", methodology: "productivity/1", version: 1,
    forecastDelivery: "2027-02-10", plannedDelivery: "2027-01-31", delayDays: 10, confidence: 0.6, createdByName: "Ana" },
];

beforeEach(() => {
  vi.mocked(api.fetchForecast).mockResolvedValue(forecast());
  vi.mocked(api.fetchForecastHistory).mockResolvedValue(history);
});
afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("ForecastSection", () => {
  it("shows delivery, delays, confidence, method and the deterministic explanation", async () => {
    wrap(<ForecastSection projectId="p1" />);
    const kpis = await screen.findByTestId("forecast-kpis");
    expect(kpis).toHaveTextContent("Entrega prevista15 de fev. de 2027");
    expect(kpis).toHaveTextContent("+15 dias vs. plano");
    expect(kpis).toHaveTextContent("+26 dias vs. contrato");
    expect(kpis).toHaveTextContent("Confiança62% (média)");
    const section = screen.getByTestId("forecast-section");
    expect(section).toHaveTextContent("Cálculo determinístico do Orken, sem IA");
    expect(section).toHaveTextContent("Metodologia productivity/1 (versão 1) · automática (diária)");
    expect(section).toHaveTextContent("janela de 21 dias");
    expect(api.fetchForecast).toHaveBeenCalledWith("p1");
  });

  it("lists the factors by severity and marks weather and pending change orders as risks not applied", async () => {
    wrap(<ForecastSection projectId="p1" />);
    const items = within(await screen.findByTestId("forecast-factors")).getAllByRole("listitem");
    expect(items[0]).toHaveTextContent("Entrega depois do plano");                 // medium first
    expect(items[0]).not.toHaveTextContent("não aplicado");
    const weather = items.find((li) => li.textContent?.includes("Chuva prevista na cobertura"))!;
    expect(weather).toHaveTextContent("Risco — não aplicado às datas");
    expect(weather).toHaveTextContent("15 mm em 04/11");
    const co = items.find((li) => li.textContent?.includes("Aditivo pendente"))!;
    expect(co).toHaveTextContent("Risco — não aplicado às datas");
  });

  it("per-stage table in order with method, rates, ends, delay (inherited) and confidence", async () => {
    wrap(<ForecastSection projectId="p1" />);
    const rows = within(await screen.findByTestId("forecast-stages")).getAllByRole("row").slice(1);
    expect(rows.map((r) => r.querySelector("td p")?.textContent)).toEqual(["Fundação", "Alvenaria", "Cobertura"]);
    expect(rows[0]).toHaveTextContent("Concluída");
    expect(rows[0]).toHaveTextContent("2 dias adiantada");
    expect(rows[1]).toHaveTextContent("Ritmo dos últimos 21 dias · 6 registro(s)");
    expect(rows[1]).toHaveTextContent("1,5%/dia útil · 2,25%/dia útil");
    expect(rows[1]).toHaveTextContent("+20 dias");
    expect(rows[1]).toHaveTextContent("70%");
    expect(rows[2]).toHaveTextContent("Ritmo planejado (sem dados)");
    expect(rows[2]).toHaveTextContent("15 herdado(s)");
  });

  it("history list with the sparkline of the forecast delivery", async () => {
    wrap(<ForecastSection projectId="p1" />);
    const h = await screen.findByTestId("forecast-history");
    expect(within(h).getAllByRole("listitem")).toHaveLength(2);
    expect(h).toHaveTextContent("recalculada manualmente · Ana");
    expect(within(h).getByRole("img", { name: "Evolução da entrega prevista" })).toBeInTheDocument();
  });

  it("'Recalcular' POSTs and shows the new stored forecast", async () => {
    vi.mocked(api.recomputeForecast).mockResolvedValue(
      forecast({ source: "Manual", createdByName: "Bia", forecastDelivery: "2027-03-01", confidence: 0.81 }));
    wrap(<ForecastSection projectId="p1" />);
    await screen.findByTestId("forecast-kpis");
    fireEvent.click(screen.getByRole("button", { name: /Recalcular/ }));
    await waitFor(() => expect(api.recomputeForecast).toHaveBeenCalledWith("p1"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Previsão recalculada."));
    expect(await screen.findByText(/1 de mar\. de 2027/)).toBeInTheDocument();
    expect(screen.getByTestId("forecast-kpis")).toHaveTextContent("81% (alta)");
    expect(screen.getByTestId("forecast-section")).toHaveTextContent("recalculada manualmente em");
    expect(screen.getByTestId("forecast-section")).toHaveTextContent("por Bia");
    await waitFor(() => expect(api.fetchForecastHistory).toHaveBeenCalledTimes(2));
  });

  it("a failed recalculation is toasted", async () => {
    vi.mocked(api.recomputeForecast).mockRejectedValue(new ApiError(403, "x", "build_predict_disabled"));
    wrap(<ForecastSection projectId="p1" />);
    await screen.findByTestId("forecast-kpis");
    fireEvent.click(screen.getByRole("button", { name: /Recalcular/ }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Previsões não habilitadas para sua empresa."));
  });

  it("403 build_predict_disabled → 'Previsões não habilitadas para sua empresa.' and no Recalcular", async () => {
    vi.mocked(api.fetchForecast).mockRejectedValue(new ApiError(403, "x", "build_predict_disabled"));
    vi.mocked(api.fetchForecastHistory).mockResolvedValue([]);
    wrap(<ForecastSection projectId="p1" />);
    expect(await screen.findByText("Previsões não habilitadas para sua empresa.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Recalcular/ })).not.toBeInTheDocument();
    expect(screen.queryByTestId("forecast-kpis")).not.toBeInTheDocument();
  });

  it("another error says the forecast could not be computed; an on-the-fly forecast says it is not stored", async () => {
    vi.mocked(api.fetchForecast).mockRejectedValueOnce(new ApiError(500, "x"));
    wrap(<ForecastSection projectId="p1" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível calcular a previsão.");
    cleanup();

    vi.mocked(api.fetchForecast).mockResolvedValue(forecast({ id: null, stored: false, source: "OnTheFly", factors: [], stages: [],
      delayDays: null, daysLateVsContract: null }));
    wrap(<ForecastSection projectId="p1" />);
    expect(await screen.findByText(/calculada agora, ainda não gravada/)).toBeInTheDocument();
    expect(screen.getByText("Nenhum fator de atraso identificado.")).toBeInTheDocument();
    expect(screen.getByText("A obra ainda não tem etapas.")).toBeInTheDocument();
  });
});
