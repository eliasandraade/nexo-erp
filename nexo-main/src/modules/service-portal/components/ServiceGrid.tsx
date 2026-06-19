import { Check, Clock, Scissors, Stethoscope, Sparkles, Car, Dog, GraduationCap, Dumbbell, Wrench, type LucideIcon } from "lucide-react";
import type { PublicCatalogItem, ServiceLabels } from "../api/booking.api";
import { formatDuration, formatPrice } from "../lib/booking-format";
import { Surface, Reveal, Muted } from "./PortalPrimitives";

const ICONS: LucideIcon[] = [Sparkles, Stethoscope, Scissors, Dog, Car, Dumbbell, GraduationCap, Wrench];
function iconFor(name: string): LucideIcon {
  let h = 0;
  for (const ch of name) h = (h * 31 + ch.charCodeAt(0)) >>> 0;
  return ICONS[h % ICONS.length];
}

interface ServiceGridProps {
  items:      PublicCatalogItem[];
  showPrices: boolean;
  labels:     ServiceLabels;
  selectedId: string | null;
  onSelect:   (item: PublicCatalogItem) => void;
}

export function ServiceGrid({ items, showPrices, labels, selectedId, onSelect }: ServiceGridProps) {
  if (items.length === 0) {
    return (
      <Surface className="p-8 text-center">
        <Muted className="text-sm">Nenhum {labels.catalogItem.toLowerCase()} disponível no momento.</Muted>
      </Surface>
    );
  }

  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {items.map((item, i) => {
        const Icon = iconFor(item.name);
        const active = item.id === selectedId;
        return (
          <Reveal key={item.id} delay={i * 60}>
            <button onClick={() => onSelect(item)} className="block w-full text-left">
              <Surface
                interactive
                className="h-full p-4"
                style={active
                  ? { borderColor: "var(--p-accent)", boxShadow: "0 12px 30px -16px color-mix(in srgb, var(--p-accent) 55%, transparent)" }
                  : undefined}
              >
                <div className="flex items-start gap-3">
                  <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-[calc(var(--p-radius)*0.6)]"
                    style={{ background: "var(--p-accent-soft)", color: "var(--p-accent)" }}>
                    {active ? <Check className="h-5 w-5" /> : <Icon className="h-5 w-5" />}
                  </span>
                  <div className="min-w-0 flex-1">
                    <p className="text-[15px] font-semibold leading-tight" style={{ fontFamily: "var(--p-display)" }}>
                      {item.name}
                    </p>
                    {item.description && (
                      <p className="mt-1 line-clamp-2 text-[13px] leading-snug" style={{ color: "var(--p-muted)" }}>
                        {item.description}
                      </p>
                    )}
                    <div className="mt-2.5 flex items-center gap-3 text-[13px]">
                      <span className="inline-flex items-center gap-1" style={{ color: "var(--p-muted)" }}>
                        <Clock className="h-3.5 w-3.5" /> {formatDuration(item.durationMinutes)}
                      </span>
                      {showPrices && item.price !== null && (
                        <span className="font-semibold tabular-nums" style={{ color: "var(--p-ink)" }}>
                          {formatPrice(item.price)}
                        </span>
                      )}
                    </div>
                  </div>
                </div>
              </Surface>
            </button>
          </Reveal>
        );
      })}
    </div>
  );
}
