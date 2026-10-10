import { type LucideIcon, TrendingUp, TrendingDown } from "lucide-react";
import { cn } from "@/lib/utils";

interface MetricBlockProps {
  label: string;
  value: string | number;
  caption?: string;
  icon?: LucideIcon;
  trend?: {
    direction: "up" | "down" | "neutral";
    label: string;
  };
  variant?: "default" | "warning" | "danger" | "success";
  className?: string;
}

const variantBorder: Record<NonNullable<MetricBlockProps["variant"]>, string> = {
  default: "",
  warning: "border-warning/40",
  danger:  "border-destructive/40",
  success: "border-success/40",
};

export function MetricBlock({
  label,
  value,
  caption,
  icon: Icon,
  trend,
  variant = "default",
  className,
}: MetricBlockProps) {
  return (
    <div className={cn(
      "bg-card rounded-md border border-border p-4",
      variantBorder[variant],
      className
    )}>
      <div className="flex items-start justify-between gap-2 mb-2">
        <p className="text-xs font-medium text-muted-foreground leading-none">
          {label}
        </p>
        {Icon && <Icon className="h-3.5 w-3.5 text-muted-foreground shrink-0" />}
      </div>

      <p className={cn(
        "text-2xl font-semibold leading-none tabular-nums",
        variant === "warning"  && "text-warning",
        variant === "danger"   && "text-destructive",
        variant === "success"  && "text-success",
        variant === "default"  && "text-foreground",
      )}>
        {value}
      </p>

      {(caption || trend) && (
        <div className="flex items-center gap-2 mt-2">
          {trend && trend.direction !== "neutral" && (
            <span className={cn(
              "inline-flex items-center gap-0.5 text-[11px] font-medium",
              trend.direction === "up"   ? "text-success" : "text-destructive"
            )}>
              {trend.direction === "up"
                ? <TrendingUp className="h-3 w-3" />
                : <TrendingDown className="h-3 w-3" />
              }
              {trend.label}
            </span>
          )}
          {caption && (
            <p className="text-[11px] text-muted-foreground leading-none">{caption}</p>
          )}
        </div>
      )}
    </div>
  );
}
