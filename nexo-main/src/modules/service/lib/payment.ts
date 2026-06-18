import type { SvcPaymentMethod, SvcPaymentStatus } from "../api/service.api";
import type { BadgeVariant } from "@/components/shared/StatusBadge";

export const PAYMENT_METHODS: SvcPaymentMethod[] = [
  "Cash", "Pix", "DebitCard", "CreditCard", "BankTransfer", "Other",
];

export const PAYMENT_METHOD_LABELS: Record<SvcPaymentMethod, string> = {
  Cash: "Dinheiro",
  Pix: "Pix",
  DebitCard: "Cartão de débito",
  CreditCard: "Cartão de crédito",
  BankTransfer: "Transferência",
  Other: "Outro",
};

export const PAYMENT_STATUS_LABELS: Record<SvcPaymentStatus, string> = {
  Paid: "Pago",
  Voided: "Estornado",
};

export const PAYMENT_STATUS_VARIANTS: Record<SvcPaymentStatus, BadgeVariant> = {
  Paid: "success",
  Voided: "neutral",
};
