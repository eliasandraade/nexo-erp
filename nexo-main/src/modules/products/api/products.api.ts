import { apiClient } from "@/services/api-client";
import type { CategoryDto, ProductDto } from "../types";

// ── Shared pagination type ────────────────────────────────────────────────────

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNext: boolean;
  hasPrev: boolean;
}

// ── Products ─────────────────────────────────────────────────────────────────

export function fetchProducts(
  includeInactive = false,
  isIngredient?: boolean
): Promise<ProductDto[]> {
  const params = new URLSearchParams();
  if (includeInactive) params.set("includeInactive", "true");
  if (isIngredient !== undefined) params.set("isIngredient", String(isIngredient));
  const qs = params.toString();
  return apiClient.get<ProductDto[]>(`/products${qs ? `?${qs}` : ""}`);
}

export interface ProductsPagedParams {
  page?: number;
  pageSize?: number;
  search?: string;
  includeInactive?: boolean;
  isIngredient?: boolean;
  categoryId?: string;
  unit?: string;
}

export function fetchProductsPaged(params: ProductsPagedParams = {}): Promise<PagedResult<ProductDto>> {
  const { page = 1, pageSize = 50, search, includeInactive = false, isIngredient, categoryId, unit } = params;
  const qs = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (includeInactive) qs.set("includeInactive", "true");
  if (isIngredient !== undefined) qs.set("isIngredient", String(isIngredient));
  if (search) qs.set("search", search);
  if (categoryId) qs.set("categoryId", categoryId);
  if (unit) qs.set("unit", unit);
  return apiClient.get<PagedResult<ProductDto>>(`/products/paged?${qs}`);
}

export function fetchProductById(id: string): Promise<ProductDto> {
  return apiClient.get<ProductDto>(`/products/${id}`);
}

export interface CreateProductPayload {
  code: string;
  name: string;
  unit: string;
  salePrice: number;
  costPrice?: number;
  barcode?: string | null;
  description?: string | null;
  categoryId?: string | null;
  trackStock?: boolean;
  minStockQuantity?: number | null;
  maxStockQuantity?: number | null;
  isIngredient?: boolean;
}

export function createProduct(payload: CreateProductPayload): Promise<ProductDto> {
  return apiClient.post<ProductDto>("/products", payload);
}

export interface UpdateProductPayload {
  name: string;
  unit: string;
  costPrice: number;
  salePrice: number;
  trackStock: boolean;
  barcode?: string | null;
  description?: string | null;
  categoryId?: string | null;
  minStockQuantity?: number | null;
  maxStockQuantity?: number | null;
  isIngredient?: boolean;
}

export function updateProduct(id: string, payload: UpdateProductPayload): Promise<ProductDto> {
  return apiClient.put<ProductDto>(`/products/${id}`, payload);
}

export function activateProduct(id: string): Promise<void> {
  return apiClient.post<void>(`/products/${id}/activate`);
}

export function deactivateProduct(id: string): Promise<void> {
  return apiClient.post<void>(`/products/${id}/deactivate`);
}

// ── Categories ────────────────────────────────────────────────────────────────

export function fetchCategories(): Promise<CategoryDto[]> {
  return apiClient.get<CategoryDto[]>("/categories");
}

export interface CategoryPayload {
  name: string;
  description?: string | null;
}

export function createCategory(payload: CategoryPayload): Promise<CategoryDto> {
  return apiClient.post<CategoryDto>("/categories", payload);
}

export function updateCategory(id: string, payload: CategoryPayload): Promise<CategoryDto> {
  return apiClient.put<CategoryDto>(`/categories/${id}`, payload);
}

export function deleteCategory(id: string): Promise<void> {
  return apiClient.delete<void>(`/categories/${id}`);
}

export function patchProductImage(id: string, imageUrl: string | null): Promise<ProductDto> {
  return apiClient.patch<ProductDto>(`/products/${id}/image`, { imageUrl });
}
