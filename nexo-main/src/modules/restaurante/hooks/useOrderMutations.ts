import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  openOrder,
  addOrderItem,
  updateItemStatus,
  closeOrder,
  payOrder,
  cancelOrder,
} from "../api/restaurante.api";
import type {
  OpenOrderRequest,
  AddOrderItemRequest,
  PayOrderRequest,
} from "../types";
import { TABLES_KEY } from "./useRestauranteTables";
import { ORDERS_KEY } from "./useActiveOrder";

// ── Shared invalidation helper ────────────────────────────────────────────────

function useInvalidateOnSuccess(storeId: string) {
  const qc = useQueryClient();
  return () => {
    qc.invalidateQueries({ queryKey: TABLES_KEY(storeId) });
    qc.invalidateQueries({ queryKey: ORDERS_KEY(storeId) });
    qc.invalidateQueries({ queryKey: ["kitchen-items", storeId] });
  };
}

// ── useOpenOrder ──────────────────────────────────────────────────────────────

export function useOpenOrder(storeId: string) {
  const invalidate = useInvalidateOnSuccess(storeId);
  return useMutation({
    mutationFn: (req: OpenOrderRequest) => openOrder(req),
    onSuccess: invalidate,
  });
}

// ── useAddItem ────────────────────────────────────────────────────────────────

export function useAddItem(storeId: string) {
  const invalidate = useInvalidateOnSuccess(storeId);
  return useMutation({
    mutationFn: ({ orderId, req }: { orderId: string; req: AddOrderItemRequest }) =>
      addOrderItem(orderId, req),
    onSuccess: invalidate,
  });
}

// ── useUpdateItemStatus ───────────────────────────────────────────────────────

export function useUpdateItemStatus(storeId: string) {
  const invalidate = useInvalidateOnSuccess(storeId);
  return useMutation({
    mutationFn: ({
      orderId,
      itemId,
      status,
    }: {
      orderId: string;
      itemId: string;
      status: string;
    }) => updateItemStatus(orderId, itemId, status),
    onSuccess: invalidate,
  });
}

// ── useCloseOrder ─────────────────────────────────────────────────────────────

export function useCloseOrder(storeId: string) {
  const invalidate = useInvalidateOnSuccess(storeId);
  return useMutation({
    mutationFn: (orderId: string) => closeOrder(orderId),
    onSuccess: invalidate,
  });
}

// ── usePayOrder ───────────────────────────────────────────────────────────────

export function usePayOrder(storeId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ orderId, req }: { orderId: string; req: PayOrderRequest }) =>
      payOrder(orderId, req),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: TABLES_KEY(storeId) });
      qc.invalidateQueries({ queryKey: ORDERS_KEY(storeId) });
      qc.invalidateQueries({ queryKey: ["kitchen-items", storeId] });
      qc.invalidateQueries({ queryKey: ["sales"] });
      qc.invalidateQueries({ queryKey: ["cash"] });
    },
  });
}

// ── useCancelOrder ────────────────────────────────────────────────────────────

export function useCancelOrder(storeId: string) {
  const invalidate = useInvalidateOnSuccess(storeId);
  return useMutation({
    mutationFn: (orderId: string) => cancelOrder(orderId),
    onSuccess: invalidate,
  });
}
