import { useEffect, useState } from "react";
import { CalendarX2, Loader2 } from "lucide-react";
import type { PublicAvailabilitySlot } from "../api/booking.api";
import { groupSlotsByDay } from "../lib/booking-format";
import { cn } from "@/lib/utils";
import { Surface, Reveal, Muted } from "./PortalPrimitives";

interface SchedulerProps {
  slots:        PublicAvailabilitySlot[];
  loading:      boolean;
  isError:      boolean;
  selectedStart: string | null;
  onSelect:     (startsAt: string) => void;
}

export function Scheduler({ slots, loading, isError, selectedStart, onSelect }: SchedulerProps) {
  const days = groupSlotsByDay(slots);
  const [activeDay, setActiveDay] = useState<string | null>(days[0]?.dayKey ?? null);

  useEffect(() => {
    if (days.length && !days.some((d) => d.dayKey === activeDay)) setActiveDay(days[0].dayKey);
  }, [days, activeDay]);

  if (loading) {
    return <Surface className="flex items-center justify-center p-10"><Loader2 className="h-6 w-6 animate-spin" style={{ color: "var(--p-muted)" }} /></Surface>;
  }
  if (isError) {
    return <Surface className="p-8 text-center"><Muted className="text-sm">Não foi possível carregar os horários. Tente novamente.</Muted></Surface>;
  }
  if (days.length === 0) {
    return (
      <Surface className="flex flex-col items-center gap-2 p-10 text-center">
        <CalendarX2 className="h-8 w-8" style={{ color: "var(--p-muted)" }} />
        <p className="text-sm font-semibold">Sem horários disponíveis</p>
        <Muted className="text-[13px]">Não há horários abertos para os próximos dias.</Muted>
      </Surface>
    );
  }

  const current = days.find((d) => d.dayKey === activeDay) ?? days[0];

  return (
    <div className="flex flex-col gap-4">
      {/* Day strip */}
      <div className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
        {days.map((d) => {
          const [wd, ...rest] = d.dateLabel.split(",");
          const active = d.dayKey === current.dayKey;
          return (
            <button key={d.dayKey} onClick={() => setActiveDay(d.dayKey)}
              className={cn("flex shrink-0 flex-col items-center rounded-[calc(var(--p-radius)*0.7)] px-3.5 py-2 transition-colors")}
              style={active
                ? { background: "var(--p-accent)", color: "var(--p-accent-ink)" }
                : { background: "var(--p-surface)", color: "var(--p-ink)", border: "1px solid var(--p-line)" }}>
              <span className="text-[11px] font-medium uppercase tracking-wide opacity-80">{wd}</span>
              <span className="text-[13px] font-semibold capitalize">{rest.join(",").trim()}</span>
            </button>
          );
        })}
      </div>

      {/* Time chips */}
      <Reveal key={current.dayKey}>
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
          {current.slots.map((s) => {
            const active = s.startsAt === selectedStart;
            return (
              <button key={s.startsAt} onClick={() => onSelect(s.startsAt)}
                className="rounded-[calc(var(--p-radius)*0.55)] py-2.5 text-sm font-semibold tabular-nums transition-all duration-150 active:scale-95"
                style={active
                  ? { background: "var(--p-accent)", color: "var(--p-accent-ink)" }
                  : { background: "var(--p-surface)", color: "var(--p-ink)", border: "1px solid var(--p-line)" }}>
                {s.timeLabel}
              </button>
            );
          })}
        </div>
      </Reveal>
    </div>
  );
}
