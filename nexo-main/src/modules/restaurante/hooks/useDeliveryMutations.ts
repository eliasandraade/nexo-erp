import { useMutation, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import {
  acceptDeliveryOrder,
  rejectDeliveryOrder,
  updateDeliveryStatus,
  cancelDeliveryOrder,
  createManualDeliveryOrder,
} from "../api/restaurante.api";
import type {
  AcceptDeliveryRequest,
  RejectDeliveryRequest,
  UpdateDeliveryStatusRequest,
  CreateManualDeliveryRequest,
} from "../types";
import { DELIVERY_KEY } from "./useDeliveryOrders";

function useInvalidateDelivery(storeId: string) {
  const qc = useQueryClient();
  return () => qc.invalidateQueries({ queryKey: DELIVERY_KEY(storeId) });
}

// ── useAcceptDelivery ─────────────────────────────────────────────────────────

export function useAcceptDelivery(storeId: string) {
  const invalidate = useInvalidateDelivery(storeId);
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: AcceptDeliveryRequest }) =>
      acceptDeliveryOrder(id, req),
    onSuccess: () => {
      invalidate();
      toast.success("Pedido aceito — comanda aberta na cozinha.");
    },
    onError: () => toast.error("Erro ao aceitar pedido. Tente novamente."),
  });
}

// ── useRejectDelivery ─────────────────────────────────────────────────────────

export function useRejectDelivery(storeId: string) {
  const invalidate = useInvalidateDelivery(storeId);
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: RejectDeliveryRequest }) =>
      rejectDeliveryOrder(id, req),
    onSuccess: () => {
      invalidate();
      toast.success("Pedido rejeitado.");
    },
    onError: () => toast.error("Erro ao rejeitar pedido. Tente novamente."),
  });
}

// ── useAdvanceDelivery ────────────────────────────────────────────────────────

export function useAdvanceDelivery(storeId: string) {
  const invalidate = useInvalidateDelivery(storeId);
  return useMutation({
    mutationFn: ({ id, req }: { id: string; req: UpdateDeliveryStatusRequest }) =>
      updateDeliveryStatus(id, req),
    onSuccess: (order) => {
      invalidate();
      const label =
        order.status === "Delivered"
          ? order.orderType === "Takeaway"
            ? "Retirada confirmada!"
            : "Entrega confirmada!"
          : "Pedido despachado para entrega.";
      toast.success(label);
    },
    onError: () => toast.error("Erro ao atualizar status. Tente novamente."),
  });
}

// ── useCancelDelivery ─────────────────────────────────────────────────────────

export function useCancelDelivery(storeId: string) {
  const invalidate = useInvalidateDelivery(storeId);
  return useMutation({
    mutationFn: (id: string) => cancelDeliveryOrder(id),
    onSuccess: () => {
      invalidate();
      toast.success("Pedido cancelado.");
    },
    onError: () => toast.error("Erro ao cancelar pedido. Tente novamente."),
  });
}

// ── useCreateManualDelivery ───────────────────────────────────────────────────

export function useCreateManualDelivery(storeId: string) {
  const invalidate = useInvalidateDelivery(storeId);
  return useMutation({
    mutationFn: (req: CreateManualDeliveryRequest) =>
      createManualDeliveryOrder(req),
    onSuccess: (order) => {
      invalidate();
      toast.success(`Pedido #${order.orderNumber} criado com sucesso.`);
    },
    onError: () => toast.error("Erro ao criar pedido. Verifique os dados e tente novamente."),
  });
}
