import { fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { toast } from "sonner";
import { formatCurrency } from "@/lib/formatters";
import {
  cancelSale,
  createSaleReturn,
  getSale,
  listSaleReturns,
  type SaleDto,
  type SaleReturnDto,
} from "../api/sales.api";
import VendaDetailPage from "./VendaDetailPage";

vi.mock("../api/sales.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/sales.api")>()),
  getSale: vi.fn(),
  listSaleReturns: vi.fn(),
  cancelSale: vi.fn(),
  createSaleReturn: vi.fn(),
}));

vi.mock("@/services/pdf.api", () => ({ downloadPdf: vi.fn() }));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

// O valor como a tela o mostra: o toLocaleString separa "R$" com espaço não separável, que a busca por texto normaliza.
const naTela = (valor: number) => formatCurrency(valor).replace(/\s/g, " ");

function venda(extra: Partial<SaleDto> = {}): SaleDto {
  return {
    id: "venda-1", number: 1042, status: "Paid", customerId: "cli-1", customerName: "Maria Souza", soldByUserId: "u1",
    soldByName: "Ana", cashSessionId: "caixa-1", subtotal: 30, discountAmount: 0, taxAmount: 0, total: 30,
    confirmedAt: "2026-11-13T10:05:00Z", paidAt: "2026-11-13T10:05:00Z",
    items: [
      { id: "item-cafe", productId: "p-cafe", productName: "Café 500g", productCode: "001", quantity: 2, unitPrice: 10, discountAmount: 0, taxAmount: 0, total: 20 },
      { id: "item-acucar", productId: "p-acucar", productName: "Açúcar 1kg", productCode: "002", quantity: 1, unitPrice: 10, discountAmount: 0, taxAmount: 0, total: 10 },
    ],
    payments: [{ id: "pg-1", method: "Cash", type: "Cash", amount: 30 }],
    createdAt: "2026-11-13T10:00:00Z", updatedAt: "2026-11-13T10:05:00Z", returnedAmount: 0, ...extra,
  };
}

const devolucao: SaleReturnDto = {
  id: "dev-1", saleId: "venda-1", refundMethod: "Cash", refundAmount: 10, reason: "Embalagem violada", createdByUserId: "u1",
  createdAt: "2026-11-13T11:00:00Z", items: [{ saleItemId: "item-cafe", productId: "p-cafe", quantity: 1, amount: 10 }],
};

// Telas que derivam da venda e ficaram em cache: caixa, lista de vendas, comissões, painel e insights.
const OUTRAS_TELAS = [
  ["sales", "paged"], ["cash", "open"], ["commissions-overall"], ["commissions-by-seller"], ["commission-records"],
  ["dashboard-operational"], ["dashboard-seller-ranking"], ["dashboard-insights"], ["insights"], ["insights-stats"],
] as const;

function renderVenda() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  for (const key of OUTRAS_TELAS) client.setQueryData(key, "carregado");
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/vendas/venda-1"]}>
        <Routes>
          <Route path="/vendas/:id" element={<VendaDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  const desatualizada = (key: readonly unknown[]) => client.getQueryState(key)?.isInvalidated;
  return { desatualizada };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(listSaleReturns).mockResolvedValue([]);
});

describe("detalhe da venda: cancelamento de venda a prazo", () => {
  beforeEach(() => {
    vi.mocked(getSale).mockResolvedValueOnce(venda({ status: "Confirmed", paidAt: null }));
  });

  it("pede o motivo, cancela com ele e a tela recarregada mostra a venda cancelada", async () => {
    vi.mocked(getSale).mockResolvedValue(venda({ status: "Cancelled", cancellationReason: "Cliente desistiu da compra" }));
    vi.mocked(cancelSale).mockResolvedValue(undefined);
    const { desatualizada } = renderVenda();
    expect(await screen.findByRole("heading", { name: "Venda #1042" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Registrar devolução/ })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: /Cancelar venda/ }));
    const dialogo = await screen.findByRole("dialog");
    fireEvent.click(within(dialogo).getByRole("button", { name: "Confirmar cancelamento" }));
    expect(within(dialogo).getByText("Informe o motivo do cancelamento.")).toBeInTheDocument();
    expect(cancelSale).not.toHaveBeenCalled();

    fireEvent.change(within(dialogo).getByLabelText("Motivo do cancelamento"), { target: { value: "  Cliente desistiu da compra " } });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Confirmar cancelamento" }));

    await waitFor(() => expect(cancelSale).toHaveBeenCalledWith("venda-1", "Cliente desistiu da compra"));
    expect(await screen.findByText("Motivo: Cliente desistiu da compra")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.queryByRole("button", { name: /Cancelar venda/ })).toBeNull();
    expect(toast.success).toHaveBeenCalledWith("Venda cancelada.");
    expect(getSale).toHaveBeenCalledTimes(2);
    for (const key of OUTRAS_TELAS.filter(([k]) => k !== "cash")) expect(desatualizada(key), key.join("/")).toBe(true);
  });

  it("se o servidor recusa, mostra o erro, mantém o diálogo com o motivo e não recarrega nada", async () => {
    vi.mocked(cancelSale).mockRejectedValue(new Error("Somente o gerente pode cancelar vendas."));
    const { desatualizada } = renderVenda();
    fireEvent.click(await screen.findByRole("button", { name: /Cancelar venda/ }));
    const dialogo = await screen.findByRole("dialog");

    fireEvent.change(within(dialogo).getByLabelText("Motivo do cancelamento"), { target: { value: "Cliente desistiu" } });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Confirmar cancelamento" }));

    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Somente o gerente pode cancelar vendas."));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(within(dialogo).getByLabelText("Motivo do cancelamento")).toHaveValue("Cliente desistiu");
    expect(toast.success).not.toHaveBeenCalled();
    expect(getSale).toHaveBeenCalledTimes(1);
    for (const key of OUTRAS_TELAS) expect(desatualizada(key), key.join("/")).toBe(false);
  });
});

