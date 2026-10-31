import type {
  BuildForecastFactorKind, BuildForecastHistoryItemDto, BuildForecastMethod, BuildWeatherDayDto, BuildWeatherRiskKind,
} from "../api/build-forecast.api";
import type { BuildDashboardProjectDto } from "../api/build.api";

// Pure rules of the Wave 6 screens (forecast, weather, portfolio). No React — unit-tested.

// ── Severity / level ──────────────────────────────────────────────────────────

export const SEVERITY_META: Record<string, { label: string; cls: string }> = {
  none:   { label: "Sem risco", cls: "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400" },
  low:    { label: "Baixo",     cls: "bg-sky-100 text-sky-700 dark:bg-sky-900/30 dark:text-sky-300" },
  medium: { label: "Médio",     cls: "bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-300" },
  high:   { label: "Alto",      cls: "bg-rose-100 text-rose-700 dark:bg-rose-900/30 dark:text-rose-300" },
};

export function severityMeta(severity: string | null | undefined) {
  return SEVERITY_META[severity ?? "none"] ?? SEVERITY_META.none;
}

const SEVERITY_RANK: Record<string, number> = { high: 0, medium: 1, low: 2, none: 3 };
export const bySeverity = <T extends { severity: string }>(a: T, b: T) =>
  (SEVERITY_RANK[a.severity] ?? 9) - (SEVERITY_RANK[b.severity] ?? 9);

// ── Forecast ──────────────────────────────────────────────────────────────────

export const PREDICT_DISABLED_MESSAGE = "Previsões não habilitadas para sua empresa.";

export function methodLabel(method: BuildForecastMethod, windowDays = 21): string {
  switch (method) {
    case "completed": return "Concluída";
    case "window":    return `Ritmo dos últimos ${windowDays} dias`;
    case "overall":   return "Ritmo desde o início";
    case "planned":   return "Ritmo planejado (sem dados)";
    default:          return "Sem previsão";
  }
}

export const FACTOR_LABEL: Record<string, string> = {
  delivery_after_plan:  "Entrega depois do planejado",
  low_productivity:     "Ritmo abaixo do planejado",
  inherited_delay:      "Atraso herdado da etapa anterior",
  pending_change_order: "Aditivo pendente com impacto no prazo",
  weather_risk:         "Risco climático",
  no_productivity_data: "Etapa sem registros de avanço",
  no_forecast:          "Etapa sem previsão",
};

/** Factors that are risks only: shown, never applied to the dates. */
export function isRiskOnlyFactor(kind: BuildForecastFactorKind): boolean {
  return kind === "pending_change_order" || kind === "weather_risk";
}

/** 0.734 → "73%". */
export function fmtConfidence(c: number | null | undefined): string {
  if (c == null || !Number.isFinite(c)) return "—";
  return `${Math.round(Math.max(0, Math.min(1, c)) * 100)}%`;
}

export function confidenceLevel(c: number): "alta" | "média" | "baixa" {
  if (c >= 0.75) return "alta";
  if (c >= 0.5) return "média";
  return "baixa";
}

/** Signed days against a reference: "+12 dias", "3 dias adiantada", "no prazo"; "—" without a value. */
export function fmtDelay(days: number | null | undefined): string {
  if (days == null) return "—";
  if (days > 0) return `+${days} dia${days === 1 ? "" : "s"}`;
  if (days < 0) return `${-days} dia${days === -1 ? "" : "s"} adiantada`;
  return "no prazo";
}

/** % per working day ("2,5%/dia útil"). */
export function fmtRate(r: number | null | undefined): string {
  if (r == null || !Number.isFinite(r)) return "—";
  return `${r.toLocaleString("pt-BR", { maximumFractionDigits: 2 })}%/dia útil`;
}

const dayNumber = (iso: string) => Math.round(Date.parse(`${iso.slice(0, 10)}T00:00:00Z`) / 86_400_000);

/**
 * Sparkline of the forecast delivery over the stored history (oldest → newest): SVG polyline points in a width × height box,
 * a later delivery drawn higher. Null with fewer than two points with a delivery.
 */
export function deliverySparkline(history: BuildForecastHistoryItemDto[], width: number, height: number): string | null {
  const points = [...history].reverse().filter((h) => !!h.forecastDelivery).map((h) => dayNumber(h.forecastDelivery!));
  if (points.length < 2) return null;
  const min = Math.min(...points);
  const span = Math.max(...points) - min || 1;
  const stepX = width / (points.length - 1);
  return points
    .map((v, i) => `${(i * stepX).toFixed(1)},${(height - ((v - min) / span) * height).toFixed(1)}`)
    .join(" ");
}

