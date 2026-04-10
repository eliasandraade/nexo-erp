// ── Backend DTOs ──────────────────────────────────────────────────────────────

export interface StockItemDto {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  currentQuantity: number;
  reservedQuantity: number;
  availableQuantity: number;   // currentQuantity - reservedQuantity
  lastMovementAt: string | null;
}

export interface StockMovementDto {
  id: string;
  productId: string;
  movementType: string;        // see StockMovementType values below
  quantity: number;            // always positive
  quantityBefore: number;
  quantityAfter: number;
  referenceType: string | null;  // "Sale" | "Purchase" | "Order" | "Manual"
  referenceId: string | null;
  notes: string | null;
  createdByUserId: string;
  createdAt: string;
}

export interface AdjustStockRequest {
  productId: string;
  quantity: number;
  movementType: string;
  notes?: string;
}

// ── Enriched type (StockItemDto + Product fields for display) ─────────────────
// Built client-side by joining StockItemDto with ProductDto from the products cache.

export type StockStatus = "normal" | "low" | "zero";

export interface StockItemEnriched extends StockItemDto {
  unit: string;
  categoryName: string;
  minStockQuantity: number | null;
  status: StockStatus;
}

export function deriveStockStatus(
  availableQuantity: number,
  minStockQuantity: number | null
): StockStatus {
  if (availableQuantity <= 0) return "zero";
  if (minStockQuantity && minStockQuantity > 0 && availableQuantity < minStockQuantity) return "low";
  return "normal";
}

// ── Movement type display maps ────────────────────────────────────────────────

export const MOVEMENT_TYPE_LABEL: Record<string, string> = {
  ManualEntry:   "Entrada manual",
  ManualExit:    "Saída manual",
  Adjustment:    "Ajuste de inventário",
  SaleOutput:    "Saída por venda",
  ReturnEntry:   "Devolução",
  PurchaseEntry: "Entrada por compra",
  Transfer:      "Transferência",
  Loss:          "Perda / vencimento",
  RecipeOutput:  "Consumo (receita)",
};

export const MOVEMENT_TYPE_VARIANT: Record<string, "success" | "danger" | "warning" | "info" | "neutral"> = {
  ManualEntry:   "success",
  ManualExit:    "danger",
  Adjustment:    "warning",
  SaleOutput:    "danger",
  ReturnEntry:   "success",
  PurchaseEntry: "success",
  Transfer:      "info",
  Loss:          "danger",
  RecipeOutput:  "warning",
};

// Movement types available for manual adjustment by the user
export const ADJUSTABLE_MOVEMENT_TYPES = [
  { value: "ManualEntry", label: "Entrada manual" },
  { value: "ManualExit",  label: "Saída manual" },
  { value: "Adjustment",  label: "Ajuste de inventário" },
  { value: "Loss",        label: "Perda / vencimento" },
] as const;

export type AdjustableMovementType = typeof ADJUSTABLE_MOVEMENT_TYPES[number]["value"];

// ── Status display maps ───────────────────────────────────────────────────────

export const STOCK_STATUS_LABEL: Record<StockStatus, string> = {
  normal: "Normal",
  low:    "Abaixo do mínimo",
  zero:   "Zerado",
};

export const STOCK_STATUS_VARIANT: Record<StockStatus, "success" | "warning" | "danger"> = {
  normal: "success",
  low:    "warning",
  zero:   "danger",
};

// ── Legacy types — kept for mock data + deprecated POS service ────────────────
// Remove when POS is integrated with the real backend.

export type InventoryMovementType = "entry" | "exit" | "adjustment" | "transfer";

/** @deprecated Used only by mockInventory.ts and the deprecated inventoryService. */
export interface InventoryItem {
  id: string;
  productId: string;
  code: string;
  description: string;
  category: string;
  unit: string;
  currentStock: number;
  minStock: number;
  status: string;
  lastMovement: string;
}

/** @deprecated Used only by mockInventory.ts and the deprecated inventoryService. */
export interface InventoryMovement {
  id: string;
  date: string;
  productId: string;
  productDescription: string;
  type: InventoryMovementType;
  quantity: number;
  origin: string;
  destination: string;
  user: string;
  reason: string;
}

/** @deprecated Used only by mockInventory.ts. */
export type AlertSeverity = "critical" | "warning" | "info";

/** @deprecated Used only by mockInventory.ts. */
export interface InventoryAlert {
  id: string;
  title: string;
  description: string;
  severity: AlertSeverity;
  suggestedAction: string;
  productId?: string;
}
