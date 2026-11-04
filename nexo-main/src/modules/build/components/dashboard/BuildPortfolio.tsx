import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { AlertTriangle, ArrowUpDown, BookOpen, Globe, Sparkles } from "lucide-react";
import { cn } from "@/lib/utils";
import type { BuildDashboardDto, BuildDashboardProjectDto, BuildDashboardTotalsDto, BuildDashboardRiskDto } from "../../api/build.api";
import {
  filterPortfolio, riskTab, severityMeta, sortPortfolio, type PortfolioFilter, type PortfolioSort,
} from "../../lib/forecast";
import { fmtDate } from "../../lib/format";
import { ProjectStatusBadge } from "../ProjectStatusBadge";

const brl = (v: number | null | undefined) =>
  v == null ? "—" : v.toLocaleString("pt-BR", { style: "currency", currency: "BRL", maximumFractionDigits: 0 });
const pct = (v: number | null | undefined) => (v == null ? "—" : `${v.toLocaleString("pt-BR", { maximumFractionDigits: 0 })}%`);

const NC_LABEL: Array<[key: string, label: string]> = [["Critical", "crítica"], ["High", "alta"], ["Medium", "média"], ["Low", "baixa"]];

const FILTERS: Array<{ key: PortfolioFilter; label: string }> = [
  { key: "all", label: "Todas" },
  { key: "late", label: "Atrasadas" },
  { key: "critical", label: "Com NC crítica" },
];

const SORTS: Array<{ key: PortfolioSort; label: string }> = [
  { key: "risk", label: "Maior risco" },
  { key: "daysLate", label: "Mais atrasadas" },
  { key: "progress", label: "Maior avanço" },
  { key: "cpi", label: "Pior desempenho de custo" },
  { key: "name", label: "Nome" },
];

function DaysLateBadge({ days }: { days: number | null }) {
  if (days == null) return <span className="rounded-full bg-muted px-2 py-0.5 text-[11px] text-muted-foreground">Sem previsão</span>;
  if (days > 0)
    return <span className="rounded-full bg-rose-100 px-2 py-0.5 text-[11px] font-medium text-rose-700 dark:bg-rose-900/30 dark:text-rose-300">{days} dia{days === 1 ? "" : "s"} de atraso</span>;
  return <span className="rounded-full bg-emerald-100 px-2 py-0.5 text-[11px] font-medium text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400">No prazo</span>;
}

function Cpi({ value }: { value: number | null }) {
  if (value == null) return <span className="text-muted-foreground">—</span>;
  let tone = "text-emerald-600 dark:text-emerald-400";
  if (value < 0.9) tone = "text-rose-600 dark:text-rose-400";
  else if (value < 1) tone = "text-amber-600 dark:text-amber-400";
  return <span className={cn("font-semibold tabular-nums", tone)}>{value.toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}</span>;
}

function NcSummary({ p }: { p: BuildDashboardProjectDto }) {
  const parts = NC_LABEL.map(([k, label]) => [p.openNonConformities?.[k] ?? 0, label] as const).filter(([n]) => n > 0);
  if (parts.length === 0) return <span className="text-muted-foreground">Nenhuma NC aberta</span>;
  return (
    <span>
      {parts.map(([n, label], i) => (
        <span key={label} className={cn(label === "crítica" && "font-semibold text-rose-600 dark:text-rose-400")}>
          {i > 0 && " · "}{n} {label}
        </span>
      ))}
    </span>
  );
}

function ProgressBar({ actual, planned }: { actual: number; planned: number | null }) {
  return (
    <div className="relative h-1.5 w-full overflow-hidden rounded-full bg-muted" aria-hidden>
      <div className="h-full rounded-full bg-primary" style={{ width: `${Math.min(100, Math.max(0, actual))}%` }} />
      {planned != null && (
        <div className="absolute top-0 h-full w-0.5 bg-foreground/60" style={{ left: `${Math.min(100, Math.max(0, planned))}%` }} />
      )}
    </div>
  );
}

function Pending({ p }: { p: BuildDashboardProjectDto }) {
  const items = [
    p.openAssistance > 0 && `${p.openAssistance} assistência(s)`,
    p.pendingChangeOrders > 0 && `${p.pendingChangeOrders} aditivo(s) pendente(s)`,
    p.lateMaterials > 0 && `${p.lateMaterials} material(is) atrasado(s)`,
  ].filter(Boolean) as string[];
  return <span>{items.length > 0 ? items.join(" · ") : <span className="text-muted-foreground">Sem pendências</span>}</span>;
}

