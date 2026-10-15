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
  /** Value applied to the sale. */
  amount: number;
  dueDate?: string | null;
  /** Physical cash handed over (cash only). */
  tenderedAmount?: number | null;
  /** Troco given back (cash only). */
  changeAmount?: number;
}

export interface SaleDto {
  id: string;
  number: number;
  status: "Draft" | "Confirmed" | "Paid" | "Cancelled" | "PartiallyReturned" | "Returned";
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
  /** Refunded by devoluções so far. */
  returnedAmount?: number;
  cancellationReason?: string | null;
  surchargesAmount?: number;
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
  /** "CPF/CNPJ na nota", digits only. */
  consumerDocument?: string;
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

export function cancelSale(saleId: string, reason?: string): Promise<void> {
  return apiClient.post<void>(`/sales/${saleId}/cancel`, { reason });
}

// ── Devoluções ────────────────────────────────────────────────────────────────

export interface SaleReturnItemDto {
  saleItemId: string;
  productId: string;
  quantity: number;
  amount: number;
}

export interface SaleReturnDto {
  id: string;
  saleId: string;
  refundMethod: string;
  refundAmount: number;
  reason: string;
  createdByUserId: string;
  createdAt: string;
  items: SaleReturnItemDto[];
}

export interface CreateSaleReturnRequest {
  items: { saleItemId: string; quantity: number }[];
  /** "Cash" leaves the drawer; "Pix" | "Debit" | "Credit" | "Transfer" are counter-entries. */
  refundMethod: string;
  reason: string;
  /** One per dialog opening — a retry returns the devolução already recorded. */
  idempotencyKey: string;
}

export function createSaleReturn(saleId: string, req: CreateSaleReturnRequest): Promise<SaleReturnDto> {
  return apiClient.post<SaleReturnDto>(`/sales/${saleId}/returns`, req);
}

export function listSaleReturns(saleId: string): Promise<SaleReturnDto[]> {
  return apiClient.get<SaleReturnDto[]>(`/sales/${saleId}/returns`);
}

// ── Preço resolvido (listas de preço) ──────────────────────────────────────────

export interface ResolvedPriceDto {
  productId: string;
  resolvedPrice: number;
  source: "PriceList" | "Default";
  priceListName: string | null;
}

/** The price the server will charge for this product (price lists, customer, validity). */
export function resolvePrice(productId: string, customerId?: string): Promise<ResolvedPriceDto> {
  const qs = new URLSearchParams({ productId });
  if (customerId) qs.set("customerId", customerId);
  return apiClient.get<ResolvedPriceDto>(`/varejo/pdv/resolve-price?${qs}`);
}
