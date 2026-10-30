import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { ChecklistTemplatesSection } from "./ChecklistTemplatesSection";
import * as qualityApi from "../../api/build-quality.api";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../../api/build-quality.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-quality.api")>()),
  fetchChecklistTemplates: vi.fn(),
  createChecklistTemplate: vi.fn(),
  updateChecklistTemplate: vi.fn(),
  deleteChecklistTemplate: vi.fn(),
}));

const template = (over: Partial<qualityApi.BuildChecklistTemplateDto> = {}): qualityApi.BuildChecklistTemplateDto => ({
  id: "t1", name: "Alvenaria", category: "Estrutura", stageNameHint: "Paredes", createdAt: "", updatedAt: "",
  items: [
    { id: "b", order: 1, text: "Nível conferido", required: false, requiresPhoto: true },
    { id: "a", order: 0, text: "Prumo conferido", required: true, requiresPhoto: false },
  ],
  ...over,
});

function renderSection(ui: ReactNode = <ChecklistTemplatesSection />) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("ChecklistTemplatesSection", () => {
  beforeEach(() => { vi.spyOn(window, "confirm").mockReturnValue(true); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.restoreAllMocks(); });

  it("lists the templates with discipline, stage hint and item/photo counts", async () => {
    vi.mocked(qualityApi.fetchChecklistTemplates).mockResolvedValue([
      template(), template({ id: "t2", name: "Pintura", category: null, stageNameHint: null, items: [] }),
    ]);
    renderSection();
    expect(await screen.findByText("Alvenaria")).toBeInTheDocument();
    expect(screen.getByText("Estrutura · etapa: Paredes")).toBeInTheDocument();
    expect(screen.getByText(/2 item\(ns\)/)).toHaveTextContent("1 com foto");
    expect(screen.getByText("Sem disciplina")).toBeInTheDocument();
    expect(screen.getByText("0 item(ns)")).toBeInTheDocument();
  });

  it("empty and error states", async () => {
    vi.mocked(qualityApi.fetchChecklistTemplates).mockResolvedValue([]);
    renderSection();
    expect(await screen.findByText("Nenhum checklist cadastrado.")).toBeInTheDocument();
    cleanup();
    vi.mocked(qualityApi.fetchChecklistTemplates).mockRejectedValue(new Error("x"));
    renderSection();
    expect(await screen.findByText("Erro ao carregar os checklists.")).toBeInTheDocument();
  });

  it("creates a template: name required, at least one item, items ordered with flags", async () => {
    vi.mocked(qualityApi.fetchChecklistTemplates).mockResolvedValue([]);
    vi.mocked(qualityApi.createChecklistTemplate).mockResolvedValue(template());
    renderSection();

    fireEvent.click(await screen.findByRole("button", { name: /Novo checklist/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Novo checklist")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar checklist" }));
    expect(toast.error).toHaveBeenCalledWith("Informe o nome do checklist.");

    fireEvent.change(within(dialog).getByLabelText("Nome"), { target: { value: " Elétrica " } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar checklist" }));
    expect(toast.error).toHaveBeenCalledWith("Inclua ao menos um item.");

    fireEvent.change(within(dialog).getByLabelText("Disciplina"), { target: { value: " Instalações " } });
    fireEvent.change(within(dialog).getByLabelText("Item 1"), { target: { value: "Quadro identificado" } });
    fireEvent.click(within(dialog).getByRole("button", { name: /Adicionar item/ }));
    fireEvent.change(within(dialog).getByLabelText("Item 2"), { target: { value: " Tomadas testadas " } });
    // item 2 does not require, but requires a photo
    const item2 = within(dialog).getByLabelText("Item 2").closest("li")!;
    fireEvent.click(within(item2).getByLabelText("Obrigatório"));
    fireEvent.click(within(item2).getByLabelText("Exige foto"));
    // move it to the top
    fireEvent.click(within(dialog).getByRole("button", { name: "Subir item 2" }));
    expect(within(dialog).getByLabelText("Item 1")).toHaveValue(" Tomadas testadas ");
    // an empty third item is dropped
    fireEvent.click(within(dialog).getByRole("button", { name: /Adicionar item/ }));
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar checklist" }));

    await waitFor(() => expect(qualityApi.createChecklistTemplate).toHaveBeenCalledWith({
      name: "Elétrica", category: "Instalações", stageNameHint: null,
      items: [
        { text: "Tomadas testadas", required: false, requiresPhoto: true },
        { text: "Quadro identificado", required: true, requiresPhoto: false },
      ],
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Checklist salvo."));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("edits a template (items pre-filled in order), removes and moves items, and updates", async () => {
    vi.mocked(qualityApi.fetchChecklistTemplates).mockResolvedValue([template()]);
    vi.mocked(qualityApi.updateChecklistTemplate).mockRejectedValue(new Error("Nome repetido"));
    renderSection();

    fireEvent.click(await screen.findByRole("button", { name: "Editar checklist Alvenaria" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Editar checklist")).toBeInTheDocument();
    expect(within(dialog).getByLabelText("Nome")).toHaveValue("Alvenaria");
    expect(within(dialog).getByLabelText("Etapa sugerida")).toHaveValue("Paredes");
    expect(within(dialog).getByLabelText("Item 1")).toHaveValue("Prumo conferido");
    expect(within(dialog).getByLabelText("Item 2")).toHaveValue("Nível conferido");
    expect(within(dialog).getByRole("button", { name: "Subir item 1" })).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Descer item 2" })).toBeDisabled();

    fireEvent.click(within(dialog).getByRole("button", { name: "Descer item 1" }));
    expect(within(dialog).getByLabelText("Item 1")).toHaveValue("Nível conferido");
    fireEvent.click(within(dialog).getByRole("button", { name: "Remover item 2" }));
    expect(within(dialog).queryByLabelText("Item 2")).not.toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar checklist" }));

    await waitFor(() => expect(qualityApi.updateChecklistTemplate).toHaveBeenCalledWith("t1", {
      name: "Alvenaria", category: "Estrutura", stageNameHint: "Paredes",
      items: [{ text: "Nível conferido", required: false, requiresPhoto: true }],
    }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Nome repetido"));
    expect(screen.getByRole("dialog")).toBeInTheDocument();

    // removing every item shows the empty hint
    fireEvent.click(within(dialog).getByRole("button", { name: "Remover item 1" }));
    expect(within(dialog).getByText("Nenhum item.")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("deletes after confirmation; declining does nothing; failures are toasted", async () => {
    vi.mocked(qualityApi.fetchChecklistTemplates).mockResolvedValue([template()]);
    vi.mocked(qualityApi.deleteChecklistTemplate).mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error("Em uso"));
    renderSection();

    vi.mocked(window.confirm).mockReturnValueOnce(false);
    fireEvent.click(await screen.findByRole("button", { name: "Excluir checklist Alvenaria" }));
    expect(qualityApi.deleteChecklistTemplate).not.toHaveBeenCalled();
    expect(window.confirm).toHaveBeenCalledWith("Excluir o checklist “Alvenaria”? Vistorias já criadas não mudam.");

    fireEvent.click(screen.getByRole("button", { name: "Excluir checklist Alvenaria" }));
    await waitFor(() => expect(qualityApi.deleteChecklistTemplate).toHaveBeenCalledWith("t1"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Checklist excluído."));

    fireEvent.click(screen.getByRole("button", { name: "Excluir checklist Alvenaria" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Em uso"));
  });
});
