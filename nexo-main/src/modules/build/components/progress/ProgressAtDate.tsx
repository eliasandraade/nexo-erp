import { useState } from "react";
import { CalendarClock, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useProjectProgressAt } from "../../hooks/use-build-progress";
import { fmtDate, fmtPercent, localToday } from "../../lib/format";

/** "Como estava em": the latest daily snapshot on or before a date. */
export function ProgressAtDate({ projectId }: { projectId: string }) {
  const [draft, setDraft] = useState("");
  const [date, setDate]   = useState<string | null>(null);
  const { data, isFetching, isError } = useProjectProgressAt(projectId, date);

  return (
    <div className="space-y-3 rounded-md border border-border bg-card p-4">
      <div>
        <h3 className="flex items-center gap-1.5 text-sm font-semibold">
          <CalendarClock className="h-4 w-4 text-muted-foreground" /> Como estava em
        </h3>
        <p className="text-xs text-muted-foreground">Veja o retrato do avanço da obra registrado até uma data.</p>
      </div>

      <form className="flex flex-wrap items-end gap-2"
        onSubmit={(e) => { e.preventDefault(); if (draft) setDate(draft); }}>
        <div className="space-y-1">
          <Label htmlFor="progress-at-date" className="text-xs">Data</Label>
          <Input id="progress-at-date" type="date" value={draft} max={localToday()}
            onChange={(e) => setDraft(e.target.value)} className="w-44 text-sm" />
        </div>
        <Button type="submit" size="sm" variant="outline" disabled={!draft || isFetching}>
          {isFetching && <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" />}
          Consultar
        </Button>
      </form>

      {date && !isFetching && data === null && (
        <p className="text-sm text-muted-foreground">Nenhum registro até esta data.</p>
      )}
      {date && !isFetching && isError && (
        <p className="text-sm text-destructive">Não foi possível consultar o histórico.</p>
      )}
      {date && data && (
        <div className="space-y-2 rounded-md bg-muted/30 p-3">
          <p className="text-xs text-muted-foreground">
            Retrato de {fmtDate(data.date)}
            {data.date !== data.requestedDate && <> (o mais recente até {fmtDate(data.requestedDate)})</>}
          </p>
          <dl className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-4">
            <div><dt className="text-[11px] text-muted-foreground">Real</dt><dd className="font-semibold tabular-nums">{fmtPercent(data.actualPercent)}</dd></div>
            <div><dt className="text-[11px] text-muted-foreground">Planejado</dt><dd className="font-semibold tabular-nums">{fmtPercent(data.plannedPercent)}</dd></div>
            <div><dt className="text-[11px] text-muted-foreground">Previsto</dt><dd className="font-semibold tabular-nums">{fmtPercent(data.forecastPercent)}</dd></div>
            <div><dt className="text-[11px] text-muted-foreground">Entrega prevista</dt><dd className="font-semibold">{fmtDate(data.forecastDeliveryDate)}</dd></div>
          </dl>
          {Array.isArray(data.stages) && data.stages.length > 0 && (
            <ul className="space-y-0.5 text-xs">
              {[...data.stages].sort((a, b) => a.order - b.order).map((s) => (
                <li key={s.stageId} className="flex justify-between gap-2">
                  <span className="truncate">{s.name}</span>
                  <span className="tabular-nums text-muted-foreground">{fmtPercent(s.actualPercent)}</span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
}
