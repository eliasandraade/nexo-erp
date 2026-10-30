import type { BuildBudgetDto, BuildBudgetTotalsDto, BuildCostKind } from "../api/build.api";
import type {
  BuildChangeOrderReason, BuildChangeOrderStatus, BuildMaterialStatus, BuildMeasurementStatus, BuildSelectionStatus,
} from "../api/build-commercial.api";

/** Labels of the Wave 2 enums (the API sends the English names). */
export const COST_KINDS: Array<{ value: BuildCostKind; label: string }> = [
  { value: "Material",  label: "Material" },
  { value: "Labor",     label: "Mão de obra" },
  { value: "Service",   label: "Serviço" },
  { value: "Equipment", label: "Equipamento" },
  { value: "Other",     label: "Outros" },
];

export function costKindLabel(kind: BuildCostKind | null | undefined): string {
  return COST_KINDS.find((k) => k.value === (kind ?? "Other"))?.label ?? "Outros";
}

export const CHANGE_ORDER_REASONS: Array<{ value: BuildChangeOrderReason; label: string }> = [
  { value: "ClientRequest", label: "Pedido do cliente" },
  { value: "DesignChange",  label: "Mudança de projeto" },
  { value: "SiteCondition", label: "Condição da obra" },
  { value: "Error",         label: "Correção de erro" },
  { value: "Other",         label: "Outro" },
];

export const reasonLabel = (r: BuildChangeOrderReason) =>
  CHANGE_ORDER_REASONS.find((x) => x.value === r)?.label ?? r;

type Tone = "gray" | "blue" | "amber" | "green" | "red" | "purple";

export const TONE_CLASS: Record<Tone, string> = {
  gray:   "bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400",
  blue:   "bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400",
  amber:  "bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400",
  green:  "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400",
  red:    "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400",
  purple: "bg-purple-100 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400",
};

export const CHANGE_ORDER_STATUS: Record<BuildChangeOrderStatus, { label: string; tone: Tone }> = {
  Draft:        { label: "Rascunho",           tone: "gray" },
  SentToClient: { label: "Enviado ao cliente", tone: "blue" },
  Approved:     { label: "Aprovado",           tone: "green" },
  Rejected:     { label: "Recusado",           tone: "red" },
  Cancelled:    { label: "Cancelado",          tone: "gray" },
};

export const SELECTION_STATUS: Record<BuildSelectionStatus, { label: string; tone: Tone }> = {
  Open:      { label: "Aberta",                tone: "blue" },
  Submitted: { label: "Cliente escolheu",      tone: "amber" },
  Confirmed: { label: "Confirmada",            tone: "green" },
  Cancelled: { label: "Cancelada",             tone: "gray" },
};

export const MATERIAL_STATUS: Record<BuildMaterialStatus, { label: string; tone: Tone }> = {
  Requested:          { label: "Solicitado",          tone: "gray" },
  Quoted:             { label: "Cotado",              tone: "blue" },
  Ordered:            { label: "Pedido feito",        tone: "purple" },
  PartiallyDelivered: { label: "Entrega parcial",     tone: "amber" },
  Delivered:          { label: "Entregue",            tone: "green" },
  Cancelled:          { label: "Cancelado",           tone: "gray" },
};

export const MEASUREMENT_STATUS: Record<BuildMeasurementStatus, { label: string; tone: Tone }> = {
  Draft:     { label: "Rascunho",  tone: "gray" },
  Submitted: { label: "Enviada",   tone: "blue" },
  Approved:  { label: "Aprovada",  tone: "green" },
  Rejected:  { label: "Recusada",  tone: "red" },
};

/** Totals per kind — the server's when it sends them, otherwise computed from the items (older servers). */
export function budgetTotals(b: BuildBudgetDto): BuildBudgetTotalsDto {
  if (b.totals) return b.totals;
  const sum = (k: BuildCostKind) =>
    b.items.filter((i) => (i.kind ?? "Other") === k).reduce((s, i) => s + i.totalCost, 0);
  const subtotal = b.totalCost;
  const area = b.areaM2 ?? null;
  return {
    material: sum("Material"), labor: sum("Labor"), service: sum("Service"),
    equipment: sum("Equipment"), other: sum("Other"),
    subtotal, marginPercent: b.marginPercent, marginValue: b.finalPrice - subtotal, total: b.finalPrice,
    areaM2: area,
    costPerM2:  area ? subtotal / area : null,
    totalPerM2: area ? b.finalPrice / area : null,
  };
}

/** Per-stage cumulative % of a measurement: ≥ the stage's current % and ≤ 100. Null when valid. */
export function measuredPercentProblem(measured: number, previous: number): string | null {
  if (!Number.isFinite(measured) || !Number.isInteger(measured)) return "Informe um % inteiro.";
  if (measured < previous) return `Não pode ser menor que o atual (${previous}%).`;
  if (measured > 100) return "Não pode passar de 100%.";
  return null;
}

/** "+R$ 1.200,00" / "−R$ 300,00" — the sign always shows. */
export function fmtSignedBRL(v: number): string {
  const abs = Math.abs(v).toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
  if (v > 0) return `+${abs}`;
  if (v < 0) return `−${abs}`;
  return abs;
}

/** Text color for a signed money impact: more cost red, less cost green. */
export function signedTone(v: number): string {
  if (v > 0) return "text-red-600 dark:text-red-400";
  if (v < 0) return "text-emerald-600 dark:text-emerald-400";
  return "text-muted-foreground";
}

export function fmtDays(d: number): string {
  if (d === 0) return "sem impacto no prazo";
  const n = Math.abs(d);
  return `${d > 0 ? "+" : "−"}${n} ${n === 1 ? "dia" : "dias"}`;
}
