import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { BudgetTemplatesDialog } from "./BudgetTemplatesDialog";
import { BudgetTemplateEditor } from "./BudgetTemplateEditor";
import * as commercialApi from "../../api/build-commercial.api";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("../../api/build-commercial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-commercial.api")>()),
  fetchBudgetTemplates: vi.fn(),
  createBudgetTemplate: vi.fn(),
  updateBudgetTemplate: vi.fn(),
  deleteBudgetTemplate: vi.fn(),
}));

const template = (over: Partial<commercialApi.BuildBudgetTemplateDto> = {}): commercialApi.BuildBudgetTemplateDto => ({
  id: "t1", name: "Casa térrea", description: "Padrão médio", defaultMarginPercent: 18, createdAt: "", updatedAt: "",
  items: [
    { id: "a", order: 0, name: "Bloco cerâmico", kind: "Material", category: "Alvenaria", unit: "m²", quantityPerM2: 2.5,
      fixedQuantity: null, unitCost: 45, stageName: "Alvenaria" },
    { id: "b", order: 1, name: "Projeto", kind: "Service", category: "Geral", unit: "vb", quantityPerM2: null,
      fixedQuantity: 1, unitCost: 3000, stageName: null },
  ],
  ...over,
});

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("BudgetTemplatesDialog", () => {
  beforeEach(() => { vi.spyOn(window, "confirm").mockReturnValue(true); });
  afterEach(() => { cleanup(); vi.clearAllMocks(); vi.restoreAllMocks(); });

  it("lists the models with item counts, per-m² lines, margin and description", async () => {
    vi.mocked(commercialApi.fetchBudgetTemplates).mockResolvedValue([
      template(), template({ id: "t2", name: "Reforma", description: null, items: [template().items[1]] }),
    ]);
    wrap(<BudgetTemplatesDialog open onClose={vi.fn()} />);

    expect(await screen.findByText("Casa térrea")).toBeInTheDocument();
    expect(screen.getByText("2 itens (1 por m²) · margem 18,0% · Padrão médio")).toBeInTheDocument();
    expect(screen.getByText("1 item · margem 18,0%")).toBeInTheDocument();
  });

  it("empty list explains how to create a model", async () => {
    vi.mocked(commercialApi.fetchBudgetTemplates).mockResolvedValue([]);
    wrap(<BudgetTemplatesDialog open onClose={vi.fn()} />);
    expect(await screen.findByText(/Nenhum modelo ainda/)).toBeInTheDocument();
  });

  it("deletes after confirmation and toasts a failure", async () => {
    vi.mocked(commercialApi.fetchBudgetTemplates).mockResolvedValue([template()]);
    vi.mocked(commercialApi.deleteBudgetTemplate).mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error("Em uso"));
    wrap(<BudgetTemplatesDialog open onClose={vi.fn()} />);

    vi.mocked(window.confirm).mockReturnValueOnce(false);
    fireEvent.click(await screen.findByRole("button", { name: "Excluir Casa térrea" }));
    expect(commercialApi.deleteBudgetTemplate).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Excluir Casa térrea" }));
    await waitFor(() => expect(commercialApi.deleteBudgetTemplate).toHaveBeenCalledWith("t1"));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Modelo excluído."));
    await waitFor(() => expect(screen.getByRole("button", { name: "Excluir Casa térrea" })).not.toBeDisabled());
    fireEvent.click(screen.getByRole("button", { name: "Excluir Casa térrea" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Em uso"));
  });

  it("editing a model pre-fills its lines (per m² / fixed) and saves the replaced items", async () => {
    vi.mocked(commercialApi.fetchBudgetTemplates).mockResolvedValue([template()]);
    vi.mocked(commercialApi.updateBudgetTemplate).mockResolvedValue(template());
    wrap(<BudgetTemplatesDialog open onClose={vi.fn()} />);

    fireEvent.click(await screen.findByRole("button", { name: "Editar Casa térrea" }));
    expect(screen.getByText("Editar modelo")).toBeInTheDocument();
    expect(screen.getByLabelText("Nome")).toHaveValue("Casa térrea");
    expect(screen.getByLabelText("Margem padrão (%)")).toHaveValue("18");
    expect(screen.getByLabelText("Item 1 — quantidade")).toHaveValue("perM2");
    expect(screen.getByLabelText("Item 1 — qtd")).toHaveValue("2.5");
    expect(screen.getByLabelText("Item 2 — quantidade")).toHaveValue("fixed");
    expect(screen.getByLabelText("Item 2 — etapa")).toHaveValue("");

    fireEvent.change(screen.getByLabelText("Item 2 — custo unit."), { target: { value: "3500,50" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar modelo" }));
    await waitFor(() => expect(commercialApi.updateBudgetTemplate).toHaveBeenCalledWith("t1", {
      name: "Casa térrea", description: "Padrão médio", defaultMarginPercent: 18,
      items: [
        { name: "Bloco cerâmico", kind: "Material", category: "Alvenaria", unit: "m²", quantityPerM2: 2.5, fixedQuantity: null,
          unitCost: 45, stageName: "Alvenaria" },
        { name: "Projeto", kind: "Service", category: "Geral", unit: "vb", quantityPerM2: null, fixedQuantity: 1,
          unitCost: 3500.5, stageName: null },
      ],
    }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Modelo salvo."));
    // back to the list
    expect(await screen.findByText("Modelos de orçamento")).toBeInTheDocument();
  });

  it("'Novo modelo' opens an empty editor; 'Voltar' returns to the list", async () => {
    vi.mocked(commercialApi.fetchBudgetTemplates).mockResolvedValue([]);
    wrap(<BudgetTemplatesDialog open onClose={vi.fn()} />);
    fireEvent.click(await screen.findByRole("button", { name: /Novo modelo/ }));
    expect(screen.getByRole("heading", { name: "Novo modelo" })).toBeInTheDocument();
    expect(screen.getByLabelText("Margem padrão (%)")).toHaveValue("15");
    fireEvent.click(screen.getByRole("button", { name: "Voltar" }));
    expect(await screen.findByText("Modelos de orçamento")).toBeInTheDocument();
  });
});

describe("BudgetTemplateEditor", () => {
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("validates name, margin, quantity and cost before saving", () => {
    wrap(<BudgetTemplateEditor template={null} onDone={vi.fn()} />);
    const save = screen.getByRole("button", { name: "Salvar modelo" });

    fireEvent.click(save);
    expect(toast.error).toHaveBeenLastCalledWith("Informe o nome do modelo.");

    fireEvent.change(screen.getByLabelText("Nome"), { target: { value: "Galpão" } });
    fireEvent.change(screen.getByLabelText("Margem padrão (%)"), { target: { value: "-2" } });
    fireEvent.click(save);
    expect(toast.error).toHaveBeenLastCalledWith("Margem inválida.");

    fireEvent.change(screen.getByLabelText("Margem padrão (%)"), { target: { value: "10" } });
    fireEvent.change(screen.getByLabelText("Item 1 — descrição"), { target: { value: "Telha" } });
    fireEvent.change(screen.getByLabelText("Item 1 — qtd"), { target: { value: "0" } });
    fireEvent.click(save);
    expect(toast.error).toHaveBeenLastCalledWith("Quantidade inválida em “Telha”.");

    fireEvent.change(screen.getByLabelText("Item 1 — qtd"), { target: { value: "3" } });
    fireEvent.change(screen.getByLabelText("Item 1 — custo unit."), { target: { value: "x" } });
    fireEvent.click(save);
    expect(toast.error).toHaveBeenLastCalledWith("Custo inválido em “Telha”.");
    expect(commercialApi.createBudgetTemplate).not.toHaveBeenCalled();
  });

  it("creates a model with per-m² and fixed lines, skipping blank lines and removed ones", async () => {
    vi.mocked(commercialApi.createBudgetTemplate).mockResolvedValue(template());
    const onDone = vi.fn();
    wrap(<BudgetTemplateEditor template={null} onDone={onDone} />);

    fireEvent.change(screen.getByLabelText("Nome"), { target: { value: " Galpão " } });
    fireEvent.change(screen.getByLabelText("Descrição (opcional)"), { target: { value: "  " } });
    fireEvent.change(screen.getByLabelText("Margem padrão (%)"), { target: { value: "12,5" } });

    fireEvent.change(screen.getByLabelText("Item 1 — descrição"), { target: { value: " Telha " } });
    fireEvent.change(screen.getByLabelText("Item 1 — tipo"), { target: { value: "Material" } });
    fireEvent.change(screen.getByLabelText("Item 1 — categoria"), { target: { value: " Cobertura " } });
    fireEvent.change(screen.getByLabelText("Item 1 — etapa"), { target: { value: " Telhado " } });
    fireEvent.change(screen.getByLabelText("Item 1 — unidade"), { target: { value: "m²" } });
    fireEvent.change(screen.getByLabelText("Item 1 — quantidade"), { target: { value: "perM2" } });
    expect(screen.getByLabelText("Item 1 — qtd")).toHaveAttribute("placeholder", "por m²");
    fireEvent.change(screen.getByLabelText("Item 1 — qtd"), { target: { value: "1,1" } });
    fireEvent.change(screen.getByLabelText("Item 1 — custo unit."), { target: { value: "80" } });

    fireEvent.click(screen.getByRole("button", { name: /Adicionar item/ }));
    fireEvent.change(screen.getByLabelText("Item 2 — descrição"), { target: { value: "Mão de obra" } });
    fireEvent.change(screen.getByLabelText("Item 2 — tipo"), { target: { value: "Labor" } });
    fireEvent.change(screen.getByLabelText("Item 2 — unidade"), { target: { value: " " } });
    fireEvent.change(screen.getByLabelText("Item 2 — custo unit."), { target: { value: "5000" } });

    fireEvent.click(screen.getByRole("button", { name: /Adicionar item/ }));
    fireEvent.change(screen.getByLabelText("Item 3 — descrição"), { target: { value: "Descartar" } });
    fireEvent.click(screen.getByRole("button", { name: "Remover item 3" }));
    fireEvent.click(screen.getByRole("button", { name: /Adicionar item/ })); // blank line: ignored

    fireEvent.click(screen.getByRole("button", { name: "Salvar modelo" }));
    await waitFor(() => expect(commercialApi.createBudgetTemplate).toHaveBeenCalledWith({
      name: "Galpão", description: null, defaultMarginPercent: 12.5,
      items: [
        { name: "Telha", kind: "Material", category: "Cobertura", unit: "m²", quantityPerM2: 1.1, fixedQuantity: null,
          unitCost: 80, stageName: "Telhado" },
        { name: "Mão de obra", kind: "Labor", category: undefined, unit: "un", quantityPerM2: null, fixedQuantity: 1,
          unitCost: 5000, stageName: null },
      ],
    }));
    await waitFor(() => expect(onDone).toHaveBeenCalled());
  });

  it("a server error is toasted and the editor stays", async () => {
    vi.mocked(commercialApi.createBudgetTemplate).mockRejectedValue(new Error("Nome já usado"));
    const onDone = vi.fn();
    wrap(<BudgetTemplateEditor template={null} onDone={onDone} />);
    fireEvent.change(screen.getByLabelText("Nome"), { target: { value: "Galpão" } });
    fireEvent.click(screen.getByRole("button", { name: "Salvar modelo" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Nome já usado"));
    expect(commercialApi.createBudgetTemplate).toHaveBeenCalledWith(expect.objectContaining({ items: [] }));
    expect(onDone).not.toHaveBeenCalled();
  });
});
