import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { BuildStagesTab } from "./BuildStagesTab";
import * as buildApi from "../../api/build.api";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchStages:         vi.fn(),
  createStage:         vi.fn(),
  updateStage:         vi.fn(),
  reorderStages:       vi.fn(),
  deleteStage:         vi.fn(),
  updateStageProgress: vi.fn(),
}));

const stage = (id: string, name: string, over: Partial<buildApi.BuildStageDto> = {}): buildApi.BuildStageDto => ({
  id, projectId: "p1", name, description: null, order: 0, status: "Pending", plannedStartDate: null,
  plannedEndDate: null, actualStartDate: null, actualEndDate: null, progressPercent: 0, weight: 1, ...over,
});

const threeStages = () => [
  stage("s1", "Fundação", { order: 0, status: "Completed", progressPercent: 100, weight: 3,
    plannedStartDate: "2026-09-01", plannedEndDate: "2026-09-30", description: "Sapatas e vigas" }),
  stage("s2", "Estrutura", { order: 1, status: "InProgress", progressPercent: 40, plannedStartDate: "2026-10-01" }),
  stage("s3", "Pintura", { order: 2 }),
];

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("BuildStagesTab", () => {
  beforeEach(() => { vi.spyOn(window, "confirm").mockReturnValue(true); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.restoreAllMocks(); });

  it("lists the stages with status, weight, planned dates and progress", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue(threeStages());
    renderTab(<BuildStagesTab projectId="p1" />);

    expect(await screen.findByText("Fundação")).toBeInTheDocument();
    expect(buildApi.fetchStages).toHaveBeenCalledWith("p1");
    expect(screen.getByText("3 etapas")).toBeInTheDocument();
    expect(screen.getByText("Concluída")).toBeInTheDocument();
    expect(screen.getByText("Em andamento")).toBeInTheDocument();
    expect(screen.getByText("Pendente")).toBeInTheDocument();
    expect(screen.getByText("Sapatas e vigas")).toBeInTheDocument();
    expect(screen.getByText(/Peso 3 · Previsto: 01\/09 a 30\/09/)).toBeInTheDocument();
    expect(screen.getByText(/Previsto: 01\/10 a —/)).toBeInTheDocument();
    expect(screen.getByText("100%")).toBeInTheDocument();
    expect(screen.getByText("40%")).toBeInTheDocument();
    // first can't go up, last can't go down
    expect(screen.getByRole("button", { name: "Subir Fundação" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Descer Pintura" })).toBeDisabled();
  });

  it("empty project: shows the empty state and opens the add form from it", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([]);
    renderTab(<BuildStagesTab projectId="p1" />);

    expect(await screen.findByText("Nenhuma etapa cadastrada.")).toBeInTheDocument();
    expect(screen.getByText("0 etapas")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Adicionar primeira etapa/ }));
    expect(screen.getByLabelText("Nome da etapa")).toBeInTheDocument();
  });

  it("adds a stage with name and decimal weight", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([stage("s1", "Fundação")]);
    vi.mocked(buildApi.createStage).mockResolvedValue(stage("s9", "Telhado"));
    renderTab(<BuildStagesTab projectId="p1" />);

    expect(await screen.findByText("1 etapa")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Adicionar etapa/ }));
    const save = screen.getByRole("button", { name: "Salvar etapa" });
    expect(save).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Nome da etapa"), { target: { value: "  Telhado  " } });
    fireEvent.change(screen.getByLabelText("Peso"), { target: { value: "2,5" } });
    fireEvent.click(save);

    await waitFor(() => expect(buildApi.createStage).toHaveBeenCalledWith("p1", { name: "Telhado", weight: 2.5 }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Etapa adicionada!"));
    // the form closes
    await waitFor(() => expect(screen.queryByLabelText("Nome da etapa")).not.toBeInTheDocument());
  });

  it("Enter in the name submits with the default weight (undefined)", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([]);
    vi.mocked(buildApi.createStage).mockResolvedValue(stage("s9", "Telhado"));
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Adicionar etapa/ }));
    const name = screen.getByLabelText("Nome da etapa");
    fireEvent.change(name, { target: { value: "Telhado" } });
    fireEvent.keyDown(name, { key: "Enter" });
    await waitFor(() => expect(buildApi.createStage).toHaveBeenCalledWith("p1", { name: "Telhado", weight: undefined }));
  });

  it("an invalid weight (0 or text) is refused before calling the API", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([]);
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Adicionar etapa/ }));
    fireEvent.change(screen.getByLabelText("Nome da etapa"), { target: { value: "Telhado" } });
    fireEvent.change(screen.getByLabelText("Peso"), { target: { value: "0" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar etapa" }));
    expect(toast.error).toHaveBeenCalledWith("O peso da etapa precisa ser maior que zero.");

    fireEvent.change(screen.getByLabelText("Peso"), { target: { value: "abc" } });
    fireEvent.keyDown(screen.getByLabelText("Peso"), { key: "Enter" });
    expect(toast.error).toHaveBeenCalledTimes(2);
    expect(buildApi.createStage).not.toHaveBeenCalled();
  });

  it("create failure shows the server error and keeps the form; Cancelar closes it", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([]);
    vi.mocked(buildApi.createStage).mockRejectedValue(new Error("Nome duplicado"));
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Adicionar etapa/ }));
    fireEvent.change(screen.getByLabelText("Nome da etapa"), { target: { value: "Telhado" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar etapa" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Nome duplicado"));
    expect(screen.getByLabelText("Nome da etapa")).toHaveValue("Telhado");

    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByLabelText("Nome da etapa")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Adicionar etapa/ })).toBeInTheDocument();
  });

  it("moving a stage down swaps it with its neighbour and renumbers 0..n-1", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue(threeStages());
    vi.mocked(buildApi.reorderStages).mockResolvedValue(undefined);
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Descer Fundação" }));
    await waitFor(() => expect(buildApi.reorderStages).toHaveBeenCalledWith("p1", { entries: [
      { stageId: "s2", order: 0 }, { stageId: "s1", order: 1 }, { stageId: "s3", order: 2 },
    ] }));
  });

  it("moving up and a reorder failure shows the error", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue(threeStages());
    vi.mocked(buildApi.reorderStages).mockRejectedValue(new Error("Conflito"));
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Subir Pintura" }));
    await waitFor(() => expect(buildApi.reorderStages).toHaveBeenCalledWith("p1", { entries: [
      { stageId: "s1", order: 0 }, { stageId: "s3", order: 1 }, { stageId: "s2", order: 2 },
    ] }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Conflito"));
  });

  it("removing asks for confirmation; declining does nothing", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue(threeStages());
    vi.mocked(buildApi.deleteStage).mockResolvedValue(undefined);
    renderTab(<BuildStagesTab projectId="p1" />);

    vi.mocked(window.confirm).mockReturnValueOnce(false);
    fireEvent.click(await screen.findByRole("button", { name: "Remover Pintura" }));
    expect(buildApi.deleteStage).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Remover Pintura" }));
    await waitFor(() => expect(buildApi.deleteStage).toHaveBeenCalledWith("s3"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Etapa removida."));
  });

  it("remove failure toasts the error", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue(threeStages());
    vi.mocked(buildApi.deleteStage).mockRejectedValue(new Error("Etapa tem medições"));
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Remover Fundação" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Etapa tem medições"));
  });

  it("editing pre-fills the stage and saves trimmed details, weight and dates", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue(threeStages());
    vi.mocked(buildApi.updateStage).mockResolvedValue(stage("s1", "Fundação profunda"));
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Editar Fundação" }));
    expect(screen.getByLabelText("Nome")).toHaveValue("Fundação");
    expect(screen.getByLabelText("Descrição")).toHaveValue("Sapatas e vigas");
    expect(screen.getByLabelText("Início previsto")).toHaveValue("2026-09-01");
    expect(screen.getByLabelText("Fim previsto")).toHaveValue("2026-09-30");
    expect(screen.getAllByLabelText("Peso")[0]).toHaveValue("3");

    fireEvent.change(screen.getByLabelText("Nome"), { target: { value: " Fundação profunda " } });
    fireEvent.change(screen.getByLabelText("Descrição"), { target: { value: "   " } });
    fireEvent.change(screen.getByLabelText("Fim previsto"), { target: { value: "2026-10-15" } });
    fireEvent.change(screen.getAllByLabelText("Peso")[0], { target: { value: "4" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar etapa" }));

    await waitFor(() => expect(buildApi.updateStage).toHaveBeenCalledWith("s1", {
      name: "Fundação profunda", description: null, order: 0,
      plannedStart: "2026-09-01", plannedEnd: "2026-10-15", weight: 4,
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Etapa atualizada."));
    await waitFor(() => expect(screen.queryByLabelText("Nome")).not.toBeInTheDocument());
  });

  it("editing refuses end before start and a non-positive weight", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue(threeStages());
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Editar Fundação" }));
    fireEvent.change(screen.getByLabelText("Fim previsto"), { target: { value: "2026-08-01" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar etapa" }));
    expect(toast.error).toHaveBeenCalledWith("O fim previsto precisa ser depois do início.");

    fireEvent.change(screen.getByLabelText("Fim previsto"), { target: { value: "" } });
    fireEvent.change(screen.getAllByLabelText("Peso")[0], { target: { value: "-1" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar etapa" }));
    expect(toast.error).toHaveBeenCalledWith("O peso da etapa precisa ser maior que zero.");

    // empty name disables save
    fireEvent.change(screen.getByLabelText("Nome"), { target: { value: "  " } });
    expect(screen.getByRole("button", { name: "Salvar etapa" })).toBeDisabled();
    expect(buildApi.updateStage).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByLabelText("Nome")).not.toBeInTheDocument();
  });

  it("update failure shows the server reason (critical non-conformity)", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue(threeStages());
    vi.mocked(buildApi.updateStage).mockRejectedValue(
      new ApiError(422, "Bloqueada", "stage_has_critical_nonconformity"));
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Editar Pintura" }));
    expect(screen.getAllByLabelText("Peso")[0]).toHaveValue("1");
    fireEvent.click(screen.getByRole("button", { name: "Salvar etapa" }));
    await waitFor(() => expect(buildApi.updateStage).toHaveBeenCalledWith("s3", expect.objectContaining({
      name: "Pintura", plannedStart: null, plannedEnd: null, weight: 1,
    })));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith(expect.stringContaining("Resolva ou cancele a não conformidade crítica")));
  });

  it("the progress button opens the stage progress dialog", async () => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue(threeStages());
    renderTab(<BuildStagesTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: "Atualizar progresso de Estrutura" }));
    expect(await screen.findByLabelText("Progresso da etapa")).toBeInTheDocument();
  });
});
