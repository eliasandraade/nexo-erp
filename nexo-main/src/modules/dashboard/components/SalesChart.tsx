import { useState, useMemo } from "react";
import {
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
} from "recharts";
import { Skeleton } from "@/components/ui/skeleton";
import { useDashboardSummary } from "@/modules/dashboard/hooks/useDashboardSummary";

type Period = "7d" | "30d";

const PERIODS: Period[] = ["7d", "30d"];

export function SalesChart() {
  const [period, setPeriod] = useState<Period>("7d");
  const { data: summary, isLoading } = useDashboardSummary();

  const chartData = useMemo(() => {
    const rows = summary?.salesByDay ?? [];

    const cutoff = period === "7d"
      ? new Date(Date.now() - 7 * 24 * 60 * 60 * 1000).toISOString().split("T")[0]
      : null;

    return rows
      .filter((r) => cutoff === null || r.date >= cutoff)
      .map((r) => {
        const d    = new Date(r.date + "T12:00:00");
        const name = `${String(d.getDate()).padStart(2, "0")}/${String(d.getMonth() + 1).padStart(2, "0")}`;
        return { name, vendas: r.revenue };
      });
  }, [summary?.salesByDay, period]);

  return (
    <div className="bg-card rounded-md border border-border p-5">
      <div className="flex items-center justify-between mb-5">
        <div>
          <h3 className="text-sm font-semibold text-foreground">Vendas</h3>
          <p className="text-xs text-muted-foreground">Receita diária para comparar o ritmo de vendas.</p>
        </div>
        <div className="flex gap-1">
          {PERIODS.map((p) => (
            <button
              key={p}
              aria-pressed={period === p}
              onClick={() => setPeriod(p)}
              className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${
                period === p
                  ? "bg-primary text-primary-foreground"
                  : "text-muted-foreground hover:bg-muted"
              }`}
            >
              {p === "7d" ? "7 dias" : "30 dias"}
            </button>
          ))}
        </div>
      </div>

      {isLoading ? (
        <Skeleton className="h-[260px] w-full" />
      ) : chartData.length === 0 ? (
        <div className="h-[260px] flex flex-col items-center justify-center gap-2">
          <p className="text-sm font-medium text-foreground">Nenhuma venda ainda.</p>
          <p className="text-xs text-muted-foreground">Não há registros no período selecionado.</p>
        </div>
      ) : (
        <ResponsiveContainer width="100%" height={260}>
          <AreaChart data={chartData}>

            <CartesianGrid strokeDasharray="3 3" stroke="hsl(214, 32%, 91%)" vertical={false} />
            <XAxis
              dataKey="name"
              axisLine={false}
              tickLine={false}
              tick={{ fontSize: 11, fill: "hsl(215, 16%, 47%)" }}
            />
            <YAxis
              axisLine={false}
              tickLine={false}
              tick={{ fontSize: 11, fill: "hsl(215, 16%, 47%)" }}
              tickFormatter={(v) =>
                v >= 1000 ? `${(v / 1000).toFixed(0)}k` : String(v)
              }
            />
            <Tooltip
              contentStyle={{
                backgroundColor: "hsl(222, 47%, 11%)",
                border:          "none",
                borderRadius:    8,
                fontSize:        12,
                color:           "#fff",
              }}
              formatter={(value: number) => [
                value.toLocaleString("pt-BR", { style: "currency", currency: "BRL" }),
                "Receita",
              ]}
            />
            <Area
              type="monotone"
              dataKey="vendas"
              stroke="hsl(var(--primary))"
              strokeWidth={2}
              fill="hsl(var(--primary) / .08)"
              isAnimationActive={false}
            />
          </AreaChart>
        </ResponsiveContainer>
      )}
    </div>
  );
}
