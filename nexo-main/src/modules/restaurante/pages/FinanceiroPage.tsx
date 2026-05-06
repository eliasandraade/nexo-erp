import { useState, useMemo } from "react";
import {
  TrendingUp, TrendingDown, DollarSign, ShoppingBag,
  ArrowUpDown, ArrowUp, ArrowDown, Search,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { PageHeader } from "@/components/shared/PageHeader";
import { useCmvReport, useFinanceiroSummary } from "../hooks/use-financeiro";
import type { CmvReportItemDto } from "../api/financeiro.api";

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function fmtPct(v: number) {
  return `${v.toFixed(1)}%`;
}

function cmvColor(pct: number): string {
  if (pct < 30) return "text-green-600 dark:text-green-400";
  if (pct <= 40) return "text-yellow-600 dark:text-yellow-400";
  return "text-red-600 dark:text-red-400";
}

function cmvBadge(pct: number): string {
  if (pct < 30) return "bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400";
  if (pct <= 40) return "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-400";
  return "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400";
}

// ── Month picker helpers ──────────────────────────────────────────────────────

const MONTHS = [
  "Janeiro","Fevereiro","Março","Abril","Maio","Junho",
  "Julho","Agosto","Setembro","Outubro","Novembro","Dezembro",
];

function monthBounds(year: number, month: number): { from: string; to: string } {
  const pad   = (n: number) => String(n).padStart(2, "0");
  const last  = new Date(year, month, 0).getDate();
  return {
    from: `${year}-${pad(month)}-01`,
    to:   `${year}-${pad(month)}-${pad(last)}`,
  };
}

// ── KPI Card ──────────────────────────────────────────────────────────────────

interface KpiCardProps {
  icon:    React.ElementType;
  label:   string;
  value:   string;
  sub?:    string;
  color?:  string;
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

// ── CMV Table ─────────────────────────────────────────────────────────────────

type SortField = "productName" | "salePrice" | "unitCost" | "cmvPercent" | "margin";
type SortDir   = "asc" | "desc";

interface CmvTableProps {
  items: CmvReportItemDto[];
}

function CmvTable({ items }: CmvTableProps) {
  const [search,  setSearch]  = useState("");
  const [sortBy,  setSortBy]  = useState<SortField>("cmvPercent");
  const [sortDir, setSortDir] = useState<SortDir>("desc");

  const toggleSort = (field: SortField) => {
    if (sortBy === field) {
      setSortDir(d => d === "asc" ? "desc" : "asc");
    } else {
      setSortBy(field);
      setSortDir("desc");
    }
  };

  const SortIcon = ({ field }: { field: SortField }) => {
    if (sortBy !== field) return <ArrowUpDown className="h-3 w-3 text-muted-foreground" />;
    return sortDir === "asc"
      ? <ArrowUp className="h-3 w-3" />
      : <ArrowDown className="h-3 w-3" />;
  };

  const filtered = useMemo(() => {
    const q = search.toLowerCase();
    return items.filter(i =>
      i.productName.toLowerCase().includes(q) ||
      i.productCode.toLowerCase().includes(q),
    );
  }, [items, search]);

  const sorted = useMemo(() => {
    return [...filtered].sort((a, b) => {
      const dir = sortDir === "asc" ? 1 : -1;
      if (sortBy === "productName") return dir * a.productName.localeCompare(b.productName);
      return dir * (a[sortBy] - b[sortBy]);
    });
  }, [filtered, sortBy, sortDir]);

  const thClass = "text-left text-xs font-medium text-muted-foreground uppercase tracking-wide px-3 py-2";
  const tdClass = "px-3 py-3 text-sm";

  return (
    <div className="space-y-3">
      <div className="relative">
        <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
        <Input
          className="pl-9 text-sm"
          placeholder="Filtrar por nome ou código…"
          value={search}
          onChange={e => setSearch(e.target.value)}
        />
      </div>

      {sorted.length === 0 ? (
        <div className="text-center py-12 text-sm text-muted-foreground">
          Nenhum prato encontrado. Crie fichas técnicas para seus produtos do cardápio.
        </div>
      ) : (
        <div className="rounded-xl border border-border overflow-hidden">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[640px]">
              <thead className="bg-muted/40 border-b border-border">
                <tr>
                  <th className={thClass}>
                    <button
                      className="flex items-center gap-1 hover:text-foreground transition-colors"
                      onClick={() => toggleSort("productName")}
                    >
                      Prato <SortIcon field="productName" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button
                      className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("salePrice")}
                    >
                      Preço venda <SortIcon field="salePrice" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button
                      className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("unitCost")}
                    >
                      Custo unitário <SortIcon field="unitCost" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button
                      className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("margin")}
                    >
                      Margem <SortIcon field="margin" />
                    </button>
                  </th>
                  <th className={cn(thClass, "text-right")}>
                    <button
                      className="flex items-center gap-1 ml-auto hover:text-foreground transition-colors"
                      onClick={() => toggleSort("cmvPercent")}
                    >
                      CMV% <SortIcon field="cmvPercent" />
                    </button>
                  </th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {sorted.map(item => (
                  <tr key={item.productId} className="hover:bg-muted/20 transition-colors">
                    <td className={tdClass}>
                      <p className="font-medium">{item.productName}</p>
                      <p className="text-xs text-muted-foreground">{item.productCode}</p>
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      {fmt(item.salePrice)}
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      <span>{fmt(item.unitCost)}</span>
                      {(item.gasCost > 0 || item.laborCost > 0) && (
                        <p className="text-xs text-muted-foreground">
                          ing: {fmt(item.unitIngredientCost)}
                        </p>
                      )}
                    </td>
                    <td className={cn(tdClass, "text-right tabular-nums")}>
                      <p>{fmt(item.margin)}</p>
                      <p className="text-xs text-muted-foreground">{fmtPct(item.marginPercent)}</p>
                    </td>
                    <td className={cn(tdClass, "text-right")}>
                      <span className={cn(
                        "inline-block px-2 py-0.5 rounded-full text-xs font-semibold tabular-nums",
                        cmvBadge(item.cmvPercent),
                      )}>
                        {fmtPct(item.cmvPercent)}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      <p className="text-xs text-muted-foreground">
        {sorted.length} de {items.length} prato{items.length !== 1 ? "s" : ""} ·
        CMV verde &lt;30% · amarelo 30–40% · vermelho &gt;40%
      </p>
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function FinanceiroPage() {
  const now = new Date();
  const [month, setMonth] = useState(now.getMonth() + 1);
  const [year,  setYear]  = useState(now.getFullYear());

  const { from, to } = monthBounds(year, month);

  const { data: cmvData,  isLoading: cmvLoading  } = useCmvReport();
  const { data: summary,  isLoading: sumLoading  } = useFinanceiroSummary(from, to);

  const isLoading = cmvLoading || sumLoading;

  return (
    <div className="p-6 space-y-6">
      <PageHeader
        title="Financeiro"
        description="CMV por prato e KPIs do período selecionado."
      />

      {/* ── Period picker ──────────────────────────────────────────────── */}
      <div className="flex items-center gap-3 flex-wrap">
        <Select value={String(month)} onValueChange={v => setMonth(Number(v))}>
          <SelectTrigger className="w-36 text-sm">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {MONTHS.map((m, i) => (
              <SelectItem key={i + 1} value={String(i + 1)}>{m}</SelectItem>
            ))}
          </SelectContent>
        </Select>

        <div className="flex items-center gap-2">
          <Button
            variant="outline" size="icon" className="h-9 w-9 text-sm"
            onClick={() => setYear(y => y - 1)}
          >
            ‹
          </Button>
          <span className="tabular-nums text-sm font-medium w-12 text-center">{year}</span>
          <Button
            variant="outline" size="icon" className="h-9 w-9 text-sm"
            onClick={() => setYear(y => y + 1)}
            disabled={year >= now.getFullYear()}
          >
            ›
          </Button>
        </div>

        <span className="text-xs text-muted-foreground">
          {from} → {to}
        </span>
      </div>

      {/* ── KPI Cards ──────────────────────────────────────────────────── */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <KpiCard
          icon={DollarSign}
          label="Faturamento bruto"
          value={isLoading ? "—" : fmt(summary?.revenue ?? 0)}
          sub={isLoading ? undefined : `${summary?.ordersCount ?? 0} comanda(s)`}
          color="text-blue-600"
        />
        <KpiCard
          icon={ShoppingBag}
          label="Custo de mercadoria"
          value={isLoading ? "—" : fmt(summary?.totalCostOfGoodsSold ?? 0)}
          sub="CMG do período"
          color="text-orange-600"
        />
        <KpiCard
          icon={TrendingUp}
          label="CMV% ponderado"
          value={isLoading ? "—" : fmtPct(summary?.weightedCmvPercent ?? 0)}
          sub="Baseado nos pedidos"
          color={isLoading ? "text-muted-foreground"
            : cmvColor(summary?.weightedCmvPercent ?? 0)}
        />
        <KpiCard
          icon={TrendingDown}
          label="Margem bruta"
          value={isLoading ? "—" : fmt(summary?.grossMargin ?? 0)}
          sub={isLoading || !summary?.revenue ? undefined
            : fmtPct(100 - (summary.totalCostOfGoodsSold / summary.revenue) * 100)}
          color="text-green-600"
        />
      </div>

      {/* ── CMV Table ──────────────────────────────────────────────────── */}
      <div className="space-y-2">
        <div>
          <h2 className="text-sm font-semibold">CMV por prato</h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Custo atual baseado nas fichas técnicas. Ordene por coluna clicando no cabeçalho.
          </p>
        </div>
        {cmvLoading ? (
          <div className="space-y-2">
            {[1, 2, 3].map(i => <div key={i} className="h-14 rounded-xl bg-muted animate-pulse" />)}
          </div>
        ) : (
          <CmvTable items={cmvData?.items ?? []} />
        )}
      </div>
    </div>
  );
}