// ── Weather ───────────────────────────────────────────────────────────────────

export type WeatherIconKey = "sun" | "cloud-sun" | "cloud" | "fog" | "drizzle" | "rain" | "storm" | "snow";

/** Icon of a day from the provider's description (Open-Meteo WMO text in pt-BR) or, without it, from the rain. */
export function weatherIcon(day: Pick<BuildWeatherDayDto, "description" | "precipitationMm">): WeatherIconKey {
  const d = (day.description ?? "").toLowerCase();
  if (d.includes("trovoada")) return "storm";
  if (d.includes("neve")) return "snow";
  if (d.includes("chuva")) return "rain";
  if (d.includes("garoa")) return "drizzle";
  if (d.includes("neblina")) return "fog";
  if (d.includes("parcialmente") || d.includes("majoritariamente")) return "cloud-sun";
  if (d.includes("nublado")) return "cloud";
  if (d.includes("limpo")) return "sun";
  if ((day.precipitationMm ?? 0) >= 2) return "rain";
  return "cloud-sun";
}

export const WEATHER_RISK_LABEL: Record<string, string> = {
  heavy_rain:  "Chuva forte",
  rain:        "Chuva",
  strong_wind: "Vento forte",
};

export function weatherRiskLabel(kind: BuildWeatherRiskKind): string {
  return WEATHER_RISK_LABEL[kind] ?? kind;
}

/** "qua., 04/11" — the short weekday and date of a forecast day. */
export function fmtWeekday(iso: string): string {
  const d = new Date(`${iso.slice(0, 10)}T12:00:00`);
  return `${d.toLocaleDateString("pt-BR", { weekday: "short" })} ${d.toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit" })}`;
}

/** Stage weather sensitivity select value ↔ API value (auto = null → keyword rule). */
export type SensitivityChoice = "auto" | "yes" | "no";
export function sensitivityToChoice(v: boolean | null | undefined): SensitivityChoice {
  if (v == null) return "auto";
  return v ? "yes" : "no";
}
export function choiceToSensitivity(c: SensitivityChoice): boolean | null {
  if (c === "auto") return null;
  return c === "yes";
}

// ── Portfolio ─────────────────────────────────────────────────────────────────

export type PortfolioFilter = "all" | "late" | "critical";
export type PortfolioSort = "risk" | "name" | "daysLate" | "progress" | "cpi";

export const criticalNc = (p: BuildDashboardProjectDto) => p.openNonConformities?.Critical ?? 0;
export const totalNc = (p: BuildDashboardProjectDto) =>
  Object.values(p.openNonConformities ?? {}).reduce((s, n) => s + (n ?? 0), 0);
export const isLate = (p: BuildDashboardProjectDto) => (p.daysLate ?? 0) > 0;

export function filterPortfolio(projects: BuildDashboardProjectDto[], filter: PortfolioFilter): BuildDashboardProjectDto[] {
  if (filter === "late") return projects.filter(isLate);
  if (filter === "critical") return projects.filter((p) => criticalNc(p) > 0);
  return projects;
}

/** A rough risk score to order the portfolio: days late, critical NCs, cost performance below 1 and late materials. */
export function riskScore(p: BuildDashboardProjectDto): number {
  const late = Math.max(0, p.daysLate ?? 0);
  const cpi = p.cpiLike != null && p.cpiLike < 1 ? (1 - p.cpiLike) * 100 : 0;
  return late + criticalNc(p) * 30 + cpi + p.lateMaterials * 5;
}

export function sortPortfolio(projects: BuildDashboardProjectDto[], sort: PortfolioSort): BuildDashboardProjectDto[] {
  const list = [...projects];
  const nullsLast = (v: number | null | undefined, fallback: number) => (v == null ? fallback : v);
  switch (sort) {
    case "name":     return list.sort((a, b) => a.name.localeCompare(b.name, "pt-BR"));
    case "daysLate": return list.sort((a, b) => nullsLast(b.daysLate, -Infinity) - nullsLast(a.daysLate, -Infinity));
    case "progress": return list.sort((a, b) => b.actualPercent - a.actualPercent);
    case "cpi":      return list.sort((a, b) => nullsLast(a.cpiLike, Infinity) - nullsLast(b.cpiLike, Infinity));
    default:         return list.sort((a, b) => riskScore(b) - riskScore(a) || a.name.localeCompare(b.name, "pt-BR"));
  }
}

/** Tab of the project to open for a dashboard risk. */
export function riskTab(kind: string): string {
  switch (kind) {
    case "critical_nonconformity": return "qualidade";
    case "late_material":          return "materiais";
    default:                       return "riscos";
  }
}