function Extras({ p }: { p: BuildDashboardProjectDto }) {
  return (
    <span className="flex flex-wrap items-center gap-x-2 gap-y-0.5 text-[11px] text-muted-foreground">
      <span className={cn("inline-flex items-center gap-1", p.portalPublished && "text-emerald-600 dark:text-emerald-400")}>
        <Globe className="h-3 w-3" aria-hidden /> {p.portalPublished ? "Portal publicado" : "Portal não publicado"}
      </span>
      <span className="inline-flex items-center gap-1"><BookOpen className="h-3 w-3" aria-hidden /> Diário: {p.lastDailyLogDate ? fmtDate(p.lastDailyLogDate) : "nenhum"}</span>
      {p.pendingAiSuggestions > 0 && (
        <span className="inline-flex items-center gap-1"><Sparkles className="h-3 w-3" aria-hidden /> {p.pendingAiSuggestions} sugestão(ões) de IA</span>
      )}
    </span>
  );
}

function Totals({ t }: { t: BuildDashboardTotalsDto }) {
  const items: Array<[string, string, boolean?]> = [
    ["Obras ativas", String(t.activeProjects)],
    ["Atrasadas (previsão)", String(t.lateProjects), t.lateProjects > 0],
    ["Realizado / contratado", `${brl(t.costRealized)} / ${brl(t.contractTotal)}`],
    ["NCs abertas (críticas)", `${t.openNonConformities} (${t.openCriticalNonConformities})`, t.openCriticalNonConformities > 0],
    ["Assistências abertas", String(t.openAssistance)],
    ["Aditivos pendentes", String(t.pendingChangeOrders)],
    ["Materiais atrasados", String(t.lateMaterials), t.lateMaterials > 0],
    ["Portais publicados", String(t.publishedPortals)],
    ["Sugestões de IA pendentes", String(t.pendingAiSuggestions)],
  ];
  return (
    <dl className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-5" data-testid="portfolio-totals">
      {items.map(([label, value, alert]) => (
        <div key={label} className="min-w-0 rounded-md border border-border bg-card p-2">
          <dt className="truncate text-[11px] text-muted-foreground">{label}</dt>
          <dd className={cn("break-words text-sm font-semibold tabular-nums", alert && "text-rose-600 dark:text-rose-400")}>{value}</dd>
        </div>
      ))}
    </dl>
  );
}

