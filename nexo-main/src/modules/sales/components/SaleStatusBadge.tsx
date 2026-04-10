import { Badge } from "@/components/ui/badge";
import { saleStatusLabels } from "../types";
import type { SaleStatus } from "../types";

interface SaleStatusBadgeProps {
  status: SaleStatus;
}

const variantMap: Record<SaleStatus, "default" | "destructive" | "secondary"> = {
  completed: "default",
  cancelled: "destructive",
  partially_cancelled: "secondary",
};

export function SaleStatusBadge({ status }: SaleStatusBadgeProps) {
  return (
    <Badge variant={variantMap[status]}>
      {saleStatusLabels[status]}
    </Badge>
  );
}
