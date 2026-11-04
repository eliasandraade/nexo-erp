import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ChangeOrdersTab } from "./ChangeOrdersTab";
import * as buildApi from "../../api/build.api";
import * as commercialApi from "../../api/build-commercial.api";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchProjectFinancialSummary: vi.fn(),
  fetchStages: vi.fn(),
}));

vi.mock("../../api/build-commercial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-commercial.api")>()),
  fetchChangeOrders:  vi.fn(),
  createChangeOrder:  vi.fn(),
  sendChangeOrder:    vi.fn(),
  approveChangeOrder: vi.fn(),
  rejectChangeOrder:  vi.fn(),
}));

const co = (over: Partial<commercialApi.BuildChangeOrderDto> = {}): commercialApi.BuildChangeOrderDto => ({
  id: "co1", publicId: "pub1", projectId: "p1", number: 1, title: "Piscina", description: null,
  reason: "ClientRequest", costImpact: 15000, scheduleImpactDays: 10, stageId: "s1", stageName: "Fundação",
  status: "Draft", sentAt: null, decidedAt: null, decidedBy: null, decisionChannel: null, clientComment: null,
  signerName: null, sourceSelectionId: null, appliedToBudget: false, createdByName: "Ana", items: [],
  createdAt: "2026-10-01T10:00:00Z", updatedAt: "2026-10-01T10:00:00Z", ...over,
});

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("ChangeOrdersTab", () => {
  beforeEach(() => {
    vi.mocked(buildApi.fetchProjectFinancialSummary).mockResolvedValue({
      projectId: "p1", estimatedBudget: null, approvedBudget: 100000, totalRealizedExpenses: 0, movementCount: 0,
      lastMovementDate: null, varianceAmount: 0, variancePercent: 0,
      approvedChangeOrdersTotal: -2500, contractTotal: 97500,
    });
    vi.mocked(buildApi.fetchStages).mockResolvedValue([
      { id: "s1", projectId: "p1", name: "Fundação", description: null, order: 0, status: "InProgress",
        plannedStartDate: null, plannedEndDate: null, actualStartDate: null, actualEndDate: null, progressPercent: 40 },
    ]);
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("lists change orders with signed impact, schedule, status, channel, client comment and the contract summary", async () => {
    vi.mocked(commercialApi.fetchChangeOrders).mockResolvedValue([
      co(),
      co({ id: "co2", number: 2, title: "Troca de piso", costImpact: -2500, scheduleImpactDays: 0, status: "Approved",
        decisionChannel: "Portal", decidedBy: "Cliente (portal)", decidedAt: "2026-10-10T12:00:00Z",
        clientComment: "Pode trocar", appliedToBudget: true }),
    ]);
    renderTab(<ChangeOrdersTab projectId="p1" />);

    const first = await screen.findByTestId("change-order-1");
    expect(within(first).getByText("+R$ 15.000,00")).toHaveClass("text-red-600");
    expect(within(first).getByText("+10 dias")).toBeInTheDocument();
    expect(within(first).getByText("Rascunho")).toBeInTheDocument();

    const second = screen.getByTestId("change-order-2");
    expect(within(second).getByText("−R$ 2.500,00")).toHaveClass("text-emerald-600");
    expect(within(second).getByText("Portal do cliente")).toBeInTheDocument();
    expect(within(second).getByText("Aprovado")).toBeInTheDocument();
    expect(within(second).getByText(/Pode trocar/)).toBeInTheDocument();

    expect(await screen.findByTestId("contract-total")).toHaveTextContent("R$ 97.500,00");
    expect(screen.getByTestId("approved-change-orders-total")).toHaveTextContent("−R$ 2.500,00");
    expect(screen.getByText(/portal protegido por senha/)).toBeInTheDocument();
  });

  it("creates a draft change order", async () => {
    vi.mocked(commercialApi.fetchChangeOrders).mockResolvedValue([]);
    vi.mocked(commercialApi.createChangeOrder).mockResolvedValue(co());
    renderTab(<ChangeOrdersTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Novo aditivo/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Título"), { target: { value: "Piscina" } });
    fireEvent.change(within(dialog).getByLabelText(/Impacto no custo/), { target: { value: "15000" } });
    fireEvent.change(within(dialog).getByLabelText("Impacto no prazo (dias)"), { target: { value: "10" } });
    await within(dialog).findByRole("option", { name: "Fundação" });
    fireEvent.change(within(dialog).getByLabelText("Etapa afetada"), { target: { value: "s1" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar rascunho" }));

    await waitFor(() => expect(commercialApi.createChangeOrder).toHaveBeenCalledWith("p1", {
      title: "Piscina", description: null, reason: "ClientRequest", costImpact: 15000,
      scheduleImpactDays: 10, stageId: "s1", items: [],
    }));
  });

  it("a schedule impact without the affected stage is refused before calling the API", async () => {
    vi.mocked(commercialApi.fetchChangeOrders).mockResolvedValue([]);
    renderTab(<ChangeOrdersTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Novo aditivo/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Título"), { target: { value: "Piscina" } });
    fireEvent.change(within(dialog).getByLabelText("Impacto no prazo (dias)"), { target: { value: "5" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Salvar rascunho" }));
    await new Promise((r) => setTimeout(r, 0));
    expect(commercialApi.createChangeOrder).not.toHaveBeenCalled();
  });

  it("sends a draft to the client after confirmation", async () => {
    vi.mocked(commercialApi.fetchChangeOrders).mockResolvedValue([co()]);
    vi.mocked(commercialApi.sendChangeOrder).mockResolvedValue(co({ status: "SentToClient" }));
    renderTab(<ChangeOrdersTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Enviar ao cliente/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/aprova ou recusa por lá/)).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Enviar" }));
    await waitFor(() => expect(commercialApi.sendChangeOrder).toHaveBeenCalledWith("co1"));
  });

  it("approves internally with a comment", async () => {
    vi.mocked(commercialApi.fetchChangeOrders).mockResolvedValue([co({ status: "SentToClient" })]);
    vi.mocked(commercialApi.approveChangeOrder).mockResolvedValue(co({ status: "Approved" }));
    renderTab(<ChangeOrdersTab projectId="p1" />);

    await screen.findByTestId("change-order-1");
    expect(screen.queryByRole("button", { name: /Editar/ })).not.toBeInTheDocument();   // only drafts are editable
    fireEvent.click(screen.getByRole("button", { name: /Aprovar/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Comentário (opcional)"), { target: { value: "Aprovado por telefone" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Aprovar" }));
    await waitFor(() => expect(commercialApi.approveChangeOrder).toHaveBeenCalledWith("co1", "Aprovado por telefone"));
  });
});
