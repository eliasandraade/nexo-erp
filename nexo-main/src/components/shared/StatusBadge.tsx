import { cn } from "@/lib/utils";

export type BadgeVariant = "success" | "warning" | "danger" | "info" | "neutral";

interface StatusBadgeProps {
  label: string;
  variant?: BadgeVariant;
  className?: string;
  dot?: boolean;
  size?: "sm" | "md";
}

const variantStyles: Record<BadgeVariant, string> = {
  success: "bg-success/10 text-success",
  warning: "bg-warning/10 text-warning",
  danger:  "bg-destructive/10 text-destructive",
  info:    "bg-primary/10 text-primary",
  neutral: "bg-muted text-muted-foreground",
};

const dotStyles: Record<BadgeVariant, string> = {
  success: "bg-success",
  warning: "bg-warning",
  danger:  "bg-destructive",
  info:    "bg-primary",
  neutral: "bg-muted-foreground",
};

export function StatusBadge({
  label,
  variant = "neutral",
  className,
  dot = false,
  size = "sm",
}: StatusBadgeProps) {
  return (
    <span className={cn(
      "inline-flex items-center gap-1.5 rounded font-medium whitespace-nowrap",
      size === "sm"
        ? "px-1.5 py-0.5 text-[11px]"
        : "px-2 py-1 text-[12px]",
      variantStyles[variant],
      className
    )}>
      {dot && (
        <span className={cn("w-1.5 h-1.5 rounded-full shrink-0", dotStyles[variant])} />
      )}
      {label}
    </span>
  );
}
