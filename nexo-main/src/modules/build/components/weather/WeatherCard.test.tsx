import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import * as api from "../../api/build-forecast.api";
import { WeatherCard } from "./WeatherCard";

vi.mock("../../api/build-forecast.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-forecast.api")>()),
  fetchProjectWeather: vi.fn(),
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

const stages: api.BuildWeatherStageDto[] = [
  { stageId: "s1", name: "Concretagem da laje", sensitive: true, basis: "keyword", keyword: "concretagem", explicitSetting: null,
    activeFrom: "2026-11-02", activeTo: "2026-11-06" },
  { stageId: "s2", name: "Instalações", sensitive: false, basis: "none", keyword: null, explicitSetting: null, activeFrom: null, activeTo: null },
];

const day = (date: string, over: Partial<api.BuildWeatherDayDto> = {}): api.BuildWeatherDayDto => ({
  date, temperatureMax: 29, temperatureMin: 19, precipitationMm: 0, precipitationProbability: 10, windSpeedMaxKmh: 12,
  description: "Céu limpo", level: "none", risks: [], ...over,
});

const available = (): api.BuildWeatherDto => ({
  projectId: "p1", available: true, reason: null, message: null, source: "Open-Meteo",
  disclaimer: "Previsão do tempo para o local da obra — é previsão, não certeza.",
  today: day("2026-10-30", { description: "Parcialmente nublado" }),
  days: [
    day("2026-10-31"), day("2026-11-01", { description: "Garoa fraca", precipitationMm: 1 }),
    day("2026-11-02", {
      description: "Chuva forte", precipitationMm: 15, precipitationProbability: 85, level: "high",
      risks: [{ stageId: "s1", stageName: "Concretagem da laje", kind: "heavy_rain", level: "high",
        message: "Chuva forte prevista (15 mm, probabilidade 85%) — etapa Concretagem da laje. Previsão, pode mudar." }],
    }),
    day("2026-11-03", {
      description: "Trovoada", windSpeedMaxKmh: 45, level: "medium",
      risks: [{ stageId: "s1", stageName: "Concretagem da laje", kind: "rain", level: "medium", message: "Chuva prevista (4 mm) — Concretagem." }],
    }),
    day("2026-11-04", { description: "Nublado" }), day("2026-11-05", { description: "Neblina" }), day("2026-11-06", { description: null, precipitationMm: 5 }),
  ],
  stages,
});

afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("WeatherCard", () => {
  it("without coordinates: the server's message and a CTA to edit the site", async () => {
    vi.mocked(api.fetchProjectWeather).mockResolvedValue({
      projectId: "p1", available: false, reason: "no_coordinates",
      message: "Informe a localização (latitude e longitude) da obra para ver a previsão do tempo.",
      source: "Open-Meteo", disclaimer: "x", today: null, days: [], stages,
    });
    const onEditSite = vi.fn();
    wrap(<WeatherCard projectId="p1" onEditSite={onEditSite} />);
    expect(await screen.findByText(/Informe a localização \(latitude e longitude\)/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Informar a localização da obra/ }));
    expect(onEditSite).toHaveBeenCalled();
    expect(screen.queryByTestId("weather-days")).not.toBeInTheDocument();
    expect(screen.getByTestId("weather-card")).toHaveTextContent("Etapas sensíveis ao clima: Concretagem da laje.");
    expect(api.fetchProjectWeather).toHaveBeenCalledWith("p1");
  });

  it("weather off / provider down: message without the CTA", async () => {
    vi.mocked(api.fetchProjectWeather).mockResolvedValue({
      projectId: "p1", available: false, reason: "provider_unavailable", message: null, source: "Open-Meteo", disclaimer: "x",
      today: null, days: [], stages: [],
    });
    wrap(<WeatherCard projectId="p1" onEditSite={vi.fn()} />);
    expect(await screen.findByText("Previsão do tempo indisponível.")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.getByText(/Nenhuma etapa marcada como sensível ao clima/)).toBeInTheDocument();
  });

  it("today, the next 7 days and the risk rows of sensitive stages, worded as forecast", async () => {
    vi.mocked(api.fetchProjectWeather).mockResolvedValue(available());
    wrap(<WeatherCard projectId="p1" />);
    expect(await screen.findByTestId("weather-today")).toHaveTextContent("Hoje · Parcialmente nublado");
    expect(screen.getByTestId("weather-today")).toHaveTextContent("29° / 19°");
    expect(within(screen.getByTestId("weather-days")).getAllByRole("listitem")).toHaveLength(7);

    const risks = within(screen.getByTestId("weather-risks")).getAllByRole("listitem");
    expect(risks).toHaveLength(2);
    expect(risks[0]).toHaveTextContent("Chuva forte (previsão)");
    expect(risks[0]).toHaveTextContent("Concretagem da laje");
    expect(risks[0]).toHaveTextContent("Previsão, pode mudar.");
    expect(risks[1]).toHaveTextContent("Chuva (previsão)");
    expect(screen.getByTestId("weather-card")).toHaveTextContent("é previsão, não certeza. Fonte: Open-Meteo.");
  });

  it("no risks → says so; an error is shown", async () => {
    vi.mocked(api.fetchProjectWeather).mockResolvedValue({ ...available(), days: [day("2026-10-31")], today: null });
    wrap(<WeatherCard projectId="p1" />);
    expect(await screen.findByText("Nenhum risco climático previsto para as etapas sensíveis em campo.")).toBeInTheDocument();
    cleanup();

    vi.mocked(api.fetchProjectWeather).mockRejectedValue(new Error("rede"));
    wrap(<WeatherCard projectId="p1" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("Não foi possível consultar o clima.");
  });
});
