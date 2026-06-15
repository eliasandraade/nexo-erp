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

export function listSubscriptions(): Promise<SubscriptionDetail[]> {
  return apiClient.get("/billing/subscriptions");
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
