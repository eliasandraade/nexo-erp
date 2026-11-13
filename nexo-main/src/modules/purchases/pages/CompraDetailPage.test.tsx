import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { toast } from "sonner";
import { apiClient } from "@/services/api-client";
import { polyfillRadix } from "@/modules/platform/test/aiFixtures";
import CompraDetailPage from "./CompraDetailPage";
import type { PurchaseDto, PurchaseItemDto } from "../api/purchases.api";

vi.mock("@/services/api-client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/services/api-client")>()),
  apiClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn(), patch: vi.fn() },
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
const role = vi.hoisted(() => ({ current: "gerente" }));
vi.mock("@/modules/auth/context/AuthContext", () => ({ useAuth: () => ({ session: { role: role.current } }) }));

polyfillRadix();

const get = vi.mocked(apiClient.get);
const post = vi.mocked(apiClient.post);
const del = vi.mocked(apiClient.delete);

const BASE = "/varejo/purchases";

const line = (id: string, productId: string, productName: string, quantity: number, unitCost: number): PurchaseItemDto => ({
  id, productId, productName, productCode: productId.toUpperCase(), quantity, unitCost, total: quantity * unitCost, notes: null,
});
const withItems = (p: PurchaseDto, items: PurchaseItemDto[]): PurchaseDto =>
  ({ ...p, items, totalAmount: items.reduce((s, i) => s + i.total, 0) });

const draft = (): PurchaseDto => withItems({
  id: "pc1", purchaseNumber: 12, status: "Draft", supplierId: "f1", supplierName: "Distribuidora Grão de Ouro",
  totalAmount: 0, invoiceNumber: null, receivedAt: null, confirmedAt: null, cancelledAt: null, notes: null,
  items: [], createdAt: "2026-11-10T12:00:00Z", orderedAt: null, cancellationReason: null,
}, [line("it1", "cafe", "Café 500g", 24, 12.5)]);

const products = [
  { id: "cafe", name: "Café 500g", code: "CAFE", costPrice: 12.5, salePrice: 18.9 },
  { id: "acucar", name: "Açúcar 1kg", code: "ACU", costPrice: 3.2, salePrice: 5.49 },
];

/** The purchase as the server holds it, changed by each test as the server would. */
let purchase: PurchaseDto | null;
let client: QueryClient;

