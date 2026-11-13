import type { ReactNode } from "react";
import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { QueryClient, QueryClientProvider, useQueries } from "@tanstack/react-query";
import { addSaleItem, confirmSale, createSale, type SaleDto } from "../api/sales.api";
import type { CartItem, PaymentEntry } from "../types";
import { CASH_KEY } from "@/modules/cash/hooks/use-cash";
import { STOCK_KEY } from "@/modules/inventory/hooks/use-stock";
import { useCompleteSale, type CompleteSaleArgs } from "./use-pos-sale";

vi.mock("../api/sales.api", async (importOriginal) => ({
  ...(await importOriginal<typeof import("../api/sales.api")>()),
  createSale: vi.fn(),
  addSaleItem: vi.fn(),
  confirmSale: vi.fn(),
}));

function venda(extra: Partial<SaleDto> = {}): SaleDto {
  return {
    id: "venda-1", number: 1042, status: "Draft", soldByUserId: "u1", soldByName: "Ana", cashSessionId: "caixa-1",
    subtotal: 0, discountAmount: 0, taxAmount: 0, total: 0, items: [], payments: [],
    createdAt: "2026-11-13T10:00:00Z", updatedAt: "2026-11-13T10:00:00Z", ...extra,
  };
}

const carrinho: CartItem[] = [
  { productId: "p-cafe", code: "001", description: "Café 500g", quantity: 2, unitPrice: 10, discount: 1, totalPrice: 19 },
  { productId: "p-pao", code: "002", description: "Pão de forma", quantity: 1, unitPrice: 5.5, discount: 0, totalPrice: 5.5 },
];

const pagamentos: PaymentEntry[] = [
  { method: "cash", amount: 10 },
  { method: "pix", amount: 10 },
  { method: "card", amount: 4.5 },
];

const checkout: CompleteSaleArgs = {
  items: carrinho, payments: pagamentos, discountAmount: 0, cashSessionId: "caixa-1", consumerDocument: "52998224725",
};

// Tudo o que deriva de uma venda e pode estar aberto em outra aba do sistema enquanto o PDV vende.
const TELAS = {
  caixa: [...CASH_KEY, "open"],
  estoque: STOCK_KEY,
  vendas: ["sales", "paged"],
  painel: ["dashboard-operational"],
  maisVendidos: ["dashboard-top-products"],
  grafico: ["dashboard-sales-chart"],
  insights: ["dashboard-insights"],
  alertas: ["dashboard-alerts"],
} as const;
type Tela = keyof typeof TELAS;

const leituras = Object.fromEntries(
  Object.keys(TELAS).map((tela) => [tela, vi.fn(async () => tela)]),
) as Record<Tela, ReturnType<typeof vi.fn<() => Promise<string>>>>;

function useTelasAbertas() {
  useQueries({
    queries: (Object.entries(TELAS) as [Tela, readonly unknown[]][]).map(([tela, queryKey]) => ({ queryKey, queryFn: leituras[tela] })),
  });
}

function renderPdv() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>;
  return renderHook(() => { useTelasAbertas(); return useCompleteSale(); }, { wrapper });
}

const todasCarregadas = () => Object.values(leituras).forEach((ler) => expect(ler).toHaveBeenCalledTimes(1));

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(createSale).mockResolvedValue(venda());
  vi.mocked(addSaleItem)
    .mockResolvedValueOnce(venda({ subtotal: 19 }))
    .mockResolvedValueOnce(venda({ subtotal: 24.5 }));
  vi.mocked(confirmSale).mockResolvedValue(venda({ status: "Paid", subtotal: 24.5, total: 24.5 }));
});

describe("PDV: concluir a venda", () => {
  it("cria o rascunho no caixa aberto, lança cada item e confirma com os pagamentos no formato do servidor", async () => {
    const { result } = renderPdv();
    await waitFor(todasCarregadas);

    const paga = await act(() => result.current.mutateAsync(checkout));

    expect(paga.status).toBe("Paid");
    expect(createSale).toHaveBeenCalledWith({ cashSessionId: "caixa-1" });
    expect(vi.mocked(addSaleItem).mock.calls).toEqual([
      ["venda-1", { productId: "p-cafe", quantity: 2, unitPrice: 10, discountAmount: 1 }],
      ["venda-1", { productId: "p-pao", quantity: 1, unitPrice: 5.5, discountAmount: 0 }],
    ]);
    expect(confirmSale).toHaveBeenCalledWith("venda-1", {
      payments: [
        { method: "Cash", type: "Cash", amount: 10 },
        { method: "Pix", type: "Cash", amount: 10 },
        { method: "Debit", type: "Cash", amount: 4.5 },
      ],
      discountAmount: 0,
      taxAmount: 0,
      consumerDocument: "52998224725",
      managerApproval: undefined,
    });
  });

  it("depois da venda, caixa, estoque, lista de vendas e painel são buscados de novo", async () => {
    const { result } = renderPdv();
    await waitFor(todasCarregadas);

    await act(() => result.current.mutateAsync(checkout));

    await waitFor(() => {
      for (const [tela, ler] of Object.entries(leituras)) expect(ler, tela).toHaveBeenCalledTimes(2);
    });
  });

  it("CPF na nota em branco não vai para o servidor", async () => {
    const { result } = renderPdv();

    await act(() => result.current.mutateAsync({ ...checkout, consumerDocument: "" }));

    expect(vi.mocked(confirmSale).mock.calls[0][1].consumerDocument).toBeUndefined();
  });

  it("se o servidor cobrou outro preço, para antes de cobrar e não recarrega nada", async () => {
    vi.mocked(addSaleItem).mockReset()
      .mockResolvedValueOnce(venda({ subtotal: 21 }))
      .mockResolvedValueOnce(venda({ subtotal: 26.5 }));
    const { result } = renderPdv();
    await waitFor(todasCarregadas);

    await act(async () => {
      await expect(result.current.mutateAsync(checkout)).rejects.toThrow(/Os preços mudaram/);
    });

    expect(confirmSale).not.toHaveBeenCalled();
    await waitFor(() => expect(result.current.isError).toBe(true));
    todasCarregadas();
  });

  it("repetir o mesmo checkout depois de uma confirmação que falhou reconfirma o mesmo rascunho, sem cobrar duas vezes", async () => {
    vi.mocked(confirmSale)
      .mockRejectedValueOnce(new Error("Sem conexão com o servidor."))
      .mockResolvedValueOnce(venda({ status: "Paid", subtotal: 24.5, total: 24.5 }));
    const { result } = renderPdv();
    await waitFor(todasCarregadas);

    await act(async () => {
      await expect(result.current.mutateAsync(checkout)).rejects.toThrow("Sem conexão com o servidor.");
    });
    todasCarregadas();

    await act(() => result.current.mutateAsync(checkout));

    expect(createSale).toHaveBeenCalledTimes(1);
    expect(addSaleItem).toHaveBeenCalledTimes(2);
    expect(vi.mocked(confirmSale).mock.calls.map(([id]) => id)).toEqual(["venda-1", "venda-1"]);
    await waitFor(() => expect(leituras.caixa).toHaveBeenCalledTimes(2));
  });
});
