import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BuildBudgetTab } from "./BuildBudgetTab";
import * as buildApi from "../../api/build.api";
import * as commercialApi from "../../api/build-commercial.api";
import * as pdfApi from "@/services/pdf.api";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchBudgets:  vi.fn(),
  fetchBudget:   vi.fn(),
  fetchProjects: vi.fn(),
  approveBudget: vi.fn(),
  rejectBudget:  vi.fn(),
}));

vi.mock("../../api/build-commercial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-commercial.api")>()),
  fetchBudgetVersions: vi.fn(),
  createBudgetVersion: vi.fn(),
  fetchBudgetTemplates: vi.fn(),
}));

vi.mock("@/services/pdf.api", () => ({ downloadPdf: vi.fn(), downloadFile: vi.fn() }));

const item = (id: string, kind: buildApi.BuildCostKind, totalCost: number): buildApi.BuildBudgetItemDto => ({
  id, budgetId: "b1", stageId: null, name: `Item ${id}`, category: "Geral", quantity: 1, unit: "un",
  unitCost: totalCost, totalCost, kind, stageName: null,
});

const budget = (over: Partial<buildApi.BuildBudgetDto> = {}): buildApi.BuildBudgetDto => ({
  id: "b1", projectId: "p1", name: "Casa Silva", status: "Sent", totalCost: 10000, marginPercent: 20,
  finalPrice: 12000, createdAt: "2026-10-01T10:00:00Z", updatedAt: "2026-10-01T10:00:00Z",
  items: [item("1", "Material", 6000), item("2", "Labor", 3000), item("3", "Service", 1000)],
  version: 2, rootBudgetId: "b0", parentBudgetId: "b0", areaM2: 100, isExpired: false, validUntil: "2026-12-31",
  totals: {
    material: 6000, labor: 3000, service: 1000, equipment: 0, other: 0, subtotal: 10000, marginPercent: 20,
    marginValue: 2000, total: 12000, areaM2: 100, costPerM2: 100, totalPerM2: 120,
  },
  ...over,
});

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}><MemoryRouter>{ui}</MemoryRouter></QueryClientProvider>);
}

function mockBudget(b: buildApi.BuildBudgetDto) {
  vi.mocked(buildApi.fetchBudgets).mockResolvedValue({ items: [b], total: 1, page: 1, pageSize: 50 });
  vi.mocked(buildApi.fetchBudget).mockResolvedValue(b);
}

describe("BuildBudgetTab", () => {
  beforeEach(() => {
    vi.mocked(buildApi.fetchProjects).mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100 });
    vi.mocked(commercialApi.fetchBudgetVersions).mockResolvedValue([
      { id: "b0", version: 1, name: "Casa Silva", status: "Rejected", finalPrice: 13000, parentBudgetId: null,
        validUntil: null, isExpired: false, createdAt: "2026-09-01T10:00:00Z" },
      { id: "b1", version: 2, name: "Casa Silva", status: "Sent", finalPrice: 12000, parentBudgetId: "b0",
        validUntil: "2026-12-31", isExpired: false, createdAt: "2026-10-01T10:00:00Z" },
    ]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("shows the item kind and the totals per kind, subtotal, margin, total and total per m²", async () => {
    mockBudget(budget());
    renderTab(<BuildBudgetTab project={{ id: "p1", areaM2: 100 }} />);

    const totals = await screen.findByTestId("budget-totals");
    expect(within(totals).getByLabelText("Total Material")).toHaveTextContent("R$ 6.000,00");
    expect(within(totals).getByLabelText("Total Mão de obra")).toHaveTextContent("R$ 3.000,00");
    expect(within(totals).getByLabelText("Total Serviço")).toHaveTextContent("R$ 1.000,00");
    expect(within(totals).getByLabelText("Total Equipamento")).toHaveTextContent("R$ 0,00");
    expect(within(totals).getByLabelText("Subtotal")).toHaveTextContent("R$ 10.000,00");
    expect(within(totals).getByLabelText("Total do orçamento")).toHaveTextContent("R$ 12.000,00");
    expect(within(totals).getByLabelText("Total por m²")).toHaveTextContent("R$ 120,00/m²");
    // kind column of the items
    expect(screen.getAllByText("Mão de obra").length).toBeGreaterThan(1);
    // versions of the family
    expect(await screen.findByRole("button", { name: /Versão 1/ })).toBeInTheDocument();
    expect(screen.getByText(/· Versão 2/)).toBeInTheDocument();
  });

  it("an expired budget shows 'Vencido' and cannot be approved, with the reason", async () => {
    mockBudget(budget({ isExpired: true, validUntil: "2026-10-01" }));
    renderTab(<BuildBudgetTab project={{ id: "p1", areaM2: 100 }} />);

    expect(await screen.findByText("Vencido")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Aprovar/ })).toBeDisabled();
    expect(screen.getByRole("alert")).toHaveTextContent(/validade deste orçamento venceu/);
    expect(buildApi.approveBudget).not.toHaveBeenCalled();
  });

  it("approving a valid budget asks for confirmation and calls approve", async () => {
    mockBudget(budget());
    vi.mocked(buildApi.approveBudget).mockResolvedValue(budget({ status: "Approved" }));
    renderTab(<BuildBudgetTab project={{ id: "p1", areaM2: 100 }} />);

    fireEvent.click(await screen.findByRole("button", { name: /Aprovar/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Aprovar" }));
    await waitFor(() => expect(buildApi.approveBudget).toHaveBeenCalledWith("b1"));
  });

  it("rejecting sends the optional reason", async () => {
    mockBudget(budget());
    vi.mocked(buildApi.rejectBudget).mockResolvedValue(budget({ status: "Rejected" }));
    renderTab(<BuildBudgetTab project={{ id: "p1", areaM2: 100 }} />);

    fireEvent.click(await screen.findByRole("button", { name: /Recusar/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Motivo (opcional)"), { target: { value: "Preço alto" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Recusar" }));
    await waitFor(() => expect(buildApi.rejectBudget).toHaveBeenCalledWith("b1", "Preço alto"));
  });

  it("'Baixar PDF' downloads the budget PDF endpoint as orcamento-<name>-v<N>.pdf", async () => {
    mockBudget(budget());
    vi.mocked(pdfApi.downloadPdf).mockResolvedValue();
    renderTab(<BuildBudgetTab project={{ id: "p1", areaM2: 100 }} />);

    fireEvent.click(await screen.findByRole("button", { name: /Baixar PDF/ }));
    await waitFor(() =>
      expect(pdfApi.downloadPdf).toHaveBeenCalledWith("/v1/build/budgets/b1/pdf", "orcamento-casa-silva-v2.pdf"));
  });

  it("'Nova versão' creates a new version of the budget", async () => {
    mockBudget(budget());
    vi.mocked(commercialApi.createBudgetVersion).mockResolvedValue(budget({ id: "b2", version: 3, status: "Draft" }));
    renderTab(<BuildBudgetTab project={{ id: "p1", areaM2: 100 }} />);

    fireEvent.click(await screen.findByRole("button", { name: /Nova versão/ }));
    await waitFor(() => expect(commercialApi.createBudgetVersion).toHaveBeenCalledWith("b1"));
  });

  it("a draft has no 'Nova versão' (only sent, approved or rejected budgets)", async () => {
    mockBudget(budget({ status: "Draft" }));
    renderTab(<BuildBudgetTab project={{ id: "p1", areaM2: 100 }} />);
    await screen.findByTestId("budget-totals");
    expect(screen.queryByRole("button", { name: /Nova versão/ })).not.toBeInTheDocument();
  });
});