function renderPage() {
  client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  // the stock screen was already open: receiving goods must make it reload
  client.setQueryData(["stock"], [{ productId: "cafe", currentQuantity: 3 }]);
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/compras/pc1"]}>
        <Routes>
          <Route path="/compras/:id" element={<CompraDetailPage />} />
          <Route path="/compras" element={<p>lista de compras</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
const stockIsStale = () => client.getQueryState(["stock"])?.isInvalidated;
/** The server answers a change with the purchase as it is now. */
const answer = (next: PurchaseDto) => { purchase = next; return structuredClone(next); };

beforeEach(() => {
  role.current = "gerente";
  purchase = draft();
  get.mockImplementation(async (url: string) => {
    if (url === `${BASE}/pc1`) {
      if (!purchase) throw new Error("Compra não encontrada.");
      return structuredClone(purchase);
    }
    if (url === "/products") return structuredClone(products);
    throw new Error(`GET inesperado: ${url}`);
  });
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe("CompraDetailPage — a purchase being prepared", () => {
  it("shows supplier, status, items with their totals and every action to a manager", async () => {
    renderPage();

    expect(await screen.findByText("Compra #12")).toBeInTheDocument();
    expect(screen.getByText("Distribuidora Grão de Ouro · Em elaboração")).toBeInTheDocument();
    const row = screen.getByText("Café 500g").closest("tr")!;
    expect(within(row).getByText("24")).toBeInTheDocument();
    expect(within(row).getAllByText(/300,00/)).toHaveLength(1);
    expect(screen.getByText(/Total: R\$\s300,00/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Enviar pedido/ })).toBeEnabled();
    expect(screen.getByRole("button", { name: /Receber mercadoria/ })).toBeEnabled();
    expect(screen.getByRole("button", { name: /Cancelar/ })).toBeInTheDocument();
  });

  it("a salesperson can prepare the order but not receive or cancel it", async () => {
    role.current = "vendedor";
    renderPage();

    await screen.findByText("Compra #12");
    expect(screen.getByRole("button", { name: /Enviar pedido/ })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Receber mercadoria/ })).toBeNull();
    expect(screen.queryByRole("button", { name: /^Cancelar$/ })).toBeNull();
  });

  it("adds an item, filling in the product's last cost, and refreshes purchases and stock", async () => {
    post.mockImplementation(async (url: string, body: unknown) => {
      if (url !== `${BASE}/pc1/items`) throw new Error(`POST inesperado: ${url}`);
      expect(body).toEqual({ productId: "acucar", quantity: 10, unitCost: 3.2 });
      return answer(withItems(purchase!, [...purchase!.items, line("it2", "acucar", "Açúcar 1kg", 10, 3.2)]));
    });
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.keyDown(screen.getByLabelText("Produto"), { key: "Enter" });
    fireEvent.keyDown(await screen.findByRole("option", { name: "Açúcar 1kg (ACU)" }), { key: "Enter" });
    expect(screen.getByLabelText("Custo unitário")).toHaveValue("3.2");
    expect(screen.getByRole("button", { name: "Adicionar" })).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Quantidade"), { target: { value: "10" } });
    fireEvent.click(screen.getByRole("button", { name: "Adicionar" }));

    expect(await screen.findByText("Açúcar 1kg")).toBeInTheDocument();
    expect(screen.getByText(/Total: R\$\s332,00/)).toBeInTheDocument();
    expect(screen.getByLabelText("Quantidade")).toHaveValue("");
    expect(stockIsStale()).toBe(true);
  });

  it("an item the server refuses is reported and nothing changes", async () => {
    post.mockRejectedValueOnce(new Error("Produto sem controle de estoque."));
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.keyDown(screen.getByLabelText("Produto"), { key: "Enter" });
    fireEvent.keyDown(await screen.findByRole("option", { name: "Açúcar 1kg (ACU)" }), { key: "Enter" });
    fireEvent.change(screen.getByLabelText("Quantidade"), { target: { value: "2,5" } });
    fireEvent.click(screen.getByRole("button", { name: "Adicionar" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Produto sem controle de estoque."));
    expect(post).toHaveBeenCalledWith(`${BASE}/pc1/items`, { productId: "acucar", quantity: 2.5, unitCost: 3.2 });
    expect(stockIsStale()).toBe(false);
  });

  it("removes an item from the order", async () => {
    del.mockImplementation(async (url: string) => {
      if (url !== `${BASE}/pc1/items/it1`) throw new Error(`DELETE inesperado: ${url}`);
      return answer(withItems(purchase!, []));
    });
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.click(screen.getByRole("button", { name: "Remover Café 500g" }));

    await waitFor(() => expect(screen.queryByText("Café 500g")).toBeNull());
    // an order with no items cannot be sent or received
    expect(screen.getByRole("button", { name: /Enviar pedido/ })).toBeDisabled();
    expect(screen.getByRole("button", { name: /Receber mercadoria/ })).toBeDisabled();
  });

  it("sends the order to the supplier, after which the items are frozen", async () => {
    post.mockImplementation(async (url: string) => {
      if (url !== `${BASE}/pc1/order`) throw new Error(`POST inesperado: ${url}`);
      return answer({ ...purchase!, status: "Ordered", orderedAt: "2026-11-13T10:00:00Z" });
    });
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.click(screen.getByRole("button", { name: /Enviar pedido/ }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Pedido enviado ao fornecedor."));
    expect(post).toHaveBeenCalledWith(`${BASE}/pc1/order`, {});
    expect(await screen.findByText("Distribuidora Grão de Ouro · Pedido enviado")).toBeInTheDocument();
    expect(screen.getByText("Itens congelados.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remover Café 500g" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Adicionar" })).toBeNull();
    // still receivable once ordered
    expect(screen.getByRole("button", { name: /Receber mercadoria/ })).toBeEnabled();
  });

  it("an order the server refuses is reported", async () => {
    post.mockRejectedValueOnce(new Error("Fornecedor inativo."));
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.click(screen.getByRole("button", { name: /Enviar pedido/ }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Fornecedor inativo."));
    expect(screen.getByText("Distribuidora Grão de Ouro · Em elaboração")).toBeInTheDocument();
  });

  it("goes back to the purchase list", async () => {
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.click(screen.getByRole("button", { name: /Voltar/ }));

    expect(await screen.findByText("lista de compras")).toBeInTheDocument();
  });

  it("a purchase that does not exist says so", async () => {
    purchase = null;
    renderPage();

    expect(await screen.findByText("Compra não encontrada")).toBeInTheDocument();
  });
});

describe("CompraDetailPage — receiving and cancelling", () => {
  it("receiving posts the invoice and due date, then stock and payables are updated", async () => {
    post.mockImplementation(async (url: string) => {
      if (url !== `${BASE}/pc1/confirm`) throw new Error(`POST inesperado: ${url}`);
      return answer({ ...purchase!, status: "Confirmed", invoiceNumber: "NF 4521", confirmedAt: "2026-11-13T10:00:00Z" });
    });
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.click(screen.getByRole("button", { name: /Receber mercadoria/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/conta a pagar de\s+R\$\s300,00/)).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("Nota fiscal"), { target: { value: " NF 4521 " } });
    fireEvent.change(within(dialog).getByLabelText("Vencimento da conta"), { target: { value: "2026-12-10" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar recebimento" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Mercadoria recebida: estoque e contas a pagar atualizados."));
    expect(post).toHaveBeenCalledWith(`${BASE}/pc1/confirm`, {
      dueDate: new Date("2026-12-10T12:00:00").toISOString(),
      paidNow: false,
      invoiceNumber: "NF 4521",
    });
    expect(stockIsStale()).toBe(true);
    expect(await screen.findByText("Distribuidora Grão de Ouro · Recebida")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Receber mercadoria/ })).toBeNull();
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
  });

  it("an already-paid purchase is received with no due date and the bill recorded as paid", async () => {
    post.mockImplementation(async () => answer({ ...purchase!, status: "Confirmed" }));
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.click(screen.getByRole("button", { name: /Receber mercadoria/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("checkbox"));
    expect(within(dialog).getByLabelText("Vencimento da conta")).toBeDisabled();
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar recebimento" }));

    await waitFor(() => expect(post).toHaveBeenCalledWith(`${BASE}/pc1/confirm`, {
      dueDate: undefined, paidNow: true, invoiceNumber: undefined,
    }));
  });

  it("a failed receipt keeps the dialog open with the server's reason, and stock untouched", async () => {
    post.mockRejectedValueOnce(new Error("Fornecedor sem prazo de pagamento cadastrado."));
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.click(screen.getByRole("button", { name: /Receber mercadoria/ }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar recebimento" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Fornecedor sem prazo de pagamento cadastrado."));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(stockIsStale()).toBe(false);
  });

  it("cancelling a received purchase warns of the reversal and records the reason", async () => {
    purchase = { ...draft(), status: "Confirmed" };
    post.mockImplementation(async (url: string, body: unknown) => {
      if (url !== `${BASE}/pc1/cancel`) throw new Error(`POST inesperado: ${url}`);
      const { reason } = body as { reason?: string };
      return answer({ ...purchase!, status: "Cancelled", cancellationReason: reason ?? null });
    });
    renderPage();
    await screen.findByText("Distribuidora Grão de Ouro · Recebida");

    fireEvent.click(screen.getByRole("button", { name: /Cancelar/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText(/A mercadoria sai do estoque e o financeiro é estornado/)).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("Motivo"), { target: { value: "Lote vencido" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar cancelamento" }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Compra cancelada."));
    expect(post).toHaveBeenCalledWith(`${BASE}/pc1/cancel`, { reason: "Lote vencido" });
    expect(await screen.findByText("Motivo do cancelamento: Lote vencido")).toBeInTheDocument();
    expect(stockIsStale()).toBe(true);
    expect(screen.queryByRole("button", { name: /^Cancelar$/ })).toBeNull();
  });

  it("cancelling an order not yet received says nothing was posted to stock or finance", async () => {
    post.mockRejectedValueOnce(new Error("Compra já cancelada."));
    renderPage();
    await screen.findByText("Compra #12");

    fireEvent.click(screen.getByRole("button", { name: /Cancelar/ }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("O pedido é cancelado. Nada foi lançado em estoque ou financeiro.")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirmar cancelamento" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Compra já cancelada."));
    expect(post).toHaveBeenCalledWith(`${BASE}/pc1/cancel`, { reason: undefined });
  });
});
