import { cn } from "@/lib/utils";

type BadgeVariant = "success" | "warning" | "danger" | "info" | "neutral";

interface StatusBadgeProps {
  label: string;
  variant?: BadgeVariant;
  className?: string;
  /** Show a colored dot before the label */
  dot?: boolean;
}

const variantStyles: Record<BadgeVariant, string> = {
  success: "bg-success/10 text-success",
  warning: "bg-warning/10 text-warning",
  danger:  "bg-destructive/10 text-destructive",
  info:    "bg-secondary/10 text-secondary",
  neutral: "bg-muted text-muted-foreground",
};

const dotStyles: Record<BadgeVariant, string> = {
  success: "bg-success",
  warning: "bg-warning",
  danger:  "bg-destructive",
  info:    "bg-secondary",
  neutral: "bg-muted-foreground",
};

export function StatusBadge({
  label,
  variant = "neutral",
  className,
  dot = false,
}: StatusBadgeProps) {
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-[11px] font-medium",
        variantStyles[variant],
        className
      )}
    >
      {dot && (
        <span className={cn("w-1.5 h-1.5 rounded-full shrink-0", dotStyles[variant])} />
      )}
      {label}
    </span>
  );
}
