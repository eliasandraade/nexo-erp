import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  activateProduct,
  createProduct,
  deactivateProduct,
  fetchCategories,
  fetchProductById,
  fetchProducts,
  fetchProductsPaged,
  updateProduct,
  createCategory,
  updateCategory,
  deleteCategory,
  type CreateProductPayload,
  type UpdateProductPayload,
  type CategoryPayload,
  type ProductsPagedParams,
} from "../api/products.api";

export const PRODUCTS_KEY = ["products"] as const;
export const CATEGORIES_KEY = ["categories"] as const;

// ── Queries ───────────────────────────────────────────────────────────────────

export function useProducts(options?: { includeInactive?: boolean; isIngredient?: boolean }) {
  const { includeInactive = false, isIngredient } = options ?? {};
  return useQuery({
    queryKey: [...PRODUCTS_KEY, { includeInactive, isIngredient }],
    queryFn: () => fetchProducts(includeInactive, isIngredient),
  });
}

export function useProduct(id: string | undefined) {
  return useQuery({
    queryKey: [...PRODUCTS_KEY, id],
    queryFn: () => fetchProductById(id!),
    enabled: !!id,
  });
}

export function useProductsPaged(params: ProductsPagedParams) {
  return useQuery({
    queryKey: [...PRODUCTS_KEY, "paged", params],
    queryFn: () => fetchProductsPaged(params),
    staleTime: 30_000,
    placeholderData: (prev) => prev,
  });
}

export function useCategories() {
  return useQuery({
    queryKey: CATEGORIES_KEY,
    queryFn: fetchCategories,
    staleTime: 5 * 60_000, // categories change rarely — 5 min cache
  });
}

// ── Mutations ─────────────────────────────────────────────────────────────────

export function useCreateProduct() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateProductPayload) => createProduct(payload),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PRODUCTS_KEY });
    },
  });
}

export function useUpdateProduct(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: UpdateProductPayload) => updateProduct(id, payload),
    onSuccess: (updated) => {
      qc.setQueryData([...PRODUCTS_KEY, id], updated);
      qc.invalidateQueries({ queryKey: PRODUCTS_KEY });
    },
  });
}

export function useSetProductActive(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (active: boolean) =>
      active ? activateProduct(id) : deactivateProduct(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: [...PRODUCTS_KEY, id] });
      qc.invalidateQueries({ queryKey: PRODUCTS_KEY });
    },
  });
}

// ── Category mutations ────────────────────────────────────────────────────────

export function useCreateCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CategoryPayload) => createCategory(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: CATEGORIES_KEY }),
  });
}

export function useUpdateCategory(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CategoryPayload) => updateCategory(id, payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: CATEGORIES_KEY }),
  });
}

export function useDeleteCategory() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteCategory(id),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: CATEGORIES_KEY });
      qc.invalidateQueries({ queryKey: PRODUCTS_KEY });
    },
  });
}
