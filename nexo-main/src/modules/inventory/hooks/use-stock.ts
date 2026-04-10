import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  adjustStock,
  fetchProductMovements,
  fetchStockItem,
  fetchStockItems,
} from "../api/stock.api";
import type { AdjustStockRequest } from "../types";

export const STOCK_KEY = ["stock"] as const;

export function useStockItems() {
  return useQuery({
    queryKey: STOCK_KEY,
    queryFn:  fetchStockItems,
  });
}

export function useStockItem(productId: string | undefined) {
  return useQuery({
    queryKey: [...STOCK_KEY, "product", productId],
    queryFn:  () => fetchStockItem(productId!),
    enabled:  !!productId,
  });
}

export function useProductMovements(productId: string | undefined) {
  return useQuery({
    queryKey: [...STOCK_KEY, "movements", productId],
    queryFn:  () => fetchProductMovements(productId!),
    enabled:  !!productId,
  });
}

export function useAdjustStock() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (request: AdjustStockRequest) => adjustStock(request),
    onSuccess:  (_, req) => {
      qc.invalidateQueries({ queryKey: STOCK_KEY });
      qc.invalidateQueries({ queryKey: [...STOCK_KEY, "movements", req.productId] });
    },
  });
}
