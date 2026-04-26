import type { PublicMenuDto, OrderTrackingDto } from "../types";

// Portal uses plain fetch — no auth headers, no tenant context
const BASE = import.meta.env.VITE_API_URL ?? "";

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
  get<PublicMenuDto>(`${BASE}/api/public/menu/${slug}`);

export const trackOrder = (token: string): Promise<OrderTrackingDto> =>
  get<OrderTrackingDto>(`${BASE}/api/public/orders/${token}`);

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
}

export interface PortalOrderCreatedDto {
  id:           string;
  orderNumber:  number;
  trackingToken: string;
  status:       string;
  total:        number;
}

export const createPortalOrder = (req: CreatePortalOrderRequest): Promise<PortalOrderCreatedDto> =>
  post<PortalOrderCreatedDto>(`${BASE}/api/public/orders`, req);
