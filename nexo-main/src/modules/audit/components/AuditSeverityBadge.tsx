import { StatusBadge } from "@/components/shared/StatusBadge";
import { auditSeverityLabels } from "../types";
import type { AuditSeverity } from "../types";

const variantMap: Record<AuditSeverity, "danger" | "warning" | "info"> = {
  critical: "danger",
  warning: "warning",
  info: "info",
};

interface AuditSeverityBadgeProps {
  severity: AuditSeverity;
}

export function AuditSeverityBadge({ severity }: AuditSeverityBadgeProps) {
  return (
    <StatusBadge
      label={auditSeverityLabels[severity]}
      variant={variantMap[severity]}
    />
  );
}
