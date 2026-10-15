export type PeriodPreset = "today" | "7d" | "month" | "lastMonth" | "custom";

export interface Period { from: string; to: string }

/** Longest period the server accepts (days, both ends included). */
export const MAX_DAYS = 366;

const iso = (d: Date) =>
  `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;

/** Calendar days in the browser's zone — for an operation in Brazil, the Brazil day the server also uses. */
export function presetPeriod(preset: Exclude<PeriodPreset, "custom">, now = new Date()): Period {
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  if (preset === "today") return { from: iso(today), to: iso(today) };
  if (preset === "7d") {
    const start = new Date(today);
    start.setDate(start.getDate() - 6);
    return { from: iso(start), to: iso(today) };
  }
  if (preset === "month") return { from: iso(new Date(today.getFullYear(), today.getMonth(), 1)), to: iso(today) };
  return {
    from: iso(new Date(today.getFullYear(), today.getMonth() - 1, 1)),
    to: iso(new Date(today.getFullYear(), today.getMonth(), 0)),
  };
}

/** Why a custom period cannot be asked for, or null when it can. */
export function periodProblem({ from, to }: Period): string | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(from) || !/^\d{4}-\d{2}-\d{2}$/.test(to)) return "Informe as duas datas.";
  if (to < from) return "A data final vem antes da inicial.";
  const days = Math.round((Date.parse(`${to}T12:00:00Z`) - Date.parse(`${from}T12:00:00Z`)) / 86_400_000) + 1;
  if (days > MAX_DAYS) return `Escolha um período de até ${MAX_DAYS} dias.`;
  return null;
}

export const fmtDay = (day: string) => `${day.slice(8, 10)}/${day.slice(5, 7)}`;

export const fmtPeriod = ({ from, to }: Period) =>
  from === to ? `${fmtDay(from)}/${from.slice(0, 4)}` : `${fmtDay(from)}/${from.slice(0, 4)} a ${fmtDay(to)}/${to.slice(0, 4)}`;

const METHOD_LABELS: Record<string, string> = {
  Cash: "Dinheiro",
  Debit: "Cartão de débito",
  Credit: "Cartão de crédito",
  Pix: "Pix",
  Transfer: "Transferência",
  Check: "Cheque",
  Mixed: "Misto",
  Other: "Outro",
};

export const methodLabel = (method: string) => METHOD_LABELS[method] ?? method;
