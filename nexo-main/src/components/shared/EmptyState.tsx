import { type LucideIcon, Inbox } from "lucide-react";
import { cn } from "@/lib/utils";

interface EmptyStateProps {
  icon?: LucideIcon;
  title: string;
  description?: string;
  action?: React.ReactNode;
  className?: string;
  /** Compact variant for inline use (e.g. inside a table) */
  compact?: boolean;
}

export function EmptyState({
  icon: Icon = Inbox,
  title,
  description,
  action,
  className,
  compact = false,
}: EmptyStateProps) {
  return (
    <div
      className={cn(
        "flex flex-col items-center justify-center text-center",
        compact ? "py-8" : "py-16",
        className
      )}
    >
      {/* Icon container */}
      <div className="relative mb-4">
        <div className="w-12 h-12 rounded-full border border-border bg-muted flex items-center justify-center">
          <Icon className="h-5 w-5 text-muted-foreground" />
        </div>
        {/* Subtle ring */}
        <div className="absolute inset-0 rounded-full border border-border/40 scale-[1.35] pointer-events-none" />
      </div>

      <h3 className="text-[14px] font-semibold text-foreground">{title}</h3>
      {description && (
        <p className="text-[13px] text-muted-foreground mt-1.5 max-w-xs leading-relaxed">
          {description}
        </p>
      )}
      {action && <div className="mt-5">{action}</div>}
    </div>
  );
}
