import { apiClient } from "@/services/api-client";

// ── Backend DTOs ──────────────────────────────────────────────────────────────

export interface SaleItemDto {
  id: string;
  productId: string;
  productName: string;
  productCode: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  taxAmount: number;
  total: number;
}

export interface SalePaymentDto {
  id: string;
  method: string;
  type: string;
  amount: number;
  dueDate?: string | null;
}

export interface SaleDto {
  id: string;
  number: number;
  status: "Draft" | "Confirmed" | "Paid" | "Cancelled";
  customerId?: string | null;
  customerName?: string | null;
  soldByUserId: string;
  soldByName: string;
  cashSessionId?: string | null;
  subtotal: number;
  discountAmount: number;
  taxAmount: number;
  total: number;
  notes?: string | null;
  confirmedAt?: string | null;
  paidAt?: string | null;
  cancelledAt?: string | null;
  items: SaleItemDto[];
  payments: SalePaymentDto[];
  createdAt: string;
  updatedAt: string;
}

// ── Request types ─────────────────────────────────────────────────────────────

export interface CreateSaleRequest {
  customerId?: string | null;
  cashSessionId?: string | null;
  notes?: string | null;
}

export interface AddSaleItemRequest {
  productId: string;
  quantity: number;
  unitPrice: number;
  discountAmount?: number;
  notes?: string | null;
}

export type BackendPaymentMethod =
  | "Cash"
  | "Debit"
  | "Credit"
  | "Pix"
  | "Transfer"
  | "Check"
  | "Mixed"
  | "Other";

export interface PaymentInput {
  method: BackendPaymentMethod;
  type: "Cash" | "Credit";
  amount: number;
  dueDate?: string;
}

export interface ConfirmSaleRequest {
  payments: PaymentInput[];
  discountAmount?: number;
  taxAmount?: number;
}

// ── API functions ─────────────────────────────────────────────────────────────

export function listSales(): Promise<SaleDto[]> {
  return apiClient.get<SaleDto[]>("/sales");
}

export function getSale(id: string): Promise<SaleDto> {
  return apiClient.get<SaleDto>(`/sales/${id}`);
}

export function createSale(req: CreateSaleRequest): Promise<SaleDto> {
  return apiClient.post<SaleDto>("/sales", req);
}

export function addSaleItem(saleId: string, req: AddSaleItemRequest): Promise<SaleDto> {
  return apiClient.post<SaleDto>(`/sales/${saleId}/items`, req);
}

export function confirmSale(saleId: string, req: ConfirmSaleRequest): Promise<SaleDto> {
  return apiClient.post<SaleDto>(`/sales/${saleId}/confirm`, req);
}

export function cancelSale(saleId: string): Promise<void> {
  return apiClient.post<void>(`/sales/${saleId}/cancel`, {});
}
