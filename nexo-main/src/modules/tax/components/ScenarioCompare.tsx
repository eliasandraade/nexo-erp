import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency } from "@/lib/formatters";
import { cn } from "@/lib/utils";
import type { ScenarioResult } from "../api/tax.api";
import { EngineResult, TechnicalTrail } from "./EngineResult";
import { ToneBadge } from "./TaxBadges";

export function Delta({ value, invert = false }: Readonly<{ value: number; invert?: boolean }>) {
  if (Math.abs(value) < 0.005) return <span className="text-muted-foreground">sem mudança</span>;
  // for costs, lower is better: a negative delta is good
  const good = invert ? value > 0 : value < 0;
  return <span className={cn("tabular-nums", good ? "text-success" : "text-destructive")}>{value > 0 ? "+" : "−"}{formatCurrency(Math.abs(value))}</span>;
}

/** Side by side: what each scenario charges, its tax, the credit it may give and the effective cost. */
export function ScenarioCompare({ scenarios }: Readonly<{ scenarios: ScenarioResult[] }>) {
  return (
    <div className="space-y-4">
      <div className="overflow-x-auto rounded-md border border-border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Cenário</TableHead>
              <TableHead className="text-right">Valor cobrado</TableHead>
              <TableHead className="text-right">Imposto</TableHead>
              <TableHead className="text-right">Crédito estimado</TableHead>
              <TableHead className="text-right">Custo efetivo</TableHead>
              <TableHead className="text-right">Quanto muda</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {scenarios.map((s, i) => (
              <TableRow key={`${i}-${s.name}`}>
                <TableCell className="min-w-40">
                  <span className="font-medium">{s.name}</span>
                  {s.isBaseline && <ToneBadge label="atual" tone="neutral" className="ml-2" />}
                  {s.noCashEffectInPeriod && <p className="text-xs text-muted-foreground">Sem efeito de caixa no período.</p>}
                </TableCell>
                <TableCell className="text-right tabular-nums">{formatCurrency(s.charged)}</TableCell>
                <TableCell className="text-right tabular-nums">{formatCurrency(s.taxTotal)}</TableCell>
                <TableCell className="text-right tabular-nums">{formatCurrency(s.creditEstimate)}</TableCell>
                <TableCell className="text-right tabular-nums font-medium">{formatCurrency(s.effectiveCost)}</TableCell>
                <TableCell className="text-right text-sm">{s.isBaseline ? "—" : <Delta value={s.effectiveCostDeltaVsBaseline} />}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>

      {scenarios.map((s, i) => (
        <details key={`${i}-${s.name}`} className="rounded-md border border-border">
          <summary className="cursor-pointer px-3 py-2 text-sm font-medium">Detalhe: {s.name}</summary>
          <div className="space-y-3 border-t border-border p-3">
            {s.hypotheses.length > 0 && (
              <div className="rounded-md border border-warning/40 bg-warning/5 p-2 text-sm">
                <p className="font-medium text-warning">Hipóteses deste cenário</p>
                <ul className="list-disc pl-5">{s.hypotheses.map((h) => <li key={h}>{h}</li>)}</ul>
              </div>
            )}
            {!s.isBaseline && <p className="text-sm">Imposto em relação ao atual: <Delta value={s.taxDeltaVsBaseline} /></p>}
            <EngineResult output={s.output} input={s.input} />
            <TechnicalTrail output={s.output} />
          </div>
        </details>
      ))}
    </div>
  );
}
