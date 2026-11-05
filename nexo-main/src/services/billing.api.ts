import { apiClient } from "./api-client";

export interface SubscriptionDetail {
  moduleKey: string;
  status: string;
  planType: string;
  currentPeriodStart: string;
  currentPeriodEnd: string | null;
  cancelAtPeriodEnd: boolean;
  stripeSubscriptionId: string | null;
}

export interface CheckoutResponse {
  sessionId: string;
  checkoutUrl: string;
}

export interface PortalResponse {
  portalUrl: string;
}

export interface CheckoutAvailability {
  newCheckoutsEnabled: boolean;
}

/** Code of the 403 of a checkout while the server has new purchases closed. */
export const CHECKOUT_DISABLED_CODE = "checkout_disabled";

export function listSubscriptions(): Promise<SubscriptionDetail[]> {
  return apiClient.get("/billing/subscriptions");
}

export function getCheckoutAvailability(): Promise<CheckoutAvailability> {
  return apiClient.get("/billing/checkout-availability");
}

export function createCheckout(
  moduleKey: string,
  billingPeriod: "monthly" | "annual"
): Promise<CheckoutResponse> {
  return apiClient.post("/billing/checkout", { moduleKey, billingPeriod });
}

export function createPortal(): Promise<PortalResponse> {
  return apiClient.post("/billing/portal", {});
}
