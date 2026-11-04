/** Shared formatting for the Build module's newer components (pt-BR). */

export function fmtBRL(v: number | null | undefined): string {
  if (v == null) return "—";
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

/** yyyy-MM-dd → "05 de out. de 2026" style short date; "—" when empty. */
export function fmtDate(d: string | null | undefined): string {
  if (!d) return "—";
  return new Date(`${d.slice(0, 10)}T12:00:00`).toLocaleDateString("pt-BR", {
    day: "2-digit", month: "short", year: "numeric",
  });
}

export function fmtDateTime(d: string | null | undefined): string {
  if (!d) return "—";
  return new Date(d).toLocaleString("pt-BR", {
    day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

/** Percent with one decimal ("42,5%"); "—" when there is no value. */
export function fmtPercent(v: number | null | undefined): string {
  if (v == null) return "—";
  return `${v.toLocaleString("pt-BR", { minimumFractionDigits: 1, maximumFractionDigits: 1 })}%`;
}

/** Today's date in the user's calendar (not UTC — after 9 pm in Brazil UTC is already tomorrow). */
export function localToday(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

/** "1,5" or "1.5" → 1.5; empty → null; anything else → NaN. */
export function parseDecimal(raw: string): number | null {
  const t = raw.trim();
  if (!t) return null;
  const n = Number(t.replace(",", "."));
  return Number.isFinite(n) ? n : Number.NaN;
}
