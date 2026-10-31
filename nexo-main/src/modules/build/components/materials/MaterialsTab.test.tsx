import { type ReactNode } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, cleanup, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MaterialsTab } from "./MaterialsTab";
import * as buildApi from "../../api/build.api";
import * as commercialApi from "../../api/build-commercial.api";
import * as interpreterApi from "../../api/interpreter.api";
import * as suppliersApi from "@/modules/suppliers/api/suppliers.api";

vi.mock("../../api/build.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build.api")>()),
  fetchStages: vi.fn(),
}));
vi.mock("../../api/interpreter.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/interpreter.api")>()),
  fetchProjectMovements: vi.fn(),
}));
vi.mock("@/modules/suppliers/api/suppliers.api", () => ({ fetchSuppliers: vi.fn() }));
vi.mock("../../api/build-commercial.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../../api/build-commercial.api")>()),
  fetchMaterials:       vi.fn(),
  deliverMaterial:      vi.fn(),
  linkMaterialMovement: vi.fn(),
}));

const material = (over: Partial<commercialApi.BuildMaterialRequestDto> = {}): commercialApi.BuildMaterialRequestDto => ({
  id: "m1", projectId: "p1", stageId: null, stageName: null, materialName: "Cimento", unit: "sc", quantity: 10,
  neededBy: "2026-10-01", status: "Ordered", isLate: true, supplierId: "sup1", supplierName: "Depósito Silva",
  estimatedUnitCost: 40, estimatedTotal: 400, quotedAt: null, orderedAt: null, deliveredQuantity: 0,
  remainingQuantity: 10, deliveredAt: null, movementId: null, notes: null, createdByName: "Ana",
  deliveries: [], createdAt: "2026-10-01T10:00:00Z", updatedAt: "2026-10-01T10:00:00Z", ...over,
});

function renderTab(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("MaterialsTab", () => {
  beforeEach(() => {
    vi.mocked(buildApi.fetchStages).mockResolvedValue([]);
    vi.mocked(suppliersApi.fetchSuppliers).mockResolvedValue([]);
    vi.mocked(interpreterApi.fetchProjectMovements).mockResolvedValue({
      items: [{ id: "mv1", direction: "Out", nature: "Expense", amount: 400, date: "2026-10-05", description: "NF cimento",
        contextType: "Obra", contextId: "p1", supplierId: null, status: "Confirmed", createdAt: "2026-10-05T10:00:00Z" }],
      totalCount: 1, page: 1, pageSize: 30,
    });
  });
  afterEach(() => { cleanup(); vi.clearAllMocks(); });

  it("flags late materials and filters by 'só atrasados'", async () => {
    vi.mocked(commercialApi.fetchMaterials).mockResolvedValue([material()]);
    renderTab(<MaterialsTab projectId="p1" />);

    const card = await screen.findByTestId("material-m1");
    expect(within(card).getByText("Atrasado")).toBeInTheDocument();
    expect(within(card).getByText("Pedido feito")).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText("Só atrasados"));
    await waitFor(() => expect(commercialApi.fetchMaterials).toHaveBeenLastCalledWith("p1", { late: true }));
  });

  it("registers a partial delivery and refuses more than what is left", async () => {
    vi.mocked(commercialApi.fetchMaterials).mockResolvedValue([material()]);
    vi.mocked(commercialApi.deliverMaterial).mockResolvedValue(
      material({ status: "PartiallyDelivered", deliveredQuantity: 4, remainingQuantity: 6 }));
    renderTab(<MaterialsTab projectId="p1" />);

    fireEvent.click(await screen.findByRole("button", { name: /Registrar entrega/ }));
    const dialog = await screen.findByRole("dialog");
    const qty = within(dialog).getByLabelText("Quantidade recebida");

    fireEvent.change(qty, { target: { value: "12" } });
    expect(within(dialog).getByRole("alert")).toHaveTextContent("Passa do que falta entregar (10 sc).");
    expect(within(dialog).getByRole("button", { name: "Registrar" })).toBeDisabled();

    fireEvent.change(qty, { target: { value: "4" } });
    expect(within(dialog).getByText(/Entrega parcial: o pedido continua aberto/)).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("Recebido por"), { target: { value: "João" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Registrar" }));

    await waitFor(() => expect(commercialApi.deliverMaterial).toHaveBeenCalledWith("m1",
      expect.objectContaining({ quantity: 4, receivedBy: "João" })));
  });

  it("shows the delivery history and links an obra expense", async () => {
    vi.mocked(commercialApi.fetchMaterials).mockResolvedValue([material({
      status: "PartiallyDelivered", isLate: false, deliveredQuantity: 4, remainingQuantity: 6,
      deliveries: [{ id: "d1", quantity: 4, date: "2026-10-06", note: null, receivedBy: "João",
        recordedByName: "Ana", createdAt: "2026-10-06T10:00:00Z" }],
    })]);
    vi.mocked(commercialApi.linkMaterialMovement).mockResolvedValue(material({ movementId: "mv1" }));
    renderTab(<MaterialsTab projectId="p1" />);

    fireEvent.click(await screen.findByText(/Histórico de entregas \(1\)/));
    expect(screen.getByText(/recebido por João/)).toBeInTheDocument();

    const link = screen.getByLabelText("Despesa da obra de Cimento");
    await within(link).findByRole("option", { name: /NF cimento/ });
    fireEvent.change(link, { target: { value: "mv1" } });
    await waitFor(() => expect(commercialApi.linkMaterialMovement).toHaveBeenCalledWith("m1", "mv1"));
  });
});
