import type {
  SvcAppointmentDto,
  SvcOrderDto,
  SvcPaymentDto,
} from "../api/service.api";

/**
 * Dashboard aggregators. Every figure is derived from a real list endpoint (there are no
 * aggregate/report endpoints in the Service backend yet) — no invented numbers. Pure, so the
 * derivations are unit-tested independent of the network.
 */

export interface AppointmentStats {
  total: number;
  done: number;
  remaining: number;
}

/** Counts a day's appointments by progress (done = Completed; remaining = not-yet-terminal). */
export function appointmentStats(appointments: SvcAppointmentDto[]): AppointmentStats {
  let done = 0;
  let remaining = 0;
  for (const a of appointments) {
    if (a.status === "Completed") done += 1;
    else if (a.status === "Scheduled" || a.status === "Confirmed" || a.status === "InProgress") remaining += 1;
  }
  return { total: appointments.length, done, remaining };
}

export interface OpenOrdersStats {
  count: number;
  total: number;
}

/** Orders still in play (Open or InProgress) and the sum of their current totals. */
export function openOrdersStats(orders: SvcOrderDto[]): OpenOrdersStats {
  const open = orders.filter((o) => o.status === "Open" || o.status === "InProgress");
  return {
    count: open.length,
    total: open.reduce((sum, o) => sum + o.totalAmount, 0),
  };
}

/** Sum of received (Paid, non-voided) payment amounts. */
export function paidTotal(payments: SvcPaymentDto[]): number {
  return payments
    .filter((p) => p.status === "Paid")
    .reduce((sum, p) => sum + p.amount, 0);
}
