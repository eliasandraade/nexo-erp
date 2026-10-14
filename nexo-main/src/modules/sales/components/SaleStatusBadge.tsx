import { Badge } from "@/components/ui/badge";

const labelMap: Record<string, string> = {
  Draft:     "Rascunho",
  Confirmed: "Confirmada",
  Paid:      "Paga",
  Cancelled: "Cancelada",
  PartiallyReturned: "Parc. devolvida",
  Returned:  "Devolvida",
  // legacy frontend values
  completed:           "Concluída",
  cancelled:           "Cancelada",
  partially_cancelled: "Parc. cancelada",
  partially_returned:  "Parc. devolvida",
  returned:            "Devolvida",
};

const variantMap: Record<string, "default" | "destructive" | "secondary"> = {
  Draft:     "secondary",
  Confirmed: "default",
  Paid:      "default",
  Cancelled: "destructive",
  PartiallyReturned: "secondary",
  Returned:  "secondary",
  completed:           "default",
  cancelled:           "destructive",
  partially_cancelled: "secondary",
  partially_returned:  "secondary",
  returned:            "secondary",
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
