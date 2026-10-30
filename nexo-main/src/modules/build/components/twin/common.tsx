import { lazy, type ReactNode } from "react";
import { cn } from "@/lib/utils";
import type { BuildElementState } from "../../api/build-twin.api";
import { STATE_META, UNBOUND_META } from "../../lib/twin";

// three.js lives only in these lazy chunks (the main bundle and the Build page chunk never import it)
export const LazyModelViewer3D = lazy(() => import("./viewer/ModelViewer3D"));
export const LazyPano360Viewer = lazy(() => import("./viewer/Pano360Viewer"));

export function ProgressBar({ value, label }: { value: number; label: string }) {
  const pct = Math.max(0, Math.min(100, Math.round(value)));
  return (
    <div className="space-y-1">
      <div className="flex justify-between text-xs text-muted-foreground"><span>{label}</span><span className="tabular-nums">{pct}%</span></div>
      <div role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct}
        className="h-2 w-full overflow-hidden rounded-full bg-muted">
        <div className="h-full bg-primary transition-[width]" style={{ width: `${pct}%` }} />
      </div>
    </div>
  );
}

export function Card({ title, actions, children, className }: { title?: ReactNode; actions?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={cn("min-w-0 space-y-3 rounded-md border border-border bg-card p-3 sm:p-4", className)}>
      {(title || actions) && (
        <div className="flex flex-wrap items-center justify-between gap-2">
          {title && <h3 className="text-sm font-semibold">{title}</h3>}
          {actions}
        </div>
      )}
      {children}
    </section>
  );
}

export function StateDot({ state }: { state: BuildElementState | null | undefined }) {
  const meta = state ? STATE_META[state] : UNBOUND_META;
  return (
    <span className="inline-flex items-center gap-1.5 whitespace-nowrap text-xs">
      <span className={cn("inline-block h-2.5 w-2.5 rounded-full", meta.dot)} aria-hidden />
      {meta.label}
    </span>
  );
}

export const selectClass =
  "h-9 w-full min-w-0 rounded-md border border-input bg-background px-2 text-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-ring";
