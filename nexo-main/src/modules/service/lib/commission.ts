import type {
  ServiceLabels,
  SvcCommissionEntryDto,
  SvcCommissionPayoutStatus,
  SvcCommissionSource,
} from "../api/service.api";
import type { BadgeVariant } from "@/components/shared/StatusBadge";

/** Where the commission came from, in the vertical's own words (Comanda / OS, Agendamento…). */
export function commissionSourceLabel(source: SvcCommissionSource, labels?: ServiceLabels | null): string {
  switch (source) {
    case "OrderItem":    return labels?.order ?? "Ordem de serviço";
    case "Appointment":  return labels?.appointment ?? "Agendamento";
    case "PackageUsage": return "Pacote";
  }
}

export const PAYOUT_STATUS_LABELS: Record<SvcCommissionPayoutStatus, string> = {
  Pending: "A pagar",
  Paid: "Pago",
};

export const PAYOUT_STATUS_VARIANTS: Record<SvcCommissionPayoutStatus, BadgeVariant> = {
  Pending: "warning",
  Paid: "success",
};

/** An entry is "Fechada" once it belongs to a payout, "Em aberto" before. */
export function entryStatus(entry: Pick<SvcCommissionEntryDto, "payoutId">): { label: string; variant: BadgeVariant } {
  return entry.payoutId ? { label: "Fechada", variant: "neutral" } : { label: "Em aberto", variant: "info" };
}

/** yyyy-MM-dd of a local date (what <input type="date"> speaks). */
export function toDateInputValue(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Default period: first day of the current month → today. */
export function currentMonthPeriod(now: Date = new Date()): { from: string; to: string } {
  return { from: toDateInputValue(new Date(now.getFullYear(), now.getMonth(), 1)), to: toDateInputValue(now) };
}

/**
 * Converts a local-day period (yyyy-MM-dd, inclusive on both ends) into the UTC instants the API
 * expects: start of `from` and the last millisecond of `to`, both in the user's timezone. Returns
 * null when a date is missing/invalid or the range is inverted.
 */
export function periodToUtcRange(from: string, to: string): { start: string; end: string } | null {
  if (!from || !to) return null;
  const start = new Date(`${from}T00:00:00`);
  const end = new Date(`${to}T23:59:59.999`);
  if (Number.isNaN(start.getTime()) || Number.isNaN(end.getTime()) || start > end) return null;
  return { start: start.toISOString(), end: end.toISOString() };
}

/** Sum of commission amounts, rounded to cents (the API already stores cents per entry). */
export function sumCommission(entries: Pick<SvcCommissionEntryDto, "commissionAmount">[]): number {
  return Math.round(entries.reduce((acc, e) => acc + e.commissionAmount, 0) * 100) / 100;
}
