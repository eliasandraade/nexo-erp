import { apiClient } from "@/services/api-client";

export interface PriceListDto {
  id: string;
  name: string;
  description: string | null;
  isDefault: boolean;
  isActive: boolean;
  itemCount: number;
  createdAt: string;
  validFrom: string | null;
  validUntil: string | null;
  priority: number;
}

export interface PriceListDetailDto extends Omit<PriceListDto, "itemCount"> {
  items: { id: string; productId: string; productName: string; productCode: string; price: number }[];
  customers: { customerId: string; name: string }[] | null;
}

export interface PriceListInput {
  name: string;
  description?: string | null;
  validFrom?: string | null;
  validUntil?: string | null;
  priority?: number;
}

const BASE = "/varejo/price-lists";

export const PRICE_LISTS_KEY = ["price-lists"] as const;

export const listPriceLists = () => apiClient.get<PriceListDto[]>(`${BASE}?includeInactive=true`);
export const getPriceList = (id: string) => apiClient.get<PriceListDetailDto>(`${BASE}/${id}`);
export const createPriceList = (req: PriceListInput & { isDefault?: boolean }) => apiClient.post<PriceListDto>(BASE, req);
export const updatePriceList = (id: string, req: PriceListInput) => apiClient.put<PriceListDto>(`${BASE}/${id}`, req);
export const setDefaultPriceList = (id: string) => apiClient.post<PriceListDto>(`${BASE}/${id}/set-default`, {});
export const setPriceListActive = (id: string, active: boolean) =>
  apiClient.post<PriceListDto>(`${BASE}/${id}/${active ? "activate" : "deactivate"}`, {});
export const setListPrice = (id: string, productId: string, price: number) =>
  apiClient.put<PriceListDetailDto>(`${BASE}/${id}/products`, { productId, price });
export const removeListPrice = (id: string, productId: string) =>
  apiClient.delete<PriceListDetailDto>(`${BASE}/${id}/products/${productId}`);
export const linkCustomer = (id: string, customerId: string) =>
  apiClient.put<PriceListDetailDto>(`${BASE}/${id}/customers/${customerId}`, {});
export const unlinkCustomer = (id: string, customerId: string) =>
  apiClient.delete<PriceListDetailDto>(`${BASE}/${id}/customers/${customerId}`);