function TopRisks({ risks, onOpen }: { risks: BuildDashboardRiskDto[]; onOpen: (r: BuildDashboardRiskDto) => void }) {
  return (
    <section className="space-y-2 rounded-md border border-border bg-card p-3" aria-label="Principais riscos" data-testid="portfolio-risks">
      <h3 className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        <AlertTriangle className="h-3.5 w-3.5" aria-hidden /> Principais riscos
      </h3>
      {risks.length === 0 ? (
        <p className="text-sm text-muted-foreground">Nenhum risco relevante nas obras ativas.</p>
      ) : (
        <ul className="divide-y divide-border">
          {risks.map((r, i) => (
            <li key={`${r.projectId}-${r.kind}-${i}`}>
              <button type="button" onClick={() => onOpen(r)}
                className="flex w-full flex-wrap items-center gap-2 rounded px-1 py-2 text-left hover:bg-muted/30">
                <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-medium", severityMeta(r.severity).cls)}>{severityMeta(r.severity).label}</span>
                <span className="text-sm font-medium">{r.projectName}</span>
                <span className="min-w-0 basis-full break-words text-xs text-muted-foreground sm:basis-auto">{r.message}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}

/** Portfolio of the active projects (GET /dashboard, Wave 6): totals, top risks and one row/card per project. */
export function BuildPortfolio({ data }: { data: BuildDashboardDto }) {
  const navigate = useNavigate();
  const [filter, setFilter] = useState<PortfolioFilter>("all");
  const [sort, setSort] = useState<PortfolioSort>("risk");
  const projects = useMemo(() => data.projects ?? [], [data.projects]);
  const shown = useMemo(() => sortPortfolio(filterPortfolio(projects, filter), sort), [projects, filter, sort]);
  const open = (id: string, tab?: string) => navigate(`/build/projetos/${id}${tab ? `?aba=${tab}` : ""}`);

  return (
    <div className="space-y-3" data-testid="build-portfolio">
      {data.totals && <Totals t={data.totals} />}
      <TopRisks risks={data.topRisks ?? []} onOpen={(r) => open(r.projectId, riskTab(r.kind))} />

      <section className="space-y-2 rounded-md border border-border bg-card p-3" aria-label="Carteira de obras ativas">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <h3 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            Carteira de obras ativas{data.asOf ? ` · ${fmtDate(data.asOf)}` : ""}
          </h3>
          <label className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <ArrowUpDown className="h-3.5 w-3.5" aria-hidden />
            <span className="sr-only">Ordenar por</span>
            <select aria-label="Ordenar por" value={sort} onChange={(e) => setSort(e.target.value as PortfolioSort)}
              className="h-8 rounded-md border border-input bg-background px-2 text-xs text-foreground">
              {SORTS.map((s) => <option key={s.key} value={s.key}>{s.label}</option>)}
            </select>
          </label>
        </div>
        <div className="flex flex-wrap gap-1.5" role="group" aria-label="Filtrar obras">
          {FILTERS.map((f) => (
            <button key={f.key} type="button" aria-pressed={filter === f.key} onClick={() => setFilter(f.key)}
              className={cn("rounded-full px-3 py-1 text-xs font-medium transition-colors",
                filter === f.key ? "bg-primary text-primary-foreground" : "bg-muted text-muted-foreground hover:text-foreground")}>
              {f.label}
            </button>
          ))}
        </div>

        {shown.length === 0 ? (
          <p className="py-4 text-center text-sm text-muted-foreground">
            {projects.length === 0 ? "Nenhuma obra ativa." : "Nenhuma obra neste filtro."}
          </p>
        ) : (
          <>
            {/* Desktop: table */}
            <div className="hidden overflow-x-auto md:block" data-testid="portfolio-table">
              <table className="w-full text-xs">
                <thead className="text-left text-[11px] text-muted-foreground">
                  <tr className="border-b border-border">
                    <th className="px-2 py-1.5 font-medium">Obra</th>
                    <th className="px-2 py-1.5 font-medium">Real × planejado</th>
                    <th className="px-2 py-1.5 font-medium">Entrega prevista</th>
                    <th className="px-2 py-1.5 font-medium">Realizado / contrato</th>
                    <th className="px-2 py-1.5 font-medium" title="(% real × contrato) ÷ realizado; abaixo de 1 = gastando mais que o avanço">CPI≈</th>
                    <th className="px-2 py-1.5 font-medium">NCs abertas</th>
                    <th className="px-2 py-1.5 font-medium">Pendências</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {shown.map((p) => (
                    <tr key={p.projectId} data-testid="portfolio-row" className="cursor-pointer align-top hover:bg-muted/30" onClick={() => open(p.projectId)}>
                      <td className="px-2 py-2">
                        <button type="button" className="text-left text-sm font-medium hover:underline" onClick={(e) => { e.stopPropagation(); open(p.projectId); }}>{p.name}</button>
                        <p className="text-[11px] text-muted-foreground">{p.clientName}</p>
                        <div className="mt-1"><ProjectStatusBadge status={p.status} /></div>
                        <div className="mt-1"><Extras p={p} /></div>
                      </td>
                      <td className="w-32 px-2 py-2">
                        <p className="tabular-nums">{pct(p.actualPercent)} × {pct(p.plannedPercent)}</p>
                        <ProgressBar actual={p.actualPercent} planned={p.plannedPercent} />
                      </td>
                      <td className="px-2 py-2">
                        <p>{fmtDate(p.forecastDelivery)}</p>
                        <p className="text-[11px] text-muted-foreground">contrato {fmtDate(p.expectedEnd)}</p>
                        <DaysLateBadge days={p.daysLate} />
                      </td>
                      <td className="px-2 py-2 tabular-nums">{brl(p.costRealized)} / {brl(p.contractTotal)}</td>
                      <td className="px-2 py-2"><Cpi value={p.cpiLike} /></td>
                      <td className="px-2 py-2"><NcSummary p={p} /></td>
                      <td className="px-2 py-2"><Pending p={p} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Mobile: cards */}
            <ul className="space-y-2 md:hidden" data-testid="portfolio-cards">
              {shown.map((p) => (
                <li key={p.projectId} data-testid="portfolio-card">
                  <button type="button" onClick={() => open(p.projectId)}
                    className="w-full min-w-0 space-y-2 rounded-md border border-border p-3 text-left hover:bg-muted/20">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="break-words text-sm font-semibold">{p.name}</p>
                        <p className="truncate text-[11px] text-muted-foreground">{p.clientName}</p>
                      </div>
                      <DaysLateBadge days={p.daysLate} />
                    </div>
                    <div className="space-y-1">
                      <p className="text-xs tabular-nums">Real {pct(p.actualPercent)} · planejado {pct(p.plannedPercent)}</p>
                      <ProgressBar actual={p.actualPercent} planned={p.plannedPercent} />
                    </div>
                    <dl className="grid grid-cols-2 gap-x-2 gap-y-1 text-xs">
                      <dt className="text-muted-foreground">Entrega prevista</dt><dd>{fmtDate(p.forecastDelivery)}</dd>
                      <dt className="text-muted-foreground">Contrato</dt><dd>{fmtDate(p.expectedEnd)}</dd>
                      <dt className="text-muted-foreground">Realizado</dt><dd className="tabular-nums">{brl(p.costRealized)}</dd>
                      <dt className="text-muted-foreground">Contrato (R$)</dt><dd className="tabular-nums">{brl(p.contractTotal)}</dd>
                      <dt className="text-muted-foreground">CPI≈</dt><dd><Cpi value={p.cpiLike} /></dd>
                      <dt className="text-muted-foreground">NCs abertas</dt><dd><NcSummary p={p} /></dd>
                    </dl>
                    <p className="text-xs"><Pending p={p} /></p>
                    <Extras p={p} />
                  </button>
                </li>
              ))}
            </ul>
          </>
        )}
      </section>
    </div>
  );
}
