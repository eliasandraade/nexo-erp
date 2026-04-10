/**
 * inventoryService — DEPRECATED
 *
 * list(), listMovements(), listAlerts(), listProductsForSelection(),
 * listStores(), createAdjustment(), createTransfer() are removed.
 * Use hooks/use-stock.ts + api/stock.api.ts instead.
 *
 * applySale() and revertSaleItems() are kept temporarily because posService
 * still calls them for in-memory POS mock state. They will be removed when
 * the POS is integrated with the real backend (server handles stock deduction
 * as part of the sale transaction).
 */

import type { InventoryMovement } from "../types";
import { mockMovements } from "../data/mockInventory";
import { auditService } from "@/modules/audit/services/auditService";

// Local in-memory movements for POS mock — not used by the real backend flow
const movements: InventoryMovement[] = [...mockMovements];

export const inventoryService = {
  /**
   * @deprecated Called by posService for mock POS stock deduction.
   * Remove when POS is integrated with real backend.
   */
  applySale(
    saleItems: Array<{ productId: string; description: string; quantity: number }>,
    saleId: string
  ): void {
    for (const saleItem of saleItems) {
      movements.unshift({
        id: `mov-sale-${saleId}-${saleItem.productId}`,
        date: new Date().toISOString(),
        productId: saleItem.productId,
        productDescription: saleItem.description,
        type: "exit",
        quantity: -saleItem.quantity,
        origin: "PDV",
        destination: "Cliente",
        user: "PDV",
        reason: `Venda ${saleId}`,
      });
    }
    auditService.addAuditRecord({
      actionType: "sale_completed",
      severity: "info",
      actor: "PDV",
      entityType: "sale",
      entityId: saleId,
      description: `Estoque decrementado para venda ${saleId}: ${saleItems.length} produto(s) vendido(s).`,
      metadata: { itemCount: saleItems.length },
    });
  },

  /**
   * @deprecated Called by posService for mock POS stock reversion.
   * Remove when POS is integrated with real backend.
   */
  revertSaleItems(
    saleItems: Array<{ productId: string; description: string; quantity: number }>,
    saleId: string,
    reason: string
  ): void {
    for (const saleItem of saleItems) {
      movements.unshift({
        id: `mov-revert-${saleId}-${saleItem.productId}-${Date.now()}`,
        date: new Date().toISOString(),
        productId: saleItem.productId,
        productDescription: saleItem.description,
        type: "entry",
        quantity: saleItem.quantity,
        origin: "Cancelamento",
        destination: "Estoque",
        user: "PDV",
        reason,
      });
    }
    auditService.addAuditRecord({
      actionType: "sale_cancelled",
      severity: "warning",
      actor: "PDV",
      entityType: "sale",
      entityId: saleId,
      description: `Estoque restituído para venda ${saleId}: ${saleItems.length} produto(s) devolvido(s) ao estoque.`,
      metadata: { itemCount: saleItems.length, reason },
    });
  },
};
