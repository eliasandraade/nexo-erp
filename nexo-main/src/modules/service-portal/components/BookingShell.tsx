import type { ReactNode } from "react";
import { ArrowLeft, CalendarCheck } from "lucide-react";
import { cn } from "@/lib/utils";

interface BookingShellProps {
  storeName:  string;
  ramo:       string;
  /** 1-based step index (1..totalSteps), or null to hide the progress bar (success screen). */
  stepIndex:  number | null;
  totalSteps: number;
  canBack:    boolean;
  onBack:     () => void;
  children:   ReactNode;
}

export function BookingShell({
  storeName, ramo, stepIndex, totalSteps, canBack, onBack, children,
}: BookingShellProps) {
  return (
    <div className="min-h-screen bg-background text-foreground flex flex-col">
      {/* Header */}
      <header className="border-b border-border bg-card/40">
        <div className="mx-auto w-full max-w-md px-4 py-4">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/15 text-primary">
              <CalendarCheck className="h-5 w-5" />
            </div>
            <div className="min-w-0">
              <h1 className="truncate text-base font-bold leading-tight">{storeName}</h1>
              <p className="truncate text-xs text-muted-foreground">{ramo} · Agendamento online</p>
            </div>
          </div>

          {stepIndex !== null && (
            <div className="mt-4 flex items-center gap-3">
              {canBack ? (
                <button
                  onClick={onBack}
                  className="flex items-center gap-1 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground"
                >
                  <ArrowLeft className="h-3.5 w-3.5" /> Voltar
                </button>
              ) : (
                <span className="text-xs text-muted-foreground">Etapa {stepIndex} de {totalSteps}</span>
              )}
              <div className="flex flex-1 items-center gap-1">
                {Array.from({ length: totalSteps }, (_, i) => (
                  <div
                    key={i}
                    className={cn(
                      "h-1 flex-1 rounded-full transition-colors",
                      i < stepIndex ? "bg-primary" : "bg-muted",
                    )}
                  />
                ))}
              </div>
            </div>
          )}
        </div>
      </header>

      {/* Step body */}
      <main className="mx-auto w-full max-w-md flex-1 px-4 py-5 pb-28">{children}</main>
    </div>
  );
}
