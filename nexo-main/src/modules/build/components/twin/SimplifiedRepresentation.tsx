import type { BuildElementState, BuildModelZoneDto } from "../../api/build-twin.api";
import { SIMPLIFIED_LABEL, STATE_META, stackBoxes, stateColor } from "../../lib/twin";

interface Props {
  zones: BuildModelZoneDto[];
  zoneStates: Map<string, BuildElementState>;
}

/**
 * Front view of the boxes the construtora typed (width × height, stacked by elevation; boxes on the same elevation side by
 * side). Nothing is generated: no box without measures, and the mandatory label is always shown with it.
 */
export function SimplifiedRepresentation({ zones, zoneStates }: Props) {
  const boxes = stackBoxes(zones);
  if (boxes.length === 0) return null;

  // lay out: one row per elevation, boxes left to right with a 0.5 m gap
  const gap = 0.5;
  const rows = new Map<number, number>();            // elevation → next x
  const placed = boxes.map((z) => {
    const b = z.simplifiedBox!;
    const x = rows.get(b.elevation) ?? 0;
    rows.set(b.elevation, x + b.width + gap);
    return { z, x, y: b.elevation, w: b.width, h: b.height };
  });
  const minY = Math.min(...placed.map((p) => p.y));
  const maxY = Math.max(...placed.map((p) => p.y + p.h));
  const maxX = Math.max(...placed.map((p) => p.x + p.w));
  const pad = Math.max(maxX, maxY - minY) * 0.04 || 0.5;
  const vbW = maxX + pad * 2;
  const vbH = maxY - minY + pad * 2;
  const font = Math.max(vbW, vbH) / 32;

  return (
    <figure className="min-w-0 space-y-2" data-testid="simplified-representation">
      <figcaption className="rounded-md border border-amber-300 bg-amber-50 px-2 py-1.5 text-xs font-medium text-amber-900 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-200">
        {SIMPLIFIED_LABEL}
      </figcaption>
      <svg viewBox={`0 0 ${vbW} ${vbH}`} className="h-auto max-h-[24rem] w-full rounded-md border border-border bg-muted/20"
        role="img" aria-label="Caixas da representação simplificada por cota">
        {placed.map(({ z, x, y, w, h }) => {
          const state = zoneStates.get(z.id) ?? null;
          const c = stateColor(state);
          const top = pad + (maxY - (y + h));
          return (
            <g key={z.id}>
              <rect x={pad + x} y={top} width={w} height={h} fill={c.color} fillOpacity={state ? 0.75 : 0.25}
                stroke="currentColor" strokeOpacity={0.5} strokeWidth={font / 12} />
              <text x={pad + x + w / 2} y={top + h / 2} textAnchor="middle" dominantBaseline="middle" fontSize={font}
                className="fill-foreground">
                {z.name}
              </text>
              <text x={pad + x + w / 2} y={top + h / 2 + font * 1.2} textAnchor="middle" dominantBaseline="middle"
                fontSize={font * 0.75} className="fill-muted-foreground">
                {state ? STATE_META[state].label : "sem vínculo"}
              </text>
            </g>
          );
        })}
      </svg>
    </figure>
  );
}
