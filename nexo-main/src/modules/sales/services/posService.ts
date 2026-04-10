import { cashService } from "@/modules/cash/services/cashService";
import { inventoryService } from "@/modules/inventory/services/inventoryService";
import { auditService } from "@/modules/audit/services/auditService";
import type { CartItem, PaymentEntry, CompletedSale, ProductSearchResult, SaleCancellationRecord } from "../types";
import { mockPosProducts } from "../data/mockPosProducts";
import { mockSales } from "../data/mockSales";

// Seeded with historical mock sales (most-recent first). New sales are prepended via unshift.
const completedSales: CompletedSale[] = [...mockSales];

const delay = (ms = 300) => new Promise((r) => setTimeout(r, ms));

export const posService = {
  /**
   * Searches products by barcode or name.
   * Ranking (highest to lowest priority):
   *   1. Exact code match (barcode scanner hit)
   *   2. Code starts with query
   *   3. Code contains query
   *   4. Description contains query
   *
   * This makes scanner-first operation predictable: a full barcode scan
   * always yields the correct product as the first result.
   */
  searchProduct(query: string): ProductSearchResult[] {
    const q = query.trim();
    if (!q) return [];
    const qLower = q.toLowerCase();

    const results = mockPosProducts.filter(
      (p) =>
        p.code.toLowerCase().includes(qLower) ||
        p.description.toLowerCase().includes(qLower)
    );

    return results.sort((a, b) => {
      const scoreOf = (p: ProductSearchResult): number => {
        const code = p.code.toLowerCase();
        if (code === qLower) return 0;               // exact code match
        if (code.startsWith(qLower)) return 1;       // code prefix match
        if (code.includes(qLower)) return 2;         // code partial match
        return 3;                                     // description match only
      };
      return scoreOf(a) - scoreOf(b);
    });
  },

  async completeSale(
    items: CartItem[],
    payments: PaymentEntry[],
    discountTotal: number,
    operator = "Operador"
  ): Promise<CompletedSale> {
    await delay(500);

    // --- Business rule validations ---

    if (items.length === 0) {
      throw new Error("O carrinho está vazio.");
    }

    const subtotal = items.reduce((acc, i) => acc + i.unitPrice * i.quantity, 0);
    const total = Math.max(0, subtotal - discountTotal);

    if (total <= 0) {
      throw new Error("O valor total da venda deve ser maior que zero.");
    }

    const totalPaid = payments.reduce((acc, p) => acc + p.amount, 0);
    if (totalPaid < total - 0.001) {
      throw new Error("O valor pago é insuficiente para cobrir o total da venda.");
    }

    // Change is calculated on the cash portion only.
    // PIX and card payments never produce change — the amount must match exactly
    // or the cash portion absorbs any remainder.
    const cashPaid = payments
      .filter((p) => p.method === "cash")
      .reduce((acc, p) => acc + p.amount, 0);
    const nonCashPaid = payments
      .filter((p) => p.method !== "cash")
      .reduce((acc, p) => acc + p.amount, 0);
    const remainingForCash = Math.max(0, total - nonCashPaid);
    const change = Math.max(0, cashPaid - remainingForCash);

    // --- Build sale record ---

    const saleId = `venda-${String(completedSales.length + 1).padStart(4, "0")}`;
    const timestamp = new Date().toISOString();

    const sale: CompletedSale = {
      id: saleId,
      timestamp,
      operator,
      status: "completed",
      items,
      subtotal,
      discountTotal,
      total,
      payments,
      change,
    };

    completedSales.unshift(sale);

    auditService.addAuditRecord({
      actionType: "sale_completed",
      severity: "info",
      actor: operator,
      entityType: "sale",
      entityId: saleId,
      description: `Venda ${saleId} concluída por ${operator}. Total: R$ ${total.toFixed(2)}. Itens: ${items.length}.`,
      metadata: { total, itemCount: items.length, payments: payments.map((p) => p.method) },
    });

    // --- Side effects: inventory and cash ---
    // These are called after the sale record is committed.
    // The UI resets the cart only after this function resolves successfully.

    // 1. Decrement inventory stock for each sold item.
    inventoryService.applySale(
      items.map((i) => ({
        productId: i.productId,
        description: i.description,
        quantity: i.quantity,
      })),
      saleId
    );

    // 2. Register movement in cash session.
    //    Will throw if no cash session is open, which is the correct behavior.
    const primaryPayment = payments[0];
    cashService.addSaleMovement(total, `Venda ${saleId}`, primaryPayment?.method);

    return sale;
  },

  async getRecentSales(): Promise<CompletedSale[]> {
    await delay(200);
    return [...completedSales];
  },

  getSaleById(id: string): CompletedSale | undefined {
    return completedSales.find((s) => s.id === id);
  },

  /**
   * Cancels an entire sale. All active items are reverted in inventory and
   * any cash payment is reversed in the current cash session (if open).
   *
   * Rules:
   * - Sale must be in status "completed" or "partially_cancelled"
   * - Already fully cancelled sales are rejected
   */
  async cancelSale(
    saleId: string,
    cancelledBy: string,
    authorizedBy: string,
    reason: string
  ): Promise<CompletedSale> {
    await delay(400);

    const idx = completedSales.findIndex((s) => s.id === saleId);
    if (idx === -1) throw new Error("Venda não encontrada.");

    const sale = completedSales[idx];

    if (sale.status === "cancelled") {
      throw new Error("Esta venda já foi cancelada.");
    }

    const now = new Date().toISOString();
    const recordId = `cancel-${Date.now()}`;

    // Items to revert: only those still active (handles partial → full path)
    const activeItems = sale.items.filter((i) => !i.status || i.status === "active");

    // Calculate cash portion to reverse
    const cashPaid = sale.payments
      .filter((p) => p.method === "cash")
      .reduce((acc, p) => acc + p.amount, 0);
    const cashRefund = Math.min(cashPaid, sale.total);

    // Update item statuses
    const updatedItems: CartItem[] = sale.items.map((item) => {
      if (!item.status || item.status === "active") {
        return { ...item, status: "cancelled" as const, cancelledAt: now, cancelledBy, authorizedBy, cancellationReason: reason };
      }
      return item;
    });

    const record: SaleCancellationRecord = {
      id: recordId,
      type: "full",
      cancelledAt: now,
      cancelledBy,
      authorizedBy,
      reason,
      cashRefundAmount: cashRefund,
    };

    const updated: CompletedSale = {
      ...sale,
      items: updatedItems,
      status: "cancelled",
      cancelledAt: now,
      cancelledBy,
      authorizedBy,
      cancellationReason: reason,
      cancellationRecords: [...(sale.cancellationRecords ?? []), record],
    };

    completedSales[idx] = updated;

    auditService.addAuditRecord({
      actionType: "sale_cancelled",
      severity: "warning",
      actor: cancelledBy,
      entityType: "sale",
      entityId: saleId,
      description: `Venda ${saleId} cancelada por ${cancelledBy}. Autorizado por: ${authorizedBy}. Motivo: ${reason}.`,
      metadata: { cancelledBy, authorizedBy, reason, cashRefund },
    });

    // Side effects
    inventoryService.revertSaleItems(
      activeItems.map((i) => ({ productId: i.productId, description: i.description, quantity: i.quantity })),
      saleId,
      `Cancelamento da venda ${saleId}`
    );
    cashService.addCancellationMovement(cashRefund, `Cancelamento venda ${saleId}`);

    return { ...updated };
  },

  /**
   * Cancels a single item within a sale.
   * If this causes all items to be cancelled, the sale status becomes "cancelled".
   * Otherwise the sale becomes "partially_cancelled".
   *
   * Cash reversal is proportional to the item's share of the total (cash-only portion).
   */
  async cancelSaleItem(
    saleId: string,
    itemProductId: string,
    cancelledBy: string,
    authorizedBy: string,
    reason: string
  ): Promise<CompletedSale> {
    await delay(400);

    const idx = completedSales.findIndex((s) => s.id === saleId);
    if (idx === -1) throw new Error("Venda não encontrada.");

    const sale = completedSales[idx];

    if (sale.status === "cancelled") {
      throw new Error("Esta venda já foi completamente cancelada.");
    }

    const itemIdx = sale.items.findIndex((i) => i.productId === itemProductId);
    if (itemIdx === -1) throw new Error("Item não encontrado na venda.");

    const item = sale.items[itemIdx];
    if (item.status === "cancelled") {
      throw new Error("Este item já foi cancelado.");
    }

    const now = new Date().toISOString();
    const recordId = `cancel-item-${Date.now()}`;

    // Cash refund proportional to item's share of total (cash portion only)
    const cashPaid = sale.payments
      .filter((p) => p.method === "cash")
      .reduce((acc, p) => acc + p.amount, 0);
    const cashProportion = sale.total > 0 ? cashPaid / sale.total : 0;
    const itemCashRefund = Math.round(item.totalPrice * cashProportion * 100) / 100;

    const updatedItems: CartItem[] = sale.items.map((i, i_idx) => {
      if (i_idx === itemIdx) {
        return { ...i, status: "cancelled" as const, cancelledAt: now, cancelledBy, authorizedBy, cancellationReason: reason };
      }
      return i;
    });

    const allCancelled = updatedItems.every((i) => i.status === "cancelled");
    const newStatus = allCancelled ? "cancelled" : "partially_cancelled";

    const record: SaleCancellationRecord = {
      id: recordId,
      type: "item",
      itemProductId,
      itemDescription: item.description,
      cancelledAt: now,
      cancelledBy,
      authorizedBy,
      reason,
      cashRefundAmount: itemCashRefund,
    };

    const updated: CompletedSale = {
      ...sale,
      items: updatedItems,
      status: newStatus,
      ...(allCancelled ? { cancelledAt: now, cancelledBy, authorizedBy, cancellationReason: reason } : {}),
      cancellationRecords: [...(sale.cancellationRecords ?? []), record],
    };

    completedSales[idx] = updated;

    auditService.addAuditRecord({
      actionType: "sale_item_cancelled",
      severity: "warning",
      actor: cancelledBy,
      entityType: "sale",
      entityId: saleId,
      description: `Item "${item.description}" cancelado na venda ${saleId} por ${cancelledBy}. Autorizado por: ${authorizedBy}. Motivo: ${reason}.`,
      metadata: { itemProductId, cancelledBy, authorizedBy, reason, itemCashRefund },
    });

    // Side effects
    inventoryService.revertSaleItems(
      [{ productId: item.productId, description: item.description, quantity: item.quantity }],
      saleId,
      `Cancelamento item ${item.description} — venda ${saleId}`
    );
    cashService.addCancellationMovement(
      itemCashRefund,
      `Cancelamento item ${item.code} — venda ${saleId}`
    );

    return { ...updated };
  },
};
