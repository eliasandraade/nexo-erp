import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import * as platformApi from "../services/platformApi";
import { polyfillRadix, renderAi } from "../test/aiFixtures";
import PlatformBuildPage from "./PlatformBuildPage";

const { allowed } = vi.hoisted(() => ({ allowed: { value: true as boolean | null } }));

vi.mock("../hooks/usePlatformSecurity", () => ({ useAdminActionsAllowed: () => allowed.value }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../services/platformApi", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../services/platformApi")>()),
  fetchPlatformBuildOverview: vi.fn(),
  fetchPlatformBuildJobs: vi.fn(),
  retryPlatformBuildJob: vi.fn(),
}));

const tenant = (over: Partial<platformApi.PlatformBuildTenant> = {}): platformApi.PlatformBuildTenant => ({
  tenantId: "t1", companyName: "Construtora Alfa", moduleStatus: "Active", projectsActive: 3, projectsTotal: 5, publishedPortals: 2,
  modelVersionsByStatus: { Ready: 4, Failed: 1, Queued: 0 }, jobsFailed: 1, jobsPending: 2,
  lastJobError: "Falha ao ler o IFC (esquema não suportado).", lastJobErrorAt: "2026-10-29T12:00:00Z",
  storage: { modelsBytes: 5 * 1024 * 1024, realityCaptureBytes: 1536, documentsBytes: 2048, photosBytes: null, photosCount: 12,
    totalBytes: 5 * 1024 * 1024 + 3584 },
  ai30d: { suggestions: 10, accepted: 6, rejected: 2, acceptedRate: 0.75, inputTokens: 12000, outputTokens: 3000, costMicros: 250000 },
  flags: { build3D: true, buildAi: false, buildVision: false, buildPredict: true, customized: false },
  ...over,
});

const overview = (): platformApi.PlatformBuildOverview => ({
  generatedAt: "2026-10-30T10:00:00Z",
  tenants: [tenant(), tenant({ tenantId: "t2", companyName: "Obras Beta", moduleStatus: null, jobsFailed: 0, lastJobError: null,
    modelVersionsByStatus: {}, storage: { modelsBytes: 0, realityCaptureBytes: 0, documentsBytes: 0, photosBytes: 500, photosCount: 1, totalBytes: 500 },
    ai30d: { suggestions: 0, accepted: 0, rejected: 0, acceptedRate: null, inputTokens: 0, outputTokens: 0, costMicros: 0 },
    flags: { build3D: true, buildAi: true, buildVision: true, buildPredict: true, customized: true } })],
  totals: { tenants: 2, projectsActive: 6, projectsTotal: 10, publishedPortals: 4, jobsFailed: 1, jobsPending: 4,
    storageBytes: 3 * 1024 * 1024 * 1024, aiSuggestions30d: 10, aiCostMicros30d: 250000 },
});

const failedJob: platformApi.PlatformBuildJob = {
  id: "j1", tenantId: "t1", companyName: "Construtora Alfa", projectId: "p1", versionId: "v1", versionNumber: 3, sourceFormat: "Ifc",
  kind: "IfcExtract", status: "Failed", attempts: 2, error: "Tempo esgotado no processamento.", createdAt: "2026-10-29T11:00:00Z",
  finishedAt: "2026-10-29T11:05:00Z",
};

