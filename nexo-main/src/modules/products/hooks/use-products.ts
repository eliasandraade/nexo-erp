import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  activateProduct,
  createProduct,
  deactivateProduct,
  fetchCategories,
  fetchProductById,
  fetchProducts,
  updateProduct,
  type CreateProductPayload,
  type UpdateProductPayload,
} from "../api/products.api";

export const PRODUCTS_KEY = ["products"] as const;
export const CATEGORIES_KEY = ["categories"] as const;

// ── Queries ───────────────────────────────────────────────────────────────────

export function useProducts(includeInactive = false) {
  return useQuery({
    queryKey: [...PRODUCTS_KEY, { includeInactive }],
    queryFn: () => fetchProducts(includeInactive),
  });
}

export function useProduct(id: string | undefined) {
  return useQuery({
    queryKey: [...PRODUCTS_KEY, id],
    queryFn: () => fetchProductById(id!),
    enabled: !!id,
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
