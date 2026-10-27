import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup } from "@testing-library/react";
import { renderTax } from "../test/render";
import TaxOverviewPage from "./TaxOverviewPage";
import TaxProfilePage from "./TaxProfilePage";
import * as api from "../api/tax.api";
import type { TaxOverviewDto, TaxProfileDto } from "../api/tax.api";

const session = { role: "diretoria" as string };
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session }) }));
vi.mock("../api/tax.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/tax.api")>()),
  getTaxOverview: vi.fn(),
  getTaxProfile: vi.fn(),
}));

globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;

const overview = (over: Partial<TaxOverviewDto> = {}): TaxOverviewDto => ({
  profileConfigured: true, periodFrom: "2026-09-28", periodTo: "2026-10-27", calculations: 4, taxedBase: 12000, estimatedTax: 120,
  effectiveRatePercent: 1, creditsAvailable: 30, creditsBlocked: 0, creditsNotAppropriated: 5, openOpportunities: 3,
  potentialSavings: 210.5, potentialDeferrals: 900, potentialCashFlow: 40, potentialCredits: 15, openHighRisk: 1,
  blockedOpportunities: 1, splitDivergences: 0, activeRulesToday: 9, pendingRulesToday: 0, ...over,
} as TaxOverviewDto);

describe("TaxOverviewPage", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(cleanup);

  it("keeps savings, deferrals, cash flow and credits apart — never one total", async () => {
    vi.mocked(api.getTaxOverview).mockResolvedValue(overview());
    renderTax(<TaxOverviewPage />);

    expect(await screen.findByText("R$ 210,50")).toBeInTheDocument();
    expect(screen.getByText("R$ 900,00")).toBeInTheDocument();
    expect(screen.getByText("Diferimento — não é economia")).toBeInTheDocument();
    expect(screen.queryByText("R$ 1.165,50")).not.toBeInTheDocument();
    expect(screen.getByText(/1 oportunidade de risco alto/)).toBeInTheDocument();
  });

  it("asks for the tax profile before anything else", async () => {
    vi.mocked(api.getTaxOverview).mockResolvedValue(overview({ profileConfigured: false }));
    renderTax(<TaxOverviewPage />);

    expect(await screen.findByText("Cadastre o perfil tributário")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Cadastrar perfil tributário" })).toHaveAttribute("href", "/tributos/perfil");
  });

  it("shows an empty state instead of zeros when nothing was calculated in the period", async () => {
    vi.mocked(api.getTaxOverview).mockResolvedValue(overview({ calculations: 0, taxedBase: 0, estimatedTax: 0, effectiveRatePercent: null }));
    renderTax(<TaxOverviewPage />);

    expect(await screen.findByText("Nenhum cálculo nos últimos 30 dias")).toBeInTheDocument();
  });
});

const version = {
  id: "v2", versionNumber: 2, validFrom: "2026-07-01", regime: "LucroPresumido", state: "SP", cityIbgeCode: "3550308",
  primaryCnae: "4781400", secondaryCnaes: [], specialRegimes: [], benefits: [], accessoryObligationsCompliant: true,
  reason: "Saída do Simples", createdByName: "Diretora", createdAt: "2026-07-01T12:00:00Z",
} as TaxProfileDto["versions"][number];

describe("TaxProfilePage", () => {
  beforeEach(() => { vi.clearAllMocks(); session.role = "diretoria"; });
  afterEach(cleanup);

  it("shows the version in force and the history, and lets Diretoria add a new version", async () => {
    vi.mocked(api.getTaxProfile).mockResolvedValue({
      current: version, versions: [version, { ...version, id: "v1", versionNumber: 1, validFrom: "2026-01-01", regime: "SimplesNacional", reason: "Início" }],
      suggestion: null,
    });
    renderTax(<TaxProfilePage />);

    expect(await screen.findByText("Em vigor: versão 2")).toBeInTheDocument();
    expect(screen.getByText(/as anteriores ficam guardadas/)).toBeInTheDocument();
    expect(screen.getByText("Saída do Simples")).toBeInTheDocument();
    expect(screen.getByText("Início")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Registrar nova versão" })).toBeInTheDocument();
  });

  it("does not offer a manager to change the regime", async () => {
    session.role = "gerente";
    vi.mocked(api.getTaxProfile).mockResolvedValue({ current: version, versions: [version], suggestion: null });
    renderTax(<TaxProfilePage />);

    expect(await screen.findByText("Só a diretoria registra uma nova versão do perfil tributário.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Registrar nova versão" })).not.toBeInTheDocument();
  });

  it("starts a first profile from the fiscal issuer data", async () => {
    vi.mocked(api.getTaxProfile).mockResolvedValue({ current: null, versions: [], suggestion: { regime: "LucroReal", state: "RJ", cityIbgeCode: "3304557" } });
    renderTax(<TaxProfilePage />);

    expect(await screen.findByText(/Os dados do emissor \(Fiscal\) já preenchem/)).toBeInTheDocument();
    expect(screen.getByLabelText("Município (código IBGE)")).toHaveValue("3304557");
  });
});
