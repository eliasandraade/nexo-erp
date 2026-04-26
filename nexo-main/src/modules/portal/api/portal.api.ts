import axios from "axios";
import type { PublicMenuDto, OrderTrackingDto } from "../types";

// Portal uses raw axios — no auth headers, no tenant context
const BASE = import.meta.env.VITE_API_URL ?? "";

export const getPublicMenu = (slug: string): Promise<PublicMenuDto> =>
  axios.get<PublicMenuDto>(`${BASE}/api/public/menu/${slug}`).then((r) => r.data);

export const trackOrder = (token: string): Promise<OrderTrackingDto> =>
  axios.get<OrderTrackingDto>(`${BASE}/api/public/orders/${token}`).then((r) => r.data);

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
  axios.post<PortalOrderCreatedDto>(`${BASE}/api/public/orders`, req).then((r) => r.data);
