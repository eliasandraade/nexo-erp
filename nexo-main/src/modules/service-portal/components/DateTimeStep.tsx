import { CalendarX2, Loader2 } from "lucide-react";
import type { PublicAvailability } from "../api/booking.api";
import { groupSlotsByDay } from "../lib/booking-format";

interface DateTimeStepProps {
  availability: PublicAvailability | undefined;
  loading:      boolean;
  isError:      boolean;
  onSelect:     (startsAt: string) => void;
}

export function DateTimeStep({ availability, loading, isError, onSelect }: DateTimeStepProps) {
  const days = availability ? groupSlotsByDay(availability.slots) : [];
  const empty = !loading && !isError && days.length === 0;

  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold">Escolha data e horário</h2>

      {loading && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {isError && (
        <p className="rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-muted-foreground">
          Não foi possível carregar os horários. Tente novamente em instantes.
        </p>
      )}

      {empty && (
        <div className="flex flex-col items-center gap-2 rounded-xl border border-dashed border-border px-4 py-10 text-center">
          <CalendarX2 className="h-8 w-8 text-muted-foreground" />
          <p className="text-sm font-medium">Sem horários disponíveis</p>
          <p className="text-xs text-muted-foreground">
            Este profissional não tem horários abertos para os próximos dias.
          </p>
        </div>
      )}

      <div className="flex flex-col gap-5">
        {days.map((day) => (
          <div key={day.dayKey}>
            <p className="mb-2 text-sm font-semibold capitalize">{day.dateLabel}</p>
            <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
              {day.slots.map((slot) => (
                <button
                  key={slot.startsAt}
                  onClick={() => onSelect(slot.startsAt)}
                  className="rounded-lg border border-border bg-card py-2.5 text-sm font-medium tabular-nums transition-colors hover:border-primary hover:bg-primary/10"
                >
                  {slot.timeLabel}
                </button>
              ))}
            </div>
          </div>
        ))}
      </div>
    </section>
  );
}
