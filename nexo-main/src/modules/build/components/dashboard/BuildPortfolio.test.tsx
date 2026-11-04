import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import * as buildApi from "../../api/build.api";
import { BuildDashboardSection } from "../BuildDashboardSection";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchBuildDashboard: vi.fn(),
}));

function Where() {
  const loc = useLocation();
  return <p data-testid="where">{loc.pathname}{loc.search}</p>;
}

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={["/build"]}>
        <Routes>
          <Route path="/build" element={ui} />
          <Route path="*" element={<Where />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

const project = (over: Partial<buildApi.BuildDashboardProjectDto> = {}): buildApi.BuildDashboardProjectDto => ({
  projectId: "p1", name: "Casa Alfa", clientName: "Ana", status: "InProgress", actualPercent: 40, plannedPercent: 50,
  expectedEnd: "2027-01-20", forecastDelivery: "2027-02-15", daysLate: 26, forecastMethodology: "productivity/1",
  costRealized: 120000, contractTotal: 300000, cpiLike: 0.8, openNonConformities: { Low: 1, Medium: 0, High: 0, Critical: 0 },
  openAssistance: 0, pendingChangeOrders: 1, lateMaterials: 0, portalPublished: true, lastDailyLogDate: "2026-10-29",
  pendingAiSuggestions: 2, ...over,
});

const base: buildApi.BuildDashboardDto = {
  totalProjects: 3, planningCount: 1, inProgressCount: 2, pausedCount: 0, completedCount: 0, cancelledCount: 0, overdueCount: 1,
  totalEstimated: 900000, totalApproved: 800000, totalRealized: 250000, balance: 550000, avgStageProgress: 35, recentExpenses: [],
};

const wave6 = (): buildApi.BuildDashboardDto => ({
  ...base,
  asOf: "2026-10-30",
  projects: [
    project(),
    project({ projectId: "p2", name: "Edifício Beta", clientName: "Construtora B", daysLate: -3, cpiLike: 1.1,
      openNonConformities: { Low: 0, Medium: 0, High: 1, Critical: 2 }, portalPublished: false, lastDailyLogDate: null,
      pendingAiSuggestions: 0, lateMaterials: 3, openAssistance: 1 }),
    project({ projectId: "p3", name: "Reforma Gama", status: "Planning", daysLate: null, forecastDelivery: null, cpiLike: null,
      actualPercent: 0, plannedPercent: null, contractTotal: null, costRealized: 0, openNonConformities: {}, pendingChangeOrders: 0 }),
  ],
  totals: { activeProjects: 3, lateProjects: 1, costRealized: 240000, contractTotal: 600000, openNonConformities: 4,
    openCriticalNonConformities: 2, openAssistance: 1, pendingChangeOrders: 2, lateMaterials: 3, publishedPortals: 2, pendingAiSuggestions: 4 },
  topRisks: [
    { projectId: "p2", projectName: "Edifício Beta", kind: "critical_nonconformity", severity: "high", message: "2 não conformidade(s) crítica(s) aberta(s).", value: 2 },
    { projectId: "p1", projectName: "Casa Alfa", kind: "forecast_late", severity: "medium", message: "Entrega prevista 26 dias depois do contrato.", value: 26 },
  ],
});

const names = (container: HTMLElement) =>
  within(container).getAllByTestId("portfolio-row").map((r) => r.querySelector("td button")?.textContent);

afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("Build dashboard — portfolio (Wave 6)", () => {
  it("renders the totals, the portfolio per active project and keeps the old KPIs", async () => {
    vi.mocked(buildApi.fetchBuildDashboard).mockResolvedValue(wave6());
    wrap(<BuildDashboardSection />);

    const totals = await screen.findByTestId("portfolio-totals");
    expect(totals).toHaveTextContent("Obras ativas3");
    expect(totals).toHaveTextContent("Atrasadas (previsão)1");
    expect(totals).toHaveTextContent("NCs abertas (críticas)4 (2)");
    expect(screen.getByText("Progresso médio")).toBeInTheDocument();       // old KPI strip still there

    const table = screen.getByTestId("portfolio-table");
    expect(names(table)).toEqual(["Edifício Beta", "Casa Alfa", "Reforma Gama"]);   // default: higher risk first
    const alfa = within(table).getAllByTestId("portfolio-row")[1];
    expect(alfa).toHaveTextContent("40% × 50%");
    expect(alfa).toHaveTextContent("26 dias de atraso");
    expect(alfa).toHaveTextContent("contrato 20 de jan. de 2027");
    expect(alfa.textContent).toMatch(/R\$\s120\.000 \/ R\$\s300\.000/);
    expect(alfa).toHaveTextContent("0,80");
    expect(alfa).toHaveTextContent("1 baixa");
    expect(alfa).toHaveTextContent("1 aditivo(s) pendente(s)");
    expect(alfa).toHaveTextContent("Portal publicado");
    expect(alfa).toHaveTextContent("2 sugestão(ões) de IA");
    const beta = within(table).getAllByTestId("portfolio-row")[0];
    expect(beta).toHaveTextContent("No prazo");
    expect(beta).toHaveTextContent("2 crítica · 1 alta");
    expect(beta).toHaveTextContent("Diário: nenhum");
    expect(within(table).getAllByTestId("portfolio-row")[2]).toHaveTextContent("Sem previsão");

    // mobile cards carry the same projects
    expect(within(screen.getByTestId("portfolio-cards")).getAllByTestId("portfolio-card")).toHaveLength(3);
  });

  it("filters late projects and projects with a critical NC; sorts by name", async () => {
    vi.mocked(buildApi.fetchBuildDashboard).mockResolvedValue(wave6());
    wrap(<BuildDashboardSection />);
    const table = await screen.findByTestId("portfolio-table");

    fireEvent.click(screen.getByRole("button", { name: "Atrasadas" }));
    expect(screen.getByRole("button", { name: "Atrasadas" })).toHaveAttribute("aria-pressed", "true");
    expect(names(screen.getByTestId("portfolio-table"))).toEqual(["Casa Alfa"]);

    fireEvent.click(screen.getByRole("button", { name: "Com NC crítica" }));
    expect(names(screen.getByTestId("portfolio-table"))).toEqual(["Edifício Beta"]);

    fireEvent.click(screen.getByRole("button", { name: "Todas" }));
    fireEvent.change(screen.getByLabelText("Ordenar por"), { target: { value: "name" } });
    expect(names(screen.getByTestId("portfolio-table"))).toEqual(["Casa Alfa", "Edifício Beta", "Reforma Gama"]);
    fireEvent.change(screen.getByLabelText("Ordenar por"), { target: { value: "daysLate" } });
    expect(names(screen.getByTestId("portfolio-table"))).toEqual(["Casa Alfa", "Edifício Beta", "Reforma Gama"]);
    fireEvent.change(screen.getByLabelText("Ordenar por"), { target: { value: "cpi" } });
    expect(names(screen.getByTestId("portfolio-table"))).toEqual(["Casa Alfa", "Edifício Beta", "Reforma Gama"]);
    fireEvent.change(screen.getByLabelText("Ordenar por"), { target: { value: "progress" } });
    expect(names(screen.getByTestId("portfolio-table"))[2]).toBe("Reforma Gama");
    expect(table).toBeInTheDocument();
  });

  it("a filter with no match says so", async () => {
    vi.mocked(buildApi.fetchBuildDashboard).mockResolvedValue({ ...wave6(), projects: [project({ daysLate: 0 })] });
    wrap(<BuildDashboardSection />);
    await screen.findByTestId("portfolio-table");
    fireEvent.click(screen.getByRole("button", { name: "Atrasadas" }));
    expect(screen.getByText("Nenhuma obra neste filtro.")).toBeInTheDocument();
  });

  it("'Principais riscos' link to the project's tab", async () => {
    vi.mocked(buildApi.fetchBuildDashboard).mockResolvedValue(wave6());
    wrap(<BuildDashboardSection />);
    const risks = await screen.findByTestId("portfolio-risks");
    expect(within(risks).getAllByRole("listitem")).toHaveLength(2);
    fireEvent.click(within(risks).getByRole("button", { name: /Casa Alfa/ }));
    expect(await screen.findByTestId("where")).toHaveTextContent("/build/projetos/p1?aba=riscos");
  });

  it("a critical NC risk opens Qualidade; a card opens the project", async () => {
    vi.mocked(buildApi.fetchBuildDashboard).mockResolvedValue(wave6());
    wrap(<BuildDashboardSection />);
    fireEvent.click(within(await screen.findByTestId("portfolio-risks")).getByRole("button", { name: /Edifício Beta/ }));
    expect(await screen.findByTestId("where")).toHaveTextContent("/build/projetos/p2?aba=qualidade");
    cleanup();

    wrap(<BuildDashboardSection />);
    fireEvent.click((await screen.findAllByTestId("portfolio-card"))[0].querySelector("button")!);
    expect(await screen.findByTestId("where")).toHaveTextContent("/build/projetos/p2");
  });

  it("an older API without the portfolio keeps the previous dashboard only", async () => {
    vi.mocked(buildApi.fetchBuildDashboard).mockResolvedValue(base);
    wrap(<BuildDashboardSection />);
    expect(await screen.findByText("Em andamento")).toBeInTheDocument();
    expect(screen.queryByTestId("build-portfolio")).not.toBeInTheDocument();
  });

  it("no active projects and no risks", async () => {
    vi.mocked(buildApi.fetchBuildDashboard).mockResolvedValue({ ...wave6(), projects: [], topRisks: [] });
    wrap(<BuildDashboardSection />);
    expect(await screen.findByText("Nenhuma obra ativa.")).toBeInTheDocument();
    expect(screen.getByText("Nenhum risco relevante nas obras ativas.")).toBeInTheDocument();
  });
});
