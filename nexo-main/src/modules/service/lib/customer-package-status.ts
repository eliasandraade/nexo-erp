import type { SvcCustomerPackageStatus } from "../api/service.api";
import type { BadgeVariant } from "@/components/shared/StatusBadge";

export const CUSTOMER_PACKAGE_STATUS_LABELS: Record<SvcCustomerPackageStatus, string> = {
  Active: "Ativo",
  Consumed: "Consumido",
  Expired: "Expirado",
  Cancelled: "Cancelado",
};

export const CUSTOMER_PACKAGE_STATUS_VARIANTS: Record<SvcCustomerPackageStatus, BadgeVariant> = {
  Active: "success",
  Consumed: "neutral",
  Expired: "warning",
  Cancelled: "danger",
};

/** A customer package can only be consumed / paid while Active. */
export function isCustomerPackageActive(status: SvcCustomerPackageStatus): boolean {
  return status === "Active";
}
