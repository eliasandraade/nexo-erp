import type { SaleDto } from "../api/sales.api";
import type { CartItem, CompletedSale, PaymentEntry, PaymentMethod } from "../types";

function mapPaymentMethod(method: string): PaymentMethod {
  switch (method) {
    case "Cash":
      return "cash";
    case "Pix":
      return "pix";
    case "Debit":
    case "Credit":
      return "card";
    default:
      return "cash";
  }
}

export function saleToLegacy(dto: SaleDto): CompletedSale {
  const items: CartItem[] = dto.items.map((item) => ({
    productId: item.productId,
    code: item.productCode,
    description: item.productName,
    quantity: item.quantity,
    unitPrice: item.unitPrice,
    discount: item.discountAmount,
    totalPrice: item.total,
    status: "active" as const,
  }));

  const payments: PaymentEntry[] = dto.payments.map((p) => ({
    method: mapPaymentMethod(p.method),
    amount: p.amount,
  }));

  const cashPaid = dto.payments
    .filter((p) => p.method === "Cash")
    .reduce((sum, p) => sum + p.amount, 0);

  const change = Math.max(0, cashPaid - dto.total);

  const status: CompletedSale["status"] =
    dto.status === "Cancelled" ? "cancelled" : "completed";

  const timestamp = dto.confirmedAt ?? dto.paidAt ?? dto.createdAt;

  return {
    id: `#${dto.number}`,
    timestamp,
    operator: dto.soldByName,
    status,
    customerId: dto.customerId ?? undefined,
    customerName: dto.customerName ?? undefined,
    items,
    subtotal: dto.subtotal,
    discountTotal: dto.discountAmount,
    total: dto.total,
    payments,
    change,
    cancelledAt: dto.cancelledAt ?? undefined,
  };
}
