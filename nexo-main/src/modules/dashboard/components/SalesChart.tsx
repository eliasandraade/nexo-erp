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
import { useQuery } from "@tanstack/react-query";
import { Skeleton } from "@/components/ui/skeleton";
import { posService } from "@/modules/sales/services/posService";

type Period = "7d" | "30d" | "Todos";

const PERIODS: Period[] = ["7d", "30d", "Todos"];

function getDaysCutoff(period: Period): number {
  if (period === "7d") return 7;
  if (period === "30d") return 30;
  return 365;
}

export function SalesChart() {
  const [period, setPeriod] = useState<Period>("7d");

  const { data: sales = [], isLoading } = useQuery({
    queryKey: ["dashboard-sales-chart"],
    queryFn: () => posService.getRecentSales(),
  });

  const chartData = useMemo(() => {
    const cutoff = Date.now() - getDaysCutoff(period) * 24 * 60 * 60 * 1000;

    const byDate = new Map<string, number>();
    for (const sale of sales) {
      if (sale.status === "cancelled") continue;
      const ts = new Date(sale.timestamp).getTime();
      if (ts < cutoff) continue;
      // Key as YYYY-MM-DD for deterministic sort
      const isoDate = new Date(sale.timestamp).toISOString().split("T")[0];
      byDate.set(isoDate, (byDate.get(isoDate) ?? 0) + sale.total);
    }

    return Array.from(byDate.entries())
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([isoDate, vendas]) => {
        const d = new Date(isoDate + "T12:00:00");
        const name = `${String(d.getDate()).padStart(2, "0")}/${String(d.getMonth() + 1).padStart(2, "0")}`;
        return { name, vendas: Math.round(vendas * 100) / 100 };
      });
  }, [sales, period]);

  return (
    <div
      className="bg-card rounded-lg border border-border p-5 shadow-sm animate-fade-in"
      style={{ animationDelay: "300ms" }}
    >
      <div className="flex items-center justify-between mb-5">
        <div>
          <h3 className="text-sm font-semibold text-foreground">Vendas</h3>
          <p className="text-xs text-muted-foreground">Receita por data</p>
        </div>
        <div className="flex gap-1">
          {PERIODS.map((p) => (
            <button
              key={p}
              onClick={() => setPeriod(p)}
              className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${
                period === p
                  ? "bg-primary text-primary-foreground"
                  : "text-muted-foreground hover:bg-muted"
              }`}
            >
              {p}
            </button>
          ))}
        </div>
      </div>

      {isLoading ? (
        <Skeleton className="h-[260px] w-full" />
      ) : chartData.length === 0 ? (
        <div className="h-[260px] flex flex-col items-center justify-center gap-2">
          <p className="text-sm font-medium text-foreground">Nenhuma venda ainda.</p>
          <p className="text-xs text-muted-foreground">Abra o caixa e registre a primeira venda pelo PDV.</p>
        </div>
      ) : (
        <ResponsiveContainer width="100%" height={260}>
          <AreaChart data={chartData}>
            <defs>
              <linearGradient id="salesGradient" x1="0" y1="0" x2="0" y2="1">
                <stop offset="5%" stopColor="hsl(217, 91%, 60%)" stopOpacity={0.2} />
                <stop offset="95%" stopColor="hsl(217, 91%, 60%)" stopOpacity={0} />
              </linearGradient>
            </defs>
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
                border: "none",
                borderRadius: 8,
                fontSize: 12,
                color: "#fff",
              }}
              formatter={(value: number) => [
                value.toLocaleString("pt-BR", { style: "currency", currency: "BRL" }),
                "Receita",
              ]}
            />
            <Area
              type="monotone"
              dataKey="vendas"
              stroke="hsl(217, 91%, 60%)"
              strokeWidth={2}
              fill="url(#salesGradient)"
            />
          </AreaChart>
        </ResponsiveContainer>
      )}
    </div>
  );
}
