/**
 * Friendly weekly working-hours model ⇄ the backend `WorkingHoursJson` shape (PR12):
 *   [{ "weekday": 0-6, "windows": [{ "start": "HH:mm", "end": "HH:mm" }] }]   (0 = Sunday)
 *
 * The UI works in `DayHours` (one optional break per day = up to two windows). The owner never
 * sees raw JSON. Parsing is lenient — a malformed/empty column yields an all-disabled week.
 */

export interface TimeWindow { start: string; end: string; }

export interface DayHours {
  weekday:    number; // 0 = Sun … 6 = Sat
  enabled:    boolean;
  start:      string; // "HH:mm"
  end:        string;
  hasBreak:   boolean;
  breakStart: string;
  breakEnd:   string;
}

/** Monday-first display order. */
export const WEEKDAY_ORDER = [1, 2, 3, 4, 5, 6, 0];

export const WEEKDAY_LABEL: Record<number, string> = {
  0: "Domingo", 1: "Segunda", 2: "Terça", 3: "Quarta", 4: "Quinta", 5: "Sexta", 6: "Sábado",
};

const TIME_RE = /^([01]\d|2[0-3]):[0-5]\d$/;
function isTime(v: unknown): v is string {
  return typeof v === "string" && TIME_RE.test(v);
}

function defaultDay(weekday: number, enabled = false): DayHours {
  return {
    weekday, enabled,
    start: "09:00", end: "18:00",
    hasBreak: false, breakStart: "12:00", breakEnd: "13:00",
  };
}

export function emptyWeek(): DayHours[] {
  return WEEKDAY_ORDER.map((w) => defaultDay(w));
}

export function parseWorkingHours(json: string | null | undefined): DayHours[] {
  const week = new Map<number, DayHours>(WEEKDAY_ORDER.map((w) => [w, defaultDay(w)]));

  if (json) {
    try {
      const arr = JSON.parse(json) as Array<{ weekday?: number; windows?: TimeWindow[] }>;
      if (Array.isArray(arr)) {
        for (const entry of arr) {
          if (typeof entry?.weekday !== "number" || !week.has(entry.weekday)) continue;
          const windows = (entry.windows ?? [])
            .filter((w) => isTime(w?.start) && isTime(w?.end) && w.start < w.end)
            .sort((a, b) => a.start.localeCompare(b.start));
          if (windows.length === 0) continue;

          const day = week.get(entry.weekday)!;
          day.enabled = true;
          if (windows.length >= 2) {
            day.start = windows[0].start;
            day.breakStart = windows[0].end;
            day.breakEnd = windows[1].start;
            day.end = windows[1].end;
            day.hasBreak = day.breakStart < day.breakEnd;
          } else {
            day.start = windows[0].start;
            day.end = windows[0].end;
            day.hasBreak = false;
          }
        }
      }
    } catch {
      /* malformed → all-disabled defaults */
    }
  }

  return WEEKDAY_ORDER.map((w) => week.get(w)!);
}

function dayWindows(day: DayHours): TimeWindow[] {
  if (!day.enabled || !isTime(day.start) || !isTime(day.end) || day.start >= day.end) return [];
  if (day.hasBreak
      && isTime(day.breakStart) && isTime(day.breakEnd)
      && day.start < day.breakStart && day.breakStart < day.breakEnd && day.breakEnd < day.end) {
    return [{ start: day.start, end: day.breakStart }, { start: day.breakEnd, end: day.end }];
  }
  return [{ start: day.start, end: day.end }];
}

/** Backend JSON (or null when no day is configured). */
export function buildWorkingHoursJson(days: DayHours[]): string | null {
  const out = days
    .map((d) => ({ weekday: d.weekday, windows: dayWindows(d) }))
    .filter((d) => d.windows.length > 0);
  return out.length === 0 ? null : JSON.stringify(out);
}

/** Inline validation message for a day, or null when valid (or disabled). */
export function dayError(day: DayHours): string | null {
  if (!day.enabled) return null;
  if (!isTime(day.start) || !isTime(day.end)) return "Horário inválido.";
  if (day.start >= day.end) return "O início deve ser antes do fim.";
  if (day.hasBreak) {
    if (!isTime(day.breakStart) || !isTime(day.breakEnd)) return "Pausa inválida.";
    if (!(day.start < day.breakStart && day.breakStart < day.breakEnd && day.breakEnd < day.end)) {
      return "A pausa deve ficar dentro do expediente.";
    }
  }
  return null;
}

export function weekHasErrors(days: DayHours[]): boolean {
  return days.some((d) => dayError(d) !== null);
}

/** True when a professional's stored JSON yields at least one real working window. */
export function professionalHasHours(json: string | null | undefined): boolean {
  return buildWorkingHoursJson(parseWorkingHours(json)) !== null;
}