describe("detalhe da venda: devolução de venda paga", () => {
  it("devolve a quantidade escolhida, reembolsa em dinheiro e a tela recarregada lista a devolução", async () => {
    vi.mocked(getSale)
      .mockResolvedValueOnce(venda())
      .mockResolvedValue(venda({ status: "PartiallyReturned", returnedAmount: 10 }));
    vi.mocked(listSaleReturns).mockResolvedValueOnce([]).mockResolvedValue([devolucao]);
    vi.mocked(createSaleReturn).mockResolvedValue(devolucao);
    const { desatualizada } = renderVenda();
    expect(await screen.findByRole("heading", { name: "Venda #1042" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Cancelar venda/ })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: /Registrar devolução/ }));
    const dialogo = await screen.findByRole("dialog");
    fireEvent.change(within(dialogo).getByLabelText("Quantidade devolvida de Café 500g"), { target: { value: "1" } });
    fireEvent.change(within(dialogo).getByLabelText("Motivo"), { target: { value: "Embalagem violada" } });
    expect(within(dialogo).getByText(naTela(10))).toBeInTheDocument();
    fireEvent.click(within(dialogo).getByRole("button", { name: "Confirmar devolução" }));

    await waitFor(() => expect(createSaleReturn).toHaveBeenCalledWith("venda-1", {
      items: [{ saleItemId: "item-cafe", quantity: 1 }],
      refundMethod: "Cash",
      reason: "Embalagem violada",
      idempotencyKey: expect.any(String),
    }));
    expect(toast.success).toHaveBeenCalledWith(`Devolução registrada: ${formatCurrency(10)} reembolsados.`);
    expect(await screen.findByText("Motivo: Embalagem violada")).toBeInTheDocument();
    expect(screen.getByText(`Total reembolsado: ${naTela(10)} de ${naTela(30)}.`)).toBeInTheDocument();
    expect(screen.getByText("1 × Café 500g")).toBeInTheDocument();
    expect(screen.queryByRole("dialog")).toBeNull();
    for (const key of [["sales", "paged"], ["cash", "open"], ["dashboard-operational"]] as const) {
      expect(desatualizada(key), key.join("/")).toBe(true);
    }
    expect(desatualizada(["commission-records"])).toBe(false);
  });

  it("se a devolução é recusada, o motivo da recusa aparece no diálogo e o caixa não é recarregado", async () => {
    vi.mocked(getSale).mockResolvedValue(venda());
    vi.mocked(createSaleReturn).mockRejectedValue(new Error("Caixa fechado: abra o caixa para devolver em dinheiro."));
    const { desatualizada } = renderVenda();

    fireEvent.click(await screen.findByRole("button", { name: /Registrar devolução/ }));
    const dialogo = await screen.findByRole("dialog");
    fireEvent.change(within(dialogo).getByLabelText("Quantidade devolvida de Açúcar 1kg"), { target: { value: "1" } });
    fireEvent.change(within(dialogo).getByLabelText("Motivo"), { target: { value: "Produto vencido" } });
    fireEvent.click(within(dialogo).getByRole("button", { name: "Confirmar devolução" }));

    expect(await within(dialogo).findByText("Caixa fechado: abra o caixa para devolver em dinheiro.")).toBeInTheDocument();
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(toast.success).not.toHaveBeenCalled();
    expect(getSale).toHaveBeenCalledTimes(1);
    expect(desatualizada(["cash", "open"])).toBe(false);
  });
});
