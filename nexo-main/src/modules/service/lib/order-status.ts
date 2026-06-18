import type { SvcOrderStatus } from "../api/service.api";
import type { BadgeVariant } from "@/components/shared/StatusBadge";

export const ORDER_STATUS_LABELS: Record<SvcOrderStatus, string> = {
  Draft: "Rascunho",
  Open: "Aberta",
  InProgress: "Em andamento",
  Completed: "Concluída",
  Cancelled: "Cancelada",
};

export const ORDER_STATUS_VARIANTS: Record<SvcOrderStatus, BadgeVariant> = {
  Draft: "neutral",
  Open: "info",
  InProgress: "warning",
  Completed: "success",
  Cancelled: "danger",
};

/** Action verb shown for a status transition. */
export const ORDER_ACTION_LABELS: Record<SvcOrderStatus, string> = {
  Draft: "Rascunho",
  Open: "Abrir",
  InProgress: "Iniciar",
  Completed: "Concluir",
  Cancelled: "Cancelar",
};

/**
 * Allowed order transitions — mirrors `SvcOrder.CanTransition`
 * (Nexo.Domain/Modules/Service/SvcOrder.cs). The backend still enforces the machine (422).
 */
const TRANSITIONS: Record<SvcOrderStatus, SvcOrderStatus[]> = {
  Draft: ["Open", "Cancelled"],
  Open: ["InProgress", "Cancelled"],
  InProgress: ["Completed", "Cancelled"],
  Completed: [],
  Cancelled: [],
};

export function allowedOrderTransitions(status: SvcOrderStatus): SvcOrderStatus[] {
  return TRANSITIONS[status];
}

/** Items can be added/edited/removed only while the order is non-terminal (backend EnsureMutable). */
export function isOrderMutable(status: SvcOrderStatus): boolean {
  return status !== "Completed" && status !== "Cancelled";
}