describe("PlatformBuildPage (SuperAdmin)", () => {
  beforeEach(() => {
    polyfillRadix();
    vi.mocked(platformApi.fetchPlatformBuildOverview).mockResolvedValue(overview());
    vi.mocked(platformApi.fetchPlatformBuildJobs).mockResolvedValue([failedJob]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); allowed.value = true; });

  it("renders the totals and the overview per company, with bytes formatted and no file names", async () => {
    renderAi(<PlatformBuildPage />);
    const totals = await screen.findByTestId("platform-build-totals");
    expect(totals).toHaveTextContent("Empresas2");
    expect(totals).toHaveTextContent("Obras ativas / total6 / 10");
    expect(totals).toHaveTextContent("Armazenamento3 GB");
    expect(totals).toHaveTextContent("Custo de IA (30 dias)US$ 0,25");

    const cards = screen.getAllByTestId("platform-build-tenant");
    expect(cards).toHaveLength(2);
    const alfa = cards[0];
    expect(alfa).toHaveTextContent("Construtora Alfa");
    expect(alfa).toHaveTextContent("Módulo: Active");
    expect(alfa).toHaveTextContent("4 prontas · 1 com falha");
    expect(alfa).toHaveTextContent("Último erro");
    expect(alfa).toHaveTextContent("esquema não suportado");
    const storage = within(alfa).getByTestId("platform-build-storage");
    expect(storage).toHaveTextContent("Modelos 5 MB · Capturas 1,5 KB · Documentos 2 KB");
    expect(storage).toHaveTextContent("Fotos do diário: 12 (tamanho não registrado)");
    expect(alfa).toHaveTextContent("10 sugestões · 6 aceitas · 2 recusadas (75% aceitas)");
    expect(alfa).toHaveTextContent("padrões");
    expect(cards[1]).toHaveTextContent("sem assinatura (só dados)");
    expect(cards[1]).toHaveTextContent("Versões de modeloNenhuma");
    expect(cards[1]).toHaveTextContent("Fotos do diário: 1 · 500 B");
    expect(document.body.textContent).not.toMatch(/\.ifc\b/);
  });

  it("lists the failed jobs; 'Reprocessar' confirms and POSTs the retry", async () => {
    vi.mocked(platformApi.retryPlatformBuildJob).mockResolvedValue({ ...failedJob, id: "j2", status: "Pending" });
    renderAi(<PlatformBuildPage />);
    const job = await screen.findByTestId("platform-build-job");
    expect(job).toHaveTextContent("Versão 3 · Ifc · IfcExtract · 2 tentativa(s)");
    expect(job).toHaveTextContent("Tempo esgotado no processamento.");
    expect(platformApi.fetchPlatformBuildJobs).toHaveBeenCalledWith("Failed");

    fireEvent.click(within(job).getByRole("button", { name: /Reprocessar/ }));
    const dialog = await screen.findByRole("alertdialog");
    expect(dialog).toHaveTextContent("Reprocessar este job?");
    expect(dialog).toHaveTextContent("versão 3 de Construtora Alfa");
    expect(platformApi.retryPlatformBuildJob).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole("button", { name: "Reprocessar" }));
    await waitFor(() => expect(platformApi.retryPlatformBuildJob).toHaveBeenCalledWith("j1"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Job recolocado na fila."));
  });

  it("without a two-step sign-in 'Reprocessar' is disabled", async () => {
    allowed.value = false;
    renderAi(<PlatformBuildPage />);
    const job = await screen.findByTestId("platform-build-job");
    expect(within(job).getByRole("button", { name: /Reprocessar/ })).toBeDisabled();
    expect(screen.getByText("Reprocessar exige login com verificação em duas etapas.")).toBeInTheDocument();
  });

  it("a refused retry shows the server's reason; other statuses have no retry", async () => {
    vi.mocked(platformApi.retryPlatformBuildJob).mockRejectedValue(
      new ApiError(422, "x", "job_already_queued", undefined, undefined, "Já existe um job na fila para esta versão."));
    renderAi(<PlatformBuildPage />);
    fireEvent.click(within(await screen.findByTestId("platform-build-job")).getByRole("button", { name: /Reprocessar/ }));
    fireEvent.click(within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Reprocessar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Já existe um job na fila para esta versão."));

    vi.mocked(platformApi.fetchPlatformBuildJobs).mockResolvedValue([{ ...failedJob, id: "j3", status: "Pending", error: null }]);
    fireEvent.change(screen.getByLabelText("Situação dos jobs"), { target: { value: "Pending" } });
    await waitFor(() => expect(platformApi.fetchPlatformBuildJobs).toHaveBeenCalledWith("Pending"));
    await waitFor(() => expect(screen.queryByRole("button", { name: /Reprocessar/ })).not.toBeInTheDocument());
  });

  it("empty and error states", async () => {
    vi.mocked(platformApi.fetchPlatformBuildOverview).mockResolvedValue({ ...overview(), tenants: [] });
    vi.mocked(platformApi.fetchPlatformBuildJobs).mockResolvedValue([]);
    renderAi(<PlatformBuildPage />);
    expect(await screen.findByText("Nenhuma empresa usa o Orken Build.")).toBeInTheDocument();
    expect(await screen.findByText("Nenhum job nesta situação.")).toBeInTheDocument();
    cleanup();

    vi.mocked(platformApi.fetchPlatformBuildOverview).mockRejectedValue(new Error("x"));
    vi.mocked(platformApi.fetchPlatformBuildJobs).mockRejectedValue(new Error("x"));
    renderAi(<PlatformBuildPage />);
    expect(await screen.findByText("Não foi possível carregar a visão do Orken Build.")).toBeInTheDocument();
    expect(await screen.findByText("Não foi possível consultar os jobs.")).toBeInTheDocument();
  });
});
