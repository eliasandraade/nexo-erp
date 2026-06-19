import type { PublicAvailabilitySlot } from "../api/booking.api";

const DEFAULT_LOCALE = "pt-BR";

export interface FormatOptions {
  locale?: string;
  /** IANA tz; omit to use the viewer's local timezone (the production default). */
  timeZone?: string;
}

export function formatPrice(value: number, locale = DEFAULT_LOCALE): string {
  return new Intl.NumberFormat(locale, { style: "currency", currency: "BRL" }).format(value);
}

export function formatDuration(minutes: number): string {
  if (minutes < 60) return `${minutes} min`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 ? `${h}h` : `${h}h${String(m).padStart(2, "0")}`;
}

export function formatTime(startsAtIso: string, opts: FormatOptions = {}): string {
  return new Intl.DateTimeFormat(opts.locale ?? DEFAULT_LOCALE, {
    timeZone: opts.timeZone, hour: "2-digit", minute: "2-digit", hour12: false,
  }).format(new Date(startsAtIso));
}

export function formatDateLong(startsAtIso: string, opts: FormatOptions = {}): string {
  return new Intl.DateTimeFormat(opts.locale ?? DEFAULT_LOCALE, {
    timeZone: opts.timeZone, weekday: "short", day: "2-digit", month: "short",
  }).format(new Date(startsAtIso));
}

/** "qua., 19 de jun. · 14:00" — used in confirmation + success summaries. */
export function formatSlotLabel(startsAtIso: string, opts: FormatOptions = {}): string {
  return `${formatDateLong(startsAtIso, opts)} · ${formatTime(startsAtIso, opts)}`;
}

export interface DaySlots {
  dayKey:    string; // YYYY-MM-DD (stable, for keys)
  dateLabel: string; // localized, e.g. "qua., 19 de jun."
  slots:     { startsAt: string; timeLabel: string }[];
}

/**
 * Groups availability slots by calendar day (in the chosen timezone) preserving order. Days and
 * times within a day come out sorted because the backend already returns slots ascending.
 */
export function groupSlotsByDay(
  slots: PublicAvailabilitySlot[], opts: FormatOptions = {},
): DaySlots[] {
  const keyFmt = new Intl.DateTimeFormat("en-CA", {
    timeZone: opts.timeZone, year: "numeric", month: "2-digit", day: "2-digit",
  });

  const byDay = new Map<string, DaySlots>();
  for (const slot of slots) {
    const dayKey = keyFmt.format(new Date(slot.startsAt));
    let entry = byDay.get(dayKey);
    if (!entry) {
      entry = { dayKey, dateLabel: formatDateLong(slot.startsAt, opts), slots: [] };
      byDay.set(dayKey, entry);
    }
    entry.slots.push({ startsAt: slot.startsAt, timeLabel: formatTime(slot.startsAt, opts) });
  }
  return [...byDay.values()];
}

/** Backend requires ≥ 8 digits; mirror it client-side so the UI fails fast. */
export function isValidPhone(phone: string): boolean {
  return (phone.match(/\d/g)?.length ?? 0) >= 8;
}
