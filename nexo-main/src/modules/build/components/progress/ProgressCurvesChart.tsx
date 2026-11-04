import { CartesianGrid, Legend, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts";
import type { BuildProgressCurvesDto } from "../../api/build-progress.api";

const SERIES = [
  { key: "planned",  label: "Planejado", color: "hsl(var(--muted-foreground))", dash: "6 4" },
  { key: "actual",   label: "Real",      color: "hsl(var(--primary))",          dash: undefined },
  { key: "forecast", label: "Previsto",  color: "hsl(var(--warning))", dash: "2 4" },
] as const;

type Row = { date: string; planned?: number; actual?: number; forecast?: number };

/** Merges the three weekly curves into one row per date (a series can miss a date). */
function mergeCurves(curves: BuildProgressCurvesDto): Row[] {
  const rows = new Map<string, Row>();
  for (const { key } of SERIES) {
    for (const p of curves[key]) {
      const row = rows.get(p.date) ?? { date: p.date };
      row[key] = p.percent;
      rows.set(p.date, row);
    }
  }
  return [...rows.values()].sort((a, b) => a.date.localeCompare(b.date));
}

const tick = (iso: string) => `${iso.slice(8, 10)}/${iso.slice(5, 7)}`;

/** Planned / actual / forecast % over time. Lazy-loaded (recharts). */
export default function ProgressCurvesChart({ curves }: { curves: BuildProgressCurvesDto }) {
  const data = mergeCurves(curves);
  return (
    <ResponsiveContainer width="100%" height={260}>
      <LineChart data={data}>
        <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="hsl(var(--border))" />
        <XAxis dataKey="date" tickFormatter={tick} axisLine={false} tickLine={false}
          tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
        <YAxis domain={[0, 100]} unit="%" axisLine={false} tickLine={false} width={44}
          tick={{ fontSize: 11, fill: "hsl(var(--muted-foreground))" }} />
        <Tooltip
          labelFormatter={(d: string) => new Date(`${d}T12:00:00`).toLocaleDateString("pt-BR")}
          formatter={(v: number, name: string) => [`${v.toLocaleString("pt-BR", { maximumFractionDigits: 1 })}%`,
            SERIES.find((s) => s.key === name)?.label ?? name]}
          contentStyle={{ backgroundColor: "hsl(var(--popover))", border: "1px solid hsl(var(--border))", borderRadius: 8, fontSize: 12 }}
        />
        <Legend formatter={(name: string) => SERIES.find((s) => s.key === name)?.label ?? name}
          wrapperStyle={{ fontSize: 12 }} />
        {SERIES.map((s) => (
          <Line key={s.key} type="monotone" dataKey={s.key} stroke={s.color} strokeWidth={2}
            strokeDasharray={s.dash} dot={false} connectNulls isAnimationActive={false} />
        ))}
      </LineChart>
    </ResponsiveContainer>
  );
}
