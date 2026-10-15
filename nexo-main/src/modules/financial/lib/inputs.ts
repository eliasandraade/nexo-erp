/** "12,50" / "12.50" / "1.000" / "1.234,56" → number in cents precision; empty → 0; invalid → NaN. */
export function money(value: string): number {
  const v = value.trim();
  if (!v) return 0;
  // pt-BR: with a comma, dots are thousands; "1.000" (dot groups of 3 digits) is one thousand; "12.50" is decimal
  const thousandsOnly = /^-?\d{1,3}(\.\d{3})+$/.test(v);
  const normalized = v.includes(",") || thousandsOnly ? v.replace(/\./g, "").replace(",", ".") : v;
  const n = Number(normalized);
  return Number.isFinite(n) ? Math.round(n * 100) / 100 : NaN;
}

export function todayInput() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

export function monthInput(offset = 0) {
  const d = new Date();
  d.setDate(1);
  d.setMonth(d.getMonth() + offset);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}`;
}

/** A local calendar day at noon, so the date never shifts a day across time zones. */
export const dayToIso = (day: string) => new Date(`${day}T12:00:00`).toISOString();
