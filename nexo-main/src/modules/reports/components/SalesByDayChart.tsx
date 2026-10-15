import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import { formatCurrency } from "@/lib/formatters";
import type { SalesReportDayDto } from "../api/reports.api";
import { fmtDay } from "../lib/period";

/** Net revenue per day of the period (every day, sold or not). */
export default function SalesByDayChart({ days }: Readonly<{ days: SalesReportDayDto[] }>) {
  const data = days.map((d) => ({ name: fmtDay(d.date), receita: d.revenue, vendas: d.sales }));
  // overflow-hidden: the (hidden) tooltip sits beside the chart and would widen a phone screen
  return (
    <div className="w-full overflow-hidden">
    <ResponsiveContainer width="100%" height={260}>
      <BarChart data={data}>
        <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" vertical={false} />
        <XAxis dataKey="name" axisLine={false} tickLine={false} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} minTickGap={12} />
        <YAxis
          axisLine={false}
          tickLine={false}
          tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
          tickFormatter={(v: number) => (v >= 1000 ? `${(v / 1000).toFixed(0)}k` : String(v))}
        />
        <Tooltip
          cursor={{ fill: "hsl(var(--muted))" }}
          contentStyle={{ backgroundColor: "hsl(var(--popover))", border: "1px solid hsl(var(--border))", borderRadius: 8, fontSize: 12 }}
          formatter={(value: number, key: string) => (key === "receita" ? [formatCurrency(value), "Faturamento"] : [value, "Vendas"])}
        />
        <Bar dataKey="receita" fill="hsl(var(--primary))" radius={[3, 3, 0, 0]} isAnimationActive={false} />
      </BarChart>
    </ResponsiveContainer>
    </div>
  );
}
