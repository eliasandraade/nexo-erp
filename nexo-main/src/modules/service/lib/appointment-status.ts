import type { SvcAppointmentStatus } from "../api/service.api";
import type { BadgeVariant } from "@/components/shared/StatusBadge";

/** PT labels for the appointment status machine. */
export const APPOINTMENT_STATUS_LABELS: Record<SvcAppointmentStatus, string> = {
  Scheduled: "Agendado",
  Confirmed: "Confirmado",
  InProgress: "Em atendimento",
  Completed: "Concluído",
  NoShow: "Não compareceu",
  Cancelled: "Cancelado",
};

export const APPOINTMENT_STATUS_VARIANTS: Record<SvcAppointmentStatus, BadgeVariant> = {
  Scheduled: "info",
  Confirmed: "info",
  InProgress: "warning",
  Completed: "success",
  NoShow: "neutral",
  Cancelled: "danger",
};

/**
 * Allowed status transitions — must mirror the backend `SvcAppointment.CanTransition`
 * (Nexo.Domain/Modules/Service/SvcAppointment.cs). The UI offers only valid actions; the
 * backend still enforces the machine (422 on an invalid transition).
 */
const TRANSITIONS: Record<SvcAppointmentStatus, SvcAppointmentStatus[]> = {
  Scheduled: ["Confirmed", "Cancelled", "NoShow"],
  Confirmed: ["InProgress", "Cancelled", "NoShow"],
  InProgress: ["Completed", "Cancelled"],
  Completed: [],
  NoShow: [],
  Cancelled: [],
};

export function allowedTransitions(status: SvcAppointmentStatus): SvcAppointmentStatus[] {
  return TRANSITIONS[status];
}

export function isTerminalStatus(status: SvcAppointmentStatus): boolean {
  return TRANSITIONS[status].length === 0;
}

/** Status changes that should capture a reason (cancellation / no-show). */
export function transitionNeedsReason(target: SvcAppointmentStatus): boolean {
  return target === "Cancelled" || target === "NoShow";
}
