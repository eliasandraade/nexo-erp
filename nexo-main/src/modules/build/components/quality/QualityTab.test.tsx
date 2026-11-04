import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { QualityTab } from "./QualityTab";
import * as buildApi from "../../api/build.api";
import * as qualityApi from "../../api/build-quality.api";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
// The runner and the NC section have their own tests.
vi.mock("./InspectionRunner", () => ({
  InspectionRunner: ({ inspectionId, onBack }: { inspectionId: string; onBack: () => void }) => (
    <div data-testid="runner">{inspectionId}<button type="button" onClick={onBack}>stub-back</button></div>
  ),
}));
vi.mock("./NonConformitiesSection", () => ({ NonConformitiesSection: () => <div data-testid="ncs-section" /> }));

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchStages: vi.fn(),
}));
vi.mock("../../api/build-quality.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-quality.api")>()),
  fetchQualitySummary:     vi.fn(),
  fetchInspections:        vi.fn(),
  createInspection:        vi.fn(),
  fetchChecklistTemplates: vi.fn(),
}));

const inspection = (over: Partial<qualityApi.BuildInspectionDto> = {}): qualityApi.BuildInspectionDto => ({
  id: "i1", projectId: "p1", kind: "Quality", stageId: "s1", stageName: "Estrutura", templateId: null, title: "Concretagem laje",
  scheduledFor: "2026-11-05", status: "InProgress", inspectorName: "Carlos", startedAt: null, completedAt: null,
  completedByName: null, notes: null, okItems: 3, notOkItems: 1, pendingItems: 2, items: [],
  createdAt: "2026-11-01T10:00:00Z", updatedAt: "2026-11-01T10:00:00Z", ...over,
});

const summary = (over: Partial<qualityApi.BuildQualitySummaryDto> = {}): qualityApi.BuildQualitySummaryDto => ({
  projectId: "p1", inspectionsByStatus: { Passed: 4, Failed: 1 }, completedInspections: 5, passRate: 80,
  openNonConformities: 3, openNonConformitiesBySeverity: { Critical: 1, Low: 2 }, overdueNonConformities: 1,
  awaitingVerification: 2, criticalOpen: 1, ...over,
});

