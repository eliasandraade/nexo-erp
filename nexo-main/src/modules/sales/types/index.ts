export type PaymentMethod = "cash" | "pix" | "card";

export type SaleStatus = "completed" | "cancelled" | "partially_cancelled";

export const paymentMethodLabels: Record<PaymentMethod, string> = {
  cash: "Dinheiro",
  pix: "PIX",
  card: "Cartão",
};

export const saleStatusLabels: Record<SaleStatus, string> = {
  completed: "Concluída",
  cancelled: "Cancelada",
  partially_cancelled: "Parcial. cancelada",
};

export interface CartItem {
  productId: string;
  code: string;
  description: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  totalPrice: number;
  /** Set when this item is cancelled via item-level cancellation */
  status?: "active" | "cancelled";
  cancelledAt?: string;
  cancelledBy?: string;
  authorizedBy?: string;
  cancellationReason?: string;
}

export interface PaymentEntry {
  method: PaymentMethod;
  amount: number;
}

export interface SaleCancellationRecord {
  id: string;
  /** "full" = whole sale cancelled; "item" = single item cancelled */
  type: "full" | "item";
  /** Only present for item-level cancellations */
  itemProductId?: string;
  itemDescription?: string;
  cancelledAt: string;
  /** Operator or user who initiated the cancellation */
  cancelledBy: string;
  /** Manager/Diretoria login that authorized */
  authorizedBy: string;
  reason: string;
  /** Cash amount reversed (only cash payments affect the drawer) */
  cashRefundAmount: number;
}

export interface CompletedSale {
  id: string;
  timestamp: string;
  operator: string;
  status: SaleStatus;
  /** Reserved for future customer linkage */
  customerId?: string;
  customerName?: string;
  items: CartItem[];
  subtotal: number;
  discountTotal: number;
  total: number;
  payments: PaymentEntry[];
  change: number;
  /** Populated when the sale (or items) are cancelled */
  cancellationRecords?: SaleCancellationRecord[];
  cancelledAt?: string;
  cancelledBy?: string;
  authorizedBy?: string;
  cancellationReason?: string;
}

export interface SaleListFilters {
  search: string;
  paymentMethod: PaymentMethod | "all";
  status: SaleStatus | "all";
}

export interface ProductSearchResult {
  id: string;
  code: string;
  description: string;
  price: number;
  unit: string;
  stock: number;
}
