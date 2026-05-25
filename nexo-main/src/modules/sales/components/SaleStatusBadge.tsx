import { Badge } from "@/components/ui/badge";

const labelMap: Record<string, string> = {
  Draft:     "Rascunho",
  Confirmed: "Confirmada",
  Paid:      "Paga",
  Cancelled: "Cancelada",
  // legacy frontend values
  completed:           "Concluída",
  cancelled:           "Cancelada",
  partially_cancelled: "Parc. cancelada",
};

const variantMap: Record<string, "default" | "destructive" | "secondary"> = {
  Draft:     "secondary",
  Confirmed: "default",
  Paid:      "default",
  Cancelled: "destructive",
  completed:           "default",
  cancelled:           "destructive",
  partially_cancelled: "secondary",
};

interface SaleStatusBadgeProps {
  status: string;
}

export function SaleStatusBadge({ status }: SaleStatusBadgeProps) {
  return (
    <Badge variant={variantMap[status] ?? "secondary"}>
      {labelMap[status] ?? status}
    </Badge>
  );
}
