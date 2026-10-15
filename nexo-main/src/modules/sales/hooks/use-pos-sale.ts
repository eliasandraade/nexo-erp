import { useRef } from "react";
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
  /** "CPF/CNPJ na nota", digits only; omitted when the customer did not ask. */
  consumerDocument?: string;
  /** Manager login/password for a discount above the limit; never part of the retry key. */
  managerApproval?: { login: string; password: string };
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
  // A draft whose items are all in, kept across a failed confirm. Retrying the SAME checkout
  // re-confirms that draft — the backend answers an already-confirmed sale with the sale itself
  // (idempotent replay) — instead of creating a second sale and charging twice.
  const pending = useRef<{ key: string; saleId: string } | null>(null);

  return useMutation({
    mutationFn: async ({
      items,
      payments,
      discountAmount,
      cashSessionId,
      consumerDocument,
      managerApproval,
    }: CompleteSaleArgs): Promise<SaleDto> => {
      const key = JSON.stringify({ items, payments, discountAmount, cashSessionId });
      const confirmBody = {
        payments:       payments.map(toPaymentInput),
        discountAmount,
        taxAmount: 0,
        consumerDocument: consumerDocument || undefined,
        managerApproval,
      };

      if (pending.current?.key === key) {
        return confirmSale(pending.current.saleId, confirmBody);
      }

      // 1. Create draft sale tied to the open cash session
      const draft = await createSale({ cashSessionId });

      // 2. Add items sequentially (backend validates stock per-item and decides the price)
      let latest = draft;
      for (const item of items) {
        latest = await addSaleItem(draft.id, {
          productId:      item.productId,
          quantity:       item.quantity,
          unitPrice:      item.unitPrice,
          discountAmount: item.discount ?? 0,
        });
      }

      // The server prices each line (price lists). If a price changed since the cart was built,
      // the payments no longer match — stop before charging and let the operator review.
      const cartSubtotal = items.reduce((s, i) => s + i.unitPrice * i.quantity - (i.discount ?? 0), 0);
      if (Math.round(latest.subtotal * 100) !== Math.round(cartSubtotal * 100)) {   // compare in cents
        throw new Error("Os preços mudaram desde que os itens entraram no carrinho. Remova e adicione os itens de novo.");
      }
      pending.current = { key, saleId: draft.id };

      // 3. Confirm — backend handles stock deduction, cash movement, and
      //    financial transaction atomically in a single DB transaction.
      return confirmSale(draft.id, confirmBody);
    },

    onSuccess: () => {
      pending.current = null;
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
