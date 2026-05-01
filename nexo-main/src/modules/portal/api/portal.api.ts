import type { PublicMenuDto, OrderTrackingDto } from "../types";

// Portal uses plain fetch — no auth headers, no tenant context.
// Derives the backend root from VITE_API_BASE_URL (same var as apiClient) so
// we don't need a second env var and Railway deploys work automatically.
// VITE_API_BASE_URL is e.g. "https://backend.railway.app/api" — we keep the
// /api prefix since every path below already starts with /api/public/...
const BASE = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000/api";

async function get<T>(url: string): Promise<T> {
  const res = await fetch(url);
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json() as Promise<T>;
}

async function post<T>(url: string, body: unknown): Promise<T> {
  const res = await fetch(url, {
    method:  "POST",
    headers: { "Content-Type": "application/json" },
    body:    JSON.stringify(body),
  });
  if (!res.ok) throw new Error(`HTTP ${res.status}`);
  return res.json() as Promise<T>;
}

export const getPublicMenu = (slug: string): Promise<PublicMenuDto> =>
  get<PublicMenuDto>(`${BASE}/public/menu/${slug}`);

export const trackOrder = (token: string): Promise<OrderTrackingDto> =>
  get<OrderTrackingDto>(`${BASE}/public/orders/${token}`);

export interface CreatePortalOrderItemModifier { modifierId: string }

export interface CreatePortalOrderItem {
  productId:  string;
  quantity:   number;
  notes?:     string | null;
  modifiers?: CreatePortalOrderItemModifier[];
}

export interface CreatePortalOrderRequest {
  publicSlug:          string;
  orderType:           "Delivery" | "Takeaway";
  customerName:        string;
  customerPhone:       string;
  customerEmail?:      string | null;
  deliveryAddressJson?: string | null;
  notes?:              string | null;
  items?:              CreatePortalOrderItem[];
  deliveryZoneId?:     string | null;
  couponCode?:         string | null;
}

export interface PortalOrderCreatedDto {
  id:           string;
  orderNumber:  number;
  trackingToken: string;
  status:       string;
  total:        number;
}

export const createPortalOrder = (req: CreatePortalOrderRequest): Promise<PortalOrderCreatedDto> =>
  post<PortalOrderCreatedDto>(`${BASE}/public/orders`, req);

// ── Delivery Zones ────────────────────────────────────────────────────────────

export interface DeliveryZoneDto {
  id:           string;
  neighborhood: string;
  fee:          number;
}

export const getDeliveryZones = (slug: string): Promise<DeliveryZoneDto[]> =>
  get<DeliveryZoneDto[]>(`${BASE}/public/delivery-zones/${slug}`);

// ── Coupon validation ─────────────────────────────────────────────────────────

export interface ValidateCouponRequest {
  publicSlug:    string;
  couponCode:    string;
  customerPhone: string;
  itemsSubtotal: number;
  deliveryFee:   number;
  neighborhood?: string;
}

export interface ValidateCouponResponse {
  valid:          boolean;
  error?:         string;
  discountAmount: number;
  discountType:   string;
  discountValue:  number;
}

export const validateCoupon = (req: ValidateCouponRequest): Promise<ValidateCouponResponse> =>
  post<ValidateCouponResponse>(`${BASE}/public/coupons/validate`, req);
