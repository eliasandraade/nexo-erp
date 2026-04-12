import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createSale, addSaleItem, confirmSale } from "../api/sales.api";
import type { CartItem, PaymentEntry } from "../types";
import type { SaleDto, PaymentInput } from "../api/sales.api";
import { CASH_KEY } from "@/modules/cash/hooks/use-cash";
import { STOCK_KEY } from "@/modules/inventory/hooks/use-stock";

export interface CompleteSaleArgs {
  items: CartItem[];
  payments: PaymentEntry[];
  discountAmount: number;
  cashSessionId: string;
}

/** Maps frontend PaymentMethod ("cash" | "pix" | "card") to backend PaymentInput. */
function toPaymentInput(entry: PaymentEntry): PaymentInput {
  switch (entry.method) {
    case "cash": return { method: "Cash",  type: "Cash", amount: entry.amount };
    case "pix":  return { method: "Pix",   type: "Cash", amount: entry.amount };
    case "card": return { method: "Debit", type: "Cash", amount: entry.amount };
    default:     return { method: "Cash",  type: "Cash", amount: entry.amount };
  }
}

/**
 * Executes a full POS sale against the real backend:
 *   1. POST /api/sales          → create draft
 *   2. POST /api/sales/{id}/items (×N) → add each cart item
 *   3. POST /api/sales/{id}/confirm    → payments + discount → stock deducted atomically
 *
 * On success, invalidates cash, stock, sales, and all dashboard queries.
 */
export function useCompleteSale() {
  const qc = useQueryClient();

  return useMutation({
    mutationFn: async ({
      items,
      payments,
      discountAmount,
      cashSessionId,
    }: CompleteSaleArgs): Promise<SaleDto> => {
      // 1. Create draft sale tied to the open cash session
      const draft = await createSale({ cashSessionId });

      // 2. Add items sequentially (backend validates stock per-item)
      for (const item of items) {
        await addSaleItem(draft.id, {
          productId:      item.productId,
          quantity:       item.quantity,
          unitPrice:      item.unitPrice,
          discountAmount: item.discount ?? 0,
        });
      }

      // 3. Confirm — backend handles stock deduction, cash movement, and
      //    financial transaction atomically in a single DB transaction.
      return confirmSale(draft.id, {
        payments:       payments.map(toPaymentInput),
        discountAmount,
        taxAmount: 0,
      });
    },

    onSuccess: () => {
      // Cash session balance changed
      qc.invalidateQueries({ queryKey: CASH_KEY });
      // Stock levels reduced
      qc.invalidateQueries({ queryKey: STOCK_KEY });
      // Sales list and detail
      qc.invalidateQueries({ queryKey: ["sales"] });
      // Dashboard widgets that derive from sales
      qc.invalidateQueries({ queryKey: ["dashboard-operational"] });
      qc.invalidateQueries({ queryKey: ["dashboard-top-products"] });
      qc.invalidateQueries({ queryKey: ["dashboard-sales-chart"] });
      qc.invalidateQueries({ queryKey: ["dashboard-insights"] });
      qc.invalidateQueries({ queryKey: ["dashboard-alerts"] });
    },
  });
}
