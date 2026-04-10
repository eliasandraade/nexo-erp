/**
 * Shared formatting utilities.
 * Keep all locale-sensitive formatting centralized here so the entire app
 * stays consistent and future backend integration has a single place to adjust.
 */

/** Formats a number as BRL currency: R$ 1.234,56 */
export function formatCurrency(value: number): string {
  return value.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

/** Formats an ISO timestamp as a date: 12/03/2026 */
export function formatDate(iso: string): string {
  return new Date(iso).toLocaleString("pt-BR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
  });
}

/** Formats an ISO timestamp as date + time: 12/03/2026 14:30 */
export function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("pt-BR", {
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

/** Formats an ISO timestamp as time + short date: 14:30 · 12/03 */
export function formatTimeShort(iso: string): string {
  return new Date(iso).toLocaleString("pt-BR", {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}
