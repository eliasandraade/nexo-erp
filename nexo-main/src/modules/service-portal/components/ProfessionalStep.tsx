import { ChevronRight, Loader2, UserRound } from "lucide-react";
import type { PublicProfessional, ServiceLabels } from "../api/booking.api";

interface ProfessionalStepProps {
  items:    PublicProfessional[];
  loading:  boolean;
  labels:   ServiceLabels;
  onSelect: (professional: PublicProfessional) => void;
}

export function ProfessionalStep({ items, loading, labels, onSelect }: ProfessionalStepProps) {
  const noun = labels.professional.toLowerCase();
  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold">Escolha o {noun}</h2>

      {loading && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {!loading && items.length === 0 && (
        <p className="rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-muted-foreground">
          Nenhum {noun} disponível no momento.
        </p>
      )}

      <div className="flex flex-col gap-2">
        {items.map((p) => (
          <button
            key={p.id}
            onClick={() => onSelect(p)}
            className="flex items-center gap-3 rounded-xl border border-border bg-card p-4 text-left transition-colors hover:border-primary/50 hover:bg-primary/5"
          >
            <span
              className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full text-sm font-semibold"
              style={{
                backgroundColor: p.color ? `${p.color}22` : undefined,
                color:           p.color ?? undefined,
              }}
            >
              {p.color ? p.name.charAt(0).toUpperCase() : <UserRound className="h-5 w-5 text-muted-foreground" />}
            </span>
            <div className="min-w-0 flex-1">
              <p className="text-sm font-semibold leading-tight">{p.name}</p>
              {(p.specialty || p.role) && (
                <p className="mt-0.5 truncate text-xs text-muted-foreground">{p.specialty || p.role}</p>
              )}
            </div>
            <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
          </button>
        ))}
      </div>
    </section>
  );
}
