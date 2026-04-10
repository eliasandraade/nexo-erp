import { Badge } from "@/components/ui/badge";
import { commissionStatusLabels } from "../types";
import type { CommissionStatus } from "../types";

interface CommissionStatusBadgeProps {
  status: CommissionStatus;
}

export function CommissionStatusBadge({ status }: CommissionStatusBadgeProps) {
  return (
    <Badge variant={status === "active" ? "default" : "secondary"}>
      {commissionStatusLabels[status]}
    </Badge>
  );
}
