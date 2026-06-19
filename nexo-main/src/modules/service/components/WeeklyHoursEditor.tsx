import { Copy, Plus, X } from "lucide-react";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { type DayHours, WEEKDAY_LABEL, dayError } from "../lib/working-hours";

interface WeeklyHoursEditorProps {
  value:    DayHours[];
  onChange: (next: DayHours[]) => void;
}

/** Friendly weekly editor — never exposes raw JSON (see lib/working-hours for the (de)serialization). */
export function WeeklyHoursEditor({ value, onChange }: WeeklyHoursEditorProps) {
  function update(weekday: number, patch: Partial<DayHours>) {
    onChange(value.map((d) => (d.weekday === weekday ? { ...d, ...patch } : d)));
  }

  function replicate(src: DayHours) {
    onChange(value.map((d) => ({
      ...d,
      enabled: true,
      start: src.start, end: src.end,
      hasBreak: src.hasBreak, breakStart: src.breakStart, breakEnd: src.breakEnd,
    })));
  }

  return (
    <div className="space-y-2">
      {value.map((day) => {
        const err = dayError(day);
        return (
          <div
            key={day.weekday}
            className={cn(
              "rounded-lg border px-3 py-2.5",
              day.enabled ? "border-border bg-card" : "border-border/50 bg-muted/20",
            )}
          >
            <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
              <label className="flex w-24 shrink-0 cursor-pointer select-none items-center gap-2">
                <Checkbox
                  checked={day.enabled}
                  onCheckedChange={(v) => update(day.weekday, { enabled: !!v })}
                />
                <span className="text-sm font-medium">{WEEKDAY_LABEL[day.weekday]}</span>
              </label>

              {day.enabled ? (
                <div className="flex flex-1 flex-wrap items-center gap-2">
                  <TimeRange
                    start={day.start} end={day.end}
                    onStart={(v) => update(day.weekday, { start: v })}
                    onEnd={(v) => update(day.weekday, { end: v })}
                  />

                  {day.hasBreak ? (
                    <div className="flex items-center gap-1.5">
                      <span className="text-xs text-muted-foreground">pausa</span>
                      <TimeRange
                        start={day.breakStart} end={day.breakEnd}
                        onStart={(v) => update(day.weekday, { breakStart: v })}
                        onEnd={(v) => update(day.weekday, { breakEnd: v })}
                      />
                      <button
                        type="button"
                        onClick={() => update(day.weekday, { hasBreak: false })}
                        title="Remover pausa"
                        className="text-muted-foreground hover:text-destructive"
                      >
                        <X className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  ) : (
                    <button
                      type="button"
                      onClick={() => update(day.weekday, { hasBreak: true })}
                      className="inline-flex items-center gap-1 text-xs text-primary hover:underline"
                    >
                      <Plus className="h-3 w-3" /> pausa
                    </button>
                  )}

                  <button
                    type="button"
                    onClick={() => replicate(day)}
                    title="Aplicar este horário a todos os dias"
                    className="ml-auto inline-flex items-center gap-1 text-[11px] text-muted-foreground hover:text-foreground"
                  >
                    <Copy className="h-3 w-3" /> todos os dias
                  </button>
                </div>
              ) : (
                <span className="text-xs text-muted-foreground">Fechado</span>
              )}
            </div>

            {err && <p className="mt-1.5 text-xs text-destructive">{err}</p>}
          </div>
        );
      })}
    </div>
  );
}

function TimeRange({ start, end, onStart, onEnd }: {
  start: string; end: string; onStart: (v: string) => void; onEnd: (v: string) => void;
}) {
  return (
    <div className="flex items-center gap-1.5">
      <Input type="time" value={start} onChange={(e) => onStart(e.target.value)} className="h-8 w-[6.5rem]" />
      <span className="text-xs text-muted-foreground">às</span>
      <Input type="time" value={end} onChange={(e) => onEnd(e.target.value)} className="h-8 w-[6.5rem]" />
    </div>
  );
}
