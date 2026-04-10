import { AlertTriangle, AlertCircle, Info } from "lucide-react";
import type { InventoryAlert, AlertSeverity } from "../types";

const severityConfig: Record<AlertSeverity, { icon: typeof AlertTriangle; color: string; bg: string }> = {
  critical: { icon: AlertCircle, color: "text-destructive", bg: "bg-destructive/10" },
  warning: { icon: AlertTriangle, color: "text-warning", bg: "bg-warning/10" },
  info: { icon: Info, color: "text-secondary", bg: "bg-secondary/10" },
};

interface InventoryAlertCardProps {
  alert: InventoryAlert;
}

export function InventoryAlertCard({ alert }: InventoryAlertCardProps) {
  const config = severityConfig[alert.severity];
  const Icon = config.icon;

  return (
    <div className={`flex items-start gap-3 rounded-lg border border-border p-4 ${config.bg}`}>
      <Icon className={`h-5 w-5 mt-0.5 shrink-0 ${config.color}`} />
      <div className="flex-1 min-w-0">
        <p className="text-sm font-semibold text-foreground">{alert.title}</p>
        <p className="text-xs text-muted-foreground mt-0.5">{alert.description}</p>
      </div>
      <span className="text-xs font-medium text-primary whitespace-nowrap">{alert.suggestedAction}</span>
    </div>
  );
}
