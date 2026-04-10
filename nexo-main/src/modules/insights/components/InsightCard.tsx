import { cn } from "@/lib/utils";
import { formatTimeShort } from "@/lib/formatters";
import {
  Package,
  Landmark,
  ShoppingCart,
  Percent,
  Activity,
} from "lucide-react";
import type { ManagerialInsight, InsightCategory, InsightSeverity } from "../types";
import { insightCategoryLabels } from "../types";

const categoryIcons: Record<InsightCategory, React.ElementType> = {
  inventory: Package,
  cash: Landmark,
  sales: ShoppingCart,
  commissions: Percent,
  operations: Activity,
};

const severityBarClass: Record<InsightSeverity, string> = {
  critical: "bg-destructive",
  warning: "bg-yellow-500",
  info: "bg-blue-400",
};

interface InsightCardProps {
  insight: ManagerialInsight;
}

export function InsightCard({ insight }: InsightCardProps) {
  const Icon = categoryIcons[insight.category];

  return (
    <div className="flex rounded-lg border bg-card overflow-hidden">
      <div className={cn("w-1 shrink-0", severityBarClass[insight.severity])} />
      <div className="flex items-start gap-3 p-4 flex-1 min-w-0">
        <div className="rounded-md bg-muted p-2 shrink-0 mt-0.5">
          <Icon className="h-4 w-4 text-muted-foreground" />
        </div>
        <div className="flex-1 min-w-0">
          <div className="flex items-start justify-between gap-2 flex-wrap">
            <p className="font-semibold text-sm leading-snug">{insight.title}</p>
            <span className="text-xs text-muted-foreground shrink-0 tabular-nums">
              {formatTimeShort(insight.generatedAt)}
            </span>
          </div>
          <p className="text-sm text-muted-foreground mt-0.5 leading-snug">
            {insight.description}
          </p>
          <div className="flex items-center gap-2 mt-2 flex-wrap">
            <span className="text-xs rounded-full border px-2 py-0.5 text-muted-foreground">
              {insightCategoryLabels[insight.category]}
            </span>
            {insight.value && (
              <span className="text-xs font-semibold bg-muted rounded-full px-2 py-0.5 tabular-nums">
                {insight.value}
              </span>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