const template: qualityApi.BuildChecklistTemplateDto = {
  id: "t1", name: "Alvenaria", category: "Estrutura", stageNameHint: "estru", createdAt: "", updatedAt: "",
  items: [{ id: "a", order: 0, text: "Prumo", required: true, requiresPhoto: true }],
};

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("QualityTab", () => {
  beforeEach(() => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([
      { id: "s1", projectId: "p1", name: "Estrutura", description: null, order: 0, status: "InProgress", plannedStartDate: null,
        plannedEndDate: null, actualStartDate: null, actualEndDate: null, progressPercent: 30 },
    ]);
    vi.mocked(qualityApi.fetchChecklistTemplates).mockResolvedValue([template]);
    vi.mocked(qualityApi.fetchQualitySummary).mockResolvedValue(summary());
    vi.mocked(qualityApi.fetchInspections).mockResolvedValue([]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("shows the quality summary: inspections by status, pass rate, open NCs by severity and attention points", async () => {
    renderTab(<QualityTab projectId="p1" />);
    const box = await screen.findByTestId("quality-summary");
    expect(within(box).getByText("Aprovada")).toBeInTheDocument();
    expect(within(box).getByText("4")).toBeInTheDocument();
    expect(within(box).getByText("Reprovada")).toBeInTheDocument();
    expect(within(box).getByText("80,0%")).toBeInTheDocument();
    expect(within(box).getByText("5 vistoria(s) concluída(s)")).toBeInTheDocument();
    expect(within(box).getByText("Crítica: 1")).toBeInTheDocument();
    expect(within(box).getByText("Baixa: 2")).toBeInTheDocument();
    expect(within(box).getByText("Críticas abertas").parentElement).toHaveTextContent("1");
    expect(within(box).getByText("Aguardando verificação").parentElement).toHaveTextContent("2");
  });

  it("an empty summary shows 'Nenhuma ainda' and '—'; an unexpected body hides it", async () => {
    vi.mocked(qualityApi.fetchQualitySummary).mockResolvedValue(summary({
      inspectionsByStatus: {}, passRate: null, completedInspections: 0, openNonConformitiesBySeverity: {}, criticalOpen: 0,
    }));
    renderTab(<QualityTab projectId="p1" />);
    const box = await screen.findByTestId("quality-summary");
    expect(within(box).getByText("Nenhuma ainda")).toBeInTheDocument();
    expect(within(box).getByText("—")).toBeInTheDocument();
    cleanup();

    vi.mocked(qualityApi.fetchQualitySummary).mockResolvedValue([] as unknown as qualityApi.BuildQualitySummaryDto);
    renderTab(<QualityTab projectId="p1" />);
    expect(await screen.findByText(/Nenhuma vistoria de qualidade nesta obra/)).toBeInTheDocument();
    expect(screen.queryByTestId("quality-summary")).not.toBeInTheDocument();
  });

  it("lists the quality inspections, filters by status and opens one in the runner", async () => {
    vi.mocked(qualityApi.fetchInspections).mockResolvedValue([
      inspection(),
      inspection({ id: "i2", title: "Sem etapa", stageName: null, scheduledFor: null, inspectorName: null, status: "Passed" }),
    ]);
    renderTab(<QualityTab projectId="p1" />);

    expect(await screen.findByText("Concretagem laje")).toBeInTheDocument();
    expect(qualityApi.fetchInspections).toHaveBeenCalledWith("p1", { kind: "Quality", status: undefined });
    expect(screen.getByText(/Estrutura · .*2026 · Carlos/)).toBeInTheDocument();
    expect(screen.getAllByText("3 ok · 1 não ok · 2 pendente(s)")).toHaveLength(2);
    expect(screen.getByText("Sem etapa", { selector: "p.text-xs" })).toBeInTheDocument();

    fireEvent.change(screen.getByLabelText("Filtrar vistorias por status"), { target: { value: "Failed" } });
    await waitFor(() => expect(qualityApi.fetchInspections).toHaveBeenLastCalledWith("p1", { kind: "Quality", status: "Failed" }));

    vi.mocked(qualityApi.fetchInspections).mockResolvedValue([inspection()]);
    fireEvent.change(screen.getByLabelText("Filtrar vistorias por status"), { target: { value: "" } });
    fireEvent.click(await screen.findByText("Concretagem laje"));
    expect(screen.getByTestId("runner")).toHaveTextContent("i1");
    fireEvent.click(screen.getByText("stub-back"));
    expect(await screen.findByTestId("quality-summary")).toBeInTheDocument();
  });

  it("filtered empty list and a load error", async () => {
    renderTab(<QualityTab projectId="p1" />);
    fireEvent.change(await screen.findByLabelText("Filtrar vistorias por status"), { target: { value: "Cancelled" } });
    expect(await screen.findByText("Nenhuma vistoria de qualidade com esse status.")).toBeInTheDocument();
    cleanup();

    vi.mocked(qualityApi.fetchInspections).mockRejectedValue(new Error("x"));
    renderTab(<QualityTab projectId="p1" />);
    expect(await screen.findByText("Erro ao carregar as vistorias.")).toBeInTheDocument();
  });

  it("creates an inspection from a checklist (title and stage from the template) and opens it", async () => {
    vi.mocked(qualityApi.createInspection).mockResolvedValue(inspection({ id: "i9" }));
    renderTab(<QualityTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Nova vistoria/ }));
    const dialog = await screen.findByRole("dialog");
    // wait for the stages so the template hint can match one
    await within(dialog).findByRole("option", { name: "Estrutura" });
    fireEvent.change(within(dialog).getByLabelText("Checklist"), { target: { value: "t1" } });
    expect(within(dialog).getByLabelText("Título")).toHaveValue("Alvenaria");
    expect(within(dialog).getByLabelText("Etapa")).toHaveValue("s1");
    expect(within(dialog).getByText("1 item(ns), 1 com foto obrigatória.")).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("Vistoriador"), { target: { value: " Carlos " } });
    fireEvent.change(within(dialog).getByLabelText("Data prevista"), { target: { value: "2026-11-05" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Criar vistoria" }));

    await waitFor(() => expect(qualityApi.createInspection).toHaveBeenCalledWith("p1", {
      title: "Alvenaria", templateId: "t1", stageId: "s1", scheduledFor: "2026-11-05", inspectorName: "Carlos", notes: null, items: [],
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Vistoria criada."));
    expect(await screen.findByTestId("runner")).toHaveTextContent("i9");
  });

  it("creation needs a title and a checklist or item; a server error is toasted", async () => {
    vi.mocked(qualityApi.createInspection).mockRejectedValue(new Error("Etapa encerrada"));
    renderTab(<QualityTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Nova vistoria/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Criar vistoria" }));
    expect(toast.error).toHaveBeenCalledWith("Informe o título da vistoria.");
    fireEvent.change(within(dialog).getByLabelText("Título"), { target: { value: "Avulsa" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Criar vistoria" }));
    expect(toast.error).toHaveBeenCalledWith("Escolha um checklist ou inclua ao menos um item.");

    fireEvent.click(within(dialog).getByRole("button", { name: /Adicionar item/ }));
    fireEvent.change(within(dialog).getByLabelText("Item 1"), { target: { value: "Rejunte" } });
    fireEvent.change(within(dialog).getByLabelText("Observações (interno)"), { target: { value: "  " } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Criar vistoria" }));
    await waitFor(() => expect(qualityApi.createInspection).toHaveBeenCalledWith("p1", expect.objectContaining({
      title: "Avulsa", templateId: null, stageId: null, notes: null,
      items: [{ text: "Rejunte", required: true, requiresPhoto: false }],
    })));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Etapa encerrada"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("switches to the NC and checklist sections", async () => {
    renderTab(<QualityTab projectId="p1" />);
    fireEvent.click(await screen.findByRole("tab", { name: "Não conformidades" }));
    expect(screen.getByTestId("ncs-section")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "Checklists" }));
    expect(await screen.findByText("Alvenaria")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Checklists" })).toHaveAttribute("aria-selected", "true");
  });
});
