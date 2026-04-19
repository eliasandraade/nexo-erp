import { useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { ArrowLeft, ShoppingBag, CircleDollarSign, Receipt, Clock } from "lucide-react";
import { cn } from "@/lib/utils";
import { fetchRestauranteSummary } from "../api/restaurante.api";
import type { RestauranteSummaryDto } from "../api/restaurante.api";

// ─── Period presets ───────────────────────────────────────────────────────────

type Preset = "today" | "7d" | "30d" | "custom";

const PRESETS: { key: Preset; label: string }[] = [
  { key: "today", label: "Hoje" },
  { key: "7d",    label: "7 dias" },
  { key: "30d",   label: "30 dias" },
  { key: "custom", label: "Personalizado" },
];

function isoDate(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function presetDates(preset: Preset): { from: string; to: string } {
  const today = new Date();
  const to    = isoDate(today);
  if (preset === "today") return { from: to, to };
  if (preset === "7d")    return { from: isoDate(new Date(today.getTime() - 6 * 86400_000)), to };
  if (preset === "30d")   return { from: isoDate(new Date(today.getTime() - 29 * 86400_000)), to };
  return { from: to, to }; // custom — caller overrides
}

function formatMinutes(mins: number): string {
  if (mins < 1)  return "< 1 min";
  if (mins < 60) return `${Math.round(mins)} min`;
  const h = Math.floor(mins / 60);
  const m = Math.round(mins % 60);
  return m > 0 ? `${h}h ${m}min` : `${h}h`;
}

// ─── KPI card ─────────────────────────────────────────────────────────────────

interface KpiCardProps {
  icon: React.ElementType;
  label: string;
  value: string;
  sub?: string;
  color?: string;
}

function KpiCard({ icon: Icon, label, value, sub, color = "text-primary" }: KpiCardProps) {
  return (
    <div className="rounded-xl border border-border bg-card p-4 flex flex-col gap-2">
      <div className="flex items-center gap-2">
        <div className={cn("p-2 rounded-lg bg-muted/60", color)}>
          <Icon className="h-4 w-4" />
        </div>
        <span className="text-xs font-medium text-muted-foreground">{label}</span>
      </div>
      <p className="text-2xl font-bold text-foreground tabular-nums leading-none">{value}</p>
      {sub && <p className="text-xs text-muted-foreground">{sub}</p>}
    </div>
  );
}

// ─── Main page ────────────────────────────────────────────────────────────────

export default function RelatoriosPage() {
  const navigate = useNavigate();

  const [preset,    setPreset]    = useState<Preset>("30d");
  const [customFrom, setCustomFrom] = useState(isoDate(new Date(Date.now() - 29 * 86400_000)));
  const [customTo,   setCustomTo]   = useState(isoDate(new Date()));

  const { from, to } = useMemo(() => {
    if (preset === "custom") return { from: customFrom, to: customTo };
    return presetDates(preset);
  }, [preset, customFrom, customTo]);

  const { data, isLoading, isError } = useQuery<RestauranteSummaryDto>({
    queryKey: ["restaurante-summary", from, to],
    queryFn:  () => fetchRestauranteSummary(from, to),
    staleTime: 2 * 60_000,
  });

  // ─────────────────────────────────────────────────────────────────────────
  return (
    <div className="flex flex-col h-screen overflow-hidden">
      {/* Header */}
      <div className="px-4 pt-5 pb-3 flex items-center gap-3 border-b border-border shrink-0">
        <button onClick={() => navigate("/restaurante")} className="p-1 text-muted-foreground hover:text-foreground transition-colors">
          <ArrowLeft className="h-5 w-5" />
        </button>
        <div>
          <h1 className="text-base font-semibold">Relatórios</h1>
          <p className="text-xs text-muted-foreground">Resumo operacional do restaurante</p>
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-4 py-4 space-y-4">

        {/* ── Period selector ──────────────────────────────────────────────── */}
        <div className="space-y-2">
          <div className="flex gap-1.5 flex-wrap">
            {PRESETS.map(p => (
              <button
                key={p.key}
                onClick={() => setPreset(p.key)}
                className={cn(
                  "px-3.5 py-1.5 rounded-full text-sm font-medium transition-colors",
                  preset === p.key
                    ? "bg-primary text-primary-foreground"
                    : "bg-muted text-muted-foreground hover:text-foreground"
                )}
              >
                {p.label}
              </button>
            ))}
          </div>

          {preset === "custom" && (
            <div className="flex items-center gap-2">
              <input
                type="date"
                value={customFrom}
                onChange={e => setCustomFrom(e.target.value)}
                className="h-9 rounded-lg border border-border bg-card px-3 text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
              />
              <span className="text-muted-foreground text-sm">até</span>
              <input
                type="date"
                value={customTo}
                onChange={e => setCustomTo(e.target.value)}
                className="h-9 rounded-lg border border-border bg-card px-3 text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
              />
            </div>
          )}

          {/* Period label */}
          <p className="text-xs text-muted-foreground">
            {from === to
              ? new Date(from + "T12:00:00").toLocaleDateString("pt-BR", { day: "2-digit", month: "long", year: "numeric" })
              : `${new Date(from + "T12:00:00").toLocaleDateString("pt-BR", { day: "2-digit", month: "short" })} — ${new Date(to + "T12:00:00").toLocaleDateString("pt-BR", { day: "2-digit", month: "short", year: "numeric" })}`
            }
          </p>
        </div>

        {/* ── KPI cards ────────────────────────────────────────────────────── */}
        {isLoading ? (
          <div className="grid grid-cols-2 gap-3">
            {[1, 2, 3, 4].map(i => (
              <div key={i} className="rounded-xl border border-border bg-card p-4 h-24 animate-pulse bg-muted/30" />
            ))}
          </div>
        ) : isError ? (
          <div className="rounded-xl border border-destructive/20 bg-destructive/5 p-6 text-center">
            <p className="text-sm text-destructive">Erro ao carregar relatório.</p>
            <p className="text-xs text-muted-foreground mt-1">Verifique a conexão e tente novamente.</p>
          </div>
        ) : data ? (
          <>
            <div className="grid grid-cols-2 gap-3">
              <KpiCard
                icon={ShoppingBag}
                label="Comandas fechadas"
                value={data.ordersCount.toString()}
                sub="no período"
                color="text-blue-500"
              />
              <KpiCard
                icon={CircleDollarSign}
                label="Faturamento"
                value={`R$ ${data.revenue.toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`}
                sub="total pago"
                color="text-green-500"
              />
              <KpiCard
                icon={Receipt}
                label="Ticket médio"
                value={data.ordersCount > 0 ? `R$ ${data.averageTicket.toLocaleString("pt-BR", { minimumFractionDigits: 2 })}` : "—"}
                sub="por comanda"
                color="text-orange-500"
              />
              <KpiCard
                icon={Clock}
                label="Tempo médio de mesa"
                value={data.ordersCount > 0 ? formatMinutes(data.averageTableMinutes) : "—"}
                sub="da abertura ao fechamento"
                color="text-purple-500"
              />
            </div>

            {/* Zero-state note */}
            {data.ordersCount === 0 && (
              <p className="text-center text-sm text-muted-foreground pt-2">
                Nenhuma comanda fechada neste período.
              </p>
            )}
          </>
        ) : null}
      </div>
    </div>
  );
}
