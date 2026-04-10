import { StatusBadge } from "@/components/shared/StatusBadge";
import { auditActionTypeLabels } from "../types";
import type { AuditActionType } from "../types";

const variantMap: Record<AuditActionType, "success" | "warning" | "danger" | "info" | "neutral"> = {
  stock_adjustment: "neutral",
  stock_transfer: "neutral",
  cash_open: "success",
  cash_movement: "neutral",
  cash_close: "info",
  sale_completed: "success",
  sale_cancelled: "danger",
  sale_item_cancelled: "warning",
  manager_authorization: "warning",
  user_created: "info",
  user_updated: "neutral",
};

interface AuditActionBadgeProps {
  actionType: AuditActionType;
}

export function AuditActionBadge({ actionType }: AuditActionBadgeProps) {
  return (
    <StatusBadge
      label={auditActionTypeLabels[actionType]}
      variant={variantMap[actionType]}
    />
  );
}
