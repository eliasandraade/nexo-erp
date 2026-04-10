import { StatusBadge } from "@/components/shared/StatusBadge";
import { quotationStatusLabels, quotationStatusVariant } from "../types/quotation";
import type { QuotationStatus } from "../types/quotation";

interface QuotationStatusBadgeProps {
  status: QuotationStatus;
}

export function QuotationStatusBadge({ status }: QuotationStatusBadgeProps) {
  return (
    <StatusBadge
      label={quotationStatusLabels[status]}
      variant={quotationStatusVariant[status]}
    />
  );
}
