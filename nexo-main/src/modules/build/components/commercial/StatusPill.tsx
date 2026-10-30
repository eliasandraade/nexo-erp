import { cn } from "@/lib/utils";
import { TONE_CLASS } from "../../lib/commercial";

interface Props {
  label: string;
  tone: keyof typeof TONE_CLASS;
  className?: string;
}

/** Small rounded status badge (same look as BudgetStatusBadge). */
export function StatusPill({ label, tone, className }: Props) {
  return (
    <span className={cn("inline-flex items-center whitespace-nowrap rounded-full px-2 py-0.5 text-xs font-semibold", TONE_CLASS[tone], className)}>
      {label}
    </span>
  );
}
