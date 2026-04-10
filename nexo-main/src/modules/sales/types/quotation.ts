export type QuotationStatus = "draft" | "sent" | "approved" | "expired" | "converted";

export interface QuotationItem {
  id: string;
  productId: string;
  code: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  /** (unitPrice * quantity) - discount */
  totalPrice: number;
}

export interface Quotation {
  id: string;
  createdAt: string;
  updatedAt: string;
  createdBy: string;
  operator: string;
  customerId: string | null;
  customerName: string | null;
  status: QuotationStatus;
  notes: string;
  items: QuotationItem[];
  subtotal: number;
  discountTotal: number;
  total: number;
  /** Populated after convertToSale is implemented */
  convertedToSaleId?: string;
  convertedAt?: string;
}

export interface QuotationFormInput {
  operator: string;
  customerId: string;
  customerName: string;
  status: QuotationStatus;
  notes: string;
  items: QuotationItem[];
}

export interface QuotationListFilters {
  search: string;
  status: QuotationStatus | "all";
  operator: string; // "all" or operator name
}

export const quotationStatusLabels: Record<QuotationStatus, string> = {
  draft: "Rascunho",
  sent: "Enviado",
  approved: "Aprovado",
  expired: "Expirado",
  converted: "Convertido",
};

export const quotationStatusVariant: Record<
  QuotationStatus,
  "success" | "warning" | "danger" | "info" | "neutral"
> = {
  draft: "neutral",
  sent: "info",
  approved: "success",
  expired: "danger",
  converted: "success",
};
