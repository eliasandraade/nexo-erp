import { type LucideIcon, Inbox } from "lucide-react";
import { cn } from "@/lib/utils";

interface EmptyStateProps {
  icon?: LucideIcon;
  title: string;
  description?: string;
  action?: React.ReactNode;
  className?: string;
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
    <div className={cn(
      "flex flex-col items-center justify-center text-center",
      compact ? "py-8" : "py-14",
      className
    )}>
      <div className="w-10 h-10 rounded-lg border border-border bg-muted flex items-center justify-center mb-3">
        <Icon className="h-4 w-4 text-muted-foreground" />
      </div>

      <p className="text-[13.5px] font-semibold text-foreground">{title}</p>
      {description && (
        <p className="text-[12.5px] text-muted-foreground mt-1 max-w-xs leading-relaxed">
          {description}
        </p>
      )}
      {action && <div className="mt-4">{action}</div>}
    </div>
  );
}
