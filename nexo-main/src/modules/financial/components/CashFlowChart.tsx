import { Bar, CartesianGrid, ComposedChart, Line, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { CashFlowBucketDto } from "../api/financial.api";

const brl = (v: number) => v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
const label = (b: CashFlowBucketDto) =>
  b.start === b.end ? `${b.start.slice(8, 10)}/${b.start.slice(5, 7)}` : `${b.start.slice(8, 10)}/${b.start.slice(5, 7)}–${b.end.slice(8, 10)}/${b.end.slice(5, 7)}`;

/** Entradas e saídas (realizado + previsto) por período, com a linha do saldo. Lazy-loaded (recharts). */
export default function CashFlowChart({ buckets }: { buckets: CashFlowBucketDto[] }) {
  const data = buckets.map((b) => ({
    name: label(b),
    entradas: b.realizedIn + b.projectedIn,
    saidas: -(b.realizedOut + b.projectedOut),
    saldo: b.balance,
  }));

  return (
    <ResponsiveContainer width="100%" height={280}>
      <ComposedChart data={data} stackOffset="sign">
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--border))" />
        <XAxis dataKey="name" axisLine={false} tickLine={false} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
        <YAxis axisLine={false} tickLine={false} tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }}
          tickFormatter={(v: number) => (Math.abs(v) >= 1000 ? `${(v / 1000).toFixed(0)}k` : String(v))} />
        <Tooltip
          contentStyle={{ backgroundColor: "hsl(var(--popover))", border: "1px solid hsl(var(--border))", borderRadius: 8, fontSize: 12 }}
          formatter={(value: number, name: string) => [brl(Math.abs(value)), name === "saidas" ? "Saídas" : name === "entradas" ? "Entradas" : "Saldo"]}
        />
        <Bar dataKey="entradas" stackId="flow" fill="hsl(var(--success))" isAnimationActive={false} />
        <Bar dataKey="saidas" stackId="flow" fill="hsl(var(--destructive))" isAnimationActive={false} />
        <Line type="monotone" dataKey="saldo" stroke="hsl(var(--primary))" strokeWidth={2} dot={false} isAnimationActive={false} />
      </ComposedChart>
    </ResponsiveContainer>
  );
}
