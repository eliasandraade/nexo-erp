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

// ── Paginated list DTO ────────────────────────────────────────────────────────

export interface SaleListItemDto {
  id: string;
  number: number;
  status: string;
  customerId?: string | null;
  customerName?: string | null;
  soldByName: string;
  total: number;
  timestamp: string;
  itemCount: number;
  totalQuantity: number;
  firstItemName?: string | null;
  paymentMethods: string[];
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNext: boolean;
  hasPrev: boolean;
}

export interface SalesPagedParams {
  page?: number;
  pageSize?: number;
  search?: string;
  status?: string;
  paymentMethod?: string;
}

// ── API functions ─────────────────────────────────────────────────────────────

export function listSalesPaged(params: SalesPagedParams = {}): Promise<PagedResult<SaleListItemDto>> {
  const { page = 1, pageSize = 25, search, status, paymentMethod } = params;
  const qs = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (search)        qs.set("search", search);
  if (status)        qs.set("status", status);
  if (paymentMethod) qs.set("paymentMethod", paymentMethod);
  return apiClient.get<PagedResult<SaleListItemDto>>(`/sales/paged?${qs}`);
}

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
