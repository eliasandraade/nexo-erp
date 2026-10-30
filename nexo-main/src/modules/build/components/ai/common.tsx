import type { ReactNode } from "react";
import { Sparkles } from "lucide-react";
import { cn } from "@/lib/utils";
import { AI_DISABLED_MESSAGE, AI_TAGLINE, VISION_DISABLED_MESSAGE, confidencePercent } from "../../lib/ai";

/** Confidence of the AI (0–1) as a bar + percentage. */
export function ConfidenceBar({ value, label = "Confiança", compact }: { value: unknown; label?: string; compact?: boolean }) {
  const pct = confidencePercent(value);
  const tone = pct >= 75 ? "bg-emerald-500" : pct >= 50 ? "bg-amber-500" : "bg-rose-500";
  return (
    <div className={cn("flex items-center gap-2", compact ? "min-w-[96px]" : "min-w-[140px]")}>
      {!compact && <span className="text-xs text-muted-foreground">{label}</span>}
      <div role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={pct}
        className="h-1.5 w-full min-w-[48px] overflow-hidden rounded-full bg-muted">
        <div className={cn("h-full", tone)} style={{ width: `${pct}%` }} />
      </div>
      <span className="text-xs tabular-nums text-muted-foreground">{pct}%</span>
    </div>
  );
}

/** Shell of every AI panel: title, the tagline and the switch notice. */
export function AiPanel({ title, description, aiEnabled, loading, children, testId }: {
  title: string; description?: ReactNode; aiEnabled: boolean; loading?: boolean; children: ReactNode; testId?: string;
}) {
  return (
    <section data-testid={testId} className="min-w-0 space-y-3 rounded-md border border-primary/30 bg-primary/[0.03] p-3 sm:p-4">
      <div className="space-y-0.5">
        <h3 className="flex items-center gap-1.5 text-sm font-semibold">
          <Sparkles className="h-4 w-4 text-primary" aria-hidden /> {title}
        </h3>
        <p className="text-xs text-muted-foreground">
          {description}{description ? " " : ""}<span className="font-medium text-foreground/80">{AI_TAGLINE}</span>
        </p>
      </div>
      {!loading && !aiEnabled && <AiDisabledNotice />}
      {children}
    </section>
  );
}

export function AiDisabledNotice({ vision }: { vision?: boolean }) {
  return (
    <p role="note" className="rounded-md border border-amber-300 bg-amber-50 px-2 py-1.5 text-xs text-amber-900 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-200">
      {vision ? VISION_DISABLED_MESSAGE : AI_DISABLED_MESSAGE}
    </p>
  );
}

export function AiErrorAlert({ message }: { message: string | null }) {
  if (!message) return null;
  return (
    <p role="alert" className="rounded-md border border-destructive/40 bg-destructive/5 px-2 py-1.5 text-xs text-destructive">
      {message}
    </p>
  );
}

export const selectClass =
  "h-9 w-full min-w-0 rounded-md border border-input bg-background px-2 text-sm focus:outline-none focus-visible:ring-2 focus-visible:ring-ring";
