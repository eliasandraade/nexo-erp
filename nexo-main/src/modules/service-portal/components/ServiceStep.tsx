import { Clock, ChevronRight, Loader2, PawPrint } from "lucide-react";
import type { PublicCatalogItem, ServiceLabels } from "../api/booking.api";
import { formatDuration, formatPrice } from "../lib/booking-format";

interface ServiceStepProps {
  items:      PublicCatalogItem[];
  loading:    boolean;
  showPrices: boolean;
  labels:     ServiceLabels;
  onSelect:   (item: PublicCatalogItem) => void;
}

export function ServiceStep({ items, loading, showPrices, labels, onSelect }: ServiceStepProps) {
  return (
    <section className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold">Escolha o {labels.catalogItem.toLowerCase()}</h2>

      {loading && (
        <div className="flex justify-center py-10">
          <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
        </div>
      )}

      {!loading && items.length === 0 && (
        <p className="rounded-xl border border-dashed border-border px-4 py-8 text-center text-sm text-muted-foreground">
          Nenhum {labels.catalogItem.toLowerCase()} disponível no momento.
        </p>
      )}

      <div className="flex flex-col gap-2">
        {items.map((item) => (
          <button
            key={item.id}
            onClick={() => onSelect(item)}
            className="flex items-center gap-3 rounded-xl border border-border bg-card p-4 text-left transition-colors hover:border-primary/50 hover:bg-primary/5"
          >
            <div className="min-w-0 flex-1">
              <p className="text-sm font-semibold leading-tight">{item.name}</p>
              {item.description && (
                <p className="mt-0.5 line-clamp-2 text-xs text-muted-foreground">{item.description}</p>
              )}
              <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                <span className="inline-flex items-center gap-1">
                  <Clock className="h-3.5 w-3.5" /> {formatDuration(item.durationMinutes)}
                </span>
                {item.requiresSubject && (
                  <span className="inline-flex items-center gap-1">
                    <PawPrint className="h-3.5 w-3.5" /> Requer {labels.subject.toLowerCase()}
                  </span>
                )}
                {showPrices && item.price !== null && (
                  <span className="font-semibold tabular-nums text-foreground">{formatPrice(item.price)}</span>
                )}
              </div>
            </div>
            <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
          </button>
        ))}
      </div>
    </section>
  );
}
