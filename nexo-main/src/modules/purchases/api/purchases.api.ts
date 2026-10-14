import { apiClient } from "@/services/api-client";

export type PurchaseStatus = "Draft" | "Ordered" | "Confirmed" | "Cancelled";

export interface PurchaseItemDto {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  quantity: number;
  unitCost: number;
  total: number;
  notes: string | null;
}

export interface PurchaseDto {
  id: string;
  purchaseNumber: number;
  status: PurchaseStatus;
  supplierId: string;
  supplierName: string;
  totalAmount: number;
  invoiceNumber: string | null;
  receivedAt: string | null;
  confirmedAt: string | null;
  cancelledAt: string | null;
  notes: string | null;
  items: PurchaseItemDto[];
  createdAt: string;
  orderedAt: string | null;
  cancellationReason: string | null;
}

const BASE = "/varejo/purchases";

export const listPurchases = () => apiClient.get<PurchaseDto[]>(BASE);
export const getPurchase = (id: string) => apiClient.get<PurchaseDto>(`${BASE}/${id}`);
export const createPurchase = (req: { supplierId: string; invoiceNumber?: string; notes?: string }) =>
  apiClient.post<PurchaseDto>(BASE, req);
export const addPurchaseItem = (id: string, req: { productId: string; quantity: number; unitCost: number }) =>
  apiClient.post<PurchaseDto>(`${BASE}/${id}/items`, req);
export const removePurchaseItem = (id: string, itemId: string) =>
  apiClient.delete<PurchaseDto>(`${BASE}/${id}/items/${itemId}`);
export const orderPurchase = (id: string) => apiClient.post<PurchaseDto>(`${BASE}/${id}/order`, {});
/** Recebimento: estoque + custo + conta a pagar. dueDate omitted = supplier payment terms. */
export const receivePurchase = (id: string, req: { dueDate?: string; paidNow?: boolean; invoiceNumber?: string }) =>
  apiClient.post<PurchaseDto>(`${BASE}/${id}/confirm`, req);
export const cancelPurchase = (id: string, reason?: string) =>
  apiClient.post<PurchaseDto>(`${BASE}/${id}/cancel`, { reason });

export const PURCHASE_STATUS_LABEL: Record<PurchaseStatus, string> = {
  Draft: "Em elaboração",
  Ordered: "Pedido enviado",
  Confirmed: "Recebida",
  Cancelled: "Cancelada",
};

export const PURCHASES_KEY = ["purchases"] as const;
