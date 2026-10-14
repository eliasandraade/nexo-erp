import { apiClient } from "@/services/api-client";

// ── Inventário (contagem física) ──────────────────────────────────────────────

export interface InventoryCountItemDto {
  productId: string;
  productName: string;
  productCode: string;
  expectedQuantity: number;
  countedQuantity: number | null;
  /** System quantity when the count was typed — the base of the difference. */
  systemQuantityAtCount: number | null;
  systemQuantityAtApply: number | null;
  /** Counted − system at count: positive = sobra, negative = falta. Added to the stock on apply. */
  difference: number | null;
}

export interface InventoryCountDto {
  id: string;
  number: number;
  status: "Open" | "Applied" | "Cancelled";
  notes: string | null;
  createdAt: string;
  closedAt: string | null;
  countedItems: number;
  items: InventoryCountItemDto[];
}

export const listCounts = () => apiClient.get<InventoryCountDto[]>("/stock/counts");
export const getCount = (id: string) => apiClient.get<InventoryCountDto>(`/stock/counts/${id}`);
/** productIds omitted = every active product that tracks stock. */
export const openCount = (req: { productIds?: string[]; notes?: string }) =>
  apiClient.post<InventoryCountDto>("/stock/counts", req);
export const saveCount = (id: string, items: { productId: string; countedQuantity: number | null }[]) =>
  apiClient.put<InventoryCountDto>(`/stock/counts/${id}`, { items });
export const applyCount = (id: string) => apiClient.post<InventoryCountDto>(`/stock/counts/${id}/apply`, {});
export const cancelCount = (id: string) => apiClient.post<InventoryCountDto>(`/stock/counts/${id}/cancel`, {});

// ── Transferência ─────────────────────────────────────────────────────────────

export interface StockTransferDto {
  id: string;
  number: number;
  fromStoreId: string;
  toStoreId: string;
  direction: "out" | "in";
  notes: string | null;
  createdAt: string;
  items: { productId: string; productName: string; quantity: number }[];
}

export const listTransfers = () => apiClient.get<StockTransferDto[]>("/stock/transfers");
export const createTransfer = (req: {
  toStoreId: string;
  items: { productId: string; quantity: number }[];
  notes?: string;
}) => apiClient.post<StockTransferDto>("/stock/transfers", req);
